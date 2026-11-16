namespace Sitbio.Desktop.Services;

/// <summary>
/// Configuración de la propia institución (API-TEN, HU-024) para el Administrador (AC-01). La
/// ruta no lleva id: el tenant lo decide el token en la API (RN-008).
/// </summary>
public interface IServicioInstitucion
{
    Task<Institucion> ObtenerAsync(CancellationToken cancellationToken);

    Task<Institucion> ActualizarAsync(DatosInstitucion datos, CancellationToken cancellationToken);
}

/// <summary>Cuerpo de PUT /tenant/actual.</summary>
public sealed record DatosInstitucion(string Nombre, string Nit, int DiasAnticipacionAlerta);

/// <summary>TenantDto de la API: datos, plan contratado y su consumo (RF-024).</summary>
public sealed record Institucion(
    Guid Id,
    string Nombre,
    string Nit,
    int DiasAnticipacionAlerta,
    string Plan,
    DateOnly? VigenteHasta,
    ConsumoPlan Consumo);

public sealed record ConsumoPlan(MedidorPlan UsuariosGestion, MedidorPlan EquiposActivos, MedidorPlan AlmacenamientoGb, bool HayAlerta);

/// <summary>Uso frente al máximo; la API marca la alerta desde el 80 % y el límite al llegar al máximo.</summary>
public sealed record MedidorPlan(decimal Usados, int Maximo, int Porcentaje, bool Alerta, bool LimiteAlcanzado);
