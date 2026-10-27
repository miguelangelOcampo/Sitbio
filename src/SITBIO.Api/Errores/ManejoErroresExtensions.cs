using System.Diagnostics;

namespace Sitbio.Api.Errores;

public static class ManejoErroresExtensions
{
    /// <summary>Problem Details (application/problem+json) con traceId y correlationId en toda respuesta de error.</summary>
    public static IServiceCollection AddManejoErrores(this IServiceCollection services)
    {
        services.AddProblemDetails(opciones => opciones.CustomizeProblemDetails = contexto =>
        {
            var extensiones = contexto.ProblemDetails.Extensions;
            extensiones["traceId"] = Activity.Current?.Id ?? contexto.HttpContext.TraceIdentifier;
            extensiones["correlationId"] = CorrelacionMiddleware.Obtener(contexto.HttpContext);
        });
        services.AddExceptionHandler<ManejadorErrores>();

        return services;
    }

    /// <summary>Correlación, excepciones y códigos sin cuerpo (por ejemplo 404 de rutas inexistentes).</summary>
    public static IApplicationBuilder UseManejoErrores(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelacionMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}
