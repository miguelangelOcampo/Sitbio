using Sitbio.Desktop.Services;

namespace Sitbio.Desktop.Tests.ViewModels;

/// <summary>Datos y reloj de prueba para UI-005 y UI-006 (hoy = 06/11/2026, Bogotá).</summary>
internal static class EquiposPrueba
{
    public static readonly DateOnly Hoy = new(2026, 11, 6);

    public static Equipo Equipo(
        string placa = "HSV-EQ-0412",
        string estado = "OPERATIVO",
        string avisoRegistro = AvisosRegistro.Vigente,
        DateOnly? venceRegistro = null,
        string? clase = "IIb",
        bool calibra = true,
        int? mesesCalibracion = 12,
        PlanPreventivo? plan = null,
        DateTimeOffset? baja = null) =>
        new(
            Guid.NewGuid(), placa, "MX450-77812", "Philips", "IntelliVue MX450",
            Guid.NewGuid(), "Monitor de signos vitales", Guid.NewGuid(), "UCI adultos",
            null, null, estado, estado == "OPERATIVO" ? "Operativo" : estado == "BAJA" ? "Baja" : "En mantenimiento",
            new DateOnly(2022, 3, 15), baja, "registro_sanitario", "INVIMA 2019DM-0019830",
            venceRegistro ?? new DateOnly(2028, 1, 1), avisoRegistro, clase, calibra, calibra ? mesesCalibracion : null, false, plan);

    public static PlanPreventivo Plan(DateOnly proxima, bool vencido = false, DateOnly? ultima = null) =>
        new(Guid.NewGuid(), 90, ultima, proxima, vencido);

    public static PaginaEquipos Pagina(int total, params Equipo[] equipos) => new(equipos, 1, 20, total);

    /// <summary>Reloj fijo a las 10:00 de Bogotá.</summary>
    public sealed class Reloj : TimeProvider
    {
        private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("Bogota", TimeSpan.FromHours(-5), "Bogota", "Bogota");

        public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => Bogota;
    }
}
