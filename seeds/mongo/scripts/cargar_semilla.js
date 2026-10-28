// =====================================================================
// SITBIO - Semilla de laboratorio para MongoDB (guía NoSQL semana 2)
// Carga intervenciones y configuracion_campos de los dos tenants de
// laboratorio. Es idempotente: repone cada documento por _id con
// replaceOne + upsert, así que repetirla no aumenta los conteos.
// Nunca usa drop ni borra sin filtro. Uso: ver seeds/mongo/README.md.
// =====================================================================
const fs = require("fs");
const path = require("path");

const TENANTS = [
  "11111111-1111-4111-8111-111111111111", // Clínica Demo A
  "22222222-2222-4222-8222-222222222222", // Clínica Demo B
];
const MAX_REPUESTOS = 20;
const CAMPOS_SNAPSHOT = ["nombre", "marca", "modelo", "tipo_equipo", "ubicacion", "capturado_en"];

// relaxed: false conserva Int32 y Double; en modo relajado 0.0 se guardaría como Int32
// y el validador del script 02 rechazaría costo_total.
const carpetaDatos = path.join(__dirname, "..", "data");
const leer = (archivo) => EJSON.parse(fs.readFileSync(path.join(carpetaDatos, archivo), "utf8"), { relaxed: false });

const intervenciones = leer("intervenciones.ejson.json");
const configuraciones = leer("configuracion_campos.ejson.json");
const errores = [];

// 1. Contrato (04_modelo_y_decisiones.md) antes de escribir
for (const doc of intervenciones) {
  const id = doc._id;
  if (!TENANTS.includes(doc.tenant_id)) errores.push(`${id}: tenant_id no es de laboratorio`);
  if (doc._id !== doc.id_intervencion) errores.push(`${id}: _id debe ser igual a id_intervencion`);
  if (typeof doc.cerrada_firmada !== "boolean") errores.push(`${id}: falta cerrada_firmada`);
  if (!Array.isArray(doc.repuestos)) errores.push(`${id}: repuestos debe ser un arreglo`);
  else if (doc.repuestos.length > MAX_REPUESTOS) errores.push(`${id}: más de ${MAX_REPUESTOS} repuestos`);
  if (doc.cerrada_firmada === true) {
    const faltan = CAMPOS_SNAPSHOT.filter((c) => doc.equipo_snapshot == null || doc.equipo_snapshot[c] == null);
    if (faltan.length) errores.push(`${id}: firmada sin snapshot completo (${faltan.join(", ")})`);
  } else if (doc.equipo_snapshot !== null) {
    errores.push(`${id}: sin firmar, equipo_snapshot debe ser null`);
  }
}
for (const cfg of configuraciones) {
  if (!TENANTS.includes(cfg.tenant_id)) errores.push(`${cfg._id}: tenant_id no es de laboratorio`);
}

// 2. Colisiones con documentos ajenos
for (const doc of intervenciones) {
  const existente = db.intervenciones.findOne({ _id: doc._id }, { tenant_id: 1 });
  if (existente && existente.tenant_id !== doc.tenant_id) {
    errores.push(`${doc._id}: ya existe en intervenciones con otro tenant`);
  }
}
for (const cfg of configuraciones) {
  const existente = db.configuracion_campos.findOne({ _id: cfg._id }, { tenant_id: 1 });
  if (existente && existente.tenant_id !== cfg.tenant_id) {
    errores.push(`${cfg._id}: ya existe en configuracion_campos con otro tenant`);
  }
  const mismaVersion = db.configuracion_campos.findOne({
    tenant_id: cfg.tenant_id, tipo_equipo: cfg.tipo_equipo, version: cfg.version, _id: { $ne: cfg._id },
  });
  if (mismaVersion) {
    errores.push(`${cfg._id}: otro documento ya usa ${cfg.tipo_equipo} versión ${cfg.version} en ese tenant`);
  }
}

if (errores.length) {
  print(`No se cargó nada. ${errores.length} problema(s):`);
  errores.forEach((e) => print(`  - ${e}`));
  quit(1);
}

// 3. Reponer cada documento por _id
function reponer(coleccion, documentos) {
  const resumen = { insertados: 0, reemplazados: 0, sinCambios: 0 };
  for (const doc of documentos) {
    const r = db.getCollection(coleccion).replaceOne({ _id: doc._id }, doc, { upsert: true });
    if (r.upsertedCount) resumen.insertados++;
    else if (r.modifiedCount) resumen.reemplazados++;
    else resumen.sinCambios++;
  }
  print(`${coleccion}: ${documentos.length} documentos -> ${resumen.insertados} insertados, ${resumen.reemplazados} reemplazados, ${resumen.sinCambios} sin cambios`);
}

reponer("configuracion_campos", configuraciones);
reponer("intervenciones", intervenciones);
print("Semilla cargada. Ejecute verificar.js para comprobarla.");
