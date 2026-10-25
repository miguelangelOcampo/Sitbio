namespace Sitbio.Api.Modules.Salud;

/// <summary>Estado del servicio para monitoreo y para verificar el despliegue (OBS).</summary>
public static class SaludEndpoints
{
    public static RouteGroupBuilder MapSalud(this RouteGroupBuilder grupo)
    {
        grupo.MapGet("/salud", () => Results.Ok(new EstadoServicio("ok", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")))
            .WithName("ObtenerSalud")
            .AllowAnonymous();

        return grupo;
    }
}

public sealed record EstadoServicio(string Estado, string Version);
