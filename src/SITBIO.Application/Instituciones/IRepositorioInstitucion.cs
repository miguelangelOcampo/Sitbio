using Sitbio.Domain.Instituciones;

namespace Sitbio.Application.Instituciones;

/// <summary>
/// La institución del token (RN-008): nunca recibe un id, así un Administrador no puede leer ni
/// cambiar otro tenant (CA-HU-024-02).
/// </summary>
public interface IRepositorioInstitucion
{
    /// <summary>El tenant del token, con seguimiento de cambios; null si ya no existe.</summary>
    Task<Tenant?> ObtenerAsync(CancellationToken cancellationToken);

    /// <summary>Usuarios de gestión activos, equipos no dados de baja y bytes de documentos del tenant.</summary>
    Task<UsoPlan> UsoAsync(CancellationToken cancellationToken);

    /// <summary>true si otra institución ya tiene ese NIT (es único en el sistema).</summary>
    Task<bool> NitEnUsoAsync(string nit, CancellationToken cancellationToken);

    Task GuardarAsync(CancellationToken cancellationToken);
}
