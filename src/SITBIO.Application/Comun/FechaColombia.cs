namespace Sitbio.Application.Comun;

/// <summary>
/// Fecha del día en Colombia (UTC−5, sin horario de verano). Los vencimientos (registro
/// INVIMA, preventivos) se comparan contra este día, aunque el servidor esté en UTC.
/// </summary>
public static class FechaColombia
{
    public static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

    public static DateOnly Hoy(TimeProvider tiempo) => DateOnly.FromDateTime(tiempo.GetUtcNow().ToOffset(Desfase).DateTime);
}
