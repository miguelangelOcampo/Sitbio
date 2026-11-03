using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-004 Usuarios y roles (HU-011, solo AC-01). Pestaña «Usuarios»: indicadores, filtros, tabla
/// y acciones por fila; pestaña «Roles y permisos»: guía de solo consulta. La API valida el permiso
/// y las reglas (RN-002, RN-006, RN-009); aquí solo se muestran sus respuestas.
/// </summary>
public partial class UsuariosViewModel(IServicioUsuarios servicio, SesionActual sesion, IPortapapeles portapapeles) : ObservableObject
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private IReadOnlyList<FilaUsuario> _todos = [];

    public static IReadOnlyList<OpcionFiltro> OpcionesRol { get; } =
        [new(string.Empty, "Todos los roles"), .. GuiaRoles.Roles.Select(r => new OpcionFiltro(r.Codigo, r.Etiqueta))];

    public static IReadOnlyList<OpcionFiltro> OpcionesEstado { get; } =
        [new(string.Empty, "Todos los estados"), new("Activo", "Activo"), new("Bloqueado", "Bloqueado"), new("Inactivo", "Inactivo")];

    // ---------- Pestañas ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerUsuarios))]
    private bool _verRoles;

    public bool VerUsuarios => !VerRoles;

    // ---------- Carga y avisos ----------

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    private bool _ocupado;

    /// <summary>Aviso de la pantalla: errores de carga o confirmación de la última acción.</summary>
    [ObservableProperty]
    private Aviso? _aviso;

    // ---------- Usuarios ----------

    public IReadOnlyList<FilaUsuario> Todos
    {
        get => _todos;
        private set
        {
            _todos = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Activos));
            OnPropertyChanged(nameof(Bloqueados));
            OnPropertyChanged(nameof(Inactivos));
            OnPropertyChanged(nameof(HayBloqueados));
            OnPropertyChanged(nameof(TextoBloqueados));
            OnPropertyChanged(nameof(TituloPestanaUsuarios));
            OnPropertyChanged(nameof(RolesGuia));
            OnPropertyChanged(nameof(TextoVerUsuarios));
            ActualizarFiltrados();
        }
    }

    public int Total => Todos.Count;

    public int Activos => Todos.Count(u => u.Estado == "Activo");

    public int Bloqueados => Todos.Count(u => u.Estado == "Bloqueado");

    public int Inactivos => Todos.Count(u => u.Estado == "Inactivo");

    public bool HayBloqueados => Bloqueados > 0;

    public string TextoBloqueados => Bloqueados == 1 ? "1 cuenta bloqueada por intentos fallidos" : $"{Bloqueados} cuentas bloqueadas por intentos fallidos";

    public string TituloPestanaUsuarios => $"Usuarios ({Total})";

    [ObservableProperty]
    private string _busqueda = string.Empty;

    [ObservableProperty]
    private OpcionFiltro _filtroRol = OpcionesRol[0];

    [ObservableProperty]
    private OpcionFiltro _filtroEstado = OpcionesEstado[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinCoincidencias), nameof(ResumenLista))]
    private IReadOnlyList<FilaUsuario> _filtrados = [];

    public bool SinCoincidencias => !Cargando && Filtrados.Count == 0 && Total > 0;

    public string ResumenLista => $"{Filtrados.Count} de {Total} usuarios";

    // ---------- Panel, confirmación y clave temporal ----------

    [ObservableProperty]
    private FormularioUsuarioViewModel? _formulario;

    [ObservableProperty]
    private FilaUsuario? _porDesactivar;

    [ObservableProperty]
    private ClaveEmitida? _claveEmitida;

    // ---------- Guía de roles ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RolesGuia), nameof(Matriz), nameof(TextoVerUsuarios))]
    private InfoRol _rolGuia = GuiaRoles.Roles[1];

    public IReadOnlyList<RolEnGuia> RolesGuia => GuiaRoles.Roles
        .Select(r => new RolEnGuia(r, Todos.Count(u => u.Rol == r.Codigo && u.Estado == "Activo"), r == RolGuia))
        .ToList();

    public IReadOnlyList<FilaMatriz> Matriz => GuiaRoles.Matriz
        .Select(m => new FilaMatriz(m.Modulo, m.PorRol
            .Select((permiso, i) => new CeldaPermiso(permiso, GuiaRoles.Nivel(permiso), GuiaRoles.Roles[i] == RolGuia))
            .ToList()))
        .ToList();

    public IReadOnlyList<InfoRol> RolesMatriz => GuiaRoles.Roles;

    public string TextoVerUsuarios => $"Ver usuarios ({Todos.Count(u => u.Rol == RolGuia.Codigo)})";

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            var usuarios = await servicio.ListarAsync(CancellationToken.None);
            Todos = usuarios.Select(Fila).ToList();
            if (Aviso?.PuedeReintentar == true)
            {
                Aviso = null;
            }
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso("No se pudo cargar la lista de usuarios", error.Message, error.SinConexion ? "MSG-011" : error.Codigo, PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
            OnPropertyChanged(nameof(SinCoincidencias));
        }
    }

    [RelayCommand]
    private void MostrarUsuarios() => VerRoles = false;

    [RelayCommand]
    private void MostrarRoles() => VerRoles = true;

    [RelayCommand]
    private void ElegirRolGuia(InfoRol rol) => RolGuia = rol;

    [RelayCommand]
    private void VerUsuariosDelRol()
    {
        Busqueda = string.Empty;
        FiltroEstado = OpcionesEstado[0];
        FiltroRol = OpcionesRol.First(o => o.Valor == RolGuia.Codigo);
        VerRoles = false;
    }

    [RelayCommand]
    private void CrearConRolGuia() => Formulario = new FormularioUsuarioViewModel(null, RolGuia.Codigo);

    [RelayCommand]
    private void NuevoUsuario() => Formulario = new FormularioUsuarioViewModel(null);

    [RelayCommand]
    private void Editar(FilaUsuario fila) => Formulario = new FormularioUsuarioViewModel(fila.Usuario);

    [RelayCommand]
    private void CerrarFormulario() => Formulario = null;

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (Formulario is not { } formulario || !formulario.Validar())
        {
            return;
        }

        Ocupado = true;
        try
        {
            if (formulario.EsNuevo)
            {
                var creado = await servicio.CrearAsync(formulario.Datos(), CancellationToken.None);
                Reemplazar(creado.Usuario);
                Formulario = null;
                ClaveEmitida = new ClaveEmitida(NombreCompleto(creado.Usuario), creado.Usuario.Correo, creado.ClaveTemporal, EsNueva: true);
            }
            else
            {
                var anterior = Todos.FirstOrDefault(u => u.Id == formulario.Id)?.Rol;
                var actualizado = await servicio.ActualizarAsync(formulario.Id!.Value, formulario.Datos(), CancellationToken.None);
                Reemplazar(actualizado);
                Formulario = null;
                Aviso = Exito("Cambios guardados", anterior is not null && anterior != actualizado.Rol
                    ? "El nuevo rol aplica desde su próximo inicio de sesión."
                    : $"Se actualizaron los datos de {NombreCompleto(actualizado)}.");
            }
        }
        catch (ErrorApi error)
        {
            formulario.MostrarError(error);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private Task DesbloquearAsync(FilaUsuario fila) =>
        EjecutarAsync(async () =>
        {
            Reemplazar(await servicio.CambiarEstadoAsync(fila.Id, "desbloquear", CancellationToken.None));
            Aviso = Exito($"Cuenta de {fila.NombreCompleto} desbloqueada", "Ya puede iniciar sesión; el contador de intentos volvió a cero (RN-006).");
        });

    [RelayCommand]
    private Task RestablecerClaveAsync(FilaUsuario fila) =>
        EjecutarAsync(async () =>
        {
            var clave = await servicio.AsignarClaveTemporalAsync(fila.Id, CancellationToken.None);
            ClaveEmitida = new ClaveEmitida(fila.NombreCompleto, fila.Correo, clave, EsNueva: false);
        });

    [RelayCommand]
    private void PedirDesactivar(FilaUsuario fila) => PorDesactivar = fila;

    [RelayCommand]
    private void CancelarDesactivar() => PorDesactivar = null;

    [RelayCommand]
    private Task ConfirmarDesactivarAsync()
    {
        if (PorDesactivar is not { } fila)
        {
            return Task.CompletedTask;
        }

        PorDesactivar = null;
        return EjecutarAsync(async () =>
        {
            Reemplazar(await servicio.CambiarEstadoAsync(fila.Id, "desactivar", CancellationToken.None));
            Aviso = Exito($"{fila.NombreCompleto} desactivado", "Ya no puede iniciar sesión. Su historial se conserva y puedes reactivarlo después.");
        });
    }

    [RelayCommand]
    private Task ReactivarAsync(FilaUsuario fila) =>
        EjecutarAsync(async () =>
        {
            Reemplazar(await servicio.CambiarEstadoAsync(fila.Id, "reactivar", CancellationToken.None));
            Aviso = Exito($"{fila.NombreCompleto} reactivado", "Recupera el acceso con su rol anterior.");
        });

    [RelayCommand]
    private void CopiarClave()
    {
        if (ClaveEmitida is { } emitida)
        {
            ClaveEmitida = emitida with { Copiada = portapapeles.Copiar(emitida.Clave) };
        }
    }

    [RelayCommand]
    private void CerrarClave() => ClaveEmitida = null;

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    // ---------- Apoyo ----------

    partial void OnBusquedaChanged(string value) => ActualizarFiltrados();

    partial void OnFiltroRolChanged(OpcionFiltro value) => ActualizarFiltrados();

    partial void OnFiltroEstadoChanged(OpcionFiltro value) => ActualizarFiltrados();

    private void ActualizarFiltrados()
    {
        var texto = Busqueda.Trim();
        Filtrados = Todos
            .Where(u => FiltroRol.Valor.Length == 0 || u.Rol == FiltroRol.Valor)
            .Where(u => FiltroEstado.Valor.Length == 0 || u.Estado == FiltroEstado.Valor)
            .Where(u => texto.Length == 0
                || Cultura.CompareInfo.IndexOf($"{u.NombreCompleto} {u.Correo}", texto, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .ToList();
    }

    private async Task EjecutarAsync(Func<Task> accion)
    {
        Ocupado = true;
        try
        {
            await accion();
        }
        catch (ErrorApi error)
        {
            var titulo = error.Codigo switch
            {
                "RN-009" => "Límite del plan alcanzado",
                "USR-002" => "No se puede aplicar a tu propia cuenta",
                _ => error.SinConexion ? "Sin conexión" : "No se completó la acción",
            };
            var codigo = error.Codigo == "RN-009" ? "MSG-019" : error.SinConexion ? "MSG-011" : error.Codigo;
            Aviso = new Aviso(titulo, error.Message, codigo);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Reemplazar(UsuarioResumen usuario)
    {
        var fila = Fila(usuario);
        var lista = Todos.ToList();
        var indice = lista.FindIndex(u => u.Id == usuario.Id);
        if (indice >= 0)
        {
            lista[indice] = fila;
        }
        else
        {
            lista.Insert(0, fila);
        }

        Todos = lista;
    }

    private FilaUsuario Fila(UsuarioResumen usuario) => new(usuario, usuario.Id == sesion.UsuarioId);

    private static string NombreCompleto(UsuarioResumen usuario) => $"{usuario.Nombres} {usuario.Apellidos}";

    private static Aviso Exito(string titulo, string mensaje) => new(titulo, mensaje, null, EsError: false);
}

public sealed record OpcionFiltro(string Valor, string Texto)
{
    // La caja de la lista desplegable muestra el elemento elegido con ToString().
    public override string ToString() => Texto;
}

/// <summary>Fila de la tabla de usuarios con lo que la vista necesita mostrar y qué acciones ofrece.</summary>
public sealed record FilaUsuario(UsuarioResumen Usuario, bool EsPropio)
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    public Guid Id => Usuario.Id;

    public string NombreCompleto => $"{Usuario.Nombres} {Usuario.Apellidos}";

    public string NombreMostrado => EsPropio ? $"{NombreCompleto} (tú)" : NombreCompleto;

    public string Correo => Usuario.Correo;

    public string Rol => Usuario.Rol;

    public string NombreRol => GuiaRoles.NombreDe(Usuario.Rol);

    public string Estado => Usuario.Estado;

    public string UltimoAcceso => Usuario.UltimoAcceso?.ToLocalTime().ToString("d MMM yyyy, HH:mm", Cultura) ?? "Nunca";

    public bool PuedeDesbloquear => Estado == "Bloqueado";

    public bool PuedeRestablecer => Estado != "Inactivo";

    /// <summary>La propia cuenta no se desactiva (USR-002); la API también lo impide.</summary>
    public bool PuedeDesactivar => !EsPropio && Estado != "Inactivo";

    public bool PuedeReactivar => !EsPropio && Estado == "Inactivo";
}

/// <summary>Clave temporal recién emitida (RF-012): se muestra una sola vez.</summary>
public sealed record ClaveEmitida(string Nombre, string Correo, string Clave, bool EsNueva, bool Copiada = false)
{
    public string Titulo => EsNueva ? $"Usuario creado: {Nombre}" : "Contraseña temporal generada";
}

public sealed record RolEnGuia(InfoRol Info, int Activos, bool Seleccionado)
{
    public string TextoActivos => Activos == 1 ? "1 activo" : $"{Activos} activos";

    public string Detalle => $"{Info.Frecuencia} · {Info.Limite}";
}

public sealed record FilaMatriz(string Modulo, IReadOnlyList<CeldaPermiso> Celdas);

public sealed record CeldaPermiso(string Texto, NivelPermiso Nivel, bool Seleccionada);
