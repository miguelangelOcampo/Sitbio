using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>
/// Documentos de los equipos sobre PostgreSQL; el filtro global limita todo al tenant del token.
/// La fotografía vigente del equipo (RF-031) es un documento más, pero no sale en la lista de
/// documentos ni se toca desde ella: se gestiona por API-EQ-FOTO.
/// </summary>
public sealed class RepositorioDocumentos(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioDocumentos
{
    public Task<bool> ExisteEquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.AnyAsync(e => e.Id == equipoId, cancellationToken);

    public async Task<IReadOnlyList<DocumentoDto>> ListarAsync(Guid equipoId, CancellationToken cancellationToken) =>
        await (
            from d in DocumentosSinFoto(equipoId).AsNoTracking()
            join u in contexto.Usuarios on d.SubidoPorId equals u.Id
            orderby d.FechaCarga descending
            select new DocumentoDto(d.Id, d.EquipoId, d.TipoDocumento, d.NombreArchivo, d.TipoMime, d.TamanoBytes, d.FechaCarga, u.Nombres + " " + u.Apellidos))
            .ToListAsync(cancellationToken);

    public Task<DocumentoEquipo?> ObtenerAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken) =>
        DocumentosSinFoto(equipoId).SingleOrDefaultAsync(d => d.Id == documentoId, cancellationToken);

    public Task<bool> NombreEnUsoAsync(Guid equipoId, string nombreArchivo, CancellationToken cancellationToken)
    {
        var nombre = nombreArchivo.ToLowerInvariant();
        return DocumentosSinFoto(equipoId).AnyAsync(d => d.NombreArchivo.ToLower() == nombre, cancellationToken);
    }

    public async Task<string> NombreUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await contexto.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.Nombres + " " + u.Apellidos).SingleOrDefaultAsync(cancellationToken)
        ?? string.Empty;

    public async Task<CapacidadAlmacenamiento> CapacidadAsync(CancellationToken cancellationToken)
    {
        // tenant no lleva filtro global (raíz del aislamiento): se usa el tenant del token.
        var tenantId = contextoUsuario.TenantId;
        var plan = await contexto.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Plan, t.MaxAlmacenamientoGb })
            .SingleAsync(cancellationToken);

        // Los desactivados también cuentan: su archivo se conserva (V010). La fotografía, con sus dos versiones.
        var usados = await contexto.DocumentosEquipo.SumAsync(d => (long?)d.TamanoBytes, cancellationToken) ?? 0;
        return new CapacidadAlmacenamiento(plan.Plan, plan.MaxAlmacenamientoGb, usados);
    }

    public Task<EquipoBiomedico?> ObtenerEquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.SingleOrDefaultAsync(e => e.Id == equipoId, cancellationToken);

    public Task<DocumentoEquipo?> ObtenerFotoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        (from e in contexto.Equipos
         join d in contexto.DocumentosEquipo on e.FotoDocumentoId equals d.Id
         where e.Id == equipoId && d.EquipoId == equipoId && d.EstadoActivo
         select d).SingleOrDefaultAsync(cancellationToken);

    public void Agregar(DocumentoEquipo documento) => contexto.DocumentosEquipo.Add(documento);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);

    private IQueryable<DocumentoEquipo> DocumentosSinFoto(Guid equipoId) =>
        from d in contexto.DocumentosEquipo
        where d.EquipoId == equipoId && d.EstadoActivo
              && !contexto.Equipos.Any(e => e.Id == equipoId && e.FotoDocumentoId == d.Id)
        select d;
}
