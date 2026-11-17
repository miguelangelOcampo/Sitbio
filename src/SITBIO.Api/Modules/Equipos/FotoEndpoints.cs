using Microsoft.AspNetCore.Mvc;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;

namespace Sitbio.Api.Modules.Equipos;

/// <summary>
/// API-EQ-FOTO (DOC-06; RF-031, CA-HU-015-03): fotografía opcional del equipo. La consultan los mismos
/// roles que ven la hoja de vida (AC-01 a AC-05 y AC-07); la cargan, cambian o retiran solo AC-02.
/// Sin token responde 401; con otro rol, 403; un equipo de otra institución, 404 (SEC-003).
/// </summary>
public static class FotoEndpoints
{
    public static RouteGroupBuilder MapFotoEquipo(this RouteGroupBuilder equipos)
    {
        var grupo = equipos.MapGroup("/{id:guid}/foto");

        // ?tam=128 (miniatura) o ?tam=512 (vista, por defecto). Sin nombre de descarga: se muestra en línea.
        grupo.MapGet("/", async (Guid id, int? tam, GestionFotoEquipo caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.DescargarAsync(id, tam ?? FotografiaEquipo.LadoMaximoVista, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoMime);
            })
            .WithName("ConsultarFotoEquipo")
            .RequireAuthorization(EquiposEndpoints.PoliticaConsulta);

        // multipart/form-data: «miniatura» (128 × 128 px), «vista» (hasta 512 px) y «avisoAceptado» = true.
        grupo.MapPut("/", async (Guid id, IFormFile? miniatura, IFormFile? vista, [FromForm] bool? avisoAceptado, GestionFotoEquipo caso, CancellationToken cancellationToken) =>
            {
                await using var contenidoMiniatura = miniatura?.OpenReadStream() ?? Stream.Null;
                await using var contenidoVista = vista?.OpenReadStream() ?? Stream.Null;
                var foto = await caso.GuardarAsync(
                    id,
                    miniatura is null ? null : new VersionFoto(contenidoMiniatura, miniatura.Length),
                    vista is null ? null : new VersionFoto(contenidoVista, vista.Length),
                    avisoAceptado ?? false,
                    cancellationToken);
                return Results.Ok(foto);
            })
            .WithName("GuardarFotoEquipo")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery()
            .RequireAuthorization(EquiposEndpoints.PoliticaGestion);

        grupo.MapDelete("/", async (Guid id, GestionFotoEquipo caso, CancellationToken cancellationToken) =>
            {
                await caso.QuitarAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("QuitarFotoEquipo")
            .RequireAuthorization(EquiposEndpoints.PoliticaGestion);

        return equipos;
    }
}
