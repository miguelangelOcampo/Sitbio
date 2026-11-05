using System.Text.RegularExpressions;
using Sitbio.Application.Auditoria;

namespace Sitbio.Application.Tests.Auditoria;

public class EventoAuditoriaTests
{
    private static readonly DateTimeOffset Momento = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nuevo_ConDatosValidos_GeneraUnIdPropio()
    {
        var usuario = Guid.NewGuid();

        var evento = EventoAuditoria.Nuevo(usuario, AccionesAuditoria.AccesoDenegado, "usuarios", Momento, ipOrigen: "10.0.0.5");

        Assert.NotEqual(Guid.Empty, evento.Id);
        Assert.Equal(usuario, evento.UsuarioId);
        Assert.Equal("ACCESO_DENEGADO", evento.Accion);
        Assert.Null(evento.EntidadId);
    }

    [Fact]
    public void Nuevo_AccionDesconocida_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => EventoAuditoria.Nuevo(Guid.NewGuid(), "INVENTADA", "usuario", Momento));
    }

    [Fact]
    public void Nuevo_SinUsuarioOSinEntidad_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() => EventoAuditoria.Nuevo(Guid.Empty, AccionesAuditoria.LoginOk, "sesion", Momento));
        Assert.Throws<ArgumentException>(() => EventoAuditoria.Nuevo(Guid.NewGuid(), AccionesAuditoria.LoginOk, " ", Momento));
    }

    /// <summary>Las constantes coinciden con el enum del validador vigente (V007); si cambia uno, cambia el otro.</summary>
    [Fact]
    public void Acciones_SonLasDelValidadorDeV007()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "SITBIO.sln")))
        {
            raiz = raiz.Parent;
        }

        var script = File.ReadAllText(Path.Combine(raiz!.FullName, "database", "V007__bitacora_acciones.js"));
        var bloque = Regex.Match(script, @"enum:\s*\[(?<valores>[^\]]*)\]", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["valores"].Value;
        var delValidador = Regex.Matches(bloque, "\"([A-Z_]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value);

        Assert.Equal(delValidador.Order(StringComparer.Ordinal), AccionesAuditoria.Todas.Order(StringComparer.Ordinal));
    }
}
