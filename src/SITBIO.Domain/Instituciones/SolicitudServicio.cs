using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Sitbio.Domain.Comun;

namespace Sitbio.Domain.Instituciones;

/// <summary>Valores de <c>solicitud_servicio.tipo</c> (V002, CHECK; RF-029).</summary>
public static class TiposSolicitudServicio
{
    public const string Demostracion = "demostracion";
    public const string Cotizacion = "cotizacion";
    public const string CambioPlan = "cambio_plan";

    public static IReadOnlyList<string> Todos { get; } = [Demostracion, Cotizacion, CambioPlan];
}

/// <summary>Valores de <c>solicitud_servicio.estado</c> (V002, CHECK). La gestión la hace el proveedor (ST-10).</summary>
public static class EstadosSolicitudServicio
{
    public const string Recibida = "recibida";
    public const string EnGestion = "en_gestion";
    public const string Cerrada = "cerrada";
}

/// <summary>
/// Solicitud de demostración, cotización o cambio de plan (tabla <c>solicitud_servicio</c>, V002;
/// HU-028, RF-029). No pertenece a un tenant hasta que la institución es cliente: solo el cambio de
/// plan lleva <see cref="TenantId"/>. Nace «recibida» y recibe su código SOL-###### al guardarse.
/// </summary>
public sealed partial class SolicitudServicio
{
    public const string Prefijo = "SOL-";
    public const int ConsecutivoMaximo = 999_999;
    public const int LongitudMaximaInstitucion = 150;
    public const int LongitudMaximaContacto = 120;
    public const int LongitudMaximaCorreo = 150;
    public const int LongitudMaximaTelefono = 30;
    public const int LongitudMaximaMensaje = 1000;

    // Constructor para EF Core.
    private SolicitudServicio()
    {
    }

    private SolicitudServicio(
        string tipo,
        Guid? tenantId,
        string institucion,
        string nit,
        string? contacto,
        string? correo,
        string? telefono,
        string? planSolicitado,
        string? mensaje,
        DateTimeOffset fecha)
    {
        Id = Guid.NewGuid();
        Tipo = tipo;
        TenantId = tenantId;
        Institucion = institucion;
        Nit = nit;
        Contacto = Texto.Requerido(contacto, "contacto", LongitudMaximaContacto);
        Correo = ValidarCorreo(correo);
        Telefono = Texto.Opcional(telefono, LongitudMaximaTelefono, "telefono");
        PlanSolicitado = planSolicitado;
        Mensaje = Texto.Opcional(mensaje, LongitudMaximaMensaje, "mensaje");
        Estado = EstadosSolicitudServicio.Recibida;
        FechaCreacion = fecha;
    }

    public Guid Id { get; private set; }

    /// <summary>Vacío hasta que la persistencia lo numera.</summary>
    public string Codigo { get; private set; } = string.Empty;

    public string Tipo { get; private set; } = string.Empty;

    /// <summary>Solo en el cambio de plan: la institución del token (RN-008).</summary>
    public Guid? TenantId { get; private set; }

    public string Institucion { get; private set; } = string.Empty;

    public string Nit { get; private set; } = string.Empty;

    public string Contacto { get; private set; } = string.Empty;

    public string Correo { get; private set; } = string.Empty;

    public string? Telefono { get; private set; }

    public string? PlanSolicitado { get; private set; }

    public string? Mensaje { get; private set; }

    public string Estado { get; private set; } = string.Empty;

    public DateTimeOffset FechaCreacion { get; private set; }

    /// <summary>
    /// Demostración o cotización de una institución que aún no es cliente (CA-HU-028-01). El plan es
    /// opcional. Lanza <see cref="ArgumentException"/> con el nombre del campo si algo no es válido.
    /// </summary>
    public static SolicitudServicio DeInstitucionNueva(
        string? tipo,
        string? institucion,
        string? nit,
        string? contacto,
        string? correo,
        string? telefono,
        string? planSolicitado,
        string? mensaje,
        DateTimeOffset fecha)
    {
        if (tipo is not (TiposSolicitudServicio.Demostracion or TiposSolicitudServicio.Cotizacion))
        {
            throw new ArgumentException("Elige si quieres una demostración o una cotización.", nameof(tipo));
        }

        var nombre = Texto.Requerido(institucion, "institucion", LongitudMaximaInstitucion);
        if (nombre.Length < Tenant.LongitudMinimaNombre)
        {
            throw new ArgumentException($"El nombre de la institución debe tener al menos {Tenant.LongitudMinimaNombre} caracteres.", nameof(institucion));
        }

        var nitValido = Texto.Requerido(nit, "nit", 20);
        if (!PatronNit().IsMatch(nitValido))
        {
            throw new ArgumentException($"Usa el formato {Tenant.FormatoNit}.", nameof(nit));
        }

        var plan = string.IsNullOrWhiteSpace(planSolicitado) ? null : ValidarPlan(planSolicitado);
        return new SolicitudServicio(tipo, null, nombre, nitValido, contacto, correo, telefono, plan, mensaje, fecha);
    }

    /// <summary>
    /// Cambio de plan del Administrador (CA-HU-028-02): institución y NIT salen del tenant, nunca del
    /// cuerpo. La capacidad la valida el caso de uso antes de crearla (RN-009).
    /// </summary>
    public static SolicitudServicio DeCambioPlan(
        Tenant tenant,
        string? contacto,
        string? correo,
        string? telefono,
        string? planSolicitado,
        string? mensaje,
        DateTimeOffset fecha)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var plan = ValidarPlan(planSolicitado);
        if (plan == tenant.Plan)
        {
            throw new ArgumentException($"La institución ya tiene el plan {plan}.", nameof(planSolicitado));
        }

        return new SolicitudServicio(TiposSolicitudServicio.CambioPlan, tenant.Id, tenant.Nombre, tenant.Nit, contacto, correo, telefono, plan, mensaje, fecha);
    }

    /// <summary>Asigna el código SOL-###### una sola vez.</summary>
    public void Numerar(int consecutivo)
    {
        if (Codigo.Length > 0)
        {
            throw new InvalidOperationException("La solicitud ya tiene código.");
        }

        if (consecutivo is < 1 or > ConsecutivoMaximo)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutivo), "Se agotó la numeración de solicitudes.");
        }

        Codigo = Prefijo + consecutivo.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>El número de un código SOL-######, o null si no tiene ese formato.</summary>
    public static int? Consecutivo(string? codigo) =>
        codigo is not null && codigo.StartsWith(Prefijo, StringComparison.Ordinal)
            && int.TryParse(codigo.AsSpan(Prefijo.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            ? numero
            : null;

    private static string ValidarPlan(string? plan) =>
        PlanServicio.Buscar(plan?.Trim()) is { } encontrado
            ? encontrado.Nombre
            : throw new ArgumentException("Elige uno de los planes vigentes.", "planSolicitado");

    private static string ValidarCorreo(string? correo)
    {
        var valor = Texto.Requerido(correo, "correo", LongitudMaximaCorreo);
        return MailAddress.TryCreate(valor, out var direccion) && direccion.Address == valor && valor.Contains('.', StringComparison.Ordinal)
            ? valor.ToLowerInvariant()
            : throw new ArgumentException("Escribe un correo válido, por ejemplo nombre@institucion.co.", "correo");
    }

    [GeneratedRegex(@"^\d{3}\.\d{3}\.\d{3}-\d$")]
    private static partial Regex PatronNit();
}
