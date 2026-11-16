using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using Sitbio.Application.Seguridad;
using Sitbio.Domain.Seguridad;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Seguridad;
using static Sitbio.SemillaDemo.Datos;

namespace Sitbio.SemillaDemo;

/// <summary>
/// Crea la institución y sus usuarios directamente en la base (no hay endpoint para eso) y después recorre
/// <see cref="Opciones.Dias"/> días de operación con la API real: catálogos, equipos con planes, calibraciones,
/// fallas, órdenes correctivas y preventivas, intervenciones en MongoDB, aprobaciones con firma y una baja.
/// Lo que el reloj no alcanza a ejecutar hoy queda abierto, como en un hospital de verdad.
/// </summary>
public sealed class Semilla(Opciones opciones)
{
    private const string Critica = "critica";
    private const string Operativo = "Operativo";
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);
    private readonly Random _azar = new(20261116);
    private readonly PriorityQueue<Evento, (DateTimeOffset, long)> _agenda = new();
    private readonly Dictionary<Guid, OrdenDemo> _ordenes = [];
    private readonly Dictionary<Guid, string> _correoPorUsuario = [];
    private readonly Dictionary<string, Guid> _tipos = [];
    private readonly Dictionary<string, Guid> _ubicaciones = [];
    private readonly List<Guid> _proveedores = [];
    private readonly List<EquipoDemo> _equipos = Equipos();
    private readonly Dictionary<string, int> _cuenta = [];
    private long _secuencia;
    private readonly RelojDemo _reloj = new(DateTimeOffset.UtcNow);
    private Sesiones? _sesiones;
    private DateTimeOffset _fin;

    private Sesiones Api => _sesiones ?? throw new InvalidOperationException("Las sesiones aún no están abiertas.");

    private static string Laura => Usuario("laura.gomez").Correo;

    private static string Andres => Usuario("andres.castano").Correo;

    private static string Admin => Usuario("admin").Correo;

    private static readonly string[] Tecnicos = ["carlos.munoz", "diana.ospina", "jorge.restrepo"];

    private sealed record Evento(DateTimeOffset Cuando, string Que, Func<Task> Accion);

    private sealed class OrdenDemo(Guid id, EquipoDemo equipo, string tecnico, bool preventiva, string? tipoFalla)
    {
        public Guid Id { get; } = id;

        public EquipoDemo Equipo { get; } = equipo;

        public string Tecnico { get; } = tecnico;

        public bool Preventiva { get; } = preventiva;

        public string? TipoFalla { get; } = tipoFalla;
    }

    public async Task EjecutarAsync()
    {
        _fin = DateTimeOffset.UtcNow.ToOffset(Colombia);
        var hoy = DateOnly.FromDateTime(_fin.DateTime);
        var inicio = new DateTimeOffset(hoy.AddDays(-opciones.Dias).ToDateTime(new TimeOnly(7, 30)), Colombia);
        _reloj.Ahora = inicio;

        await PrepararTenantAsync();
        using var api = new ApiDemo(opciones, _reloj);
        await CrearUsuariosAsync(api);
        using var sesiones = new Sesiones(api, _reloj, opciones.Clave);
        _sesiones = sesiones;

        Console.WriteLine($"Simulando del {inicio:dd/MM/yyyy} al {_fin:dd/MM/yyyy HH:mm} (hora de Colombia)...");
        await ConfigurarAsync(hoy);
        ProgramarRutina(inicio, hoy);
        await EjecutarAgendaAsync();
        await EscribirCredencialesAsync();
        Resumen();
    }

    // ---------------- Base: institución y usuarios ----------------

    private async Task PrepararTenantAsync()
    {
        await using var fuente = NpgsqlDataSource.Create(opciones.Postgres);
        await using var existe = fuente.CreateCommand("SELECT count(*) FROM sitbio.tenant WHERE id_tenant = $1");
        existe.Parameters.AddWithValue(Tenant);
        if (await existe.ExecuteScalarAsync() is long cuantas && cuantas > 0)
        {
            if (!opciones.Reemplazar)
            {
                throw new SemillaException($"«{Institucion}» ya existe. Vuelve a ejecutar con --reemplazar para borrarla y sembrarla de nuevo.");
            }

            await BorrarAsync(fuente);
        }

        await using var crear = fuente.CreateCommand(
            "INSERT INTO sitbio.tenant (id_tenant, nombre, nit, plan, max_usuarios_gestion, max_equipos, max_almacenamiento_gb) VALUES ($1, $2, $3, 'Hospital', 25, 500, 20)");
        crear.Parameters.AddWithValue(Tenant);
        crear.Parameters.AddWithValue(Institucion);
        crear.Parameters.AddWithValue(NitInstitucion);
        await crear.ExecuteNonQueryAsync();
    }

    /// <summary>Borra los datos de la institución de demo (PostgreSQL y MongoDB); solo con --reemplazar.</summary>
    private async Task BorrarAsync(NpgsqlDataSource fuente)
    {
        Console.WriteLine($"Borrando la demo anterior de «{Institucion}»...");
        var tablas = new List<string>();
        await using (var consulta = fuente.CreateCommand(
            "SELECT c.table_name FROM information_schema.columns c JOIN information_schema.tables t USING (table_schema, table_name) " +
            "WHERE c.table_schema = 'sitbio' AND c.column_name = 'tenant_id' AND t.table_type = 'BASE TABLE'"))
        await using (var lector = await consulta.ExecuteReaderAsync())
        {
            while (await lector.ReadAsync())
            {
                tablas.Add(lector.GetString(0));
            }
        }

        await using var conexion = await fuente.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();

        // Sin disparadores ni llaves foráneas durante el borrado (las tablas de historial son de solo inserción).
        await using (var replica = new NpgsqlCommand("SET LOCAL session_replication_role = replica", conexion, transaccion))
        {
            await replica.ExecuteNonQueryAsync();
        }

        foreach (var tabla in tablas.Append("tenant"))
        {
            var columna = tabla == "tenant" ? "id_tenant" : "tenant_id";
            await using var borrar = new NpgsqlCommand($"DELETE FROM sitbio.\"{tabla}\" WHERE {columna} = $1", conexion, transaccion);
            borrar.Parameters.AddWithValue(Tenant);
            await borrar.ExecuteNonQueryAsync();
        }

        await transaccion.CommitAsync();

        var baseMongo = new MongoClient(opciones.Mongo).GetDatabase(opciones.BaseMongo);
        using var nombres = await baseMongo.ListCollectionNamesAsync();
        foreach (var coleccion in await nombres.ToListAsync())
        {
            await baseMongo.GetCollection<BsonDocument>(coleccion).DeleteManyAsync(new BsonDocument("tenant_id", Tenant.ToString()));
        }
    }

    private async Task CrearUsuariosAsync(ApiDemo api)
    {
        var hash = new HasherBCrypt().Hash(opciones.Clave);
        using var alcance = api.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<SitbioDbContext>();
        var roles = await contexto.Roles.ToDictionaryAsync(r => r.Codigo ?? string.Empty, r => r.Id);
        foreach (var u in Usuarios)
        {
            var usuario = new Usuario(Tenant, roles[u.Rol], u.Nombres, u.Apellidos, u.Correo, hash, $"300{_azar.Next(1000000, 9999999)}");
            alcance.ServiceProvider.GetRequiredService<IEstablecerContextoUsuario>().Establecer(Tenant, usuario.Id, u.Rol);
            contexto.Usuarios.Add(usuario);
            await contexto.SaveChangesAsync();
            _correoPorUsuario[usuario.Id] = u.Correo;
        }
    }

    // ---------------- Día 0: configuración ----------------

    private async Task ConfigurarAsync(DateOnly hoy)
    {
        await Api.PutAsync(Admin, "/tenant/actual", new { nombre = Institucion, nit = NitInstitucion, diasAnticipacionAlerta = 30 });
        foreach (var u in Usuarios.Where(u => u.Inactivo))
        {
            await Api.PatchAsync(Admin, $"/usuarios/{IdDe(u.Correo)}/estado", new { accion = "desactivar" });
        }

        Avanzar(TimeSpan.FromMinutes(40));
        foreach (var nombre in Ubicaciones)
        {
            _ubicaciones[nombre] = IdRespuesta(await Api.PostAsync(Laura, "/catalogos/ubicaciones", new { nombre }));
        }

        foreach (var p in Proveedores)
        {
            _proveedores.Add(IdRespuesta(await Api.PostAsync(Laura, "/catalogos/proveedores",
                new { nombre = p.Nombre, nit = p.Nit, tipoProveedor = p.Tipo, nombreContacto = p.Contacto, correo = p.Correo, telefono = p.Telefono })));
        }

        foreach (var nombre in TiposFalla)
        {
            await Api.PostAsync(Laura, "/catalogos/tipos-falla", new { nombre });
        }

        foreach (var t in Tipos)
        {
            _tipos[t.Nombre] = IdRespuesta(await Api.PostAsync(Laura, "/catalogos/tipos-equipo", new
            {
                nombre = t.Nombre,
                claseRiesgo = t.ClaseRiesgo,
                requiereCalibracion = t.MesesCalibracion is not null,
                periodicidadCalibracionMeses = t.MesesCalibracion,
            }));
        }

        foreach (var (tipo, campos) in Campos)
        {
            await Api.PostAsync(Laura, $"/configuracion-campos/{_tipos[tipo]}", new { campos });
        }

        Avanzar(TimeSpan.FromHours(1));
        var dia0 = Hoy();
        foreach (var e in _equipos)
        {
            var tipo = Tipo(e);
            var datos = new Dictionary<string, object?>
            {
                ["tipoEquipoId"] = _tipos[e.Tipo],
                ["ubicacionId"] = _ubicaciones[e.Ubicacion],
                ["proveedorId"] = _proveedores[e.Proveedor],
                ["numeroSerie"] = $"SN-{e.Marca[..2].ToUpperInvariant()}{_azar.Next(100000, 999999)}",
                ["placaInventario"] = e.Placa,
                ["marca"] = e.Marca,
                ["modelo"] = e.Modelo,
                ["fechaAdquisicion"] = new DateOnly(e.AnioAdquisicion, _azar.Next(1, 13), _azar.Next(1, 28)),
                ["tipoAutorizacion"] = "registro_sanitario",
                ["registroInvima"] = $"INVIMA {e.AnioAdquisicion - 2}DM-{_azar.Next(1000000, 9999999):D7}",
                ["venceRegistro"] = e.Placa == "HDS-0013" ? hoy.AddDays(25) : hoy.AddYears(_azar.Next(1, 6)).AddDays(_azar.Next(0, 300)),
                ["claseRiesgo"] = tipo.ClaseRiesgo,
                ["requiereCalibracion"] = tipo.MesesCalibracion is not null,
                ["periodicidadCalibracionMeses"] = tipo.MesesCalibracion,
            };
            if (e.Rezago is null)
            {
                var ultima = dia0.AddDays(-_azar.Next(1, tipo.DiasPreventivo));
                datos["preventivo"] = new { periodicidadDias = tipo.DiasPreventivo, ultimaEjecucion = ultima, descripcion = $"Preventivo: {tipo.ProtocoloPreventivo}" };
            }

            e.Id = IdRespuesta(await Api.PostAsync(Laura, "/equipos", datos));
            Contar("equipos");
        }

        // Calibraciones vigentes al arrancar; dos equipos quedan rezagados para mostrar las alertas.
        Avanzar(TimeSpan.FromHours(2));
        foreach (var e in _equipos.Where(e => Tipo(e).MesesCalibracion is not null))
        {
            var meses = Tipo(e).MesesCalibracion ?? 12;
            var fecha = e.Rezago switch
            {
                "vencida" => hoy.AddDays(-18).AddMonths(-meses),
                "por_vencer" => hoy.AddDays(12).AddMonths(-meses),
                _ => dia0.AddDays(-_azar.Next(5, meses * 30 - 10)),
            };
            await CalibrarAsync(e, fecha);
        }
    }

    // ---------------- Días 1..N: operación ----------------

    private void ProgramarRutina(DateTimeOffset inicio, DateOnly hoy)
    {
        for (var dia = DateOnly.FromDateTime(inicio.DateTime).AddDays(1); dia <= hoy; dia = dia.AddDays(1))
        {
            var d = dia;
            Agendar(Hora(d, 7, 0), "alertas del día", () => RevisarAlertasAsync());
            var fallas = _azar.NextDouble() switch { < 0.45 => 0, < 0.85 => 1, _ => 2 };
            for (var i = 0; i < fallas; i++)
            {
                Agendar(Hora(d, _azar.Next(8, 17), _azar.Next(0, 60)), "reporte de falla", ReportarFallaAsync);
            }
        }

        // Trabajo reciente para que las bandejas no queden vacías: fallas de las últimas horas sin asignar o recién asignadas.
        foreach (var horas in new[] { 30, 20, 9, 4, 2 })
        {
            Agendar(_fin.AddHours(-horas), "reporte de falla reciente", ReportarFallaAsync);
        }

        foreach (var e in _equipos.Where(e => e.VenceCalibracion is not null && e.Rezago is null))
        {
            ProgramarRenovacion(e);
        }

        var baja = _equipos.Single(e => e.Rezago == "baja");
        Agendar(Hora(hoy.AddDays(-21), 10, 0), "baja de equipo", async () =>
        {
            await Api.PatchAsync(Andres, $"/equipos/{baja.Id}/estado", new { estado = "Baja", motivo = "Obsolescencia tecnológica: sin repuestos del fabricante. Reemplazo aprobado por el comité técnico." });
            baja.Operativo = false;
            Contar("bajas");
        });
    }

    private async Task EjecutarAgendaAsync()
    {
        var hechos = 0;
        while (_agenda.TryDequeue(out var evento, out _))
        {
            if (evento.Cuando > _fin)
            {
                Contar("eventos que quedan para después de hoy");
                continue;
            }

            _reloj.Ahora = evento.Cuando;
            try
            {
                await evento.Accion();
            }
            catch (SemillaException error)
            {
                throw new SemillaException($"{evento.Cuando:dd/MM/yyyy HH:mm} {evento.Que}: {error.Message}");
            }

            if (++hechos % 100 == 0)
            {
                Console.WriteLine($"  {evento.Cuando:dd/MM/yyyy}: {hechos} eventos");
            }
        }
    }

    /// <summary>GET /alertas genera las órdenes preventivas vencidas y las reparte (HU-019); aquí se toman para ejecutarlas.</summary>
    private async Task RevisarAlertasAsync()
    {
        await Api.GetAsync(Laura, "/alertas");
        var asignadas = await Api.GetAsync(Laura, "/ordenes?estado=asignada&pageSize=100");
        foreach (var o in asignadas.GetProperty("items").EnumerateArray())
        {
            var id = o.GetProperty("id").GetGuid();
            if (_ordenes.ContainsKey(id) || o.GetProperty("tipoMantenimiento").GetString() != "preventivo")
            {
                continue;
            }

            var equipo = _equipos.Single(e => e.Id == o.GetProperty("equipoId").GetGuid());
            var orden = new OrdenDemo(id, equipo, _correoPorUsuario[o.GetProperty("tecnicoId").GetGuid()], true, null);
            _ordenes[id] = orden;
            equipo.Ocupado++;
            Contar("órdenes preventivas");
            ProgramarEjecucion(orden, horasHastaInicio: _azar.Next(2, 72));
        }
    }

    private async Task ReportarFallaAsync()
    {
        var candidatos = _equipos.Where(e => e.Operativo && e.Ocupado == 0 && e.Rezago is null).ToList();
        if (candidatos.Count == 0)
        {
            return;
        }

        var equipo = candidatos[_azar.Next(candidatos.Count)];
        var tipoFalla = TiposFalla[_azar.Next(TiposFalla.Length)];
        var falla = Fallas[tipoFalla][_azar.Next(Fallas[tipoFalla].Length)];
        var criticidad = _azar.NextDouble() switch { < 0.2 => "baja", < 0.6 => "media", < 0.9 => "alta", _ => Critica };
        var clinico = equipo.Ubicacion switch
        {
            Uci or Neonatos => "paula.herrera",
            Urgencias => "mateo.vargas",
            Quirofano1 or Quirofano2 => "natalia.rios",
            _ => new[] { "paula.herrera", "mateo.vargas", "natalia.rios" }[_azar.Next(3)],
        };

        var tiquete = await Api.PostAsync(Usuario(clinico).Correo, "/tiquetes",
            new { equipoId = equipo.Id, tipoFalla, descripcion = falla.Descripcion, criticidad });
        equipo.Ocupado++;
        Contar("tiquetes");
        var espera = criticidad == Critica ? TimeSpan.FromMinutes(_azar.Next(10, 40)) : TimeSpan.FromMinutes(_azar.Next(30, 300));
        var tiqueteId = IdRespuesta(tiquete);
        Agendar(_reloj.Ahora + espera, "asignación de orden", async () =>
        {
            var tecnico = Tecnicos[_azar.Next(Tecnicos.Length)];
            var ingeniero = _azar.Next(3) == 0 ? Andres : Laura;
            var orden = await Api.PostAsync(ingeniero, "/ordenes",
                new { tiqueteId, tecnicoId = IdDe(Usuario(tecnico).Correo), prioridad = criticidad, fechaProgramada = Hoy().AddDays(criticidad is Critica or "alta" ? 0 : 1) });
            var demo = new OrdenDemo(IdRespuesta(orden), equipo, Usuario(tecnico).Correo, false, tipoFalla);
            _ordenes[demo.Id] = demo;
            Contar("órdenes correctivas");
            ProgramarEjecucion(demo, horasHastaInicio: criticidad == Critica ? 1 : _azar.Next(2, 30));
        });
    }

    private void ProgramarEjecucion(OrdenDemo orden, int horasHastaInicio)
    {
        var inicio = _reloj.Ahora.AddHours(horasHastaInicio).AddMinutes(_azar.Next(0, 60));
        Agendar(inicio, "inicio de orden", () => Api.PatchAsync(orden.Tecnico, $"/ordenes/{orden.Id}/estado", new { accion = "iniciar" }));

        var reanudar = inicio;
        if (!orden.Preventiva && _azar.NextDouble() < 0.15)
        {
            var pausa = inicio.AddHours(_azar.Next(1, 3));
            reanudar = pausa.AddDays(_azar.Next(1, 4));
            Agendar(pausa, "pausa por repuesto", () => Api.PatchAsync(orden.Tecnico, $"/ordenes/{orden.Id}/estado", new { accion = "pausar" }));
            Agendar(reanudar, "reanudación de orden", () => Api.PatchAsync(orden.Tecnico, $"/ordenes/{orden.Id}/estado", new { accion = "iniciar" }));
        }

        var cierre = reanudar.AddHours(_azar.Next(1, 6)).AddMinutes(_azar.Next(0, 60));
        var sale = !orden.Preventiva && _azar.NextDouble() < 0.07 ? "Fuera de servicio" : Operativo;
        Agendar(cierre, "cierre de orden", async () =>
        {
            await Api.PutAsync(orden.Tecnico, $"/ordenes/{orden.Id}/intervencion", Hoja(orden, sale));
            await Api.PatchAsync(orden.Tecnico, $"/ordenes/{orden.Id}/estado", new { accion = "cerrar" });
            Contar("intervenciones");
        });

        Agendar(cierre.AddHours(_azar.Next(3, 60)), "aprobación de orden", async () =>
        {
            var ingeniero = _azar.Next(3) == 0 ? Usuario("andres.castano") : Usuario("laura.gomez");
            await Api.PostAsync(ingeniero.Correo, $"/ordenes/{orden.Id}/aprobacion", new { firma = $"{ingeniero.NombreCompleto}, Ingeniero(a) Biomédico(a)" });
            orden.Equipo.Ocupado--;
            orden.Equipo.Operativo = sale == Operativo;
            Contar("órdenes aprobadas");
        });
    }

    private object Hoja(OrdenDemo orden, string sale)
    {
        var tipo = Tipo(orden.Equipo);
        string diagnostico, acciones;
        var repuestos = new List<object>();
        if (orden.Preventiva)
        {
            diagnostico = "Equipo en buen estado general; sin hallazgos que afecten su funcionamiento";
            acciones = tipo.ProtocoloPreventivo;
        }
        else
        {
            var fallas = Fallas[orden.TipoFalla ?? TiposFalla[0]];
            var falla = fallas[_azar.Next(fallas.Length)];
            diagnostico = sale == Operativo ? falla.Diagnostico : $"{falla.Diagnostico}. Requiere repuesto importado; el equipo queda fuera de servicio";
            acciones = sale == Operativo ? falla.Acciones : "Se diagnosticó la falla, se aisló el equipo y se solicitó cotización del repuesto al proveedor";
            if (sale == Operativo)
            {
                repuestos.AddRange(falla.Repuestos.Select(r => new { nombre = r.Nombre, cantidad = 1, costoUnitario = r.Costo }));
            }
        }

        var campos = new Dictionary<string, object>();
        switch (orden.Equipo.Tipo)
        {
            case Bomba:
                campos["tasa_de_goteo"] = Math.Round(95 + (_azar.NextDouble() * 10), 1);
                campos["prueba_de_oclusion"] = "Aprobada";
                break;
            case MonitorMultiparametro:
                campos["prueba_de_alarmas"] = true;
                campos["corriente_de_fuga"] = Math.Round(20 + (_azar.NextDouble() * 60), 1);
                break;
            case Desfibrilador:
                campos["energia_entregada"] = Math.Round(194 + (_azar.NextDouble() * 10), 1);
                break;
        }

        return new
        {
            diagnostico,
            accionesRealizadas = acciones,
            repuestos,
            costoManoObra = (orden.Preventiva ? _azar.Next(7, 25) : _azar.Next(9, 45)) * 5000,
            tiempoAtencionMin = orden.Preventiva ? _azar.Next(40, 150) : _azar.Next(30, 300),
            estadoEquipoSalida = sale,
            camposDinamicos = campos,
        };
    }

    private async Task CalibrarAsync(EquipoDemo equipo, DateOnly fecha)
    {
        var tecnico = Usuario(Tecnicos[_azar.Next(Tecnicos.Length)]).Correo;
        var calibracion = await Api.PostAsync(tecnico, "/calibraciones", new
        {
            equipoId = equipo.Id,
            fechaCalibracion = fecha,
            patronReferencia = equipo.Tipo switch
            {
                Desfibrilador => "Analizador Fluke Impulse 7000DP",
                Bomba => "Analizador Fluke IDA-5",
                Ventilador => "Analizador Fluke VT650",
                Incubadora => "Analizador Fluke INCU II",
                Electrobisturi => "Analizador Fluke QA-ES III",
                Tensiometro => "Manómetro patrón Fluke BP Pump 2",
                _ => "Simulador Fluke ProSim 8",
            },
            resultado = "conforme",
            numeroCertificado = $"MC-{fecha:yyyyMM}-{_azar.Next(1000, 9999)}",
            proveedorId = _proveedores[2],
        });
        await Api.EnviarAsync(Laura, HttpMethod.Patch, $"/calibraciones/{IdRespuesta(calibracion)}/aprobacion");
        equipo.VenceCalibracion = fecha.AddMonths(Tipo(equipo).MesesCalibracion ?? 12);
        Contar("calibraciones");
    }

    private void ProgramarRenovacion(EquipoDemo equipo)
    {
        var cuando = (equipo.VenceCalibracion ?? Hoy()).AddDays(-_azar.Next(3, 12));
        var momento = Hora(cuando < Hoy() ? Hoy() : cuando, _azar.Next(8, 16), _azar.Next(0, 60));
        Agendar(momento, "renovación de calibración", async () =>
        {
            await CalibrarAsync(equipo, Hoy());
            ProgramarRenovacion(equipo);
        });
    }

    // ---------------- Salida ----------------

    private async Task EscribirCredencialesAsync()
    {
        var lineas = new List<string>
        {
            $"# {Institucion}: usuarios de la demo local (archivo ignorado por git; no lo subas).",
            $"# Generado el {DateTimeOffset.Now:dd/MM/yyyy HH:mm}. Los usuarios comparten la misma contraseña.",
            $"Contraseña: {opciones.Clave}",
            string.Empty,
        };
        lineas.AddRange(Usuarios.Select(u => $"{u.Rol}  {u.Correo,-42} {u.NombreCompleto}{(u.Inactivo ? "  (inactivo)" : string.Empty)}"));
        await File.WriteAllLinesAsync(opciones.Salida, lineas);
    }

    private void Resumen()
    {
        Console.WriteLine();
        Console.WriteLine($"Listo: «{Institucion}» sembrada en PostgreSQL y en MongoDB ({opciones.BaseMongo}).");
        Console.WriteLine($"  usuarios: {Usuarios.Count}");
        foreach (var (que, cuantos) in _cuenta.OrderBy(c => c.Key))
        {
            Console.WriteLine($"  {que}: {cuantos}");
        }

        Console.WriteLine($"Usuarios y contraseña en: {Path.GetFullPath(opciones.Salida)}");
    }

    // ---------------- Apoyo ----------------

    private void Agendar(DateTimeOffset momento, string que, Func<Task> accion) =>
        _agenda.Enqueue(new Evento(momento, que, accion), (momento, _secuencia++));

    private void Avanzar(TimeSpan cuanto) => _reloj.Ahora += cuanto;

    private DateOnly Hoy() => DateOnly.FromDateTime(_reloj.Ahora.ToOffset(Colombia).DateTime);

    private static DateTimeOffset Hora(DateOnly dia, int hora, int minuto) => new(dia.ToDateTime(new TimeOnly(hora, minuto)), Colombia);

    private static TipoDemo Tipo(EquipoDemo e) => Tipos.Single(t => t.Nombre == e.Tipo);

    private Guid IdDe(string correo) => _correoPorUsuario.Single(p => p.Value == correo).Key;

    private static Guid IdRespuesta(JsonElement cuerpo) => cuerpo.GetProperty("id").GetGuid();

    private void Contar(string que) => _cuenta[que] = _cuenta.GetValueOrDefault(que) + 1;
}
