using System.Globalization;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Instituciones;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Instituciones;

/// <summary>Guarda las solicitudes del servicio y les asigna el código SOL-######.</summary>
public interface IRepositorioSolicitudesServicio
{
    Task RegistrarAsync(SolicitudServicio solicitud, CancellationToken cancellationToken);
}

/// <summary>API-SERV-PLANES (DOC-06): un plan vigente y sus límites; null = a la medida.</summary>
public sealed record PlanServicioDto(string Nombre, int? MaxUsuariosGestion, int? MaxEquipos, int? MaxAlmacenamientoGb, decimal? ValorMensualCop)
{
    public static PlanServicioDto Desde(PlanServicio plan) =>
        new(plan.Nombre, plan.MaxUsuariosGestion, plan.MaxEquipos, plan.MaxAlmacenamientoGb, plan.ValorMensualCop);
}

/// <summary>Cuerpo de POST /solicitudes-servicio (API-SERV-SOL). En el cambio de plan se ignoran institución y NIT.</summary>
public sealed record DatosSolicitudServicio(
    string? Tipo,
    string? Institucion,
    string? Nit,
    string? Contacto,
    string? Correo,
    string? Telefono,
    string? PlanSolicitado,
    string? Mensaje);

/// <summary>Respuesta: el código con el que el proveedor atiende la solicitud (MSG-021).</summary>
public sealed record SolicitudServicioCreadaDto(string Codigo, string Tipo, string Estado, string? PlanSolicitado, DateTimeOffset FechaCreacion);

/// <summary>
/// Consulta de planes y solicitud del servicio (HU-028, RF-029). La demostración y la cotización son
/// públicas; el cambio de plan exige al Administrador del Sistema (lo valida la API) y que el consumo
/// actual quepa en el plan elegido (CA-HU-028-02, RN-009).
/// </summary>
public sealed class SolicitudesServicio(
    IRepositorioSolicitudesServicio repositorio,
    IRepositorioInstitucion instituciones,
    TimeProvider tiempo)
{
    public const string CodigoLimitePlan = "RN-009";

    public static IReadOnlyList<PlanServicioDto> Planes() => [.. PlanServicio.Vigentes.Select(PlanServicioDto.Desde)];

    /// <summary>CA-HU-028-01: demostración o cotización sin cuenta.</summary>
    public async Task<SolicitudServicioCreadaDto> SolicitarAsync(DatosSolicitudServicio datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var solicitud = Validar(() => SolicitudServicio.DeInstitucionNueva(
            datos.Tipo, datos.Institucion, datos.Nit, datos.Contacto, datos.Correo, datos.Telefono, datos.PlanSolicitado, datos.Mensaje, tiempo.GetUtcNow()));
        return await RegistrarAsync(solicitud, cancellationToken);
    }

    /// <summary>
    /// CA-HU-028-02: cambio de plan de la institución del token. Si el consumo supera algún límite del
    /// plan elegido no se envía y el conflicto dice qué límite se excede (MSG-022).
    /// </summary>
    public async Task<SolicitudServicioCreadaDto> SolicitarCambioPlanAsync(DatosSolicitudServicio datos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var tenant = await instituciones.ObtenerAsync(cancellationToken) ?? throw new ErrorNoEncontrado("La institución");
        var solicitud = Validar(() => SolicitudServicio.DeCambioPlan(
            tenant, datos.Contacto, datos.Correo, datos.Telefono, datos.PlanSolicitado, datos.Mensaje, tiempo.GetUtcNow()));

        var plan = PlanServicio.Buscar(solicitud.PlanSolicitado)!;
        var uso = await instituciones.UsoAsync(cancellationToken);
        var excedidos = LimitesExcedidos(plan, uso);
        if (excedidos.Count > 0)
        {
            throw new ErrorConflicto(
                CodigoLimitePlan,
                $"El consumo actual no cabe en el plan {plan.Nombre}: {string.Join("; ", excedidos)}. La solicitud no se envió.");
        }

        return await RegistrarAsync(solicitud, cancellationToken);
    }

    /// <summary>Los límites del plan que el uso actual supera, en texto para el usuario.</summary>
    public static IReadOnlyList<string> LimitesExcedidos(PlanServicio plan, UsoPlan uso)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(uso);
        var cultura = CultureInfo.GetCultureInfo("es-CO");
        var excedidos = new List<string>();
        if (plan.MaxUsuariosGestion is { } usuarios && uso.UsuariosGestion > usuarios)
        {
            excedidos.Add($"usuarios de gestión {uso.UsuariosGestion} de {usuarios}");
        }

        if (plan.MaxEquipos is { } equipos && uso.EquiposActivos > equipos)
        {
            excedidos.Add(string.Create(cultura, $"equipos activos {uso.EquiposActivos:N0} de {equipos:N0}"));
        }

        var gb = Math.Round(uso.BytesDocumentos / (1024m * 1024m * 1024m), 2, MidpointRounding.AwayFromZero);
        if (plan.MaxAlmacenamientoGb is { } maximoGb && gb > maximoGb)
        {
            excedidos.Add(string.Create(cultura, $"almacenamiento {gb:0.##} GB de {maximoGb} GB"));
        }

        return excedidos;
    }

    /// <summary>true si el rol puede pedir un cambio de plan (solo AC-01, DOC-06).</summary>
    public static bool PuedeCambiarPlan(string rol) => rol == CodigosRol.AdministradorSistema;

    private async Task<SolicitudServicioCreadaDto> RegistrarAsync(SolicitudServicio solicitud, CancellationToken cancellationToken)
    {
        await repositorio.RegistrarAsync(solicitud, cancellationToken);
        return new SolicitudServicioCreadaDto(solicitud.Codigo, solicitud.Tipo, solicitud.Estado, solicitud.PlanSolicitado, solicitud.FechaCreacion);
    }

    private static SolicitudServicio Validar(Func<SolicitudServicio> crear)
    {
        try
        {
            return crear();
        }
        catch (ArgumentException error)
        {
            throw ErrorValidacion.DeCampo(error.ParamName ?? "solicitud", error.Message.Split(" (Parameter")[0]);
        }
    }
}
