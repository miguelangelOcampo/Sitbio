using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>D-06: ninguna opción del menú de los siete roles llega a «Pantalla en construcción».</summary>
public class MenuFuncionesFuturasTests
{
    public static TheoryData<string> Roles => new() { "AC-01", "AC-02", "AC-03", "AC-04", "AC-05", "AC-06", "AC-07" };

    [Theory]
    [MemberData(nameof(Roles))]
    public void CadaOpcionDelMenu_AbreUnaPantallaOElEstadoProximaVersion(string rol)
    {
        var fabrica = new FabricaPantallas(new ServiciosSimulados(rol));

        foreach (var opcion in MenuPorRol.Para(rol).SelectMany(m => m.Opciones))
        {
            Assert.True(fabrica.Crear(opcion.Id) is not null, $"{rol}: «{opcion.Etiqueta}» ({opcion.Id}) llega a «Pantalla en construcción».");
        }
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void ElMarco_NuncaQuedaEnConstruccionAlRecorrerElMenu(string rol)
    {
        var shell = new ShellViewModel(rol, new FabricaPantallas(new ServiciosSimulados(rol)));

        foreach (var opcion in shell.Modulos.SelectMany(m => m.Opciones))
        {
            shell.NavegarCommand.Execute(opcion);

            Assert.False(shell.EnConstruccion, $"{rol}: {opcion.Opcion.Id} en construcción");
        }
    }

    [Fact]
    public void ImportarInventario_AbreLaPantallaDeImportacion_YYaNoEsProximaVersion()
    {
        Assert.IsType<ImportacionViewModel>(new FabricaPantallas(new ServiciosSimulados("AC-01")).Crear("UI-008"));
        Assert.False(ProximaVersionViewModel.Opciones.ContainsKey("UI-008"));
    }

    [Fact]
    public void HistorialGeneral_MuestraDisponibleEnUnaProximaVersion()
    {
        var pantalla = Assert.IsType<ProximaVersionViewModel>(new FabricaPantallas(new ServiciosSimulados("AC-01")).Crear("HIST"));

        Assert.Equal("Disponible en una próxima versión", ProximaVersionViewModel.Titulo);
        Assert.Contains("historial", pantalla.Detalle);
    }

    /// <summary>
    /// Contenedor mínimo para la fábrica real: cada servicio del cliente es un proxy cuyas llamadas
    /// asíncronas fallan como sin conexión (la pantalla lo muestra, sin red), y los ViewModels se crean
    /// con sus dependencias como en App.xaml.cs.
    /// </summary>
    private sealed class ServiciosSimulados : IServiceProvider
    {
        private readonly ConcurrentDictionary<Type, object> _unicos = new();

        public ServiciosSimulados(string rol)
        {
            var sesion = new SesionActual();
            sesion.Iniciar(new SesionUsuario("a.b.c", DateTimeOffset.UtcNow.AddMinutes(30), rol, false));
            _unicos[typeof(SesionActual)] = sesion;
            _unicos[typeof(TimeProvider)] = TimeProvider.System;
        }

        public object? GetService(Type tipo)
        {
            if (_unicos.TryGetValue(tipo, out var unico))
            {
                return unico;
            }

            if (tipo.IsInterface && tipo.Namespace == typeof(SesionActual).Namespace)
            {
                return _unicos.GetOrAdd(tipo, ServicioSinConexion.Crear);
            }

            return tipo.IsClass && !tipo.IsAbstract && tipo.Namespace == typeof(ShellViewModel).Namespace
                ? ActivatorUtilities.CreateInstance(this, tipo)
                : null;
        }
    }

    /// <summary>Implementa cualquier interfaz de servicio: cada tarea falla con «sin conexión» (ErrorApi).</summary>
    public class ServicioSinConexion : DispatchProxy
    {
        public static object Crear(Type interfaz) => DispatchProxy.Create(interfaz, typeof(ServicioSinConexion));

        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
        {
            var retorno = metodo?.ReturnType ?? typeof(void);
            var error = new ErrorApi(null, "Sin conexión con el servidor.", new Dictionary<string, string>(), sinConexion: true);
            if (retorno == typeof(Task))
            {
                return Task.FromException(error);
            }

            if (retorno.IsGenericType && retorno.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
                    .MakeGenericMethod(retorno.GetGenericArguments()[0])
                    .Invoke(null, [error]);
            }

            return retorno.IsValueType && retorno != typeof(void) ? Activator.CreateInstance(retorno) : null;
        }
    }
}
