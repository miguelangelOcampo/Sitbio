using System.Globalization;
using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Equipos;

/// <summary>Fotografía vigente del equipo (RF-031): documento, formato y espacio que ocupan sus dos versiones.</summary>
public sealed record FotoEquipoDto(Guid DocumentoId, Guid EquipoId, string TipoMime, long TamanoBytes, DateTimeOffset FechaCarga);

/// <summary>Versión ya reducida por el cliente (miniatura o vista).</summary>
public sealed record VersionFoto(Stream Contenido, long TamanoBytes);

/// <summary>
/// API-EQ-FOTO (DOC-06; RF-031, CA-HU-015-03). Consulta: AC-01 a AC-05 y AC-07; carga, cambio y
/// retiro: solo AC-02 (lo exige la API). Reglas: la miniatura mide 128 × 128 px y la vista no pasa de
/// 512 px en el lado mayor (el original nunca llega ni se conserva), JPG, PNG o WebP del mismo
/// formato, aviso de datos personales (Ley 1581 de 2012) aceptado, almacenamiento del plan (RN-009,
/// MSG-019) y bitácora CREAR / ELIMINAR. Se guarda como un <c>documento_equipo</c> «Fotografía».
/// </summary>
public sealed class GestionFotoEquipo(
    IRepositorioDocumentos repositorio,
    IAlmacenDocumentos almacen,
    IRegistroAuditoria auditoria,
    IContextoUsuario contexto,
    TimeProvider tiempo)
{
    public const string Entidad = GestionDocumentos.Entidad;

    public const string AvisoDatosPersonales =
        "La fotografía debe mostrar solo el equipo: sin pacientes, historias clínicas ni datos personales (Ley 1581 de 2012).";

    public const string FormatoNoPermitido = "La fotografía debe ser JPG, PNG o WebP.";

    public async Task<FotoEquipoDto> ConsultarAsync(Guid equipoId, CancellationToken cancellationToken) =>
        Dto(await ExigirFoto(equipoId, cancellationToken));

    /// <summary>Contenido de la versión pedida: 128 (miniatura) o 512 (vista).</summary>
    public async Task<ArchivoDocumento> DescargarAsync(Guid equipoId, int tamano, CancellationToken cancellationToken)
    {
        if (tamano is not (FotografiaEquipo.LadoMiniatura or FotografiaEquipo.LadoMaximoVista))
        {
            throw new ErrorValidacion(new Dictionary<string, string[]> { ["tam"] = ["El tamaño debe ser 128 o 512."] });
        }

        var foto = await ExigirFoto(equipoId, cancellationToken);
        var ruta = tamano == FotografiaEquipo.LadoMiniatura ? FotografiaEquipo.RutaMiniatura(foto.RutaAlmacenamiento) : foto.RutaAlmacenamiento;
        var contenido = almacen.Abrir(ruta) ?? throw new ErrorNoEncontrado("El archivo de la fotografía");
        return new ArchivoDocumento(contenido, foto.NombreArchivo, foto.TipoMime);
    }

    /// <summary>Carga o reemplaza la fotografía; la anterior se desactiva (su archivo se conserva, como en HU-015).</summary>
    public async Task<FotoEquipoDto> GuardarAsync(
        Guid equipoId, VersionFoto? miniatura, VersionFoto? vista, bool avisoAceptado, CancellationToken cancellationToken)
    {
        var equipo = await repositorio.ObtenerEquipoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");

        var errores = new Dictionary<string, string[]>();
        if (!avisoAceptado)
        {
            errores["aviso"] = [AvisoDatosPersonales];
        }

        var bytesMiniatura = await Leer(miniatura, "miniatura", errores, cancellationToken);
        var bytesVista = await Leer(vista, "vista", errores, cancellationToken);
        var imagenMiniatura = Revisar(
            bytesMiniatura,
            "miniatura",
            errores,
            i => i.Ancho == FotografiaEquipo.LadoMiniatura && i.Alto == FotografiaEquipo.LadoMiniatura,
            "La miniatura debe medir 128 × 128 px.");
        var imagenVista = Revisar(
            bytesVista,
            "vista",
            errores,
            i => i.LadoMayor <= FotografiaEquipo.LadoMaximoVista,
            "La vista no debe pasar de 512 px en el lado mayor; reduce la imagen antes de subirla.");
        if (imagenMiniatura is { } m && imagenVista is { } v && m.TipoMime != v.TipoMime)
        {
            errores["miniatura"] = ["La miniatura y la vista deben tener el mismo formato."];
        }

        if (errores.Count > 0)
        {
            throw new ErrorValidacion(errores);
        }

        var mime = imagenVista!.Value.TipoMime;
        var tamano = (long)bytesMiniatura!.Length + bytesVista!.Length;
        var capacidad = await repositorio.CapacidadAsync(cancellationToken);
        if (!capacidad.Admite(tamano))
        {
            // MSG-019 (RN-009): las dos versiones cuentan en el almacenamiento del plan.
            throw new ErrorConflicto(
                GestionDocumentos.CodigoLimitePlan,
                $"Tu institución alcanzó el máximo de almacenamiento del plan {capacidad.Plan} ({capacidad.MaximoGb} GB). "
                + "Puedes seguir consultando y trabajando con lo registrado; para ampliar la capacidad, comunícate con SITBIO.");
        }

        var ahora = tiempo.GetUtcNow();
        var extension = FotografiaEquipo.Extension(mime);
        var documento = new DocumentoEquipo(
            contexto.TenantId, equipoId, contexto.UsuarioId, TiposDocumento.Fotografia, FotografiaEquipo.NombreArchivo + extension, mime, tamano, ahora);

        var nombreVista = documento.Id.ToString("N") + extension;
        var rutaVista = await almacen.GuardarAsync(contexto.TenantId, equipoId, nombreVista, new MemoryStream(bytesVista), cancellationToken);
        string? rutaMiniatura = null;
        try
        {
            rutaMiniatura = await almacen.GuardarAsync(
                contexto.TenantId, equipoId, FotografiaEquipo.RutaMiniatura(nombreVista), new MemoryStream(bytesMiniatura), cancellationToken);
            documento.UbicarEn(rutaVista);

            var anterior = await repositorio.ObtenerFotoAsync(equipoId, cancellationToken);
            anterior?.Desactivar(contexto.UsuarioId, ahora);
            repositorio.Agregar(documento);
            equipo.AsignarFoto(documento.Id);
            await repositorio.GuardarAsync(cancellationToken);
        }
        catch
        {
            almacen.Eliminar(rutaVista);
            if (rutaMiniatura is not null)
            {
                almacen.Eliminar(rutaMiniatura);
            }

            throw;
        }

        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Crear, Entidad, documento.Id.ToString(), Detalle(documento), cancellationToken);
        return Dto(documento);
    }

    /// <summary>Retira la fotografía: el documento se desactiva y la hoja de vida vuelve al ícono del tipo.</summary>
    public async Task QuitarAsync(Guid equipoId, CancellationToken cancellationToken)
    {
        var equipo = await repositorio.ObtenerEquipoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("El equipo");
        var foto = await repositorio.ObtenerFotoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("La fotografía del equipo");
        foto.Desactivar(contexto.UsuarioId, tiempo.GetUtcNow());
        equipo.QuitarFoto();
        await repositorio.GuardarAsync(cancellationToken);
        await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Eliminar, Entidad, foto.Id.ToString(), Detalle(foto), cancellationToken);
    }

    private async Task<DocumentoEquipo> ExigirFoto(Guid equipoId, CancellationToken cancellationToken)
    {
        if (!await repositorio.ExisteEquipoAsync(equipoId, cancellationToken))
        {
            throw new ErrorNoEncontrado("El equipo");
        }

        return await repositorio.ObtenerFotoAsync(equipoId, cancellationToken) ?? throw new ErrorNoEncontrado("La fotografía del equipo");
    }

    private static async Task<byte[]?> Leer(VersionFoto? version, string campo, Dictionary<string, string[]> errores, CancellationToken cancellationToken)
    {
        if (version is null || version.TamanoBytes <= 0)
        {
            errores[campo] = [campo == "miniatura" ? "Falta la miniatura de 128 px." : "Falta la vista de 512 px."];
            return null;
        }

        if (version.TamanoBytes > FotografiaEquipo.TamanoMaximoBytes)
        {
            errores[campo] = ["La imagen reducida supera 2 MB."];
            return null;
        }

        using var memoria = new MemoryStream();
        await version.Contenido.CopyToAsync(memoria, cancellationToken);
        return memoria.ToArray();
    }

    private static ImagenFoto? Revisar(byte[]? bytes, string campo, Dictionary<string, string[]> errores, Func<ImagenFoto, bool> cumple, string mensaje)
    {
        if (bytes is null)
        {
            return null;
        }

        if (FotografiaEquipo.Leer(bytes) is not { } imagen)
        {
            errores[campo] = [FormatoNoPermitido];
            return null;
        }

        if (!cumple(imagen))
        {
            errores[campo] = [mensaje];
            return null;
        }

        return imagen;
    }

    private static FotoEquipoDto Dto(DocumentoEquipo foto) => new(foto.Id, foto.EquipoId, foto.TipoMime, foto.TamanoBytes, foto.FechaCarga);

    /// <summary>Sin nombre de archivo; deja constancia de que se aceptó el aviso de datos personales (Ley 1581 de 2012).</summary>
    private static Dictionary<string, string> Detalle(DocumentoEquipo foto) => new()
    {
        ["equipo_id"] = foto.EquipoId.ToString(),
        ["tipo"] = foto.TipoDocumento,
        ["tamano_bytes"] = foto.TamanoBytes.ToString(CultureInfo.InvariantCulture),
        ["aviso_datos_personales"] = "aceptado",
    };
}
