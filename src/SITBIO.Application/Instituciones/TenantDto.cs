using Sitbio.Domain.Instituciones;

namespace Sitbio.Application.Instituciones;

/// <summary>Cuerpo de PUT /tenant/actual (HU-024): lo que el Administrador puede cambiar.</summary>
public sealed record DatosInstitucion(string? Nombre, string? Nit, int? DiasAnticipacionAlerta);

/// <summary>Uso actual del plan medido en la base (RN-009).</summary>
public sealed record UsoPlan(int UsuariosGestion, int EquiposActivos, long BytesDocumentos);

/// <summary>
/// Un medidor del plan: lo usado frente al máximo contratado. <see cref="Alerta"/> se enciende
/// desde el 80 % (CA-HU-024-03) y <see cref="LimiteAlcanzado"/> al llegar al máximo (RN-009).
/// </summary>
public sealed record MedidorPlan(decimal Usados, int Maximo, int Porcentaje, bool Alerta, bool LimiteAlcanzado)
{
    public const int UmbralAlerta = 80;

    public static MedidorPlan De(decimal usados, int maximo)
    {
        // Se trunca para que el número mostrado y la alerta coincidan: 239 de 300 (79,7 %) es «79 %» y aún no avisa.
        var porcentaje = maximo <= 0 ? 100 : (int)Math.Floor(usados * 100 / maximo);
        return new MedidorPlan(usados, maximo, porcentaje, porcentaje >= UmbralAlerta, usados >= maximo);
    }
}

/// <summary>Consumo del plan: usuarios de gestión, equipos activos y almacenamiento (GB, dos decimales).</summary>
public sealed record ConsumoPlanDto(MedidorPlan UsuariosGestion, MedidorPlan EquiposActivos, MedidorPlan AlmacenamientoGb)
{
    private const decimal BytesPorGb = 1024m * 1024m * 1024m;

    public bool HayAlerta => UsuariosGestion.Alerta || EquiposActivos.Alerta || AlmacenamientoGb.Alerta;

    public static ConsumoPlanDto Desde(Tenant tenant, UsoPlan uso) => new(
        MedidorPlan.De(uso.UsuariosGestion, tenant.MaxUsuariosGestion),
        MedidorPlan.De(uso.EquiposActivos, tenant.MaxEquipos),
        MedidorPlan.De(Math.Round(uso.BytesDocumentos / BytesPorGb, 2, MidpointRounding.AwayFromZero), tenant.MaxAlmacenamientoGb));
}

/// <summary>API-TEN (DOC-06): datos de la propia institución y consumo del plan (RF-024).</summary>
public sealed record TenantDto(
    Guid Id,
    string Nombre,
    string Nit,
    int DiasAnticipacionAlerta,
    string Plan,
    DateOnly? VigenteHasta,
    ConsumoPlanDto Consumo)
{
    public static TenantDto Desde(Tenant tenant, UsoPlan uso) => new(
        tenant.Id, tenant.Nombre, tenant.Nit, tenant.DiasAnticipacionAlerta, tenant.Plan, tenant.VigenteHasta, ConsumoPlanDto.Desde(tenant, uso));
}
