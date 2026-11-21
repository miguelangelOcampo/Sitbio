using System.Globalization;
using System.Text;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Application.Instituciones;

/// <summary>Datos de una solicitud del servicio guardada que se avisan al proveedor por correo (J-05, RF-029).</summary>
public sealed record AvisoSolicitud(
    string Codigo,
    string Tipo,
    string? Plan,
    string Institucion,
    string Nit,
    string Contacto,
    string Correo,
    string? Telefono,
    string? Mensaje,
    DateTimeOffset Fecha)
{
    public static AvisoSolicitud Desde(SolicitudServicio solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return new AvisoSolicitud(
            solicitud.Codigo, solicitud.Tipo, solicitud.PlanSolicitado, solicitud.Institucion, solicitud.Nit,
            solicitud.Contacto, solicitud.Correo, solicitud.Telefono, solicitud.Mensaje, solicitud.FechaCreacion);
    }
}

/// <summary>
/// Pone en cola el aviso de una solicitud guardada. No espera el envío: el correo sale en segundo plano y, si falla,
/// la solicitud ya quedó guardada (J-05).
/// </summary>
public interface IAvisoSolicitudes
{
    void Encolar(AvisoSolicitud aviso);
}

/// <summary>Un correo de texto plano.</summary>
public sealed record MensajeCorreo(string Para, string Asunto, string Texto, string? ResponderA = null);

/// <summary>Envía un correo por SMTP. La implementación real usa MailKit (Infrastructure).</summary>
public interface IEnviadorCorreo
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken);
}

/// <summary>Cifrado de la conexión SMTP.</summary>
public enum SeguridadSmtp
{
    /// <summary>STARTTLS obligatorio (Gmail, puerto 587).</summary>
    StartTls,

    /// <summary>TLS desde la conexión (puerto 465).</summary>
    Tls,

    /// <summary>Sin cifrado: solo para un servidor local de pruebas como smtp4dev.</summary>
    Ninguna,
}

/// <summary>
/// Configuración del correo (J-05). «Smtp:Servidor», «Smtp:Puerto», «Smtp:Seguridad», «Smtp:Usuario»,
/// «Smtp:Contrasena» y «Smtp:Remitente»; el destino es «Proveedor:CorreoSolicitudes». Usuario y contraseña de
/// aplicación solo en user-secrets o variables de entorno, nunca en el repositorio.
/// </summary>
public sealed class OpcionesCorreo
{
    public string? Servidor { get; init; }

    public int Puerto { get; init; } = 587;

    public SeguridadSmtp Seguridad { get; init; } = SeguridadSmtp.StartTls;

    public string? Usuario { get; init; }

    public string? Contrasena { get; init; }

    /// <summary>Dirección que envía; si falta, el usuario SMTP.</summary>
    public string? Remitente { get; init; }

    /// <summary>A quién se avisa cada solicitud («Proveedor:CorreoSolicitudes»).</summary>
    public string? Destino { get; init; }

    public string? RemitenteEfectivo => string.IsNullOrWhiteSpace(Remitente) ? Usuario : Remitente;

    /// <summary>Sin servidor, remitente o destino no se intenta enviar (por ejemplo, en CI).</summary>
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(Servidor) && !string.IsNullOrWhiteSpace(RemitenteEfectivo) && !string.IsNullOrWhiteSpace(Destino);
}

/// <summary>Texto del correo que recibe el proveedor por cada solicitud.</summary>
public static class PlantillaAvisoSolicitud
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("America/Bogota", TimeSpan.FromHours(-5), "Bogotá", "Bogotá");

    public static string NombreTipo(string tipo) => tipo switch
    {
        TiposSolicitudServicio.Demostracion => "Demostración",
        TiposSolicitudServicio.Cotizacion => "Cotización",
        TiposSolicitudServicio.CambioPlan => "Cambio de plan",
        _ => tipo,
    };

    public static MensajeCorreo Crear(AvisoSolicitud aviso, string destino)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        var tipo = NombreTipo(aviso.Tipo);
        var plan = aviso.Plan ?? "Por definir";
        var fecha = TimeZoneInfo.ConvertTime(aviso.Fecha, Bogota);
        var texto = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Nueva solicitud de servicio en SITBIO: {aviso.Codigo}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Tipo: {tipo}")
            .AppendLine(CultureInfo.InvariantCulture, $"Plan de interés: {plan}")
            .AppendLine(CultureInfo.InvariantCulture, $"Institución: {aviso.Institucion}")
            .AppendLine(CultureInfo.InvariantCulture, $"NIT: {aviso.Nit}")
            .AppendLine(CultureInfo.InvariantCulture, $"Persona de contacto: {aviso.Contacto}")
            .AppendLine(CultureInfo.InvariantCulture, $"Correo: {aviso.Correo}")
            .AppendLine(CultureInfo.InvariantCulture, $"Teléfono: {aviso.Telefono ?? "No lo indicó"}")
            .AppendLine(string.Create(Colombia, $"Recibida: {fecha:dd/MM/yyyy HH:mm} (hora de Colombia)"))
            .AppendLine()
            .AppendLine("Mensaje:")
            .AppendLine(string.IsNullOrWhiteSpace(aviso.Mensaje) ? "Sin mensaje." : aviso.Mensaje)
            .AppendLine()
            .AppendLine("Responde a este correo para escribirle directamente a la persona de contacto.")
            .ToString();
        return new MensajeCorreo(destino, $"[SITBIO] {aviso.Codigo} · {tipo} · {plan} · {aviso.Institucion}", texto, aviso.Correo);
    }
}
