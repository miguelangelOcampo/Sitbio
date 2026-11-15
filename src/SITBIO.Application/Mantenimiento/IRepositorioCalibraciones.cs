using Sitbio.Domain.Inventario;

namespace Sitbio.Application.Mantenimiento;

/// <summary>Calibraciones del tenant del token (el filtro lo aplica la persistencia, RN-008).</summary>
public interface IRepositorioCalibraciones
{
    /// <summary>Página de calibraciones, las más recientes primero.</summary>
    Task<(IReadOnlyList<CalibracionLeida> Items, int Total)> ListarAsync(FiltroCalibraciones filtro, CancellationToken cancellationToken);

    /// <summary>Calibración con sus datos de lectura; null si no existe o es de otro tenant.</summary>
    Task<CalibracionLeida?> ObtenerAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Calibración con seguimiento de cambios; null si no existe o es de otro tenant.</summary>
    Task<RegistroCalibracion?> CalibracionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Equipo de la institución; null si no existe o es de otro tenant.</summary>
    Task<EquipoDeCalibracion?> EquipoAsync(Guid equipoId, CancellationToken cancellationToken);

    /// <summary>Si el proveedor (laboratorio) existe en la institución.</summary>
    Task<bool> ExisteProveedorAsync(Guid proveedorId, CancellationToken cancellationToken);

    void Agregar(RegistroCalibracion calibracion);

    Task GuardarAsync(CancellationToken cancellationToken);
}
