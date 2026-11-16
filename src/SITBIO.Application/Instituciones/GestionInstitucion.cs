using Sitbio.Application.Auditoria;
using Sitbio.Application.Comun;
using Sitbio.Application.Comun.Errores;

namespace Sitbio.Application.Instituciones;

/// <summary>
/// Configuración de la institución (HU-024, RF-024). El permiso «solo AC-01» lo exige la API y
/// el tenant sale del token (<see cref="IContextoUsuario"/>). El plan y sus límites no se editan
/// aquí: los fija el proveedor (ST-10).
/// </summary>
public sealed class GestionInstitucion(IRepositorioInstitucion repositorio, IRegistroAuditoria auditoria, IContextoUsuario contexto)
{
    public const string Entidad = "tenant";
    public const string CodigoNitEnUso = "TEN-001";

    public async Task<TenantDto> ObtenerAsync(CancellationToken cancellationToken)
    {
        var tenant = await repositorio.ObtenerAsync(cancellationToken) ?? throw new ErrorNoEncontrado("La institución");
        return TenantDto.Desde(tenant, await repositorio.UsoAsync(cancellationToken));
    }

    /// <summary>Guarda nombre, NIT y anticipación de las alertas (CA-HU-024-01) y deja el cambio en la bitácora.</summary>
    public async Task<TenantDto> ActualizarAsync(DatosInstitucion datos, CancellationToken cancellationToken)
    {
        if (datos.DiasAnticipacionAlerta is not { } dias)
        {
            throw ErrorValidacion.DeCampo("diasAnticipacionAlerta", "El campo diasAnticipacionAlerta es obligatorio.");
        }

        var tenant = await repositorio.ObtenerAsync(cancellationToken) ?? throw new ErrorNoEncontrado("La institución");
        var antes = (tenant.Nombre, tenant.Nit, tenant.DiasAnticipacionAlerta);
        try
        {
            tenant.ActualizarDatos(datos.Nombre, datos.Nit, dias);
        }
        catch (ArgumentException error)
        {
            throw ErrorValidacion.DeCampo(error.ParamName ?? "datos", error.Message.Split(" (Parameter")[0]);
        }

        if (tenant.Nit != antes.Nit && await repositorio.NitEnUsoAsync(tenant.Nit, cancellationToken))
        {
            throw new ErrorConflicto(
                CodigoNitEnUso,
                $"El NIT {tenant.Nit} ya está registrado en SITBIO. Verifícalo o comunícate con el proveedor del servicio.");
        }

        var cambios = new List<string>();
        if (tenant.Nombre != antes.Nombre)
        {
            cambios.Add("nombre");
        }

        if (tenant.Nit != antes.Nit)
        {
            cambios.Add("nit");
        }

        if (tenant.DiasAnticipacionAlerta != antes.DiasAnticipacionAlerta)
        {
            cambios.Add("dias_anticipacion_alerta");
        }

        await repositorio.GuardarAsync(cancellationToken);
        if (cambios.Count > 0)
        {
            var detalle = new Dictionary<string, string>
            {
                ["campos"] = string.Join(",", cambios),
                ["dias_anticipacion_alerta"] = tenant.DiasAnticipacionAlerta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            await auditoria.RegistrarAsync(contexto.UsuarioId, AccionesAuditoria.Actualizar, Entidad, tenant.Id.ToString(), detalle, cancellationToken);
        }

        return TenantDto.Desde(tenant, await repositorio.UsoAsync(cancellationToken));
    }
}
