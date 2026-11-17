using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Comun;
using Sitbio.Application.Equipos;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Inventario;

/// <summary>Documentos de los equipos sobre PostgreSQL; el filtro global limita todo al tenant del token.</summary>
public sealed class RepositorioDocumentos(SitbioDbContext contexto, IContextoUsuario contextoUsuario) : IRepositorioDocumentos
{
    public Task<bool> ExisteEquipoAsync(Guid equipoId, CancellationToken cancellationToken) =>
        contexto.Equipos.AnyAsync(e => e.Id == equipoId, cancellationToken);

    public async Task<IReadOnlyList<DocumentoDto>> ListarAsync(Guid equipoId, CancellationToken cancellationToken) =>
        await (
            from d in contexto.DocumentosEquipo.AsNoTracking()
            join u in contexto.Usuarios on d.SubidoPorId equals u.Id
            where d.EquipoId == equipoId && d.EstadoActivo
            orderby d.FechaCarga descending
            select new DocumentoDto(d.Id, d.EquipoId, d.TipoDocumento, d.NombreArchivo, d.TipoMime, d.TamanoBytes, d.FechaCarga, u.Nombres + " " + u.Apellidos))
            .ToListAsync(cancellationToken);

    public Task<DocumentoEquipo?> ObtenerAsync(Guid equipoId, Guid documentoId, CancellationToken cancellationToken) =>
        contexto.DocumentosEquipo.SingleOrDefaultAsync(d => d.Id == documentoId && d.EquipoId == equipoId && d.EstadoActivo, cancellationToken);

    public Task<bool> NombreEnUsoAsync(Guid equipoId, string nombreArchivo, CancellationToken cancellationToken)
    {
        var nombre = nombreArchivo.ToLowerInvariant();
        return contexto.DocumentosEquipo.AnyAsync(d => d.EquipoId == equipoId && d.EstadoActivo && d.NombreArchivo.ToLower() == nombre, cancellationToken);
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

        // Los desactivados también cuentan: su archivo se conserva (V010).
        var usados = await contexto.DocumentosEquipo.SumAsync(d => (long?)d.TamanoBytes, cancellationToken) ?? 0;
        return new CapacidadAlmacenamiento(plan.Plan, plan.MaxAlmacenamientoGb, usados);
    }

    public void Agregar(DocumentoEquipo documento) => contexto.DocumentosEquipo.Add(documento);

    public Task GuardarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
