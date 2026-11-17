using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Tests.Equipos;

/// <summary>HU-015 (CA-HU-015-01 y 02; RN-009): reglas de la carga de documentos sin base de datos.</summary>
public class GestionDocumentosTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Equipo = Guid.NewGuid();
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");

    private readonly IRepositorioDocumentos _repositorio = Substitute.For<IRepositorioDocumentos>();
    private readonly IAlmacenDocumentos _almacen = Substitute.For<IAlmacenDocumentos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly GestionDocumentos _caso;

    public GestionDocumentosTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(Usuario);
        _repositorio.ExisteEquipoAsync(Equipo, Arg.Any<CancellationToken>()).Returns(true);
        _repositorio.CapacidadAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadAlmacenamiento("Clínica", 25, 0));
        _repositorio.NombreUsuarioAsync(Usuario, Arg.Any<CancellationToken>()).Returns("Laura Gómez");
        _almacen.TamanoMaximoBytes.Returns(10L * 1024 * 1024);
        _almacen.GuardarAsync(Tenant, Equipo, Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => $"{Tenant}/{Equipo}/{c.ArgAt<string>(2)}");
        _caso = new GestionDocumentos(_repositorio, _almacen, _auditoria, _contexto, TimeProvider.System);
    }

    private Task<DocumentoDto> Subir(string? tipo, string? nombre, byte[] contenido, long? tamano = null) =>
        _caso.SubirAsync(Equipo, tipo, nombre, new MemoryStream(contenido), tamano ?? contenido.Length, CancellationToken.None);

    [Theory]
    [InlineData("manual.pdf", "application/pdf")]
    [InlineData("FOTO.JPG", "image/jpeg")]
    [InlineData("foto.jpeg", "image/jpeg")]
    [InlineData("ficha.png", "image/png")]
    public async Task CA_HU_015_01_PdfJpgYPng_SeGuardanConSuTipoMimeYQuedanEnLaBitacora(string nombre, string mime)
    {
        byte[] contenido = mime switch
        {
            "image/jpeg" => [0xFF, 0xD8, 0xFF, 0xE0, 0x00],
            "image/png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00],
            _ => Pdf,
        };

        var creado = await Subir("Ficha técnica", $@"C:\Users\ing\Escritorio\{nombre}", contenido);

        Assert.Equal((nombre, mime, "Laura Gómez"), (creado.Nombre, creado.TipoMime, creado.SubidoPor));
        _repositorio.Received(1).Agregar(Arg.Is<DocumentoEquipo>(d =>
            d.TenantId == Tenant && d.SubidoPorId == Usuario && d.RutaAlmacenamiento.StartsWith($"{Tenant}/{Equipo}/{d.Id:N}.", StringComparison.Ordinal)));
        await _auditoria.Received(1).RegistrarAsync(Usuario, AccionesAuditoria.Crear, GestionDocumentos.Entidad, creado.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("programa.exe")]
    [InlineData("notas.txt")]
    [InlineData("sin-extension")]
    [InlineData("renombrado.pdf")]
    public async Task CA_HU_015_02_OtroFormato_SeRechazaSinGuardarNada(string nombre)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Subir("Otro", nombre, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]));

        Assert.Equal([GestionDocumentos.FormatoNoPermitido], error.Errores["archivo"]);
        await _almacen.DidNotReceiveWithAnyArgs().GuardarAsync(default, default, default!, default!, default);
        _repositorio.DidNotReceiveWithAnyArgs().Agregar(default!);
    }

    [Fact]
    public async Task ArchivoVacioMayorAlMaximoOTipoDesconocido_SonErroresDeValidacion()
    {
        var vacio = await Assert.ThrowsAsync<ErrorValidacion>(() => Subir("Otro", "vacio.pdf", []));
        var grande = await Assert.ThrowsAsync<ErrorValidacion>(() => Subir("Otro", "grande.pdf", Pdf, (10L * 1024 * 1024) + 1));
        var tipo = await Assert.ThrowsAsync<ErrorValidacion>(() => Subir("Contrato", "contrato.pdf", Pdf));

        Assert.Equal(["Selecciona un archivo."], vacio.Errores["archivo"]);
        Assert.Equal(["El archivo supera 10 MB."], grande.Errores["archivo"]);
        Assert.True(tipo.Errores.ContainsKey("tipo"));
    }

    [Fact]
    public async Task RN_009_SinAlmacenamientoEnElPlan_ResponderConflictoConMsg019()
    {
        _repositorio.CapacidadAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadAlmacenamiento("Consultorio", 5, (5L * CapacidadAlmacenamiento.BytesPorGb) - 4));

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Subir("Otro", "manual.pdf", Pdf));

        Assert.Equal(GestionDocumentos.CodigoLimitePlan, error.Codigo);
        Assert.StartsWith("Tu institución alcanzó el máximo de almacenamiento del plan Consultorio (5 GB).", error.Message);
        await _almacen.DidNotReceiveWithAnyArgs().GuardarAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task EquipoDeOtraInstitucion_NoExiste()
    {
        var otro = Guid.NewGuid();

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.SubirAsync(otro, "Otro", "a.pdf", new MemoryStream(Pdf), Pdf.Length, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ListarAsync(otro, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.DesactivarAsync(otro, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SiNoSePuedeRegistrar_SeBorraElArchivoGuardado()
    {
        _repositorio.GuardarAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("sin base"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Subir("Otro", "manual.pdf", Pdf));

        _almacen.Received(1).Eliminar(Arg.Is<string>(r => r.StartsWith($"{Tenant}/{Equipo}/", StringComparison.Ordinal)));
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!);
    }

    [Fact]
    public async Task Desactivar_EsLogicoYQuedaEnLaBitacoraComoEliminar()
    {
        var documento = new DocumentoEquipo(Tenant, Equipo, Usuario, "Otro", "manual.pdf", "application/pdf", 10, DateTimeOffset.UtcNow);
        _repositorio.ObtenerAsync(Equipo, documento.Id, Arg.Any<CancellationToken>()).Returns(documento);

        await _caso.DesactivarAsync(Equipo, documento.Id, CancellationToken.None);

        Assert.False(documento.EstadoActivo);
        Assert.Equal(Usuario, documento.DesactivadoPorId);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(Usuario, AccionesAuditoria.Eliminar, GestionDocumentos.Entidad, documento.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
        _almacen.DidNotReceiveWithAnyArgs().Eliminar(default!);
    }
}
