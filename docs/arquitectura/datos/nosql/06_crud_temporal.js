// =====================================================================
// SITBIO - CRUD temporal (guía NoSQL semana 3)
// Solo toca documentos con _id TMP-* y temporal: true del tenant A.
// Los documentos cumplen el validador estricto del script 02 (tipos
// Int32, Double y Date). Debe terminar con cero temporales.
// =====================================================================
const TENANT_A = "11111111-1111-4111-8111-111111111111";
const IDS = ["TMP-001", "TMP-002", "TMP-003"];
const temporales = { _id: { $in: IDS }, temporal: true };
const intervenciones = db.intervenciones;

const estado = (paso) =>
  print(`${paso}: temporales = ${intervenciones.countDocuments(temporales)}, tenant A = ${intervenciones.countDocuments({ tenant_id: TENANT_A })}`);

// Documento válido para el validador: no firmado, sin snapshot (contrato de 04_modelo_y_decisiones.md)
const temporal = (id, n) => ({
  _id: id,
  id_intervencion: id,
  temporal: true,
  tenant_id: TENANT_A,
  equipo_id: "a1c30000-0000-4000-8000-000000000001",
  orden_id: `a1b20000-0000-4000-8000-0000000009${n}0`,
  tecnico_id: "a1d40000-0000-4000-8000-000000000001",
  tipo_mantenimiento: "preventivo",
  fecha_intervencion: ISODate("2026-10-27T13:00:00Z"),
  diagnostico: "Documento temporal de práctica.",
  acciones_realizadas: "Ninguna.",
  repuestos: [],
  costo_total: Double(0),
  tiempo_atencion_min: NumberInt(30),
  cerrada_firmada: false,
  camposDinamicos: { frecuencia_cardiaca: NumberInt(70), spo2: NumberInt(97) },
  equipo_snapshot: null,
});

if (intervenciones.countDocuments({ _id: { $in: IDS } }) > 0) {
  print("Ya existen documentos TMP-*: ejecute los pasos 5 y 6 o revise antes de continuar.");
  quit(1);
}

estado("1 conteo inicial");

const r2 = intervenciones.insertOne(temporal("TMP-001", 1));
print(`2 insertOne: insertado ${r2.insertedId}`);
estado("2 después de insertOne");

const r3 = intervenciones.insertMany([temporal("TMP-002", 2), temporal("TMP-003", 3)]);
print(`3 insertMany: ${Object.keys(r3.insertedIds).length} insertados`);
estado("3 después de insertMany");

const antes = intervenciones.findOne({ _id: "TMP-001" }, { tiempo_atencion_min: 1, "camposDinamicos.spo2": 1 });
// NumberInt deja explícito el tipo Int32. mongosh también guarda 15 como Int32, pero un
// incremento con decimales (1.5) convertiría el campo en double y el validador lo rechazaría.
const r4 = intervenciones.updateOne(
  { _id: "TMP-001", temporal: true },
  { $set: { "camposDinamicos.spo2": NumberInt(93) }, $inc: { tiempo_atencion_min: NumberInt(15) } },
);
const despues = intervenciones.findOne({ _id: "TMP-001" }, { tiempo_atencion_min: 1, "camposDinamicos.spo2": 1 });
print(`4 updateOne: matchedCount ${r4.matchedCount}, modifiedCount ${r4.modifiedCount}`);
print(`  antes: tiempo ${antes.tiempo_atencion_min}, spo2 ${antes.camposDinamicos.spo2}`);
print(`  después: tiempo ${despues.tiempo_atencion_min}, spo2 ${despues.camposDinamicos.spo2}`);

const r5 = intervenciones.deleteOne({ _id: "TMP-001", temporal: true });
print(`5 deleteOne: deletedCount ${r5.deletedCount}`);

const r6 = intervenciones.deleteMany({ _id: { $in: ["TMP-002", "TMP-003"] }, temporal: true });
print(`6 deleteMany: deletedCount ${r6.deletedCount}`);

estado("7 conteo final");
