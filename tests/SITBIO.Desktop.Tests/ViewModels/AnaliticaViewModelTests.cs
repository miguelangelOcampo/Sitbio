using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sitbio.Desktop.Controls;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>UI-023 Analítica (HU-008, HU-009) con el diseño del prototipo (I-05).</summary>
public sealed class AnaliticaViewModelTests
{
    private readonly IServicioAnalitica _servicio = Substitute.For<IServicioAnalitica>();
    private readonly IDialogoArchivos _dialogo = Substitute.For<IDialogoArchivos>();

    private static readonly string[] Nombres = ["Estables", "Seguimiento", "Atención lenta", "Alto costo", "Muchas fallas", "Alto costo y muchas fallas"];

    private static List<EquipoAgrupado> Equipos(int cantidad, int k) => Enumerable.Range(0, cantidad)
        .Select(i => new EquipoAgrupado(Guid.NewGuid(), $"BIO-{i}", i % k, new Caracteristicas(4, 0.5, 1_000_000 * (i + 1), 10 * (i + 1)), "Monitor"))
        .ToList();

    private static AgrupacionAnalitica Agrupacion(int equipos, int k) => new(
        k,
        20.0 / k,
        Enumerable.Range(0, k).Select(g => new GrupoAnalitica(g, 1, new Caracteristicas(2, 0.25, 500_000, 30), Nombres[Math.Max(0, 6 - k + g)])).ToList(),
        Equipos(equipos, k));

    /// <summary>Resultado con todas las agrupaciones de 2 a min(6, equipos), como lo guarda el lote desde I-03.</summary>
    private static Agrupamiento Resultado(int equipos, int k = 3, bool todas = true)
    {
        var principal = Agrupacion(equipos, k);
        var lista = todas ? Enumerable.Range(2, Math.Min(6, equipos) - 1).Select(n => Agrupacion(equipos, n)).ToList() : null;
        return new Agrupamiento(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, equipos * 2, k, principal.Grupos, principal.Equipos, lista);
    }

    private AnaliticaViewModel Vm(string? rol = null)
    {
        SesionActual? sesion = null;
        if (rol is not null)
        {
            sesion = new SesionActual();
            sesion.Iniciar(new SesionUsuario("token", DateTimeOffset.UtcNow.AddHours(1), rol, false));
        }

        return new AnaliticaViewModel(_servicio, sesion, _dialogo) { IntervaloConsulta = TimeSpan.Zero };
    }

    [Fact]
    public async Task Carga_los_puntos_con_los_valores_de_los_ejes_y_las_tarjetas_por_grupo()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(4));
        var vm = Vm();

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.True(vm.HayDatos && vm.HayAgrupacion);
        Assert.Equal(3, vm.GruposSolicitados);
        Assert.Equal(("Fallas (correctivos)", "Costo confirmado (M COP)"), (vm.TituloEjeX, vm.TituloEjeY));
        Assert.Equal(new PuntoGrafico(2, 2, 1, "BIO-1", "Monitor", vm.Agrupacion!.Equipos[1]), vm.Puntos[1]);
        Assert.Equal(["Alto costo", "Muchas fallas", "Alto costo y muchas fallas"], vm.SeriesGrupos.Select(s => s.Nombre));
        Assert.Equal(["Brush.Primary", "Brush.Warning", "Brush.Danger"], vm.SeriesGrupos.Select(s => s.ClaveColor));

        // Tarjetas del grupo con más fallas al de menos.
        Assert.Equal(["Alto costo y muchas fallas", "Muchas fallas", "Alto costo"], vm.Resumen.Select(f => f.Nombre));
        Assert.Equal(("1 equipo", "0,5", "30 min"), (vm.Resumen[0].Equipos, vm.Resumen[0].Fallas, vm.Resumen[0].Tiempo));
        Assert.Contains("«Alto costo y muchas fallas» reúne los equipos con más fallas", vm.ComoLeerlo);
        Assert.Contains("Inercia del modelo: 6,67", vm.ComoLeerlo);
        Assert.Null(vm.AvisoGrupos);
    }

    [Fact]
    public async Task Cambiar_el_numero_de_grupos_y_los_ejes_no_consulta_de_nuevo()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(8));
        var vm = Vm();
        await vm.CargarCommand.ExecuteAsync(null);

        vm.GruposSolicitados = 6;
        vm.EjeX = AnaliticaViewModel.OpcionesEje.Single(v => v.Clave == "tiempo");
        vm.EjeY = AnaliticaViewModel.OpcionesEje.Single(v => v.Clave == "correctivos");

        Assert.Equal(6, vm.SeriesGrupos.Count);
        Assert.Equal("Brush.Danger", vm.SeriesGrupos[^1].ClaveColor);
        Assert.Equal((80d, 50d), (vm.Puntos[^1].X, vm.Puntos[^1].Y));
        Assert.Equal("Proporción de correctivos frente a tiempo de atención", vm.TituloGrafico);
        await _servicio.Received(1).AgrupamientosAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mas_grupos_que_equipos_pide_reducirlos()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(3, k: 2));
        var vm = Vm();
        await vm.CargarCommand.ExecuteAsync(null);

        vm.GruposSolicitados = 5;

        Assert.Equal("Reduce el número de grupos", vm.AvisoGrupos?.Titulo);
        Assert.False(vm.HayAgrupacion);
    }

    [Fact]
    public async Task Un_resultado_anterior_sin_esa_agrupacion_ofrece_recalcular_sin_mencionar_Python()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(6, todas: false));
        var ingeniero = Vm("AC-02");
        await ingeniero.CargarCommand.ExecuteAsync(null);

        ingeniero.GruposSolicitados = 4;

        Assert.Contains("Presiona «Recalcular»", ingeniero.AvisoGrupos?.Mensaje);
        Assert.DoesNotContain("python", ingeniero.AvisoGrupos!.Mensaje, StringComparison.OrdinalIgnoreCase);

        var gerente = Vm("AC-05");
        await gerente.CargarCommand.ExecuteAsync(null);
        gerente.GruposSolicitados = 4;
        Assert.Contains("ingeniero biomédico", gerente.AvisoGrupos?.Mensaje);
    }

    [Fact]
    public async Task Sin_resultados_es_informativo_y_el_ingeniero_puede_recalcular()
    {
        _servicio.AgrupamientosAsync(default).ThrowsAsyncForAnyArgs(new ErrorApi("ANA-001", "Aún no hay resultados", new Dictionary<string, string>()));
        var vm = Vm("AC-02");

        await vm.CargarCommand.ExecuteAsync(null);

        Assert.False(vm.HayDatos);
        Assert.Equal("ANA-001", vm.Aviso?.Codigo);
        Assert.False(vm.Aviso!.EsError);
        Assert.Contains("«Recalcular»", vm.Aviso.Mensaje);
        Assert.True(vm.PuedeRecalcular && vm.RecalcularCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("AC-05")]
    [InlineData("AC-06")]
    [InlineData(null)]
    public void Solo_el_ingeniero_recalcula(string? rol) =>
        Assert.False(Vm(rol).RecalcularCommand.CanExecute(null));

    [Fact]
    public async Task Recalcular_espera_a_que_termine_y_recarga()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(4));
        _servicio.RecalcularAsync(default).ReturnsForAnyArgs(new EstadoRecalculo(EstadoRecalculo.EnCurso, DateTimeOffset.UtcNow, null, null, null));
        _servicio.EstadoRecalculoAsync(default).ReturnsForAnyArgs(
            new EstadoRecalculo(EstadoRecalculo.EnCurso, DateTimeOffset.UtcNow, null, null, null),
            new EstadoRecalculo(EstadoRecalculo.Terminado, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));
        var vm = Vm("AC-02");

        await vm.RecalcularCommand.ExecuteAsync(null);

        await _servicio.Received(2).EstadoRecalculoAsync(Arg.Any<CancellationToken>());
        Assert.True(vm.HayDatos);
        Assert.Equal("Modelo recalculado", vm.Confirmacion?.Titulo);
        Assert.False(vm.Recalculando);
    }

    [Fact]
    public async Task Recalcular_si_falla_lo_avisa_y_si_ya_hay_uno_en_curso_lo_espera()
    {
        _servicio.RecalcularAsync(default).ThrowsAsyncForAnyArgs(new ErrorApi("ANA-002", "Ya se está recalculando", new Dictionary<string, string>()));
        _servicio.EstadoRecalculoAsync(default).ReturnsForAnyArgs(new EstadoRecalculo(EstadoRecalculo.Fallido, null, DateTimeOffset.UtcNow, "No pudimos recalcular la analítica.", null));
        var vm = Vm("AC-02");

        await vm.RecalcularCommand.ExecuteAsync(null);

        Assert.Equal(("No pudimos recalcular", "No pudimos recalcular la analítica."), (vm.Aviso?.Titulo, vm.Aviso?.Mensaje));
        await _servicio.DidNotReceiveWithAnyArgs().AgrupamientosAsync(default);
    }

    [Fact]
    public async Task Clic_en_un_punto_abre_la_hoja_de_vida_solo_si_el_rol_tiene_equipos()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(4));
        var ingeniero = Vm("AC-02");
        await ingeniero.CargarCommand.ExecuteAsync(null);
        SolicitudNavegacion? solicitud = null;
        ingeniero.NavegacionSolicitada += (_, s) => solicitud = s;

        ingeniero.AbrirEquipoCommand.Execute(ingeniero.Puntos[0]);

        var equipo = (EquipoAgrupado)ingeniero.Puntos[0].Dato!;
        Assert.Equal(new SolicitudNavegacion(EquiposViewModel.IdPantalla, EquiposViewModel.ParametroDetalle(equipo.EquipoId, "BIO-0")), solicitud);
        Assert.Contains("haz clic para abrir su hoja de vida", ingeniero.ComoLeerlo);

        var directiva = Vm("AC-06");
        await directiva.CargarCommand.ExecuteAsync(null);
        var navego = false;
        directiva.NavegacionSolicitada += (_, _) => navego = true;
        directiva.AbrirEquipoCommand.Execute(directiva.Puntos[0]);
        Assert.False(navego);
    }

    [Fact]
    public async Task Exportar_guarda_un_csv_con_el_grupo_de_cada_equipo()
    {
        _servicio.AgrupamientosAsync(default).ReturnsForAnyArgs(Resultado(4));
        _dialogo.ElegirDestino("agrupamiento_3_grupos.csv").Returns(@"C:\tmp\agrupamiento.csv");
        byte[]? guardado = null;
        _dialogo.Guardar(Arg.Any<string>(), Arg.Do<byte[]>(b => guardado = b));
        var vm = Vm("AC-05");
        await vm.CargarCommand.ExecuteAsync(null);

        vm.ExportarCommand.Execute(null);

        var lineas = Encoding.UTF8.GetString(guardado!).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Placa;Tipo;Grupo;Fallas", lineas[0]);
        Assert.Equal(5, lineas.Length);
        Assert.Equal("BIO-2;Monitor;Alto costo y muchas fallas;2;4;50;3000000;30", lineas[1]);
        Assert.Equal("Exportación lista", vm.Confirmacion?.Titulo);
    }
}
