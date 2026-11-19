using Microsoft.EntityFrameworkCore;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sitbio.Application.Analitica;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Infrastructure.Persistencia.Mongo;

/// <summary>Documento de <c>resultados_analitica</c> que escribe el lote de Python (ADR-006): uno por tenant.</summary>
[BsonIgnoreExtraElements]
public sealed class DocumentoResultadosAnalitica : IDocumentoTenant
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("generado_en")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime GeneradoEn { get; set; }

    [BsonElement("fecha_corte")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime FechaCorte { get; set; }

    [BsonElement("intervenciones_analizadas")]
    public int IntervencionesAnalizadas { get; set; }

    [BsonElement("agrupacion")]
    public DocumentoAgrupacion Agrupacion { get; set; } = new();

    /// <summary>Una agrupación por cada k de 2 a 6 (I-03); vacía en resultados anteriores.</summary>
    [BsonElement("agrupaciones")]
    public List<DocumentoAgrupacion> Agrupaciones { get; set; } = [];
}

[BsonIgnoreExtraElements]
public sealed class DocumentoAgrupacion
{
    [BsonElement("k")]
    public int K { get; set; }

    [BsonElement("inercia")]
    public double? Inercia { get; set; }

    [BsonElement("grupos")]
    public List<DocumentoGrupo> Grupos { get; set; } = [];

    [BsonElement("equipos")]
    public List<DocumentoEquipoAgrupado> Equipos { get; set; } = [];
}

[BsonIgnoreExtraElements]
public class DocumentoCaracteristicas
{
    [BsonElement("intervenciones")]
    public double? Intervenciones { get; set; }

    [BsonElement("proporcion_correctivos")]
    public double? ProporcionCorrectivos { get; set; }

    [BsonElement("costo_confirmado")]
    public double? CostoConfirmado { get; set; }

    [BsonElement("tiempo_medio_min")]
    public double? TiempoMedioMin { get; set; }

    public CaracteristicasDto Dto() => new(Intervenciones, ProporcionCorrectivos, CostoConfirmado, TiempoMedioMin);
}

[BsonIgnoreExtraElements]
public sealed class DocumentoGrupo : DocumentoCaracteristicas
{
    [BsonElement("grupo")]
    public int Grupo { get; set; }

    [BsonElement("equipos")]
    public int Equipos { get; set; }

    [BsonElement("nombre")]
    public string? Nombre { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class DocumentoEquipoAgrupado : DocumentoCaracteristicas
{
    [BsonElement("equipo_id")]
    public string EquipoId { get; set; } = string.Empty;

    [BsonElement("grupo")]
    public int Grupo { get; set; }
}

/// <summary>Último resultado del lote para el tenant del token (RN-008); solo lectura.</summary>
public sealed class AnaliticaMongo(IMongoDatabase baseDatos, IContextoUsuario contextoUsuario)
    : RepositorioMongoTenant<DocumentoResultadosAnalitica>(baseDatos, contextoUsuario, Coleccion), IRepositorioAnalitica
{
    public const string Coleccion = "resultados_analitica";

    public async Task<ResultadoAnalitica?> UltimoAsync(CancellationToken cancellationToken)
    {
        DocumentoResultadosAnalitica? documento;
        try
        {
            documento = await ObtenerUnoAsync(Filtro.Empty, cancellationToken);
        }
        catch (Exception error) when (error is MongoException or TimeoutException)
        {
            throw new ErrorServicioNoDisponible(error);
        }

        if (documento is null)
        {
            return null;
        }

        var a = documento.Agrupacion;
        return new ResultadoAnalitica(
            new DateTimeOffset(documento.GeneradoEn, TimeSpan.Zero),
            new DateTimeOffset(documento.FechaCorte, TimeSpan.Zero),
            documento.IntervencionesAnalizadas,
            a.K,
            Grupos(a),
            Equipos(a),
            documento.Agrupaciones.Select(x => new AgrupacionGuardada(x.K, x.Inercia, Grupos(x), Equipos(x))).ToList());
    }

    private static List<GrupoDto> Grupos(DocumentoAgrupacion a) =>
        a.Grupos.OrderBy(g => g.Grupo).Select(g => new GrupoDto(g.Grupo, g.Equipos, g.Dto(), g.Nombre)).ToList();

    private static List<(Guid EquipoId, int Grupo, CaracteristicasDto Caracteristicas)> Equipos(DocumentoAgrupacion a) =>
        a.Equipos
            .Where(e => Guid.TryParse(e.EquipoId, out _))
            .Select(e => (Guid.Parse(e.EquipoId), e.Grupo, e.Dto()))
            .ToList();
}

/// <summary>Placa y tipo de los equipos del tenant (el filtro global aplica RN-008).</summary>
public sealed class PlacasEquipos(SitbioDbContext contexto) : IPlacasEquipos
{
    public async Task<IReadOnlyDictionary<Guid, DatosEquipo>> PlacasAsync(IReadOnlyCollection<Guid> equipos, CancellationToken cancellationToken) =>
        await (
                from e in contexto.Equipos.AsNoTracking()
                where equipos.Contains(e.Id)
                join t in contexto.TiposEquipo.AsNoTracking() on e.TipoEquipoId equals t.Id
                select new { e.Id, e.PlacaInventario, Tipo = t.Nombre })
            .ToDictionaryAsync(x => x.Id, x => new DatosEquipo(x.PlacaInventario, x.Tipo), cancellationToken);
}
