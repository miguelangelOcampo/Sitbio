using Sitbio.Application.Equipos;
using Sitbio.Application.Usuarios;
using Sitbio.Domain.Inventario;

namespace Sitbio.Api.Modules.Usuarios;

/// <summary>
/// API-USR-FOTO (J-11, HU-011): foto de perfil de quien está en la sesión. Cualquier rol con token la consulta,
/// cambia o quita, siempre la suya (el usuario sale del token). Sin token responde 401.
/// </summary>
public static class FotoPerfilEndpoints
{
    public static RouteGroupBuilder MapFotoPerfil(this RouteGroupBuilder api)
    {
        var grupo = api.MapGroup("/usuarios/me/foto").RequireAuthorization();

        // ?tam=128 (miniatura) o ?tam=512 (vista, por defecto). Sin foto, 404.
        grupo.MapGet("/", async (int? tam, GestionFotoPerfil caso, CancellationToken cancellationToken) =>
            {
                var archivo = await caso.DescargarAsync(tam ?? FotografiaEquipo.LadoMaximoVista, cancellationToken);
                return Results.File(archivo.Contenido, archivo.TipoMime);
            })
            .WithName("ConsultarMiFoto");

        // multipart/form-data: «miniatura» (128 × 128 px) y «vista» (hasta 512 px), ya reducidas por el cliente.
        grupo.MapPut("/", async (IFormFile? miniatura, IFormFile? vista, GestionFotoPerfil caso, CancellationToken cancellationToken) =>
            {
                await using var contenidoMiniatura = miniatura?.OpenReadStream() ?? Stream.Null;
                await using var contenidoVista = vista?.OpenReadStream() ?? Stream.Null;
                await caso.GuardarAsync(
                    miniatura is null ? null : new VersionFoto(contenidoMiniatura, miniatura.Length),
                    vista is null ? null : new VersionFoto(contenidoVista, vista.Length),
                    cancellationToken);
                return Results.NoContent();
            })
            .WithName("GuardarMiFoto")
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery();

        grupo.MapDelete("/", async (GestionFotoPerfil caso, CancellationToken cancellationToken) =>
            {
                await caso.QuitarAsync(cancellationToken);
                return Results.NoContent();
            })
            .WithName("QuitarMiFoto");

        return api;
    }
}
