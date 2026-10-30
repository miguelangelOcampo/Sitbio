namespace Sitbio.Desktop.ViewModels;

/// <summary>Opción del panel. <see cref="Subgrupo"/> agrupa Gestión en Inventario, Trabajo y Prevención.</summary>
public sealed record OpcionMenu(string Id, string Etiqueta, string? Subgrupo = null);

/// <summary>Módulo del riel: nombre completo, nombre corto, ícono (clave en Icons.xaml) y opciones.</summary>
public sealed record ModuloMenu(string Nombre, string Corto, string Icono, IReadOnlyList<OpcionMenu> Opciones);

/// <summary>
/// Menú «riel con nombres y panel agrupado» por rol (prototipo vigente, NAV-UI). Es solo
/// navegación: los permisos los valida la API (RNF-003).
/// </summary>
public static class MenuPorRol
{
    public static readonly string[] OrdenSubgrupos = ["Inventario", "Trabajo", "Prevención"];

    private const string General = "General";
    private const string Gestion = "Gestión biomédica";
    private const string Solicitudes = "Solicitudes";
    private const string Analisis = "Análisis";
    private const string Administracion = "Administración";

    private static readonly Dictionary<string, (string Corto, string Icono)> Modulos = new()
    {
        [General] = ("Inicio", "Icono.Riel.Inicio"),
        [Gestion] = ("Gestión", "Icono.Riel.Gestion"),
        [Solicitudes] = ("Solicitudes", "Icono.Riel.Solicitudes"),
        [Analisis] = ("Análisis", "Icono.Riel.Analisis"),
        [Administracion] = ("Admin", "Icono.Riel.Admin"),
    };

    private static readonly OpcionMenu Equipos = new("UI-005", "Equipos biomédicos", "Inventario");
    private static readonly OpcionMenu Importar = new("UI-008", "Importar inventario", "Inventario");
    private static readonly OpcionMenu Historial = new("HIST", "Historial de intervenciones", "Inventario");
    private static readonly OpcionMenu Ordenes = new("UI-012", "Órdenes de trabajo", "Trabajo");
    private static readonly OpcionMenu Mantenimientos = new("UI-015", "Mantenimientos", "Prevención");
    private static readonly OpcionMenu Seguimiento = new("UI-010", "Seguimiento");
    private static readonly OpcionMenu Reportes = new("UI-021", "Reportes e indicadores");
    private static readonly OpcionMenu Analitica = new("UI-023", "Analítica");
    private static readonly OpcionMenu Exportacion = new("UI-022", "Exportación");
    private static readonly OpcionMenu Configuracion = new("UI-019", "Configuración");
    private static readonly OpcionMenu Auditoria = new("UI-024", "Auditoría");

    private static readonly Dictionary<string, (string Inicio, (string Modulo, OpcionMenu[] Opciones)[] Modulos)> Menus = new()
    {
        ["AC-01"] = ("UI-004",
        [
            (Administracion, [new("UI-004", "Usuarios y roles"), Configuracion, Auditoria]),
            (Gestion, [Equipos, Importar, Ordenes, Mantenimientos, Historial]),
            (Solicitudes, [Seguimiento]),
            (Analisis, [Reportes, Exportacion]),
        ]),
        ["AC-02"] = ("UI-003",
        [
            (General, [new("UI-003", "Dashboard")]),
            (Gestion, [Equipos, Ordenes, Mantenimientos, Historial]),
            (Solicitudes, [new("UI-009", "Reportar falla"), Seguimiento, new("UI-011", "Bandeja")]),
            (Analisis, [Reportes, Analitica, Exportacion]),
            (Administracion, [Configuracion]),
        ]),
        ["AC-03"] = ("UI-012",
        [
            (General, [new("UI-012", "Mis órdenes")]),
            (Gestion, [Equipos, new("UI-015", "Calibraciones", "Prevención"), Historial]),
        ]),
        ["AC-04"] = ("UI-009",
        [
            (General, [new("UI-009", "Reportar falla"), new("UI-010", "Mis tiquetes")]),
            (Gestion, [Equipos]),
        ]),
        ["AC-05"] = ("UI-021",
        [
            (Analisis, [Reportes, Analitica, Exportacion]),
            (Gestion, [Equipos, Mantenimientos, Historial]),
        ]),
        ["AC-06"] = ("UI-021",
        [
            (Analisis, [Reportes, Analitica]),
        ]),
        ["AC-07"] = ("UI-005",
        [
            (Gestion, [Equipos, Ordenes, Mantenimientos, Historial]),
            (Solicitudes, [Seguimiento]),
            (Analisis, [Reportes, Analitica, Exportacion]),
            (Administracion, [Auditoria]),
        ]),
    };

    /// <summary>Módulos del rol, en el orden del riel. Un rol desconocido no ve nada.</summary>
    public static IReadOnlyList<ModuloMenu> Para(string codigoRol) =>
        Menus.TryGetValue(codigoRol, out var menu)
            ? menu.Modulos.Select(m => new ModuloMenu(m.Modulo, Modulos[m.Modulo].Corto, Modulos[m.Modulo].Icono, m.Opciones)).ToList()
            : [];

    /// <summary>Pantalla con la que entra cada rol (HOME del prototipo).</summary>
    public static string? Inicio(string codigoRol) => Menus.TryGetValue(codigoRol, out var menu) ? menu.Inicio : null;
}
