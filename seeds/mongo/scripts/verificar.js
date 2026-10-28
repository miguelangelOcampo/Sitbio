// =====================================================================
// SITBIO - Verificador de la semilla de laboratorio (guía NoSQL semana 2)
// Revisa solo los dos tenants de laboratorio: conteos, referencias rotas
// frente a referencias_postgres.json, tipos, regla snapshot <-> firmada,
// límite de repuestos y campos dinámicos obligatorios.
// Termina con «0 errores» o con código de salida 1.
// =====================================================================
const fs = require("fs");
const path = require("path");

const MAX_REPUESTOS = 20;
const CAMPOS_SNAPSHOT = ["nombre", "marca", "modelo", "tipo_equipo", "ubicacion", "capturado_en"];
const referencias = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "data", "referencias_postgres.json"), "utf8")).tenants;
const TENANTS = Object.keys(referencias);
const errores = [];
const avisos = [];

// Índices de lo que «existe» en PostgreSQL
const equipos = {};
for (const [tenant, ref] of Object.entries(referencias)) {
  for (const e of ref.equipos) equipos[e.id] = { ...e, tenant };
}

// 1. Conteos por tenant
print("Conteos por tenant:");
for (const [tenant, ref] of Object.entries(referencias)) {
  const total = db.intervenciones.countDocuments({ tenant_id: tenant });
  const firmadas = db.intervenciones.countDocuments({ tenant_id: tenant, cerrada_firmada: true });
  const configs = db.configuracion_campos.countDocuments({ tenant_id: tenant });
  print(`  ${ref.nombre}: ${total} intervenciones (${firmadas} firmadas, ${total - firmadas} sin firmar), ${configs} configuraciones`);
}

// 2. Tipos que el contrato exige (el validador ya los exige al escribir)
const tipos = [
  ["fecha_intervencion", "date"],
  ["costo_total", "double"],
  ["tiempo_atencion_min", "int"],
  ["cerrada_firmada", "bool"],
  ["camposDinamicos", "object"],
  ["repuestos", "array"],
];
for (const [campo, tipo] of tipos) {
  db.intervenciones
    .find({ tenant_id: { $in: TENANTS }, [campo]: { $not: { $type: tipo } } }, { _id: 1 })
    .forEach((d) => errores.push(`${d._id}: ${campo} no es de tipo ${tipo}`));
}

// 3. Referencias, snapshot, repuestos y campos dinámicos, documento por documento
const configuraciones = {};
db.configuracion_campos.find({ tenant_id: { $in: TENANTS }, estado: "activa" }).forEach((c) => {
  configuraciones[`${c.tenant_id}|${c.tipo_equipo}`] = c;
  const autor = referencias[c.tenant_id].usuarios.find((u) => u.id === c.creado_por);
  if (!autor) errores.push(`${c._id}: creado_por no existe en el tenant`);
});

const ordenesVistas = {};
db.intervenciones.find({ tenant_id: { $in: TENANTS } }).forEach((d) => {
  const ref = referencias[d.tenant_id];
  const equipo = equipos[d.equipo_id];

  if (!equipo) errores.push(`${d._id}: referencia rota, equipo_id ${d.equipo_id} no existe`);
  else if (equipo.tenant !== d.tenant_id) errores.push(`${d._id}: equipo_id ${d.equipo_id} es de otro tenant`);
  if (!ref.ordenes.includes(d.orden_id)) errores.push(`${d._id}: referencia rota, orden_id ${d.orden_id} no existe en el tenant`);
  if (!ref.usuarios.some((u) => u.id === d.tecnico_id && u.rol === "AC-03")) {
    errores.push(`${d._id}: referencia rota, tecnico_id ${d.tecnico_id} no es un técnico del tenant`);
  }

  const clave = `${d.tenant_id}|${d.orden_id}`;
  if (ordenesVistas[clave]) errores.push(`${d._id}: la orden ${d.orden_id} ya tiene otra intervención (${ordenesVistas[clave]})`);
  ordenesVistas[clave] = d._id;

  if (d.cerrada_firmada === true) {
    const faltan = CAMPOS_SNAPSHOT.filter((c) => d.equipo_snapshot == null || d.equipo_snapshot[c] == null);
    if (faltan.length) errores.push(`${d._id}: firmada sin snapshot completo (${faltan.join(", ")})`);
    if (d.estado_equipo_salida == null) errores.push(`${d._id}: firmada sin estado_equipo_salida`);
    if (equipo && d.equipo_snapshot && d.equipo_snapshot.ubicacion !== equipo.ubicacion_actual) {
      avisos.push(`${d._id}: cambio histórico, snapshot en «${d.equipo_snapshot.ubicacion}», hoy en «${equipo.ubicacion_actual}»`);
    }
  } else if (d.cerrada_firmada === false && d.equipo_snapshot !== null) {
    errores.push(`${d._id}: sin firmar, equipo_snapshot debe ser null`);
  }

  if (Array.isArray(d.repuestos) && d.repuestos.length > MAX_REPUESTOS) {
    errores.push(`${d._id}: ${d.repuestos.length} repuestos, el máximo es ${MAX_REPUESTOS}`);
  }

  const config = equipo && configuraciones[`${d.tenant_id}|${equipo.tipo_equipo}`];
  if (config && d.camposDinamicos) {
    config.campos
      .filter((c) => c.obligatorio && !(c.nombre in d.camposDinamicos))
      .forEach((c) => errores.push(`${d._id}: falta el campo dinámico obligatorio ${c.nombre}`));
  }
});

// 4. Resultado
if (avisos.length) {
  print("Avisos (no son errores):");
  avisos.forEach((a) => print(`  - ${a}`));
}
if (errores.length) {
  print("Errores:");
  errores.forEach((e) => print(`  - ${e}`));
}
print(`Resultado: ${errores.length} errores`);
if (errores.length) quit(1);
