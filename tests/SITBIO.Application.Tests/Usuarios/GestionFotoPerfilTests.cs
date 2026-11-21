using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Tests.Usuarios;

/// <summary>
/// J-11 (HU-011): foto de perfil del usuario de la sesión. Mismas reglas de imagen que la foto del equipo; la
/// anterior se borra y no se toca ningún documento del plan.
/// </summary>
public class GestionFotoPerfilTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly IRepositorioUsuarios _repositorio = Substitute.For<IRepositorioUsuarios>();
    private readonly IAlmacenDocumentos _almacen = Substitute.For<IAlmacenDocumentos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Usuario _laura = new(Tenant, 4, "Laura", "Gómez", "laura@demo.co", "hash");
    private readonly GestionFotoPerfil _caso;

    public GestionFotoPerfilTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(_laura.Id);
        _repositorio.ObtenerAsync(_laura.Id, Arg.Any<CancellationToken>()).Returns((_laura, CodigosRol.PersonalClinico));
        _almacen.GuardarAsync(Tenant, _laura.Id, Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => $"{Tenant}/{_laura.Id}/{c.ArgAt<string>(2)}");
        _caso = new GestionFotoPerfil(_repositorio, _almacen, _auditoria, _contexto);
    }

    /// <summary>Cabecera PNG (firma + IHDR) con el tamaño pedido.</summary>
    private static byte[] Png(int ancho, int alto)
    {
        byte[] d = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0, 0];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), ancho);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(20), alto);
        return d;
    }

    private static VersionFoto Version(byte[] datos) => new(new MemoryStream(datos), datos.Length);

    private Task Guardar(byte[]? miniatura = null, byte[]? vista = null) =>
        _caso.GuardarAsync(Version(miniatura ?? Png(128, 128)), Version(vista ?? Png(400, 512)), CancellationToken.None);

    [Fact]
    public async Task Guardar_EnLaCarpetaDelUsuario_YRegistraLaBitacora()
    {
        await Guardar();

        Assert.NotNull(_laura.FotoRuta);
        Assert.StartsWith($"{Tenant}/{_laura.Id}/perfil-", _laura.FotoRuta, StringComparison.Ordinal);
        Assert.EndsWith(".png", _laura.FotoRuta, StringComparison.Ordinal);
        var nombre = Path.GetFileName(_laura.FotoRuta);
        await _almacen.Received(1).GuardarAsync(Tenant, _laura.Id, nombre, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _almacen.Received(1).GuardarAsync(Tenant, _laura.Id, nombre.Replace(".png", "-128.png", StringComparison.Ordinal), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
        await _auditoria.Received(1).RegistrarAsync(
            _laura.Id,
            AccionesAuditoria.Actualizar,
            GestionFotoPerfil.Entidad,
            _laura.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["foto_perfil"] == "cambiada"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cambiarla_BorraLosArchivosDeLaAnterior()
    {
        await Guardar();
        var anterior = _laura.FotoRuta!;

        await Guardar();

        Assert.NotEqual(anterior, _laura.FotoRuta);
        _almacen.Received(1).Eliminar(anterior);
        _almacen.Received(1).Eliminar(anterior.Replace(".png", "-128.png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100, 100, 400, 512, "miniatura")]
    [InlineData(128, 128, 800, 600, "vista")]
    public async Task TamanosQueNoCumplen_Responde400SinGuardar(int mAncho, int mAlto, int vAncho, int vAlto, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(Png(mAncho, mAlto), Png(vAncho, vAlto)));

        Assert.True(error.Errores.ContainsKey(campo));
        await _almacen.DidNotReceiveWithAnyArgs().GuardarAsync(default, default, default!, default!, default);
        Assert.Null(_laura.FotoRuta);
    }

    [Fact]
    public async Task NoEsUnaImagen_LoDice()
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(vista: [1, 2, 3, 4]));

        Assert.Equal([GestionFotoEquipo.FormatoNoPermitido], error.Errores["vista"]);
    }

    [Fact]
    public async Task SiNoSePuedeGuardarElUsuario_BorraLosArchivosNuevos()
    {
        _repositorio.GuardarAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("sin base"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Guardar());

        _almacen.ReceivedWithAnyArgs(2).Eliminar(default!);
    }

    [Fact]
    public async Task Quitar_VuelveALasIniciales_YBorraLosArchivos()
    {
        await Guardar();
        var ruta = _laura.FotoRuta!;

        await _caso.QuitarAsync(CancellationToken.None);

        Assert.Null(_laura.FotoRuta);
        _almacen.Received(1).Eliminar(ruta);
        await _auditoria.Received(1).RegistrarAsync(
            _laura.Id, AccionesAuditoria.Actualizar, GestionFotoPerfil.Entidad, _laura.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["foto_perfil"] == "retirada"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFoto_ConsultarOQuitar_Responde404()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.DescargarAsync(512, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.QuitarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Descargar_LaMiniaturaOLaVista_ConSuTipo()
    {
        await Guardar();
        var ruta = _laura.FotoRuta!;
        _almacen.Abrir(ruta.Replace(".png", "-128.png", StringComparison.Ordinal)).Returns(new MemoryStream([1]));

        var archivo = await _caso.DescargarAsync(128, CancellationToken.None);

        Assert.Equal(("image/png", "perfil.png"), (archivo.TipoMime, archivo.Nombre));
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.DescargarAsync(64, CancellationToken.None));
    }

    [Theory]
    [InlineData("a/perfil-1.webp", "image/webp")]
    [InlineData("a/perfil-1.jpg", "image/jpeg")]
    [InlineData("a/perfil-1.PNG", "image/png")]
    public void TipoMime_SegunLaExtension(string ruta, string mime) => Assert.Equal(mime, GestionFotoPerfil.TipoMime(ruta));
}
