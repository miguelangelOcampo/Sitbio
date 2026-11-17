using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Tests.Equipos;

/// <summary>RF-031 (CA-HU-015-03; RN-009): reglas de la fotografía del equipo sin base de datos.</summary>
public class GestionFotoEquipoTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    private readonly IRepositorioDocumentos _repositorio = Substitute.For<IRepositorioDocumentos>();
    private readonly IAlmacenDocumentos _almacen = Substitute.For<IAlmacenDocumentos>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly EquipoBiomedico _equipo = new(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SN-001", "PL-001", "Dräger", "Fabius plus", DateTimeOffset.UtcNow);
    private readonly GestionFotoEquipo _caso;

    public GestionFotoEquipoTests()
    {
        _contexto.TenantId.Returns(Tenant);
        _contexto.UsuarioId.Returns(Usuario);
        _repositorio.ExisteEquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(true);
        _repositorio.ObtenerEquipoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(_equipo);
        _repositorio.CapacidadAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadAlmacenamiento("Clínica", 25, 0));
        _almacen.GuardarAsync(Tenant, _equipo.Id, Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => $"{Tenant}/{_equipo.Id}/{c.ArgAt<string>(2)}");
        _caso = new GestionFotoEquipo(_repositorio, _almacen, _auditoria, _contexto, TimeProvider.System);
    }

    /// <summary>Cabecera WebP (VP8X) de 30 bytes con el tamaño pedido.</summary>
    private static byte[] Webp(int ancho, int alto)
    {
        var d = new byte[30];
        "RIFF"u8.CopyTo(d);
        "WEBPVP8X"u8.CopyTo(d.AsSpan(8));
        d[24] = (byte)(ancho - 1);
        d[25] = (byte)((ancho - 1) >> 8);
        d[27] = (byte)(alto - 1);
        d[28] = (byte)((alto - 1) >> 8);
        return d;
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

    private Task<FotoEquipoDto> Guardar(byte[]? miniatura = null, byte[]? vista = null, bool aviso = true) =>
        _caso.GuardarAsync(
            _equipo.Id,
            Version(miniatura ?? Webp(128, 128)),
            Version(vista ?? Webp(512, 384)),
            aviso,
            CancellationToken.None);

    [Fact]
    public async Task CA_HU_015_03_GuardaLasDosVersiones_AsignaLaFotoYRegistraElAviso()
    {
        var foto = await Guardar();

        Assert.Equal(("image/webp", 60L), (foto.TipoMime, foto.TamanoBytes));
        Assert.Equal(foto.DocumentoId, _equipo.FotoDocumentoId);
        await _almacen.Received(1).GuardarAsync(Tenant, _equipo.Id, $"{foto.DocumentoId:N}.webp", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _almacen.Received(1).GuardarAsync(Tenant, _equipo.Id, $"{foto.DocumentoId:N}-128.webp", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        _repositorio.Received(1).Agregar(Arg.Is<DocumentoEquipo>(d =>
            d.TipoDocumento == TiposDocumento.Fotografia && d.NombreArchivo == "Fotografía del equipo.webp" && d.RutaAlmacenamiento == $"{Tenant}/{_equipo.Id}/{d.Id:N}.webp"));
        await _auditoria.Received(1).RegistrarAsync(
            Usuario,
            AccionesAuditoria.Crear,
            GestionFotoEquipo.Entidad,
            foto.DocumentoId.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["aviso_datos_personales"] == "aceptado" && d["tamano_bytes"] == "60"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reemplazar_DesactivaLaFotoAnterior()
    {
        var anterior = new DocumentoEquipo(Tenant, _equipo.Id, Usuario, TiposDocumento.Fotografia, "Fotografía del equipo.png", "image/png", 10, DateTimeOffset.UtcNow);
        _repositorio.ObtenerFotoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(anterior);

        var foto = await Guardar();

        Assert.False(anterior.EstadoActivo);
        Assert.NotEqual(anterior.Id, foto.DocumentoId);
    }

    [Fact]
    public async Task CA_HU_015_03_ImagenSinReducirFormatosDistintosOSinAviso_SonErroresDeValidacion()
    {
        var original = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(vista: Webp(1024, 768)));
        var miniatura = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(miniatura: Png(128, 96)));
        var mezcla = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(miniatura: Png(128, 128)));
        var sinAviso = await Assert.ThrowsAsync<ErrorValidacion>(() => Guardar(aviso: false));

        Assert.Contains("vista", original.Errores.Keys);
        Assert.Equal("La miniatura debe medir 128 × 128 px.", miniatura.Errores["miniatura"][0]);
        Assert.Equal("La miniatura y la vista deben tener el mismo formato.", mezcla.Errores["miniatura"][0]);
        Assert.Equal(GestionFotoEquipo.AvisoDatosPersonales, sinAviso.Errores["aviso"][0]);
        await _almacen.DidNotReceiveWithAnyArgs().GuardarAsync(default, default, default!, default!, default);
        Assert.Null(_equipo.FotoDocumentoId);
    }

    [Fact]
    public async Task RN_009_SinEspacioEnElPlan_EsConflictoMsg019()
    {
        _repositorio.CapacidadAsync(Arg.Any<CancellationToken>()).Returns(new CapacidadAlmacenamiento("Consultorio", 5, 5 * CapacidadAlmacenamiento.BytesPorGb - 59));

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Guardar());

        Assert.Equal(GestionDocumentos.CodigoLimitePlan, error.Codigo);
        Assert.Contains("plan Consultorio (5 GB)", error.Message);
        await _almacen.DidNotReceiveWithAnyArgs().GuardarAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task SiFallaElRegistro_BorraLosDosArchivos()
    {
        _repositorio.GuardarAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("sin conexión"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Guardar());

        _almacen.Received(2).Eliminar(Arg.Any<string>());
        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Descargar_EntregaLaMiniaturaOLaVistaYRechazaOtroTamano()
    {
        var foto = new DocumentoEquipo(Tenant, _equipo.Id, Usuario, TiposDocumento.Fotografia, "Fotografía del equipo.webp", "image/webp", 60, DateTimeOffset.UtcNow);
        foto.UbicarEn($"{Tenant}/{_equipo.Id}/{foto.Id:N}.webp");
        _repositorio.ObtenerFotoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(foto);
        _almacen.Abrir(Arg.Any<string>()).Returns(_ => new MemoryStream([1]));

        await _caso.DescargarAsync(_equipo.Id, 128, CancellationToken.None);
        await _caso.DescargarAsync(_equipo.Id, 512, CancellationToken.None);

        _almacen.Received(1).Abrir($"{Tenant}/{_equipo.Id}/{foto.Id:N}-128.webp");
        _almacen.Received(1).Abrir($"{Tenant}/{_equipo.Id}/{foto.Id:N}.webp");
        await Assert.ThrowsAsync<ErrorValidacion>(() => _caso.DescargarAsync(_equipo.Id, 256, CancellationToken.None));
    }

    [Fact]
    public async Task Quitar_DesactivaElDocumentoYDejaElEquipoSinFoto()
    {
        var foto = new DocumentoEquipo(Tenant, _equipo.Id, Usuario, TiposDocumento.Fotografia, "Fotografía del equipo.webp", "image/webp", 60, DateTimeOffset.UtcNow);
        _equipo.AsignarFoto(foto.Id);
        _repositorio.ObtenerFotoAsync(_equipo.Id, Arg.Any<CancellationToken>()).Returns(foto);

        await _caso.QuitarAsync(_equipo.Id, CancellationToken.None);

        Assert.False(foto.EstadoActivo);
        Assert.Null(_equipo.FotoDocumentoId);
        await _auditoria.Received(1).RegistrarAsync(Usuario, AccionesAuditoria.Eliminar, GestionFotoEquipo.Entidad, foto.Id.ToString(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFotoOEquipoDeOtraInstitucion_EsNoEncontrado()
    {
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.QuitarAsync(_equipo.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.DescargarAsync(_equipo.Id, 128, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.DescargarAsync(Guid.NewGuid(), 128, CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.GuardarAsync(Guid.NewGuid(), null, null, true, CancellationToken.None));
    }
}
