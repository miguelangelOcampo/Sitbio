namespace Sitbio.SemillaDemo;

/// <summary>Datos fijos de la institución de demostración. Los datos son ficticios (EX-05).</summary>
public static class Datos
{
    public static readonly Guid Tenant = Guid.Parse("0de30000-0000-4000-8000-00000000d3e0");
    public const string Institucion = "Hospital Demo SITBIO";
    public const string NitInstitucion = "900.555.001-1";
    public const string Dominio = "hospitaldemo.sitbio.co";

    public const string Uci = "UCI Adultos";
    public const string Urgencias = "Urgencias";
    public const string Quirofano1 = "Quirófano 1";
    public const string Quirofano2 = "Quirófano 2";
    public const string Hospitalizacion = "Hospitalización Piso 3";
    public const string Neonatos = "Neonatos";
    public const string Laboratorio = "Laboratorio Clínico";
    public const string Imagenologia = "Imagenología";
    public const string MonitorMultiparametro = "Monitor multiparámetro";
    public const string Bomba = "Bomba de infusión";
    public const string Desfibrilador = "Desfibrilador";
    public const string Ventilador = "Ventilador mecánico";
    public const string Electrocardiografo = "Electrocardiógrafo";
    public const string Tensiometro = "Tensiómetro digital";
    public const string Incubadora = "Incubadora neonatal";
    public const string Electrobisturi = "Electrobisturí";
    public const string Pulsioximetro = "Pulsioxímetro";
    public const string Centrifuga = "Centrífuga de laboratorio";
    public const string Philips = "Philips";
    public const string BBraun = "B. Braun";
    public const string Drager = "Dräger";
    public const string Medtronic = "Medtronic";
    public const string Mindray = "Mindray";
    public const string Baxter = "Baxter";
    public const string Zoll = "Zoll";
    public const string Hamilton = "Hamilton";
    public const string GeHealthcare = "GE Healthcare";
    public const string WelchAllyn = "Welch Allyn";
    public const string Omron = "Omron";
    public const string Tecnico = "AC-03";
    public const string Clinico = "AC-04";
    public const string Ingeniero = "AC-02";

    public sealed record UsuarioDemo(string Clave, string Nombres, string Apellidos, string Rol, string? Area = null, bool Inactivo = false)
    {
        public string Correo => $"{Clave}@{Dominio}";

        public string NombreCompleto => $"{Nombres} {Apellidos}";
    }

    public static readonly IReadOnlyList<UsuarioDemo> Usuarios =
    [
        new("admin", "Sofía", "Ramírez Toro", "AC-01"),
        new("laura.gomez", "Laura", "Gómez Arango", Ingeniero),
        new("andres.castano", "Andrés", "Castaño Mejía", Ingeniero),
        new("carlos.munoz", "Carlos", "Muñoz Pérez", Tecnico),
        new("diana.ospina", "Diana", "Ospina Vélez", Tecnico),
        new("jorge.restrepo", "Jorge", "Restrepo Gil", Tecnico),
        new("pedro.gil", "Pedro", "Gil Zapata", Tecnico, Inactivo: true),
        new("paula.herrera", "Paula", "Herrera Cano", Clinico, "UCI"),
        new("mateo.vargas", "Mateo", "Vargas Ríos", Clinico, Urgencias),
        new("natalia.rios", "Natalia", "Ríos Salazar", Clinico, "Quirófano"),
        new("ricardo.salazar", "Ricardo", "Salazar Mesa", "AC-05"),
        new("marta.londono", "Marta", "Londoño Uribe", "AC-06"),
        new("felipe.arango", "Felipe", "Arango Duque", "AC-07"),
    ];

    public static UsuarioDemo Usuario(string clave) => Usuarios.Single(u => u.Clave == clave);

    public static readonly string[] Ubicaciones =
    [
        Uci, Urgencias, Quirofano1, Quirofano2, Hospitalizacion, Neonatos, Laboratorio, Imagenologia,
    ];

    public sealed record ProveedorDemo(string Nombre, string Nit, string Tipo, string Contacto, string Correo, string Telefono);

    public static readonly IReadOnlyList<ProveedorDemo> Proveedores =
    [
        new("Biomédica Andina SAS", "900.123.456-7", "Servicio técnico", "Juliana Pardo", "soporte@biomedicaandina.co", "6044445566"),
        new("Equipos Médicos del Valle", "900.987.654-3", "Distribuidor", "Hernán Lozano", "ventas@emvalle.co", "6023334455"),
        new("Metrología Clínica Ltda", "901.222.333-1", "Laboratorio de calibración", "Camila Duarte", "calibraciones@metroclinica.co", "6017778899"),
        new("Servicios Técnicos Hospitalarios", "901.444.555-9", "Servicio técnico", "Óscar Quintero", "ordenes@sth.co", "6046667788"),
    ];

    public static readonly string[] TiposFalla =
    [
        "No enciende", "Alarma sin causa aparente", "Lectura errónea", "Daño físico", "Falla de batería", "Error de software",
    ];

    public sealed record TipoDemo(string Nombre, string ClaseRiesgo, int? MesesCalibracion, int DiasPreventivo, string ProtocoloPreventivo);

    public static readonly IReadOnlyList<TipoDemo> Tipos =
    [
        new(MonitorMultiparametro, "IIb", 12, 90, "Inspección visual, prueba de alarmas, verificación de ECG, SpO2 y PNI con simulador"),
        new(Bomba, "IIb", 12, 60, "Verificación de flujo con analizador, prueba de oclusión y limpieza del mecanismo"),
        new(Desfibrilador, "III", 6, 45, "Prueba de descarga con analizador, revisión de palas y prueba de batería"),
        new(Ventilador, "III", 6, 45, "Cambio de filtros, prueba de fugas, verificación de volúmenes y presiones"),
        new(Electrocardiografo, "IIa", 12, 180, "Verificación de derivaciones con simulador y revisión de cables"),
        new(Tensiometro, "IIa", 12, 180, "Verificación de presión contra manómetro patrón y prueba de fugas del brazalete"),
        new(Incubadora, "IIb", 12, 60, "Verificación de temperatura y humedad, limpieza de filtros y prueba de alarmas"),
        new(Electrobisturi, "IIb", 12, 90, "Medición de potencia de salida y prueba de electrodo de retorno"),
        new(Pulsioximetro, "IIa", null, 180, "Verificación con simulador de SpO2 y revisión del sensor"),
        new(Centrifuga, "I", null, 180, "Verificación de velocidad con tacómetro y revisión del freno"),
    ];

    public sealed record EquipoDemo(string Placa, string Tipo, string Marca, string Modelo, string Ubicacion, int Proveedor, int AnioAdquisicion)
    {
        public Guid Id { get; set; }

        public int Ocupado { get; set; }

        public bool Operativo { get; set; } = true;

        /// <summary>Sin plan, sin fallas y sin renovar la calibración: queda para mostrar alertas o la baja.</summary>
        public string? Rezago { get; init; }

        public DateOnly? VenceCalibracion { get; set; }
    }

    public static List<EquipoDemo> Equipos() =>
    [
        new("HDS-0001", MonitorMultiparametro, Mindray, "BeneView T5", Uci, 0, 2019),
        new("HDS-0002", MonitorMultiparametro, Mindray, "BeneView T5", Uci, 0, 2019),
        new("HDS-0003", MonitorMultiparametro, Philips, "IntelliVue MX450", Uci, 1, 2021),
        new("HDS-0004", MonitorMultiparametro, Philips, "IntelliVue MX450", Urgencias, 1, 2021),
        new("HDS-0005", MonitorMultiparametro, Mindray, "ePM 10", Urgencias, 0, 2022),
        new("HDS-0006", MonitorMultiparametro, Philips, "IntelliVue MX450", Quirofano1, 1, 2020),
        new("HDS-0007", Bomba, BBraun, "Infusomat Space", Uci, 1, 2020),
        new("HDS-0008", Bomba, BBraun, "Infusomat Space", Uci, 1, 2020),
        new("HDS-0009", Bomba, Baxter, "Sigma Spectrum", Hospitalizacion, 1, 2018),
        new("HDS-0010", Bomba, Baxter, "Sigma Spectrum", Hospitalizacion, 1, 2018),
        new("HDS-0011", Bomba, BBraun, "Perfusor Space", Urgencias, 1, 2022),
        new("HDS-0012", Bomba, BBraun, "Perfusor Space", Neonatos, 1, 2023),
        new("HDS-0013", Desfibrilador, Zoll, "R Series", Urgencias, 3, 2017),
        new("HDS-0014", Desfibrilador, Philips, "HeartStart XL+", Uci, 1, 2020),
        new("HDS-0015", Desfibrilador, Zoll, "R Series", Quirofano2, 3, 2019),
        new("HDS-0016", Ventilador, Drager, "Evita V500", Uci, 0, 2020),
        new("HDS-0017", Ventilador, Drager, "Evita V500", Uci, 0, 2021),
        new("HDS-0018", Ventilador, Hamilton, "C3", Neonatos, 0, 2022),
        new("HDS-0019", Electrocardiografo, GeHealthcare, "MAC 2000", Urgencias, 3, 2018) { Rezago = "por_vencer" },
        new("HDS-0020", Electrocardiografo, GeHealthcare, "MAC 2000", Hospitalizacion, 3, 2019),
        new("HDS-0021", Tensiometro, WelchAllyn, "Connex Spot", Hospitalizacion, 1, 2016) { Rezago = "vencida" },
        new("HDS-0022", Tensiometro, WelchAllyn, "Connex Spot", Hospitalizacion, 1, 2019),
        new("HDS-0023", Tensiometro, Omron, "HBP-1320", Urgencias, 1, 2021),
        new("HDS-0024", Tensiometro, Omron, "HBP-1320", Imagenologia, 1, 2021),
        new("HDS-0025", Incubadora, Drager, "Isolette 8000", Neonatos, 0, 2019),
        new("HDS-0026", Incubadora, Drager, "Isolette 8000", Neonatos, 0, 2020),
        new("HDS-0027", Electrobisturi, Medtronic, "Valleylab FT10", Quirofano1, 3, 2021),
        new("HDS-0028", Electrobisturi, Medtronic, "Valleylab FT10", Quirofano2, 3, 2021),
        new("HDS-0029", Pulsioximetro, Medtronic, "Nellcor PM10N", Urgencias, 1, 2022),
        new("HDS-0030", Pulsioximetro, Medtronic, "Nellcor PM10N", Hospitalizacion, 1, 2022),
        new("HDS-0031", Centrifuga, "Eppendorf", "5702", Laboratorio, 1, 2017),
        new("HDS-0032", Centrifuga, "Hettich", "EBA 200", Laboratorio, 1, 2012) { Rezago = "baja" },
    ];

    /// <summary>Campos dinámicos de la hoja de intervención por tipo (HU-020).</summary>
    public static readonly IReadOnlyDictionary<string, object[]> Campos = new Dictionary<string, object[]>
    {
        [Bomba] =
        [
            new { nombre = "tasa_de_goteo", etiqueta = "Tasa de goteo verificada", tipoDato = "numero_decimal", obligatorio = true, unidad = "ml/h" },
            new { nombre = "prueba_de_oclusion", etiqueta = "Prueba de oclusión", tipoDato = "seleccion", obligatorio = false, opciones = new[] { "Aprobada", "No aprobada" } },
        ],
        [MonitorMultiparametro] =
        [
            new { nombre = "prueba_de_alarmas", etiqueta = "Prueba de alarmas correcta", tipoDato = "booleano", obligatorio = true },
            new { nombre = "corriente_de_fuga", etiqueta = "Corriente de fuga", tipoDato = "numero_decimal", obligatorio = false, unidad = "µA" },
        ],
        [Desfibrilador] =
        [
            new { nombre = "energia_entregada", etiqueta = "Energía entregada a 200 J", tipoDato = "numero_decimal", obligatorio = true, unidad = "J" },
        ],
    };

    public sealed record Falla(string Descripcion, string Diagnostico, string Acciones, (string Nombre, decimal Costo)[] Repuestos);

    public static readonly IReadOnlyDictionary<string, Falla[]> Fallas = new Dictionary<string, Falla[]>
    {
        ["No enciende"] =
        [
            new("No prende al conectarlo a la toma.", "Fusible de entrada abierto por sobretensión", "Se reemplazó el fusible y se verificó la fuente de alimentación", [("Fusible 2 A", 8000m)]),
            new("La pantalla queda en negro al encender.", "Fuente de poder interna defectuosa", "Se cambió la fuente de poder y se hicieron pruebas de funcionamiento", [("Fuente de poder", 380000m)]),
        ],
        ["Alarma sin causa aparente"] =
        [
            new("Suena la alarma técnica aunque el paciente está estable.", "Sensor con conector flojo", "Se ajustó el conector y se verificó con simulador", []),
            new("La alarma de oclusión se activa sola.", "Sensor de presión descalibrado", "Se calibró el sensor y se verificó con analizador", [("Sensor de presión", 145000m)]),
        ],
        ["Lectura errónea"] =
        [
            new("Las mediciones no coinciden con el equipo de respaldo.", "Desviación en la medición por desgaste del transductor", "Se ajustó la curva de calibración y se verificó contra patrón", []),
            new("La SpO2 marca valores muy bajos.", "Sensor de SpO2 deteriorado", "Se reemplazó el sensor y se verificó con simulador", [("Sensor SpO2 adulto", 210000m)]),
        ],
        ["Daño físico"] =
        [
            new("Se cayó del soporte y la carcasa quedó partida.", "Carcasa fracturada sin daño interno", "Se cambió la carcasa y se verificó el funcionamiento", [("Carcasa posterior", 260000m)]),
            new("El cable de paciente está pelado.", "Cable de paciente con aislamiento roto", "Se reemplazó el cable de paciente", [("Cable de paciente", 180000m)]),
        ],
        ["Falla de batería"] =
        [
            new("La batería dura menos de 10 minutos.", "Batería al final de su vida útil", "Se reemplazó la batería y se hizo ciclo de carga completo", [("Batería de litio", 320000m)]),
            new("No carga cuando está conectado.", "Circuito de carga con conector sulfatado", "Se limpió el conector y se verificó la carga", []),
        ],
        ["Error de software"] =
        [
            new("Se reinicia solo durante el uso.", "Error de firmware conocido por el fabricante", "Se actualizó el firmware a la versión recomendada", []),
            new("Muestra un código de error al iniciar.", "Configuración corrupta", "Se restauró la configuración de fábrica y se reconfiguró", []),
        ],
    };
}
