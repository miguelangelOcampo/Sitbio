// =====================================================================
// SITBIO - Sistema de Información para la Trazabilidad y Gestión de
//          Mantenimientos Biomédicos
// Script 02: Creación de base de datos y colecciones (NoSQL)
// Motor:    MongoDB Community 7.0
// Documento de referencia: ADSO-ARQ-01 (ADR-002 - persistencia híbrida),
//                          DOC-03-ERS v1.4
// Ejecución: mongosh --port 27017 < 02_MongoDB_SITBIO.js
// =====================================================================

// ---------------------------------------------------------------------
// 1. SELECCIÓN/CREACIÓN DE LA BASE DE DATOS
// ---------------------------------------------------------------------
use('sitbio_dev');

// =====================================================================
// 2. COLECCIÓN: intervenciones
// Documento BSON heterogéneo por tipo de equipo (ADR-002).
// La propiedad camposDinamicos{} se define por tenant + tipo_equipo
// en la colección configuracion_campos (RF-006).
// RN-004: inalterable tras cierre firmado de la OT.
// Índices: IDX-003 compuesto y IDX-004 multikey.
// =====================================================================
db.createCollection("intervenciones", {
   validator: {
      $jsonSchema: {
         bsonType: "object",
         required: ["id_intervencion", "tenant_id", "equipo_id", "orden_id",
                    "tipo_mantenimiento", "fecha_intervencion", "tecnico_id"],
         properties: {
            id_intervencion: {
               bsonType: "string",
               description: "UUID de la intervención (correlaciona con SQL)"
            },
            tenant_id: {
               bsonType: "string",
               description: "Aislamiento multi-tenant (RN-008) - obligatorio"
            },
            equipo_id: {
               bsonType: "string",
               description: "FK lógica a equipo_biomedico.id_equipo en PostgreSQL"
            },
            orden_id: {
               bsonType: "string",
               description: "FK lógica a orden_trabajo.id_orden en PostgreSQL"
            },
            tipo_mantenimiento: {
               enum: ["correctivo", "preventivo", "calibracion", "predictivo"],
               description: "Tipo de intervención realizada"
            },
            diagnostico: {
               bsonType: "string",
               description: "Descripción técnica del diagnóstico"
            },
            acciones_realizadas: {
               bsonType: "string"
            },
            repuestos: {
               bsonType: "array",
               items: {
                  bsonType: "object",
                  required: ["nombre", "cantidad"],
                  properties: {
                     nombre:        { bsonType: "string" },
                     cantidad:      { bsonType: "int" },
                     costo_unitario:{ bsonType: "double" }
                  }
               },
               description: "Repuestos utilizados en la intervención"
            },
            costo_total: {
               bsonType: "double",
               description: "Costo total en COP"
            },
            tiempo_atencion_min: {
               bsonType: "int",
               description: "Tiempo de atención en minutos"
            },
            estado_equipo_salida: {
               enum: ["Operativo", "En mantenimiento", "Fuera de servicio", "Baja"],
               description: "Estado del equipo al finalizar (RN-005/RN-007)"
            },
            tecnico_id: {
               bsonType: "string",
               description: "FK lógica a usuario.id_usuario"
            },
            fecha_intervencion: {
               bsonType: "date"
            },
            cerrada_firmada: {
               bsonType: "bool",
               description: "TRUE = inalterable (RN-004)"
            },
            // ---- MOTOR DE CAMPOS DINÁMICOS (RF-006 / RF-023) ----
            camposDinamicos: {
               bsonType: "object",
               description: "Parámetros específicos definidos en configuracion_campos por tenant + tipo_equipo"
               // Sin propiedades fijas: varía por tipo de equipo.
               // Ejemplo monitor: {"frecuencia_cardiaca": 72, "spo2": 98, "nibp_sistolica": 120}
               // Ejemplo bomba:   {"tasa_goteo_ml_h": 125, "volumen_total_ml": 1000}
            }
         }
      }
   },
   validationLevel: "strict",
   validationAction: "error"
});

// IDX-003: línea de tiempo de un equipo (compuesto)
db.intervenciones.createIndex(
   { tenant_id: 1, equipo_id: 1, fecha_intervencion: -1 },
   { name: "IDX_003_intervenciones_tenant_equipo" }
);

// IDX-004: consultas analíticas por tipo de parám. dinámico (multikey)
db.intervenciones.createIndex(
   { tenant_id: 1, "camposDinamicos.tipo": 1 },
   { name: "IDX_004_intervenciones_campos_dinamicos", sparse: true }
);

// Búsqueda de una OT específica (cross-store con PostgreSQL)
db.intervenciones.createIndex(
   { orden_id: 1 },
   { name: "ix_intervenciones_orden" }
);

// =====================================================================
// 3. COLECCIÓN: configuracion_campos
// Motor de campos dinámicos: define el formulario de intervención
// por tenant + tipo_equipo SIN migrar el esquema relacional (ADR-002).
// =====================================================================
db.createCollection("configuracion_campos", {
   validator: {
      $jsonSchema: {
         bsonType: "object",
         required: ["tenant_id", "tipo_equipo", "version", "campos"],
         properties: {
            tenant_id: { bsonType: "string" },
            tipo_equipo: {
               bsonType: "string",
               description: "Debe coincidir con catalogo_elemento de tipo_equipo en PostgreSQL"
            },
            version: {
               bsonType: "int",
               description: "Versionado de la configuración (RF-024)"
            },
            estado: {
               enum: ["activa", "historica"],
               description: "Solo una versión activa por tenant + tipo_equipo"
            },
            creado_por: { bsonType: "string" },   // usuario_id
            fecha_creacion: { bsonType: "date" },
            campos: {
               bsonType: "array",
               items: {
                  bsonType: "object",
                  required: ["nombre", "tipo_dato", "obligatorio"],
                  properties: {
                     nombre: {
                        bsonType: "string",
                        description: "Clave usada dentro de camposDinamicos"
                     },
                     etiqueta: { bsonType: "string" },
                     tipo_dato: {
                        enum: ["numero", "texto", "booleano", "fecha", "seleccion", "numero_decimal"]
                     },
                     obligatorio: { bsonType: "bool" },
                     opciones: {
                        bsonType: "array",
                        items: { bsonType: "string" },
                        description: "Solo si tipo_dato = 'seleccion'"
                     },
                     unidad: { bsonType: "string" }   // Ej: 'ml/h', 'mmHg', '%'
                  }
               }
            }
         }
      }
   },
   validationLevel: "strict",
   validationAction: "error"
});

// Una configuración activa por tenant + tipo_equipo
db.configuracion_campos.createIndex(
   { tenant_id: 1, tipo_equipo: 1, version: -1 },
   { unique: true, name: "uq_config_tenant_tipo_version" }
);

// =====================================================================
// 4. COLECCIÓN: bitacora_auditoria
// RF-013 / RNF-015: registro del 100% de operaciones críticas.
// Retención mínima: 1 año. Datos sensibles (Ley 1581/2012).
// Insert-only: la aplicación NO updatea ni borra documentos.
// =====================================================================
db.createCollection("bitacora_auditoria", {
   validator: {
      $jsonSchema: {
         bsonType: "object",
         required: ["id_evento", "tenant_id", "usuario_id", "accion",
                    "entidad", "fecha_hora"],
         properties: {
            id_evento:   { bsonType: "string" },   // UUID
            tenant_id:   { bsonType: "string" },
            usuario_id:  { bsonType: "string" },
            accion: {
               enum: ["LOGIN_OK", "LOGIN_FALLIDO", "CREAR", "ACTUALIZAR",
                      "ELIMINAR", "EXPORTAR", "CIERRE_OT", "CAMBIO_ROL",
                      "CAMBIO_TENANT", "IMPORTAR_CSV"]
            },
            entidad: {
               bsonType: "string",
               description: "Tabla/colección afectada: 'usuario', 'equipo_biomedico', 'orden_trabajo', ..."
            },
            entidad_id: { bsonType: "string" },
            ip_origen:  { bsonType: "string" },
            fecha_hora: { bsonType: "date" },
            detalle: {
               bsonType: "object",
               description: "Snapshot o diff de la operación (sin contraseñas ni datos sensibles)"
            }
         }
      }
   },
   validationLevel: "strict",
   validationAction: "error"
});

db.bitacora_auditoria.createIndex(
   { tenant_id: 1, fecha_hora: -1 },
   { name: "ix_bitacora_tenant_fecha" }
);

db.bitacora_auditoria.createIndex(
   { usuario_id: 1, accion: 1 },
   { name: "ix_bitacora_usuario_accion" }
);

// TTL de respaldo documental a 13 meses (retención mínima legal: 1 año)
// La retención primaria se gestiona con respaldos mongodump (RPO 24h).
db.bitacora_auditoria.createIndex(
   { fecha_hora: 1 },
   { expireAfterSeconds: 31536000, name: "ttl_bitacora_12m", partialFilterExpression: { archivado: true } }
);

// =====================================================================
// 5. DATOS DE EJEMPLO (opcional - entorno DEV)
// =====================================================================
db.configuracion_campos.insertOne({
   tenant_id: "00000000-0000-0000-0000-000000000001",  // Reemplazar por UUID real del tenant
   tipo_equipo: "monitor_multiparametro",
   version: 1,
   estado: "activa",
   fecha_creacion: new Date(),
   campos: [
      { nombre: "frecuencia_cardiaca", etiqueta: "Frecuencia cardíaca", tipo_dato: "numero",  obligatorio: true,  unidad: "lpm" },
      { nombre: "spo2",                etiqueta: "Saturación O₂",       tipo_dato: "numero",  obligatorio: true,  unidad: "%" },
      { nombre: "nibp_sistolica",      etiqueta: "Presión sistólica",   tipo_dato: "numero",  obligatorio: false, unidad: "mmHg" },
      { nombre: "alarma_activa",       etiqueta: "¿Alarma activa?",     tipo_dato: "booleano",obligatorio: false }
   ]
});

db.configuracion_campos.insertOne({
   tenant_id: "00000000-0000-0000-0000-000000000001",
   tipo_equipo: "bomba_infusion",
   version: 1,
   estado: "activa",
   fecha_creacion: new Date(),
   campos: [
      { nombre: "tasa_goteo_ml_h",  etiqueta: "Tasa de goteo",    tipo_dato: "numero_decimal", obligatorio: true,  unidad: "ml/h" },
      { nombre: "volumen_total_ml", etiqueta: "Volumen total",    tipo_dato: "numero",         obligatorio: true,  unidad: "ml" },
      { nombre: "via_acceso",       etiqueta: "Vía de acceso",    tipo_dato: "seleccion",      obligatorio: false, opciones: ["Periférica", "Central", "Intraósea"] }
   ]
});

// =====================================================================
// FIN DEL SCRIPT 02 - MongoDB
// =====================================================================
print("SITBIO: base de datos 'sitbio_dev' y colecciones creadas correctamente.");
