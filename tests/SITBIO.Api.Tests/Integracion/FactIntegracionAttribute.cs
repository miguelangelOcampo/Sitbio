namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Prueba contra PostgreSQL real (docker compose) y, si se pide, también contra MongoDB.
/// Se omite si no están definidas las variables SITBIO_PRUEBAS_POSTGRES (y
/// SITBIO_PRUEBAS_MONGO), como ocurre en el CI.
/// </summary>
public sealed class FactIntegracionAttribute : FactAttribute
{
    public const string VariablePostgres = "SITBIO_PRUEBAS_POSTGRES";
    public const string VariableMongo = "SITBIO_PRUEBAS_MONGO";

    public FactIntegracionAttribute()
        : this(requiereMongo: false)
    {
    }

    public FactIntegracionAttribute(bool requiereMongo)
    {
        foreach (var variable in requiereMongo ? new[] { VariablePostgres, VariableMongo } : [VariablePostgres])
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            {
                Skip = $"Defina {variable} para ejecutar esta prueba de integración.";
                return;
            }
        }
    }
}
