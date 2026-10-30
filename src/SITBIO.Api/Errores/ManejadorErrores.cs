using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Api.Errores;

/// <summary>
/// Traduce las excepciones a Problem Details según la estrategia de errores
/// (DOC-06 Tabla 40). Lo inesperado responde 500 genérico, sin trazas ni SQL.
/// </summary>
public sealed class ManejadorErrores(IProblemDetailsService problemDetails, ILogger<ManejadorErrores> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken cancellationToken)
    {
        var (estado, titulo, nivel) = excepcion switch
        {
            ErrorValidacion => (StatusCodes.Status400BadRequest, "Datos no válidos", LogLevel.Information),
            ErrorNoAutenticado => (StatusCodes.Status401Unauthorized, "No autenticado", LogLevel.Information),
            ErrorPermiso => (StatusCodes.Status403Forbidden, "Acceso denegado", LogLevel.Warning),
            ErrorNoEncontrado => (StatusCodes.Status404NotFound, "Recurso no encontrado", LogLevel.Information),
            ErrorConflicto => (StatusCodes.Status409Conflict, "Conflicto con una regla de negocio", LogLevel.Warning),
            ErrorServicioNoDisponible => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible", LogLevel.Error),
            _ => (StatusCodes.Status500InternalServerError, "Error inesperado", LogLevel.Error),
        };

        var problema = excepcion is ErrorValidacion validacion
            ? new HttpValidationProblemDetails(validacion.Errores)
            : new ProblemDetails();
        problema.Status = estado;
        problema.Title = titulo;
        problema.Detail = excepcion is ErrorAplicacion ? excepcion.Message : null;

        var codigo = excepcion switch
        {
            ErrorConflicto conflicto => conflicto.Codigo,
            ErrorNoAutenticado { Codigo: not null } noAutenticado => noAutenticado.Codigo,
            _ => null,
        };
        if (codigo is not null)
        {
            problema.Extensions["codigo"] = codigo;
        }

        // Sin datos personales: solo método, ruta (sin consulta) y código.
        var detalleTecnico = nivel == LogLevel.Error ? excepcion : null;
        logger.Log(nivel, detalleTecnico, "Error {Estado} en {Metodo} {Ruta}", estado, http.Request.Method, http.Request.Path);

        http.Response.StatusCode = estado;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problema,
            Exception = excepcion,
        });
    }
}
