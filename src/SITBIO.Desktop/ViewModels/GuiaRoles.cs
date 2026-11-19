namespace Sitbio.Desktop.ViewModels;

/// <summary>Qué puede hacer un rol (DOC-01 Tabla 3, DOC-03 Tabla 8 y RF). Solo consulta: los permisos los aplica la API.</summary>
public sealed record InfoRol(
    string Codigo,
    string Nombre,
    string Quien,
    string Frecuencia,
    IReadOnlyList<string> Funciones,
    IReadOnlyList<string> Restricciones)
{
    /// <summary>RN-009: AC-01, AC-02, AC-03, AC-05 y AC-06 cuentan en el plan; AC-04 es ilimitado y AC-07 temporal.</summary>
    public bool CuentaEnPlan => GuiaRoles.Gestion.Contains(Codigo);

    public string Limite => CuentaEnPlan ? "cuenta en el plan" : Codigo == "AC-04" ? "sin límite" : "temporal, no cuenta";

    /// <summary>Opciones reales del menú del rol (las mismas que arma el shell).</summary>
    public IReadOnlyList<string> Menu => MenuPorRol.Para(Codigo).SelectMany(m => m.Opciones).Select(o => o.Etiqueta).ToList();

    public string Etiqueta => $"{Codigo} · {Nombre}";
}

public enum NivelPermiso
{
    Ninguno,
    Consulta,
    Gestiona,
}

/// <summary>Fila de la matriz de permisos: un módulo y el permiso de AC-01 a AC-07.</summary>
public sealed record PermisoModulo(string Modulo, IReadOnlyList<string> PorRol);

/// <summary>
/// Guía de roles de UI-004 › Roles y permisos (prototipo vigente), con el ajuste de roles del
/// 01/11/2026: AC-05 «Gerente de Tecnología Biomédica»; campos dinámicos y catálogos técnicos
/// para AC-02; ubicaciones para AC-01 y AC-02.
/// </summary>
public static class GuiaRoles
{
    public static readonly string[] Gestion = ["AC-01", "AC-02", "AC-03", "AC-05", "AC-06"];

    public static IReadOnlyList<InfoRol> Roles { get; } =
    [
        new("AC-01", "Administrador del Sistema",
            "Personal de TI de la institución. En IPS pequeñas lo asume el coordinador biomédico con una cuenta aparte.", "Semanal",
            [
                "Crea, edita y desactiva usuarios y les asigna un rol.",
                "Desbloquea cuentas y emite contraseñas temporales.",
                "Configura los datos de la institución y gestiona las áreas y ubicaciones junto con el Ingeniero Biomédico.",
                "Importa el inventario inicial desde Excel o CSV.",
                "Consulta el plan contratado y solicita el cambio de plan al proveedor.",
                "Consulta la bitácora de auditoría.",
            ],
            [
                "No registra ni edita equipos uno a uno ni reporta fallas.",
                "Consulta los campos dinámicos y los catálogos técnicos sin editarlos.",
                "No ve la analítica K-Means.",
                "Solo administra su propia institución.",
            ]),
        new("AC-02", "Ingeniero Biomédico", "Coordinador o ingeniero del área de ingeniería clínica.", "Diaria, uso intensivo",
            [
                "Registra, edita y da de baja las hojas de vida de los equipos.",
                "Recibe tiquetes y asigna órdenes a los técnicos.",
                "Programa el mantenimiento preventivo.",
                "Aprueba y firma órdenes y calibraciones.",
                "Define los campos dinámicos por tipo de equipo y gestiona los catálogos técnicos y las ubicaciones.",
                "Consulta indicadores y analítica y exporta reportes.",
            ],
            [
                "No gestiona usuarios ni consulta la bitácora.",
                "No edita los datos de la institución ni el plan.",
            ]),
        new("AC-03", "Técnico de Mantenimiento", "Técnicos que ejecutan los mantenimientos en campo.", "Diaria, varias veces",
            [
                "Ejecuta sus órdenes y registra la intervención con repuestos, costos y campos dinámicos.",
                "Registra calibraciones para aprobación del ingeniero.",
                "Consulta hojas de vida y tiquetes.",
            ],
            [
                "Solo ve las órdenes asignadas a él.",
                "No aprueba ni firma.",
                "No puede dejar un equipo Operativo con la calibración vencida.",
            ]),
        new("AC-04", "Personal Clínico", "Médicos, enfermería y jefes de servicio asistencial.", "Ocasional, en turnos",
            [
                "Reporta fallas indicando la criticidad.",
                "Consulta el estado de sus tiquetes.",
                "Consulta los equipos de su servicio.",
            ],
            [
                "Solo ve sus propios tiquetes.",
                "No accede a órdenes, mantenimientos ni indicadores.",
            ]),
        new("AC-05", "Gerente de Tecnología Biomédica",
            "Responsable gerencial del área biomédica (no es el gerente general de la IPS, que hace parte de la Directiva).", "Semanal",
            [
                "Consulta indicadores de disponibilidad, cumplimiento y costos.",
                "Consulta la analítica de agrupamiento.",
                "Exporta reportes y consulta los planes preventivos.",
            ],
            ["Solo lectura: no registra ni aprueba."]),
        new("AC-06", "Directiva Institucional", "Gerencia y junta directiva de la IPS.", "Mensual",
            [
                "Consulta los indicadores consolidados de disponibilidad y costos.",
                "Consulta los resultados de la analítica.",
            ],
            ["Sin acceso a la operación: equipos, tiquetes, órdenes y configuración."]),
        new("AC-07", "Ente Regulador / Auditor", "INVIMA o Secretaría de Salud durante una auditoría (cuenta temporal).", "Durante la auditoría",
            [
                "Consulta hojas de vida, historiales y la línea de tiempo de cada equipo.",
                "Descarga la hoja de vida en PDF.",
                "Consulta la bitácora de auditoría.",
            ],
            [
                "Solo lectura; no modifica ningún dato.",
                "Solo ve la institución auditada.",
            ]),
    ];

    /// <summary>DOC-03 Tabla 8, columnas AC-01 a AC-07, con el ajuste de roles del 01/11/2026.</summary>
    public static IReadOnlyList<PermisoModulo> Matriz { get; } =
    [
        new("Gestión de usuarios", ["Crear/Editar", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso"]),
        new("Institución y ubicaciones", ["Configurar", "Gestionar ubicaciones", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso"]),
        new("Catálogos técnicos", ["Consultar", "Crear/Editar", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso"]),
        new("Hojas de vida", ["Consultar e importar", "Crear/Editar", "Solo consulta", "Solo consulta", "Solo consulta", "Sin acceso", "Solo consulta"]),
        new("Reporte de fallas", ["Sin acceso", "Crear/Editar", "Solo consulta", "Crear tiquete", "Sin acceso", "Sin acceso", "Sin acceso"]),
        new("Seguimiento de tiquetes", ["Consultar", "Consultar", "Consultar", "Consultar propios", "Sin acceso", "Sin acceso", "Solo consulta"]),
        new("Programación preventiva", ["Consultar", "Crear/Editar", "Consultar", "Sin acceso", "Consultar", "Sin acceso", "Solo consulta"]),
        new("Órdenes e intervenciones", ["Consultar", "Asignar/Aprobar", "Registrar/Editar", "Sin acceso", "Sin acceso", "Sin acceso", "Solo consulta"]),
        new("Campos dinámicos", ["Consultar", "Definir/Publicar", "Consultar y diligenciar", "Sin acceso", "Sin acceso", "Sin acceso", "Solo consulta"]),
        new("Calibraciones", ["Consultar", "Aprobar", "Registrar/Editar", "Sin acceso", "Sin acceso", "Sin acceso", "Solo consulta"]),
        new("Reportes e indicadores", ["Consultar", "Consultar", "Sin acceso", "Sin acceso", "Consultar KPIs", "Consultar KPIs", "Solo consulta"]),
        new("Analítica K-Means", ["Sin acceso", "Consultar", "Sin acceso", "Sin acceso", "Consultar KPIs", "Consultar KPIs", "Solo consulta"]),
        new("Planes del servicio", ["Consultar y solicitar cambio", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso"]),
        new("Bitácora de auditoría", ["Consultar", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Sin acceso", "Consultar"]),
    ];

    public static InfoRol? Buscar(string codigo) => Roles.FirstOrDefault(r => r.Codigo == codigo);

    public static string NombreDe(string codigo) => Buscar(codigo)?.Nombre ?? codigo;

    /// <summary>«Sin acceso» → ninguno; consultar (sin importar) → consulta; lo demás → gestiona.</summary>
    public static NivelPermiso Nivel(string permiso) =>
        permiso == "Sin acceso" ? NivelPermiso.Ninguno
        : permiso.Contains("onsult", StringComparison.Ordinal) && !permiso.Contains("importar", StringComparison.Ordinal) && !permiso.Contains("diligenciar", StringComparison.Ordinal)
            ? NivelPermiso.Consulta
            : NivelPermiso.Gestiona;
}
