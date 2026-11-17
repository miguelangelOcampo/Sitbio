using Microsoft.AspNetCore.Mvc;
using Sitbio.Application.Equipos;

namespace Sitbio.Api.Modules.Equipos;

/// <summary>
/// API-EQ-DOC (DOC-06; HU-015, RF-015): documentos de la hoja de vida. Consultan y descargan los
/// mismos roles que ven la hoja de vida (AC-01 a AC-05 y AC-07); cargan y desactivan solo AC-02.
/// Sin token responde 401; con otro rol, 403; un equipo o documento de otra institución, 404 (SEC-003).
/// </summary>
public static class DocumentosEndpoints
{
    public static RouteGroupBuilder MapDocumentosEquipo(this RouteGroupBuilder equipos)
    {
        var grupo = equipos.MapGroup("/{id:guid}/documentos");

        grupo.MapGet("/", async (Guid id, GestionDocumentos caso, CancellationToken cancellationToken) =>
                Results.Ok(await caso.ListarAsync(id, cancellationToken)))
            .WithName("ListarDocumentosEquipo")
            .RequireAuthorization(EquiposEndpoints.PoliticaConsulta);

        // multipart/form-data: «tipo» (Manual de usuario, Certificado de calibración, ...) y «archivo» (PDF, JPG o PNG).
        grupo.MapPost("/", async (Guid id, [FromForm] string? tipo, IFormFile? archivo, GestionDocumentos caso, CancellationToken cancellationToken) =>
            {
                await using var contenido = archivo?.OpenReadStream() ?? Stream.Null;
                var creado = await caso.SubirAsync(id, tipo, archivo?.FileName, contenido, archivo?.Length ?? 0, cancellationToken);
                return Results.Created($"{RutasApi.V1}/equipos/{id}/documentos/{creado.Id}/archivo", creado);
            })
            .WithName("AdjuntarDocumentoEquipo")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery()
            .RequireAuthorization(EquiposEndpoints.PoliticaGestion);

        grupo.MapGet("/{documentoId:guid}/archivo", async (Guid id, Guid documentoId, GestionDocumentos caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.DescargarAsync(id, documentoId, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoMime, archivo.Nombre);
            })
            .WithName("DescargarDocumentoEquipo")
            .RequireAuthorization(EquiposEndpoints.PoliticaConsulta);

        // Desactivación lógica: el documento deja de mostrarse; el archivo y el registro se conservan.
        grupo.MapDelete("/{documentoId:guid}", async (Guid id, Guid documentoId, GestionDocumentos caso, CancellationToken cancellationToken) =>
            {
                await caso.DesactivarAsync(id, documentoId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DesactivarDocumentoEquipo")
            .RequireAuthorization(EquiposEndpoints.PoliticaGestion);

        return equipos;
    }
}
