// =====================================================================
// SITBIO - V007: acciones de acceso en bitacora_auditoria (HU-010, HU-012)
// Motor:     MongoDB Community 7.0
// Requisitos: RF-013, RNF-003, RNF-015; CA-HU-010-01 (registrar el 403).
// El script 02 ya está integrado y no se edita (ADR-005): este archivo
// reemplaza el validador de la colección con collMod. El validador es el
// mismo del script 02 más dos acciones:
//   - ACCESO_DENEGADO: la API respondió 403 a una acción sin permiso.
//   - CIERRE_SESION:   cierre voluntario o por inactividad (HU-012).
// Idempotente: se puede ejecutar más de una vez.
// Ejecución: mongosh sitbio_dev V007__bitacora_acciones.js
// =====================================================================

use('sitbio_dev');

db.runCommand({
   collMod: "bitacora_auditoria",
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
                      "CAMBIO_TENANT", "IMPORTAR_CSV",
                      "ACCESO_DENEGADO", "CIERRE_SESION"]
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

print("SITBIO V007: bitacora_auditoria acepta ACCESO_DENEGADO y CIERRE_SESION.");
