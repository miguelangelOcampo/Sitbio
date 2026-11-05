using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-024 Bitácora de auditoría (HU-010, AC-01 y AC-07). Consulta de solo lectura filtrada por
/// usuario, acción y fechas (CA-HU-010-03), paginada en la API. La API decide el permiso y el
/// tenant (RN-008); aquí solo se muestran sus respuestas.
/// </summary>
public partial class BitacoraViewModel(IServicioBitacora servicio) : ObservableObject
{
    public const int TamanoPagina = 50;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");
    private static readonly string[] FormatosFecha = ["d/M/yyyy", "d-M-yyyy", "yyyy-M-d"];

    private readonly Dictionary<Guid, string> _usuariosVistos = [];
    private int _consulta;
    private bool _aplicandoVarios;

    public static IReadOnlyList<OpcionFiltro> OpcionesAccion { get; } =
        [new(string.Empty, "Todas las acciones"), .. CatalogoAcciones.Todas.Select(a => new OpcionFiltro(a.Codigo, a.Nombre))];

    public static OpcionFiltro TodosLosUsuarios { get; } = new(string.Empty, "Todos los usuarios");

    /// <summary>Usuarios que han aparecido en las consultas: solo crece, así no se pierde la selección.</summary>
    public ObservableCollection<OpcionFiltro> OpcionesUsuario { get; } = [TodosLosUsuarios];

    // ---------- Filtros ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroUsuario = TodosLosUsuarios;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private OpcionFiltro _filtroAccion = OpcionesAccion[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _desde = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayFiltros))]
    private string _hasta = string.Empty;

    [ObservableProperty]
    private string? _errorFechas;

    public bool HayFiltros => FiltroUsuario.Valor.Length > 0 || FiltroAccion.Valor.Length > 0 || Desde.Trim().Length > 0 || Hasta.Trim().Length > 0;

    // ---------- Resultado ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEventos))]
    private bool _cargando;

    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinEventos))]
    private IReadOnlyList<FilaBitacora> _filas = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinEventos))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    [ObservableProperty]
    private int _denegados;

    [ObservableProperty]
    private int _fallidos;

    public bool SinEventos => !Cargando && Aviso is not { EsError: true } && Total == 0;

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 eventos"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "evento" : "eventos")}";

    // ---------- Comandos ----------

    /// <summary>Consulta desde la primera página con los filtros actuales.</summary>
    [RelayCommand]
    private Task CargarAsync() => ConsultarAsync(1);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    [RelayCommand]
    private Task LimpiarAsync()
    {
        _aplicandoVarios = true;
        FiltroUsuario = TodosLosUsuarios;
        FiltroAccion = OpcionesAccion[0];
        Desde = string.Empty;
        Hasta = string.Empty;
        _aplicandoVarios = false;
        return ConsultarAsync(1);
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    // ---------- Apoyo ----------

    partial void OnFiltroUsuarioChanged(OpcionFiltro value) => AlCambiarFiltro();

    partial void OnFiltroAccionChanged(OpcionFiltro value) => AlCambiarFiltro();

    partial void OnDesdeChanged(string value) => AlCambiarFiltro();

    partial void OnHastaChanged(string value) => AlCambiarFiltro();

    private void AlCambiarFiltro()
    {
        if (!_aplicandoVarios)
        {
            _ = ConsultarAsync(1);
        }
    }

    private async Task ConsultarAsync(int pagina)
    {
        if (!LeerFechas(out var desde, out var hasta))
        {
            return;
        }

        var usuario = Guid.TryParse(FiltroUsuario?.Valor, out var id) ? id : (Guid?)null;
        var accion = FiltroAccion?.Valor is { Length: > 0 } a ? a : null;
        var filtro = new FiltroConsultaBitacora(usuario, desde, hasta, accion, pagina, TamanoPagina);

        // Si el usuario cambia los filtros antes de que llegue la respuesta, solo vale la última consulta.
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var principal = servicio.ConsultarAsync(filtro, CancellationToken.None);
            var denegados = servicio.ConsultarAsync(filtro with { Accion = CatalogoAcciones.AccesoDenegado, Pagina = 1, Tamano = 1 }, CancellationToken.None);
            var fallidos = servicio.ConsultarAsync(filtro with { Accion = CatalogoAcciones.LoginFallido, Pagina = 1, Tamano = 1 }, CancellationToken.None);
            await Task.WhenAll(principal, denegados, fallidos);
            if (consulta != _consulta)
            {
                return;
            }

            var resultado = await principal;
            RecordarUsuarios(resultado.Items);
            Filas = resultado.Items.Select(e => new FilaBitacora(e)).ToList();
            Total = resultado.Total;
            Pagina = pagina;
            Denegados = (await denegados).Total;
            Fallidos = (await fallidos).Total;
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            if (consulta != _consulta)
            {
                return;
            }

            Filas = [];
            Total = 0;
            Pagina = 1;
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudo consultar la bitácora",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargando = false;
            }
        }
    }

    /// <summary>Fechas del filtro: «Desde» desde las 00:00 locales; «Hasta» incluye el día completo.</summary>
    private bool LeerFechas(out DateTimeOffset? desde, out DateTimeOffset? hasta)
    {
        desde = null;
        hasta = null;
        var okDesde = LeerFecha(Desde, out var diaDesde);
        var okHasta = LeerFecha(Hasta, out var diaHasta);
        if (!okDesde || !okHasta)
        {
            ErrorFechas = "Escribe la fecha como dd/mm/aaaa, por ejemplo 05/11/2026.";
            return false;
        }

        if (diaDesde is { } d && diaHasta is { } h && h < d)
        {
            ErrorFechas = "La fecha final no puede ser anterior a la inicial.";
            return false;
        }

        ErrorFechas = null;
        desde = diaDesde is { } inicio ? MedianocheLocal(inicio) : null;
        hasta = diaHasta is { } fin ? MedianocheLocal(fin.AddDays(1)) : null;
        return true;
    }

    private static bool LeerFecha(string texto, out DateOnly? dia)
    {
        dia = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (DateOnly.TryParseExact(texto.Trim(), FormatosFecha, Cultura, DateTimeStyles.None, out var valor))
        {
            dia = valor;
            return true;
        }

        return false;
    }

    private static DateTimeOffset MedianocheLocal(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private void RecordarUsuarios(IEnumerable<EventoBitacora> eventos)
    {
        foreach (var evento in eventos)
        {
            if (_usuariosVistos.ContainsKey(evento.UsuarioId))
            {
                continue;
            }

            var nombre = evento.Usuario ?? FilaBitacora.UsuarioSinNombre;
            _usuariosVistos[evento.UsuarioId] = nombre;
            var opcion = new OpcionFiltro(evento.UsuarioId.ToString(), nombre);
            var indice = 1;
            while (indice < OpcionesUsuario.Count && string.Compare(OpcionesUsuario[indice].Texto, nombre, Cultura, CompareOptions.IgnoreCase) <= 0)
            {
                indice++;
            }

            OpcionesUsuario.Insert(indice, opcion);
        }
    }
}

/// <summary>Tono del punto de estado de la acción.</summary>
public enum TonoAccion
{
    Informativo,
    Exito,
    Critico,
    Neutro,
}

/// <summary>Acciones del validador de bitacora_auditoria (script 02 + V007) con su nombre para mostrar.</summary>
public static class CatalogoAcciones
{
    public const string AccesoDenegado = "ACCESO_DENEGADO";
    public const string LoginFallido = "LOGIN_FALLIDO";

    public sealed record Accion(string Codigo, string Nombre, TonoAccion Tono);

    public static IReadOnlyList<Accion> Todas { get; } =
    [
        new("LOGIN_OK", "Inicio de sesión", TonoAccion.Exito),
        new(LoginFallido, "Inicio de sesión fallido", TonoAccion.Critico),
        new("CIERRE_SESION", "Cierre de sesión", TonoAccion.Neutro),
        new(AccesoDenegado, "Acceso denegado", TonoAccion.Critico),
        new("CREAR", "Creación", TonoAccion.Exito),
        new("ACTUALIZAR", "Actualización", TonoAccion.Informativo),
        new("ELIMINAR", "Eliminación", TonoAccion.Critico),
        new("CAMBIO_ROL", "Cambio de rol", TonoAccion.Informativo),
        new("CAMBIO_TENANT", "Cambio de institución", TonoAccion.Informativo),
        new("CIERRE_OT", "Cierre de orden de trabajo", TonoAccion.Exito),
        new("EXPORTAR", "Exportación", TonoAccion.Informativo),
        new("IMPORTAR_CSV", "Importación CSV", TonoAccion.Informativo),
    ];

    public static Accion De(string codigo) =>
        Todas.FirstOrDefault(a => a.Codigo == codigo) ?? new Accion(codigo, codigo, TonoAccion.Informativo);
}

/// <summary>Fila de la tabla de la bitácora con los textos que la vista muestra.</summary>
public sealed record FilaBitacora(EventoBitacora Evento)
{
    public const string UsuarioSinNombre = "Usuario no disponible";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private static readonly Dictionary<string, string> Motivos = new(StringComparer.Ordinal)
    {
        ["credenciales"] = "Credenciales incorrectas",
        ["bloqueada"] = "Cuenta bloqueada",
        ["no_disponible"] = "Cuenta no disponible",
        ["voluntario"] = "Cierre voluntario",
        ["inactividad"] = "Cierre por inactividad",
    };

    public string Fecha => Evento.FechaHora.ToLocalTime().ToString("d MMM yyyy", Cultura);

    public string Hora => Evento.FechaHora.ToLocalTime().ToString("HH:mm:ss", Cultura);

    public string Usuario => Evento.Usuario ?? UsuarioSinNombre;

    public string Accion => CatalogoAcciones.De(Evento.Accion).Nombre;

    public string CodigoAccion => Evento.Accion;

    public TonoAccion Tono => CatalogoAcciones.De(Evento.Accion).Tono;

    public string Entidad => Evento.Entidad;

    public string Ip => string.IsNullOrWhiteSpace(Evento.IpOrigen) ? "—" : Evento.IpOrigen;

    /// <summary>Resumen legible del detalle; nunca trae datos personales (lo garantiza la API).</summary>
    public string Detalle
    {
        get
        {
            var detalle = Evento.Detalle;
            if (detalle is null || detalle.Count == 0)
            {
                return "—";
            }

            if (detalle.TryGetValue("ruta", out var ruta))
            {
                var metodo = detalle.GetValueOrDefault("metodo");
                var rol = detalle.GetValueOrDefault("rol");
                var texto = string.IsNullOrEmpty(metodo) ? ruta : $"{metodo} {ruta}";
                return string.IsNullOrEmpty(rol) ? texto : $"{texto} · rol {rol}";
            }

            if (detalle.TryGetValue("motivo", out var motivo) && detalle.Count == 1)
            {
                return Motivos.GetValueOrDefault(motivo, motivo);
            }

            return string.Join(" · ", detalle.Select(par => $"{par.Key}: {(par.Key == "motivo" ? Motivos.GetValueOrDefault(par.Value, par.Value) : par.Value)}"));
        }
    }
}
