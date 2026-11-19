using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Sitbio.Desktop.Services;
using Sitbio.Desktop.ViewModels;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>
/// WPF pide el texto (ToString) de los elementos de tablas y listas. El ToString generado de un record imprime
/// todas sus propiedades públicas; si dos records se apuntan entre sí, la recursión agota la pila y la
/// aplicación se cierra sin aviso (InsufficientExecutionStackException).
/// </summary>
public class TextoDeRecordsTests
{
    [Fact]
    public void FilaPlan_DaSuTextoSinRecursionInfinita()
    {
        var hoy = new DateOnly(2026, 11, 18);
        var plan = new PlanMantenimiento(Guid.NewGuid(), Guid.NewGuid(), "HSV-EQ-0412", "Monitor de signos vitales", "UCI adultos",
            "Mantenimiento preventivo", 30, null, hoy.AddDays(20), EstadosPlan.Activo, false, DateTimeOffset.Now);

        var texto = new FilaPlan(plan, hoy).ToString();

        Assert.Contains($"Suspender = CambioEstadoPlan {{ Plan = {plan.Id}, Estado = {EstadosPlan.Suspendido} }}", texto);
    }

    [Fact]
    public void NingunRecordDelCliente_FormaUnCicloAlImprimirse()
    {
        var records = typeof(Aviso).Assembly.GetTypes()
            .Where(t => t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(StringBuilder)]) is not null)
            .ToHashSet();

        // Un record con PrintMembers propio decide qué imprime; solo se siguen los que genera el compilador.
        bool Generado(Type t) => t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(StringBuilder)])!
            .GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

        IEnumerable<Type> Vecinos(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType)
            .Where(records.Contains);

        var ciclos = new List<string>();
        foreach (var inicio in records.Where(Generado))
        {
            var pila = new Stack<(Type Tipo, string Ruta)>([(inicio, inicio.Name)]);
            var vistos = new HashSet<Type>();
            while (pila.Count > 0)
            {
                var (tipo, ruta) = pila.Pop();
                foreach (var vecino in Vecinos(tipo))
                {
                    if (vecino == inicio)
                    {
                        ciclos.Add($"{ruta} → {vecino.Name}");
                    }
                    else if (Generado(vecino) && vistos.Add(vecino))
                    {
                        pila.Push((vecino, $"{ruta} → {vecino.Name}"));
                    }
                }
            }
        }

        Assert.Empty(ciclos);
    }
}
