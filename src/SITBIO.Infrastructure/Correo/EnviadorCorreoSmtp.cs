using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Sitbio.Application.Instituciones;

namespace Sitbio.Infrastructure.Correo;

/// <summary>
/// Envía correos por SMTP con MailKit (J-05). Para Gmail: smtp.gmail.com, puerto 587, STARTTLS y una contraseña de
/// aplicación de Google. Con <see cref="SeguridadSmtp.Ninguna"/> y sin usuario sirve para smtp4dev en local.
/// </summary>
public sealed class EnviadorCorreoSmtp(OpcionesCorreo opciones) : IEnviadorCorreo
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    public async Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        if (!opciones.EstaConfigurado)
        {
            throw new InvalidOperationException("El correo no está configurado (Smtp:Servidor, Smtp:Remitente o Proveedor:CorreoSolicitudes).");
        }

        using var correo = new MimeMessage();
        correo.From.Add(new MailboxAddress("SITBIO", opciones.RemitenteEfectivo!));
        correo.To.Add(MailboxAddress.Parse(mensaje.Para));
        if (!string.IsNullOrWhiteSpace(mensaje.ResponderA))
        {
            correo.ReplyTo.Add(MailboxAddress.Parse(mensaje.ResponderA));
        }

        correo.Subject = mensaje.Asunto;
        correo.Body = new TextPart("plain") { Text = mensaje.Texto };

        using var cliente = new SmtpClient { Timeout = (int)Espera.TotalMilliseconds };
        await cliente.ConnectAsync(opciones.Servidor!, opciones.Puerto, Seguridad(opciones.Seguridad), cancellationToken);
        if (!string.IsNullOrWhiteSpace(opciones.Usuario))
        {
            await cliente.AuthenticateAsync(opciones.Usuario, opciones.Contrasena ?? string.Empty, cancellationToken);
        }

        await cliente.SendAsync(correo, cancellationToken);
        await cliente.DisconnectAsync(true, cancellationToken);
    }

    public static SecureSocketOptions Seguridad(SeguridadSmtp seguridad) => seguridad switch
    {
        SeguridadSmtp.Tls => SecureSocketOptions.SslOnConnect,
        SeguridadSmtp.Ninguna => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };
}
