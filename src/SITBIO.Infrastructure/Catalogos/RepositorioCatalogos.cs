using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sitbio.Application.Catalogos;
using Sitbio.Application.Comun.Errores;
using Sitbio.Domain.Inventario;
using Sitbio.Infrastructure.Persistencia;
using Sitbio.Infrastructure.Persistencia.Configuraciones;

namespace Sitbio.Infrastructure.Catalogos;

/// <summary>Catálogos maestros sobre PostgreSQL; el filtro global limita todo al tenant del token (RN-008).</summary>
public sealed class RepositorioCatalogos(SitbioDbContext contexto) : IRepositorioCatalogos
{
    private static readonly string[] IndicesUnicos =
    [
        TipoEquipoConfiguracion.IndiceNombre,
        UbicacionConfiguracion.IndiceNombre,
        ProveedorConfiguracion.IndiceNit,
        TipoFallaConfiguracion.IndiceNombre,
    ];

    public async Task<IReadOnlyList<CatalogoDto>> TiposEquipoAsync(CancellationToken cancellationToken) =>
        await contexto.TiposEquipo.AsNoTracking()
            .OrderBy(t => t.Nombre).ThenBy(t => t.Id)
            .Select(t => new CatalogoDto(
                t.Id, t.Nombre, t.EstadoActivo, t.ClaseRiesgoDefecto, t.RequiereCalibracion, (int?)t.PeriodicidadCalibracionMeses,
                t.Descripcion, null, null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogoDto>> UbicacionesAsync(CancellationToken cancellationToken) =>
        await contexto.Ubicaciones.AsNoTracking()
            .OrderBy(u => u.Nombre).ThenBy(u => u.Id)
            .Select(u => new CatalogoDto(u.Id, u.Nombre, u.EstadoActivo, null, null, null, u.Descripcion, null, null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogoDto>> ProveedoresAsync(CancellationToken cancellationToken) =>
        await contexto.Proveedores.AsNoTracking()
            .OrderBy(p => p.Nombre).ThenBy(p => p.Id)
            .Select(p => new CatalogoDto(p.Id, p.Nombre, p.EstadoActivo, null, null, null, null, p.Nit, p.TipoProveedor))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogoDto>> TiposFallaAsync(CancellationToken cancellationToken) =>
        await contexto.TiposFalla.AsNoTracking()
            .OrderBy(t => t.Nombre).ThenBy(t => t.Id)
            .Select(t => new CatalogoDto(t.Id, t.Nombre, t.EstadoActivo, null, null, null, null, null, null))
            .ToListAsync(cancellationToken);

    public async Task<IElementoCatalogo?> ObtenerAsync(string tipo, Guid id, CancellationToken cancellationToken) =>
        TiposCatalogo.Normalizar(tipo) switch
        {
            TiposCatalogo.TiposEquipo => await contexto.TiposEquipo.FirstOrDefaultAsync(t => t.Id == id, cancellationToken),
            TiposCatalogo.Ubicaciones => await contexto.Ubicaciones.FirstOrDefaultAsync(u => u.Id == id, cancellationToken),
            TiposCatalogo.Proveedores => await contexto.Proveedores.FirstOrDefaultAsync(p => p.Id == id, cancellationToken),
            TiposCatalogo.TiposFalla => await contexto.TiposFalla.FirstOrDefaultAsync(t => t.Id == id, cancellationToken),
            _ => null,
        };

    public Task<bool> NombreEnUsoAsync(string tipo, string nombre, Guid? exceptoId, CancellationToken cancellationToken)
    {
        var buscado = nombre.Trim().ToLower(System.Globalization.CultureInfo.InvariantCulture);
        return TiposCatalogo.Normalizar(tipo) switch
        {
            TiposCatalogo.TiposEquipo => contexto.TiposEquipo.AnyAsync(
                t => t.Nombre.ToLower() == buscado && t.Id != exceptoId, cancellationToken),
            TiposCatalogo.Ubicaciones => contexto.Ubicaciones.AnyAsync(
                u => u.Nombre.ToLower() == buscado && u.Id != exceptoId, cancellationToken),
            TiposCatalogo.Proveedores => contexto.Proveedores.AnyAsync(
                p => p.Nombre.ToLower() == buscado && p.Id != exceptoId, cancellationToken),
            TiposCatalogo.TiposFalla => contexto.TiposFalla.AnyAsync(
                t => t.Nombre.ToLower() == buscado && t.Id != exceptoId, cancellationToken),
            _ => throw new ArgumentException($"Catálogo desconocido: {tipo}.", nameof(tipo)),
        };
    }

    public Task<bool> NitProveedorEnUsoAsync(string nit, Guid? exceptoId, CancellationToken cancellationToken)
    {
        var buscado = nit.Trim();
        return contexto.Proveedores.AnyAsync(p => p.Nit == buscado && p.Id != exceptoId, cancellationToken);
    }

    public void Agregar(IElementoCatalogo elemento) => contexto.Add(elemento);

    public async Task GuardarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgres && IndicesUnicos.Contains(postgres.ConstraintName))
        {
            // Dos registros simultáneos pasaron la consulta previa: el índice único decide.
            var dato = postgres.ConstraintName == ProveedorConfiguracion.IndiceNit ? "ese NIT" : "ese nombre";
            throw new ErrorConflicto(ErroresCatalogo.CodigoDuplicado, $"Ya existe un elemento con {dato} en el catálogo de tu institución.");
        }
    }
}
