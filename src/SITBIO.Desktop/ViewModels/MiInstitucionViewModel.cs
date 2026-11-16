using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.ViewModels;

/// <summary>
/// UI-019 Mi institución (HU-024, RF-024; prototipo Admin.tsx › MiInstitucion). El Administrador
/// (AC-01) edita nombre, NIT y anticipación de las alertas y consulta el plan y su consumo (RN-009).
/// El plan y sus límites los fija el proveedor (ST-10): aquí solo se muestran.
/// </summary>
public partial class MiInstitucionViewModel(IServicioInstitucion servicio) : ObservableObject
{
    private static readonly CultureInfo Cultura = TextosEquipo.Cultura;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDatos), nameof(TituloPlan), nameof(ValorPlan), nameof(VigenteHasta), nameof(Medidores))]
    private Institucion? _institucion;

    public bool HayDatos => Institucion is not null;

    [ObservableProperty]
    private bool _cargando;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool _guardando;

    /// <summary>Error al cargar (con «Reintentar») o al guardar (MSG-008, TEN-001, MSG-011).</summary>
    [ObservableProperty]
    private Aviso? _aviso;

    [ObservableProperty]
    private Aviso? _confirmacion;

    // ---------- Datos de la institución ----------

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string _nit = string.Empty;

    [ObservableProperty]
    private string _dias = string.Empty;

    [ObservableProperty]
    private string? _errorNombre;

    [ObservableProperty]
    private string? _errorNit;

    [ObservableProperty]
    private string? _errorDias;

    partial void OnNombreChanged(string value) => ErrorNombre = null;

    partial void OnNitChanged(string value) => ErrorNit = null;

    partial void OnDiasChanged(string value) => ErrorDias = null;

    // ---------- Plan contratado ----------

    public string TituloPlan => Institucion is null ? string.Empty : $"Plan {Institucion.Plan}";

    /// <summary>Valor mensual de referencia de DOC-01 Tabla 4; la facturación es manual (EX-03).</summary>
    public string ValorPlan => PlanServicio.Vigentes.FirstOrDefault(p => p.Nombre == Institucion?.Plan) is { } plan
        ? $"{plan.Valor} {plan.Periodo}".Trim()
        : TextosEquipo.SinDato;

    public string VigenteHasta => Institucion?.VigenteHasta is { } fecha ? TextosEquipo.Fecha(fecha) : "Sin fecha registrada";

    public IReadOnlyList<MedidorVista> Medidores => Institucion?.Consumo is not { } c ? [] :
    [
        MedidorVista.De("Usuarios de gestión", "activos", c.UsuariosGestion, decimales: false),
        MedidorVista.De("Equipos activos", "equipos", c.EquiposActivos, decimales: false),
        MedidorVista.De("Documentos", "GB", c.AlmacenamientoGb, decimales: true),
    ];

    // ---------- Comandos ----------

    [RelayCommand]
    private async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            Mostrar(await servicio.ObtenerAsync(CancellationToken.None));
            Aviso = null;
        }
        catch (ErrorApi error)
        {
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No pudimos abrir la configuración de la institución",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo,
                PuedeReintentar: true);
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        Confirmacion = null;
        if (!Validar(out var dias))
        {
            Aviso = new Aviso("Revisa los campos marcados", "Corrige los datos señalados y vuelve a guardar.", "MSG-008");
            return;
        }

        Guardando = true;
        try
        {
            var guardada = await servicio.ActualizarAsync(new DatosInstitucion(Nombre.Trim(), Nit.Trim(), dias), CancellationToken.None);
            Mostrar(guardada);
            Aviso = null;
            Confirmacion = new Aviso(
                "Datos de la institución guardados",
                $"Las alertas de vencimiento se mostrarán con {guardada.DiasAnticipacionAlerta} días de anticipación (RF-020).",
                null,
                EsError: false);
        }
        catch (ErrorApi error)
        {
            ErrorNombre = error.Errores.GetValueOrDefault("nombre") ?? ErrorNombre;
            ErrorNit = error.Errores.GetValueOrDefault("nit") ?? ErrorNit;
            ErrorDias = error.Errores.GetValueOrDefault("diasAnticipacionAlerta") ?? ErrorDias;
            Aviso = new Aviso(
                error.SinConexion ? "Sin conexión" : "No se pudieron guardar los datos",
                error.Message,
                error.SinConexion ? "MSG-011" : error.Codigo);
        }
        finally
        {
            Guardando = false;
        }
    }

    private bool PuedeGuardar() => !Guardando;

    [RelayCommand]
    private void CerrarConfirmacion() => Confirmacion = null;

    private void Mostrar(Institucion institucion)
    {
        Institucion = institucion;
        Nombre = institucion.Nombre;
        Nit = institucion.Nit;
        Dias = institucion.DiasAnticipacionAlerta.ToString(Cultura);
        (ErrorNombre, ErrorNit, ErrorDias) = (null, null, null);
    }

    /// <summary>Las mismas reglas que la API (Tenant.ActualizarDatos); la API vuelve a validarlas.</summary>
    private bool Validar(out int dias)
    {
        ErrorNombre = Nombre.Trim().Length < 4 ? "Escribe el nombre de la institución (al menos 4 caracteres)." : null;
        ErrorNit = PatronNit().IsMatch(Nit.Trim()) ? null : "Usa el formato 900.000.000-0.";
        var numero = int.TryParse(Dias.Trim(), NumberStyles.None, Cultura, out dias);
        ErrorDias = numero && dias is >= 7 and <= 90 ? null : "Usa un valor entre 7 y 90 días.";
        return ErrorNombre is null && ErrorNit is null && ErrorDias is null;
    }

    [GeneratedRegex(@"^\d{3}\.\d{3}\.\d{3}-\d$")]
    private static partial Regex PatronNit();
}

/// <summary>
/// Medidor del plan en UI-019: «X de Y», barra con el porcentaje y tono (verde, ámbar desde el
/// 80 %, rojo al llegar al límite) con su nota, como el prototipo.
/// </summary>
public sealed record MedidorVista(string Etiqueta, string Uso, string Unidad, double Barra, TonoEstado Tono, string? Nota)
{
    public static MedidorVista De(string etiqueta, string unidad, MedidorPlan medidor, bool decimales)
    {
        var formato = decimales ? "#,##0.##" : "N0";
        var uso = $"{medidor.Usados.ToString(formato, TextosEquipo.Cultura)} de {medidor.Maximo.ToString("N0", TextosEquipo.Cultura)}";
        var (tono, nota) = medidor switch
        {
            { LimiteAlcanzado: true } => (TonoEstado.Critico, "Límite alcanzado: no se pueden crear más (RN-009)."),
            { Alerta: true } => (TonoEstado.Advertencia, $"Usado el {medidor.Porcentaje} %."),
            _ => (TonoEstado.Exito, (string?)null),
        };

        // La barra siempre se ve un poco, aunque el uso sea cero.
        return new MedidorVista(etiqueta, uso, unidad, Math.Clamp(medidor.Porcentaje, 2, 100), tono, nota);
    }
}
