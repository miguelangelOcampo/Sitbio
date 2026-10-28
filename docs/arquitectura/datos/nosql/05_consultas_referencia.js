// =====================================================================
// SITBIO - Consultas de referencia (guía NoSQL semana 3)
// SOLO lectura (find y countDocuments) sobre la semilla de seeds/mongo.
// Toda consulta lleva tenant_id (RN-008). Resultados esperados y
// obtenidos: 05_resultados.md. Uso: ver la sección «Cómo ejecutar».
// =====================================================================
const TENANT_A = "11111111-1111-4111-8111-111111111111";
const TENANT_B = "22222222-2222-4222-8222-222222222222";
const MONITOR_01 = "a1c30000-0000-4000-8000-000000000001";
const ORDEN_05 = "a1b20000-0000-4000-8000-000000000005";

// A-05 = a1a10000-0000-4000-8000-000000000005; B-02 = b2a10000-...-000000000002
const corto = (id) => (id.startsWith("a1a1") ? "A-" : id.startsWith("b2a1") ? "B-" : "?-") + id.slice(-2);
const dia = (fecha) => fecha.toISOString().slice(0, 16).replace("T", " ");
const mostrar = (titulo, docs, detalle = () => "") => {
  print(`${titulo}: ${docs.length}`);
  docs.forEach((d) => print(`  ${corto(d._id)} ${detalle(d)}`.trimEnd()));
};
const intervenciones = db.intervenciones;

// C01 (Q02) historial de un equipo, más reciente primero; _id desempata
mostrar(
  "C01 historial del monitor 01",
  intervenciones
    .find({ tenant_id: TENANT_A, equipo_id: MONITOR_01 }, { tipo_mantenimiento: 1, fecha_intervencion: 1 })
    .sort({ fecha_intervencion: -1, _id: -1 })
    .toArray(),
  (d) => `${d.tipo_mantenimiento} ${dia(d.fecha_intervencion)}`,
);

// C02 (Q03) detalle por orden de trabajo
mostrar(
  "C02 detalle de la orden 05",
  intervenciones.find({ tenant_id: TENANT_A, orden_id: ORDEN_05 }).toArray(),
  (d) => `${d.tipo_mantenimiento} costo ${d.costo_total}`,
);

// C03 rango: inicio incluido ($gte), fin excluido ($lt), en UTC
mostrar(
  "C03 tenant A en septiembre de 2026",
  intervenciones
    .find(
      { tenant_id: TENANT_A, fecha_intervencion: { $gte: ISODate("2026-09-01T00:00:00Z"), $lt: ISODate("2026-10-01T00:00:00Z") } },
      { fecha_intervencion: 1 },
    )
    .sort({ fecha_intervencion: 1, _id: 1 })
    .toArray(),
  (d) => dia(d.fecha_intervencion),
);

// C04 $in con proyección de ID, tipo y fecha
mostrar(
  "C04 correctivos o calibraciones",
  intervenciones
    .find(
      { tenant_id: TENANT_A, tipo_mantenimiento: { $in: ["correctivo", "calibracion"] } },
      { _id: 1, tipo_mantenimiento: 1, fecha_intervencion: 1 },
    )
    .sort({ fecha_intervencion: 1, _id: 1 })
    .toArray(),
  (d) => `${d.tipo_mantenimiento} ${dia(d.fecha_intervencion)}`,
);

// C05 $elemMatch: ambas condiciones sobre el MISMO elemento del arreglo
const repuesto = (d, nombre) => d.repuestos.filter((r) => r.nombre === nombre).map((r) => `${r.nombre} x${r.cantidad}`).join(", ");
mostrar(
  "C05a tornillo de fijación con cantidad >= 2 ($elemMatch)",
  intervenciones
    .find({ tenant_id: TENANT_A, repuestos: { $elemMatch: { nombre: "Tornillo de fijación", cantidad: { $gte: 2 } } } }, { repuestos: 1 })
    .sort({ _id: 1 })
    .toArray(),
  (d) => repuesto(d, "Tornillo de fijación"),
);
mostrar(
  "C05b sensor de oclusión con cantidad >= 2 ($elemMatch)",
  intervenciones
    .find({ tenant_id: TENANT_A, repuestos: { $elemMatch: { nombre: "Sensor de oclusión", cantidad: { $gte: 2 } } } }, { _id: 1 })
    .sort({ _id: 1 })
    .toArray(),
);
// Contraejemplo: con rutas separadas cada condición puede cumplirla un elemento distinto
mostrar(
  "C05c sensor de oclusión con cantidad >= 2 (rutas separadas, contraejemplo)",
  intervenciones
    .find({ tenant_id: TENANT_A, "repuestos.nombre": "Sensor de oclusión", "repuestos.cantidad": { $gte: 2 } }, { repuestos: 1 })
    .sort({ _id: 1 })
    .toArray(),
  (d) => d.repuestos.map((r) => `${r.nombre} x${r.cantidad}`).join(", "),
);

// C06 null frente a ausente frente a presente
const contar = (filtro) => intervenciones.countDocuments({ tenant_id: TENANT_A, ...filtro });
mostrar(
  "C06 equipo_snapshot con valor null ($type null)",
  intervenciones.find({ tenant_id: TENANT_A, equipo_snapshot: { $type: "null" } }, { _id: 1 }).sort({ _id: 1 }).toArray(),
);
print(`C06 equipo_snapshot ausente ($exists false): ${contar({ equipo_snapshot: { $exists: false } })}`);
print(`C06 equipo_snapshot presente ($type object): ${contar({ equipo_snapshot: { $type: "object" } })}`);
print(`C06 equipo_snapshot: null (null o ausente): ${contar({ equipo_snapshot: null })}`);

// C07 campo dinámico anidado con notación de punto
mostrar(
  "C07 spo2 < 95",
  intervenciones.find({ tenant_id: TENANT_A, "camposDinamicos.spo2": { $lt: 95 } }, { "camposDinamicos.spo2": 1 }).sort({ _id: 1 }).toArray(),
  (d) => `spo2 ${d.camposDinamicos.spo2}`,
);
mostrar(
  "C07 variante spo2 < 98",
  intervenciones.find({ tenant_id: TENANT_A, "camposDinamicos.spo2": { $lt: 98 } }, { "camposDinamicos.spo2": 1 }).sort({ _id: 1 }).toArray(),
  (d) => `spo2 ${d.camposDinamicos.spo2}`,
);

// C08 costo total >= 200.000, el más costoso primero
mostrar(
  "C08 costo total >= 200.000",
  intervenciones
    .find({ tenant_id: TENANT_A, costo_total: { $gte: 200000 } }, { costo_total: 1 })
    .sort({ costo_total: -1, _id: 1 })
    .toArray(),
  (d) => `costo ${d.costo_total}`,
);

// C09 paginación de 3 en 3: skip frente a continuación por (fecha, _id)
const TAMANO = 3;
const orden = { fecha_intervencion: -1, _id: -1 };
const pagina = (filtro, salto) =>
  intervenciones.find({ tenant_id: TENANT_A, ...filtro }, { fecha_intervencion: 1 }).sort(orden).skip(salto).limit(TAMANO).toArray();
const pagina1 = pagina({}, 0);
mostrar("C09 página 1", pagina1, (d) => dia(d.fecha_intervencion));
const conSkip = pagina({}, TAMANO);
mostrar("C09 página 2 con skip(3)", conSkip, (d) => dia(d.fecha_intervencion));
const ultima = pagina1[pagina1.length - 1];
const conContinuacion = pagina(
  {
    $or: [
      { fecha_intervencion: { $lt: ultima.fecha_intervencion } },
      { fecha_intervencion: ultima.fecha_intervencion, _id: { $lt: ultima._id } },
    ],
  },
  0,
);
mostrar(`C09 página 2 con continuación después de ${corto(ultima._id)}`, conContinuacion, (d) => dia(d.fecha_intervencion));
const iguales = conSkip.map((d) => d._id).join() === conContinuacion.map((d) => d._id).join();
print(`C09 la página 2 coincide: ${iguales ? "sí" : "NO"}`);

// C10 aislamiento: cada tenant cuenta solo lo suyo
print("C10 conteo por tenant:");
print(`  tenant A: ${intervenciones.countDocuments({ tenant_id: TENANT_A })}`);
print(`  tenant B: ${intervenciones.countDocuments({ tenant_id: TENANT_B })}`);
print(`  tenant inexistente: ${intervenciones.countDocuments({ tenant_id: "99999999-9999-4999-8999-999999999999" })}`);
