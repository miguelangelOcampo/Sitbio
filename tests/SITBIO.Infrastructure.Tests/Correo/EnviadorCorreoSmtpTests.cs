using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using NSubstitute;
using Sitbio.Application.Instituciones;
using Sitbio.Infrastructure;
using Sitbio.Infrastructure.Correo;

namespace Sitbio.Infrastructure.Tests.Correo;

/// <summary>
/// J-06: el enviador con MailKit (J-05) contra un servidor SMTP mínimo en memoria, sin red externa ni credenciales, y
/// la lectura de «Smtp:*» y «Proveedor:CorreoSolicitudes».
/// </summary>
public class EnviadorCorreoSmtpTests
{
    [Fact]
    public async Task Envia_ConRemitenteDestinoResponderAAsuntoYTexto()
    {
        using var servidor = new ServidorSmtpPrueba();
        var opciones = new OpcionesCorreo
        {
            Servidor = "127.0.0.1",
            Puerto = servidor.Puerto,
            Seguridad = SeguridadSmtp.Ninguna,
            Remitente = "avisos@sitbio.co",
            Destino = "proveedor@sitbio.co",
        };
        var mensaje = new MensajeCorreo("proveedor@sitbio.co", "[SITBIO] SOL-000001 · Demostración · Clínica · Clínica San Rafael", "Institución: Clínica San Rafael", "ana@sanrafael.co");

        await new EnviadorCorreoSmtp(opciones).EnviarAsync(mensaje, CancellationToken.None);

        var recibido = await servidor.RecibidoAsync();
        Assert.Equal("avisos@sitbio.co", recibido.De);
        Assert.Equal(["proveedor@sitbio.co"], recibido.Para);
        var correo = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(recibido.Datos)));
        Assert.Equal(mensaje.Asunto, correo.Subject);
        Assert.Equal("ana@sanrafael.co", correo.ReplyTo.Mailboxes.Single().Address);
        Assert.Equal("SITBIO", correo.From.Mailboxes.Single().Name);
        Assert.Contains("Institución: Clínica San Rafael", correo.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SinConfiguracion_NoSeConecta()
    {
        var enviador = new EnviadorCorreoSmtp(new OpcionesCorreo { Servidor = "127.0.0.1" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => enviador.EnviarAsync(new MensajeCorreo("a@b.co", "x", "y"), CancellationToken.None));
    }

    [Theory]
    [InlineData(SeguridadSmtp.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(SeguridadSmtp.Tls, SecureSocketOptions.SslOnConnect)]
    [InlineData(SeguridadSmtp.Ninguna, SecureSocketOptions.None)]
    public void Seguridad_SeTraduceAMailKit(SeguridadSmtp seguridad, SecureSocketOptions esperado) =>
        Assert.Equal(esperado, EnviadorCorreoSmtp.Seguridad(seguridad));

    [Fact]
    public void Opciones_SeLeenDeLaConfiguracion_ConGmailPorDefecto()
    {
        var vacia = DependencyInjection.OpcionesCorreoDesde(Configuracion([]));
        Assert.Equal((587, SeguridadSmtp.StartTls, false), (vacia.Puerto, vacia.Seguridad, vacia.EstaConfigurado));

        var config = Configuracion(new Dictionary<string, string?>
        {
            ["Smtp:Servidor"] = "localhost",
            ["Smtp:Puerto"] = "2525",
            ["Smtp:Seguridad"] = "ninguna",
            ["Smtp:Usuario"] = "usuario",
            ["Smtp:Contrasena"] = "clave",
            ["Smtp:Remitente"] = "avisos@sitbio.co",
            ["Proveedor:CorreoSolicitudes"] = "proveedor@sitbio.co",
        });
        var opciones = DependencyInjection.OpcionesCorreoDesde(config);
        Assert.Equal(
            ("localhost", 2525, SeguridadSmtp.Ninguna, "usuario", "clave", "avisos@sitbio.co", "proveedor@sitbio.co", true),
            (opciones.Servidor, opciones.Puerto, opciones.Seguridad, opciones.Usuario, opciones.Contrasena, opciones.Remitente, opciones.Destino, opciones.EstaConfigurado));
    }

    private static IConfiguration Configuracion(Dictionary<string, string?> valores)
    {
        var configuracion = Substitute.For<IConfiguration>();
        configuracion[Arg.Any<string>()].Returns(llamada => valores.GetValueOrDefault(llamada.Arg<string>()));
        return configuracion;
    }

    /// <summary>Servidor SMTP de una sola conexión: responde los comandos básicos y guarda el sobre y los datos.</summary>
    private sealed class ServidorSmtpPrueba : IDisposable
    {
        private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
        private readonly Task<(string De, List<string> Para, string Datos)> _atencion;

        public ServidorSmtpPrueba()
        {
            _escucha.Start();
            _atencion = AtenderAsync();
        }

        public int Puerto => ((IPEndPoint)_escucha.LocalEndpoint).Port;

        public Task<(string De, List<string> Para, string Datos)> RecibidoAsync() => _atencion.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _escucha.Stop();

        private static string Direccion(string linea) => linea[(linea.IndexOf('<', StringComparison.Ordinal) + 1)..linea.IndexOf('>', StringComparison.Ordinal)];

        private async Task<(string De, List<string> Para, string Datos)> AtenderAsync()
        {
            using var conexion = await _escucha.AcceptTcpClientAsync();
            using var flujo = conexion.GetStream();
            using var lector = new StreamReader(flujo, new UTF8Encoding(false));
            using var escritor = new StreamWriter(flujo, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            await escritor.WriteLineAsync("220 prueba SMTP");
            var de = string.Empty;
            var para = new List<string>();
            var datos = new StringBuilder();
            while (await lector.ReadLineAsync() is { } linea)
            {
                var comando = linea.ToUpperInvariant();
                if (comando.StartsWith("EHLO", StringComparison.Ordinal))
                {
                    await escritor.WriteLineAsync("250-prueba");
                    await escritor.WriteLineAsync("250 8BITMIME");
                }
                else if (comando.StartsWith("MAIL FROM", StringComparison.Ordinal))
                {
                    de = Direccion(linea);
                    await escritor.WriteLineAsync("250 OK");
                }
                else if (comando.StartsWith("RCPT TO", StringComparison.Ordinal))
                {
                    para.Add(Direccion(linea));
                    await escritor.WriteLineAsync("250 OK");
                }
                else if (comando == "DATA")
                {
                    await escritor.WriteLineAsync("354 Envía el mensaje");
                    while (await lector.ReadLineAsync() is { } dato && dato != ".")
                    {
                        datos.Append(dato.StartsWith("..", StringComparison.Ordinal) ? dato[1..] : dato).Append("\r\n");
                    }

                    await escritor.WriteLineAsync("250 Recibido");
                }
                else if (comando == "QUIT")
                {
                    await escritor.WriteLineAsync("221 Adiós");
                    break;
                }
                else
                {
                    await escritor.WriteLineAsync("250 OK");
                }
            }

            return (de, para, datos.ToString());
        }
    }
}
