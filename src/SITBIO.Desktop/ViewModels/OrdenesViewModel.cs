using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-012 «Mis órdenes» / «Órdenes de trabajo» (HU-004, HU-005). El Técnico (AC-03) ve solo sus órdenes, de
/// la más urgente a la menos urgente, y desde cada una inicia, pausa, reanuda y abre la intervención
/// (UI-013), donde la cierra. Los demás roles que consultan (AC-01, AC-02, AC-07) ven las órdenes de la
/// institución y la intervención en solo lectura; el Ingeniero (AC-02) abre las «cerradas» con «Revisar y
/// aprobar» (UI-014, NAV-010) y vuelve con la orden «aprobada». La API valida los permisos.
/// </summary>
public partial class OrdenesViewModel(
    IServicioOrdenes ordenes,
    IServicioEquipos equipos,
    IServicioConfiguracionCampos configuracion,
    SesionActual sesion) : ObservableObject
{
    public const string IdPantalla = "UI-012";
    public const int TamanoPagina = 20;

    private int _consulta;

    public static IReadOnlyList<OpcionFiltro> OpcionesEstado { get; } =
        [new OpcionFiltro(string.Empty, "Todos los estados"), .. EstadosOrden.Todos.Select(e => new OpcionFiltro(e, EstadosOrden.Nombre(e)))];

    /// <summary>Solo el Técnico de Mantenimiento ejecuta (inicia, pausa y registra).</summary>
    public bool EsTecnico => sesion.Sesion?.Rol == "AC-03";

    /// <summary>Solo el Ingeniero Biomédico aprueba y firma (HU-021).</summary>
    public bool EsIngeniero => sesion.Sesion?.Rol == "AC-02";

    public string Titulo => EsTecnico ? "Mis órdenes" : "Órdenes de trabajo";

    public string Descripcion => EsTecnico
        ? "Las órdenes que tienes asignadas, de la más urgente a la menos urgente. Inicia una para registrar su intervención."
        : "Las órdenes de trabajo de tu institución, de la más urgente a la menos urgente.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinOrdenes))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinOrdenes))]
    private Aviso? _aviso;

    [ObservableProperty]
    private string? _exito;

    [ObservableProperty]
    private IReadOnlyList<FilaOrden> _filas = [];

    [ObservableProperty]
    private OpcionFiltro _estado = OpcionesEstado[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _pagina = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumenPagina), nameof(HayAnterior), nameof(HaySiguiente), nameof(SinOrdenes))]
    [NotifyCanExecuteChangedFor(nameof(PaginaAnteriorCommand), nameof(PaginaSiguienteCommand))]
    private int _total;

    /// <summary>Orden en la que se está cambiando el estado.</summary>
    [ObservableProperty]
    private Guid? _cambiando;

    /// <summary>UI-013 abierta; null = la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnLista))]
    private IntervencionViewModel? _intervencion;

    /// <summary>UI-014 abierta; null = la lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnLista))]
    private RevisionViewModel? _revision;

    public bool EnLista => Intervencion is null && Revision is null;

    public bool Cargado { get; private set; }

    public bool SinOrdenes => Cargado && !Cargando && Aviso is null && Total == 0;

    public bool HayAnterior => Pagina > 1;

    public bool HaySiguiente => Pagina * TamanoPagina < Total;

    public string ResumenPagina => Total == 0
        ? "0 órdenes"
        : $"{(Pagina - 1) * TamanoPagina + 1:N0}–{Math.Min(Pagina * TamanoPagina, Total):N0} de {Total:N0} {(Total == 1 ? "orden" : "órdenes")}";

    public string TextoSinOrdenes => EsTecnico
        ? "No tienes órdenes asignadas. Cuando el Ingeniero te asigne una, aparecerá aquí."
        : "No hay órdenes de trabajo con este filtro.";

    [RelayCommand]
    private Task CargarAsync() => ConsultarAsync(1);

    [RelayCommand(CanExecute = nameof(HayAnterior))]
    private Task PaginaAnteriorAsync() => ConsultarAsync(Pagina - 1);

    [RelayCommand(CanExecute = nameof(HaySiguiente))]
    private Task PaginaSiguienteAsync() => ConsultarAsync(Pagina + 1);

    partial void OnEstadoChanged(OpcionFiltro value)
    {
        if (Cargado)
        {
            _ = ConsultarAsync(1);
        }
    }

    [RelayCommand]
    private void CerrarAviso() => Aviso = null;

    [RelayCommand]
    private void CerrarExito() => Exito = null;

    /// <summary>Iniciar una orden asignada o reanudar una pausada.</summary>
    [RelayCommand]
    private Task IniciarAsync(FilaOrden? fila) => CambiarEstadoAsync(fila, AccionesOrden.Iniciar);

    [RelayCommand]
    private Task PausarAsync(FilaOrden? fila) => CambiarEstadoAsync(fila, AccionesOrden.Pausar);

    /// <summary>Abre UI-013: para el técnico, editable en ejecución o pausada; en los demás casos, solo lectura.</summary>
    [RelayCommand]
    private void AbrirIntervencion(FilaOrden? fila)
    {
        if (fila is null || !fila.MuestraIntervencion)
        {
            return;
        }

        Exito = null;
        var intervencion = new IntervencionViewModel(ordenes, equipos, configuracion, fila.Orden, fila.Editable, Volver);
        Intervencion = intervencion;
        intervencion.CargarCommand.Execute(null);
    }

    /// <summary>NAV-010: el Ingeniero revisa y firma una orden cerrada.</summary>
    [RelayCommand]
    private void AbrirRevision(FilaOrden? fila)
    {
        if (fila is null || !fila.PuedeAprobar)
        {
            return;
        }

        Exito = null;
        var revision = new RevisionViewModel(ordenes, equipos, configuracion, fila.Orden, Volver);
        Revision = revision;
        revision.CargarCommand.Execute(null);
    }

    private void Volver(Orden? actualizada)
    {
        Intervencion = null;
        Revision = null;
        if (actualizada is not null)
        {
            Reemplazar(actualizada);
            var referencia = actualizada.CodigoTiquete ?? actualizada.PlacaInventario;
            Exito = actualizada.Estado switch
            {
                EstadosOrden.Cerrada => $"Cerraste la orden {referencia}; queda pendiente de la aprobación del Ingeniero.",
                EstadosOrden.Aprobada => $"Aprobaste y firmaste la orden {referencia}.",
                _ => $"Intervención guardada para la orden {referencia}.",
            };
        }
    }

    private async Task CambiarEstadoAsync(FilaOrden? fila, string accion)
    {
        if (fila is null || !EsTecnico || Cambiando is not null)
        {
            return;
        }

        Cambiando = fila.Orden.Id;
        Exito = null;
        try
        {
            var orden = await ordenes.CambiarEstadoAsync(fila.Orden.Id, accion, CancellationToken.None);
            Reemplazar(orden);
            Exito = accion == AccionesOrden.Pausar
                ? $"Pausaste la orden {fila.Referencia}."
                : fila.Orden.Estado == EstadosOrden.Pausada
                    ? $"Reanudaste la orden {fila.Referencia}."
                    : $"Iniciaste la orden {fila.Referencia}. Ya puedes registrar la intervención.";
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : $"No se pudo {(accion == AccionesOrden.Pausar ? "pausar" : "iniciar")} la orden {fila.Referencia}",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Cambiando = null;
        }
    }

    private void Reemplazar(Orden orden) =>
        Filas = Filas.Select(f => f.Orden.Id == orden.Id ? new FilaOrden(orden, EsTecnico, EsIngeniero) : f).ToList();

    private async Task ConsultarAsync(int pagina)
    {
        var consulta = ++_consulta;
        Cargando = true;
        try
        {
            var resultado = await ordenes.ListarAsync(
                new FiltroOrdenes(Estado.Valor is { Length: > 0 } e ? e : null, EsTecnico, pagina, TamanoPagina), CancellationToken.None);
            if (consulta != _consulta)
            {
                return;
            }

            Filas = resultado.Items.Select(o => new FilaOrden(o, EsTecnico, EsIngeniero)).ToList();
            Total = resultado.Total;
            Pagina = pagina;
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
                error.SinConexion ? "Sin conexión" : "No se pudieron consultar las órdenes",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            if (consulta == _consulta)
            {
                Cargado = true;
                Cargando = false;
                OnPropertyChanged(nameof(SinOrdenes));
            }
        }
    }
}

/// <summary>Fila de UI-012 con los textos y las acciones que admite cada estado; solo el técnico ejecuta y solo el Ingeniero aprueba.</summary>
public sealed record FilaOrden(Orden Orden, bool EsTecnico = false, bool EsIngeniero = false)
{
    /// <summary>Código del tiquete de origen o, sin tiquete, la placa del equipo.</summary>
    public string Referencia => Orden.CodigoTiquete ?? Orden.PlacaInventario;

    public string Equipo => string.IsNullOrEmpty(Orden.TipoEquipo) ? Orden.PlacaInventario : $"{Orden.PlacaInventario} · {Orden.TipoEquipo}";

    public string Ubicacion => Orden.Ubicacion;

    public string Falla => Orden.TipoFalla ?? "Mantenimiento programado";

    public string Prioridad => TextosTiquete.Criticidad(Orden.Prioridad);

    public TonoEstado TonoPrioridad => TextosTiquete.TonoCriticidad(Orden.Prioridad);

    public string Estado => EstadosOrden.Nombre(Orden.Estado);

    public TonoEstado Tono => Orden.Estado switch
    {
        EstadosOrden.EnEjecucion => TonoEstado.Advertencia,
        EstadosOrden.Pausada => TonoEstado.Neutro,
        EstadosOrden.Aprobada or EstadosOrden.Cerrada => TonoEstado.Exito,
        _ => TonoEstado.Neutro,
    };

    public string Programada => Orden.FechaProgramada is { } f ? f.ToString("d MMM yyyy", TextosEquipo.Cultura) : "Sin fecha programada";

    public string Intervencion => Orden.TieneIntervencion ? "Intervención registrada" : "Sin intervención";

    /// <summary>«Iniciar» para una asignada, «Reanudar» para una pausada.</summary>
    public bool PuedeIniciar => EsTecnico && Orden.Estado is EstadosOrden.Asignada or EstadosOrden.Pausada;

    public string TextoIniciar => Orden.Estado == EstadosOrden.Pausada ? "Reanudar" : "Iniciar";

    public bool PuedePausar => EsTecnico && Orden.Estado == EstadosOrden.EnEjecucion;

    /// <summary>El técnico registra o corrige la intervención con la orden en ejecución o pausada.</summary>
    public bool Editable => EsTecnico && Orden.Estado is EstadosOrden.EnEjecucion or EstadosOrden.Pausada;

    /// <summary>El Ingeniero revisa y firma la orden cerrada (UI-014).</summary>
    public bool PuedeAprobar => EsIngeniero && Orden.Estado == EstadosOrden.Cerrada;

    /// <summary>Quien no puede editarla la abre en solo lectura si ya existe; la que se aprueba se ve en UI-014.</summary>
    public bool MuestraIntervencion => !PuedeAprobar && (Editable || Orden.TieneIntervencion);

    public string TextoIntervencion => Editable
        ? Orden.TieneIntervencion ? "Editar intervención" : "Registrar intervención"
        : "Ver intervención";
}
