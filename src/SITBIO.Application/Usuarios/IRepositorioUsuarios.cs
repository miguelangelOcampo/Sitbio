using Sitbio.Domain.Seguridad;

namespace Sitbio.Application.Usuarios;

/// <summary>Usuarios del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioUsuarios
{
    /// <summary>Página de usuarios del tenant con el código de su rol, ordenados por apellidos y nombres.</summary>
    Task<(IReadOnlyList<(Usuario Usuario, string CodigoRol)> Items, int Total)> ListarAsync(int pagina, int tamano, CancellationToken cancellationToken);

    /// <summary>Usuario del tenant con el código de su rol; null si no existe o es de otro tenant (SEC-003).</summary>
    Task<(Usuario Usuario, string CodigoRol)?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    Task<RolAsignable?> RolPorCodigoAsync(string codigo, CancellationToken cancellationToken);

    /// <summary>
    /// ¿Algún usuario de CUALQUIER tenant usa el correo? El correo es único en todo el
    /// sistema (V005, ADR-011); solo se devuelve sí o no, nunca de qué institución es.
    /// </summary>
    Task<bool> CorreoEnUsoAsync(string correo, Guid? exceptoUsuarioId, CancellationToken cancellationToken);

    void Agregar(Usuario usuario);

    Task GuardarAsync(CancellationToken cancellationToken);
}

/// <summary>Claves temporales aleatorias (RF-012).</summary>
public interface IGeneradorClaves
{
    string Generar();
}
