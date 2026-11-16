using Microsoft.EntityFrameworkCore;
using Sitbio.Application.Instituciones;
using Sitbio.Domain.Instituciones;
using Sitbio.Infrastructure.Persistencia;

namespace Sitbio.Infrastructure.Instituciones;

/// <summary>
/// Solicitudes del servicio sobre PostgreSQL (V002). La tabla no lleva filtro por tenant: la
/// administra el proveedor (ST-10) y la mayoría llega sin sesión. El consecutivo es único en el sistema.
/// </summary>
public sealed class RepositorioSolicitudesServicio(SitbioDbContext contexto) : IRepositorioSolicitudesServicio
{
    private const string Llave = "solicitud_servicio";

    public async Task RegistrarAsync(SolicitudServicio solicitud, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        await using var transaccion = await contexto.Database.BeginTransactionAsync(cancellationToken);

        // Un candado global mientras dure la transacción: dos solicitudes simultáneas no toman el mismo código.
        await contexto.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({Llave})::bigint)", cancellationToken);

        var ultimo = await contexto.SolicitudesServicio
            .Where(s => s.Codigo.StartsWith(SolicitudServicio.Prefijo))
            .OrderByDescending(s => s.Codigo)
            .Select(s => s.Codigo)
            .FirstOrDefaultAsync(cancellationToken);
        solicitud.Numerar((SolicitudServicio.Consecutivo(ultimo) ?? 0) + 1);

        contexto.SolicitudesServicio.Add(solicitud);
        await contexto.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
    }
}
