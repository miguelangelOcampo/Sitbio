using Microsoft.AspNetCore.Mvc;
using Sitbio.Application.Equipos;

namespace Sitbio.Api.Modules.Equipos;

/// <summary>
/// API-EQ-IMP (DOC-06; HU-016, RF-016): importación masiva de equipos desde CSV o Excel. Solo
/// AC-01 y AC-02 (matriz RBAC). Sin token responde 401; con otro rol, 403. Los equipos quedan en
/// la institución del token (RN-008), nunca en la que diga el archivo.
/// </summary>
public static class ImportacionEndpoints
{
    public static RouteGroupBuilder MapImportacionEquipos(this RouteGroupBuilder equipos)
    {
        var grupo = equipos.MapGroup("/importacion");

        // ?formato=xlsx (por defecto, con una hoja de valores permitidos) o ?formato=csv.
        grupo.MapGet("/plantilla", async (string? formato, ImportacionEquipos caso, CancellationToken cancellationToken) =>
            {
                var plantilla = await caso.PlantillaAsync(formato, cancellationToken);
                return Results.File(plantilla.Contenido, plantilla.TipoMime, plantilla.Nombre);
            })
            .WithName("PlantillaImportacionEquipos")
            .RequireAuthorization(EquiposEndpoints.PoliticaImportacion);

        // multipart/form-data: «archivo» (.csv o .xlsx) y «confirmar» (false = vista previa; true = registrar las filas válidas).
        grupo.MapPost("/", async ([FromForm] bool? confirmar, IFormFile? archivo, ImportacionEquipos caso, CancellationToken cancellationToken) =>
            {
                await using var contenido = archivo?.OpenReadStream() ?? Stream.Null;
                var resultado = await caso.ProcesarAsync(contenido, archivo?.FileName, archivo?.Length ?? 0, confirmar ?? false, cancellationToken);
                return Results.Ok(resultado);
            })
            .WithName("ImportarEquipos")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery()
            .RequireAuthorization(EquiposEndpoints.PoliticaImportacion);

        return equipos;
    }
}
