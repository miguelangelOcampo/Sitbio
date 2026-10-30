namespace Sitbio.Api.Tests.Integracion;

/// <summary>
/// Prueba contra PostgreSQL real (docker compose). Se omite si no está definida la
/// variable SITBIO_PRUEBAS_POSTGRES, como ocurre en el CI.
/// </summary>
public sealed class FactIntegracionAttribute : FactAttribute
{
    public const string VariablePostgres = "SITBIO_PRUEBAS_POSTGRES";

    public FactIntegracionAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariablePostgres)))
        {
            Skip = $"Defina {VariablePostgres} para ejecutar esta prueba de integración.";
        }
    }
}
