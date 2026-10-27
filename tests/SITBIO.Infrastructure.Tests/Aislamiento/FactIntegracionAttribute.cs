namespace Sitbio.Infrastructure.Tests.Aislamiento;

/// <summary>
/// Prueba contra una base real (docker compose). Se omite si no está definida la
/// variable de entorno con la cadena de conexión, como ocurre en el CI.
/// </summary>
public sealed class FactIntegracionAttribute : FactAttribute
{
    public const string VariablePostgres = "SITBIO_PRUEBAS_POSTGRES";
    public const string VariableMongo = "SITBIO_PRUEBAS_MONGO";

    public FactIntegracionAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Defina {variable} para ejecutar esta prueba de integración.";
        }
    }
}
