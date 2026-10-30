namespace Sitbio.Desktop.ViewModels;

/// <summary>Textos del catálogo de mensajes (DOC-05, sección 14) usados en el cliente.</summary>
public static class Mensajes
{
    public const string Msg001 = "Credenciales incorrectas. Verifique sus datos e intente de nuevo.";
    public const string Msg002 = "No fue posible iniciar sesión. Si el problema continúa, comuníquese con el administrador del sistema.";
    public const string Msg011 = "No fue posible conectar con el servidor de SITBIO. Sus datos no se han perdido; intente de nuevo.";
}

/// <summary>Aviso en línea de la pantalla (InfoBar).</summary>
public sealed record Aviso(string Titulo, string Mensaje, string? Codigo, bool EsError = true, bool PuedeReintentar = false);
