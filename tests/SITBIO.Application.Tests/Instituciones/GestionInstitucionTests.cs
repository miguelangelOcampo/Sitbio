using System.Reflection;
using NSubstitute;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;

namespace Sitbio.Application.Tests.Instituciones;

/// <summary>Configuración de la institución y consumo del plan (HU-024, RF-024, RN-009).</summary>
public class GestionInstitucionTests
{
    private static readonly Guid Administrador = Guid.NewGuid();
    private const long Gb = 1024L * 1024 * 1024;

    private readonly IRepositorioInstitucion _repositorio = Substitute.For<IRepositorioInstitucion>();
    private readonly IRegistroAuditoria _auditoria = Substitute.For<IRegistroAuditoria>();
    private readonly IContextoUsuario _contexto = Substitute.For<IContextoUsuario>();
    private readonly Tenant _tenant = TenantDePrueba("Consultorio", usuarios: 3, equipos: 300, gb: 5);
    private readonly GestionInstitucion _caso;

    public GestionInstitucionTests()
    {
        _contexto.UsuarioId.Returns(Administrador);
        _contexto.TenantId.Returns(_tenant.Id);
        _repositorio.ObtenerAsync(Arg.Any<CancellationToken>()).Returns(_tenant);
        Uso(usuarios: 1, equipos: 120, bytes: Gb);
        _caso = new GestionInstitucion(_repositorio, _auditoria, _contexto);
    }

    /// <summary>Tenant como lo deja el aprovisionamiento (ST-10): plan, límites y vigencia fijados.</summary>
    private static Tenant TenantDePrueba(string plan, int usuarios, int equipos, int gb)
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        void Fijar(string propiedad, object? valor) =>
            typeof(Tenant).GetProperty(propiedad)!.SetValue(tenant, valor, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
        Fijar(nameof(Tenant.Id), Guid.NewGuid());
        Fijar(nameof(Tenant.Plan), plan);
        Fijar(nameof(Tenant.MaxUsuariosGestion), usuarios);
        Fijar(nameof(Tenant.MaxEquipos), equipos);
        Fijar(nameof(Tenant.MaxAlmacenamientoGb), gb);
        Fijar(nameof(Tenant.VigenteHasta), (DateOnly?)new DateOnly(2027, 6, 30));
        tenant.ActualizarDatos("Clínica San Rafael", "900.123.456-7", 15);
        return tenant;
    }

    private void Uso(int usuarios, int equipos, long bytes) =>
        _repositorio.UsoAsync(Arg.Any<CancellationToken>()).Returns(new UsoPlan(usuarios, equipos, bytes));

    private Task<TenantDto> Actualizar(string? nombre = "Clínica San Rafael", string? nit = "900.123.456-7", int? dias = 15) =>
        _caso.ActualizarAsync(new DatosInstitucion(nombre, nit, dias), CancellationToken.None);

    // ---------- CA-HU-024-03: plan, vigencia y consumo ----------

    [Fact]
    public async Task CA_HU_024_03_Obtener_MuestraElPlanSuVigenciaYElConsumo()
    {
        var dto = await _caso.ObtenerAsync(CancellationToken.None);

        Assert.Equal(("Clínica San Rafael", "900.123.456-7", 15), (dto.Nombre, dto.Nit, dto.DiasAnticipacionAlerta));
        Assert.Equal(("Consultorio", new DateOnly(2027, 6, 30)), (dto.Plan, dto.VigenteHasta));
        Assert.Equal(new MedidorPlan(1, 3, 33, false, false), dto.Consumo.UsuariosGestion);
        Assert.Equal(new MedidorPlan(120, 300, 40, false, false), dto.Consumo.EquiposActivos);
        Assert.Equal(new MedidorPlan(1m, 5, 20, false, false), dto.Consumo.AlmacenamientoGb);
        Assert.False(dto.Consumo.HayAlerta);
    }

    [Theory]
    [InlineData(239, false)]
    [InlineData(240, true)]
    [InlineData(299, true)]
    public async Task CA_HU_024_03_AvisaDesdeEl80PorCiento(int equipos, bool alerta)
    {
        Uso(usuarios: 1, equipos: equipos, bytes: 0);

        var dto = await _caso.ObtenerAsync(CancellationToken.None);

        Assert.Equal(alerta, dto.Consumo.EquiposActivos.Alerta);
        Assert.Equal(alerta, dto.Consumo.HayAlerta);
        Assert.False(dto.Consumo.EquiposActivos.LimiteAlcanzado);
    }

    [Fact]
    public async Task LimiteAlcanzado_SeMarcaParaBloquearCrear()
    {
        Uso(usuarios: 3, equipos: 10, bytes: 0);

        var dto = await _caso.ObtenerAsync(CancellationToken.None);

        Assert.Equal(new MedidorPlan(3, 3, 100, true, true), dto.Consumo.UsuariosGestion);
    }

    [Fact]
    public async Task Almacenamiento_SeExpresaEnGbConDosDecimales()
    {
        Uso(usuarios: 0, equipos: 0, bytes: (long)(4.5 * Gb) + 1234);

        var dto = await _caso.ObtenerAsync(CancellationToken.None);

        Assert.Equal(4.5m, dto.Consumo.AlmacenamientoGb.Usados);
        Assert.Equal(90, dto.Consumo.AlmacenamientoGb.Porcentaje);
        Assert.True(dto.Consumo.AlmacenamientoGb.Alerta);
    }

    // ---------- CA-HU-024-01: datos y anticipación de las alertas ----------

    [Fact]
    public async Task CA_HU_024_01_Actualizar_GuardaLosDiasYLosDevuelve()
    {
        var dto = await Actualizar(dias: 30);

        Assert.Equal(30, dto.DiasAnticipacionAlerta);
        Assert.Equal(30, _tenant.DiasAnticipacionAlerta);
        await _repositorio.Received(1).GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Actualizar_QuedaEnLaBitacoraConLosCamposCambiados()
    {
        await Actualizar(nombre: "Clínica San Rafael IPS", dias: 30);

        await _auditoria.Received(1).RegistrarAsync(
            Administrador,
            AccionesAuditoria.Actualizar,
            GestionInstitucion.Entidad,
            _tenant.Id.ToString(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["campos"] == "nombre,dias_anticipacion_alerta" && d["dias_anticipacion_alerta"] == "30"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinCambios_NoEscribeEnLaBitacora()
    {
        await Actualizar();

        await _auditoria.DidNotReceiveWithAnyArgs().RegistrarAsync(default, default!, default!, default, default, default);
    }

    [Theory]
    [InlineData("IPS", "900.123.456-7", 15, "nombre")]
    [InlineData("Clínica San Rafael", "900123456", 15, "nit")]
    [InlineData("Clínica San Rafael", "900.123.456-7", 120, "diasAnticipacionAlerta")]
    [InlineData("Clínica San Rafael", "900.123.456-7", null, "diasAnticipacionAlerta")]
    public async Task DatoInvalido_Responde400EnSuCampoSinGuardar(string nombre, string nit, int? dias, string campo)
    {
        var error = await Assert.ThrowsAsync<ErrorValidacion>(() => Actualizar(nombre, nit, dias));

        Assert.Equal([campo], error.Errores.Keys);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NitDeOtraInstitucion_Responde409Ten001()
    {
        _repositorio.NitEnUsoAsync("800.111.222-3", Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ErrorConflicto>(() => Actualizar(nit: "800.111.222-3"));

        Assert.Equal(GestionInstitucion.CodigoNitEnUso, error.Codigo);
        await _repositorio.DidNotReceive().GuardarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElMismoNit_NoSeConsultaComoDuplicado()
    {
        await Actualizar(dias: 20);

        await _repositorio.DidNotReceiveWithAnyArgs().NitEnUsoAsync(default!, default);
    }

    [Fact]
    public async Task InstitucionInexistente_Responde404()
    {
        _repositorio.ObtenerAsync(Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => _caso.ObtenerAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ErrorNoEncontrado>(() => Actualizar());
    }
}
