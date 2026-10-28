# Consultas de Referencia y CRUD: Resultados Esperados y Obtenidos - SITBIO

Prueba conceptual de la guía NoSQL semana 3 sobre la semilla de laboratorio (`seeds/mongo`). Los resultados **esperados** se escribieron antes de ejecutar, a partir de los datos de la semilla; los **obtenidos** se copiaron de la salida de los scripts.

Abreviaturas: `A-05` = `a1a10000-0000-4000-8000-000000000005` (tenant A, Clínica Demo A); `B-02` = `b2a10000-0000-4000-8000-000000000002` (tenant B, Clínica Demo B).

## Cómo ejecutar

Requisito: la semilla copiada, cargada y verificada según `seeds/mongo/README.md` («Resultado: 0 errores»). El primer comando de ese README (`docker cp seeds/mongo/. sitbio-mongo:/tmp/semilla`) crea la carpeta `/tmp/semilla` del contenedor; hay que repetirlo si el contenedor se recreó (ver la bitácora). Desde la raíz del repositorio:

```bash
docker cp docs/arquitectura/datos/nosql/05_consultas_referencia.js sitbio-mongo:/tmp/semilla/05_consultas_referencia.js
```

```bash
docker cp docs/arquitectura/datos/nosql/06_crud_temporal.js sitbio-mongo:/tmp/semilla/06_crud_temporal.js
```

```bash
docker exec sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin sitbio_dev /tmp/semilla/05_consultas_referencia.js'
```

```bash
docker exec sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin sitbio_dev /tmp/semilla/06_crud_temporal.js'
```

En Git Bash, si las rutas `/tmp/...` se convierten en rutas de Windows, ejecutar antes `export MSYS_NO_PATHCONV=1`.

## Consultas (05_consultas_referencia.js, solo lectura)

| ID | Requisito | Filtro | Proyección | Orden | Resultado esperado | Obtenido | Diferencia |
|---|---|---|---|---|---|---|---|
| C01 | Q02: historial del monitor 01 del tenant A | `tenant_id` A, `equipo_id` `a1c3…01` | id, tipo, fecha | `fecha_intervencion` desc, `_id` desc | 2: `A-02` (6-oct), `A-01` (2-sep) | 2: `A-02` (6-oct 15:30), `A-01` (2-sep 13:00) | Ninguna |
| C02 | Q03: detalle de la orden `a1b2…05` | `tenant_id` A, `orden_id` `a1b2…05` | documento completo | — | 1: `A-05` (correctivo, costo 404.000) | 1: `A-05` (correctivo, costo 404000) | Ninguna |
| C03 | Tenant A en septiembre de 2026 (UTC) | `fecha_intervencion` ≥ 2026-09-01 y < 2026-10-01 | id, fecha | `fecha_intervencion` asc, `_id` asc | 4: `A-01`, `A-03`, `A-05`, `A-07` | 4: `A-01`, `A-03`, `A-05`, `A-07` | Ninguna |
| C04 | Correctivos o calibraciones del tenant A | `tipo_mantenimiento` `$in` [correctivo, calibracion] | id, tipo, fecha | `fecha_intervencion` asc, `_id` asc | 5: `A-03`, `A-05`, `A-07`, `A-02`, `A-08` | 5: `A-03`, `A-05`, `A-07`, `A-02`, `A-08` | Ninguna |
| C05a | Repuesto «Tornillo de fijación» con cantidad ≥ 2 | `repuestos` `$elemMatch` {nombre, cantidad ≥ 2} | id, repuestos | `_id` asc | 1: `A-05` (cantidad 4) | 1: `A-05` (Tornillo de fijación x4) | Ninguna |
| C05b | Repuesto «Sensor de oclusión» con cantidad ≥ 2 | `repuestos` `$elemMatch` {nombre, cantidad ≥ 2} | id | `_id` asc | 0 (el sensor tiene cantidad 1) | 0 | Ninguna |
| C05c | Contraejemplo de C05b con rutas separadas | `repuestos.nombre` = «Sensor de oclusión» y `repuestos.cantidad` ≥ 2 | id | `_id` asc | 1: `A-05`, falso positivo (el nombre lo cumple el sensor y la cantidad el tornillo) | 1: `A-05` (Sensor de oclusión x1, Puerta de casete x1, Tornillo de fijación x4) | Ninguna |
| C06 | `equipo_snapshot` null, ausente y presente en el tenant A | `$type: "null"` / `$exists: false` / `$type: "object"` / `{equipo_snapshot: null}` | id | `_id` asc | 3 (`A-04`, `A-06`, `A-08`) / 0 / 5 / 3 | 3 (`A-04`, `A-06`, `A-08`) / 0 / 5 / 3 | Ninguna |
| C07 | Campo dinámico anidado del tenant A | `camposDinamicos.spo2` < 95; variante < 98 | id, spo2 | `_id` asc | 0; variante: 1 (`A-02`, spo2 97) | 0; variante: 1 (`A-02`, spo2 97) | Ninguna |
| C08 | Intervenciones costosas del tenant A | `costo_total` ≥ 200.000 | id, costo | `costo_total` desc, `_id` asc | 3: `A-05` (404.000), `A-08` (320.000), `A-02` (275.000) | 3: `A-05` (404000), `A-08` (320000), `A-02` (275000) | Ninguna |
| C09 | Paginación de 3 en 3 del tenant A | `tenant_id` A; página 2 por continuación: fecha < última vista, o igual fecha y `_id` menor | id, fecha | `fecha_intervencion` desc, `_id` desc | Página 1: `A-08`, `A-06`, `A-04`. Página 2 con `skip(3)`: `A-02`, `A-07`, `A-05`. Página 2 con continuación: la misma | Página 1: `A-08`, `A-06`, `A-04`. Página 2 con `skip(3)`: `A-02`, `A-07`, `A-05`. Con continuación después de `A-04`: la misma («coincide: sí») | Ninguna |
| C10 | Aislamiento por tenant (RN-008) | `countDocuments` por `tenant_id` | — | — | A = 8, B = 4, tenant inexistente = 0 | A = 8, B = 4, tenant inexistente = 0 | Ninguna |

## CRUD temporal (06_crud_temporal.js)

| Paso | Operación | Resultado esperado | Obtenido | Diferencia |
|---|---|---|---|---|
| 1 | Conteo inicial | 0 temporales; tenant A = 8 | temporales = 0, tenant A = 8 | Ninguna |
| 2 | `insertOne` TMP-001 | 1 insertado; temporales = 1 | insertado TMP-001; temporales = 1 (tenant A = 9) | Ninguna |
| 3 | `insertMany` TMP-002 y TMP-003 | 2 insertados; temporales = 3; tenant A = 11 | 2 insertados; temporales = 3, tenant A = 11 | Ninguna |
| 4 | `updateOne` TMP-001: `$set` `camposDinamicos.spo2` = 93 y `$inc` `tiempo_atencion_min` con `NumberInt(15)` | `matchedCount` 1, `modifiedCount` 1; tiempo de 30 a 45, spo2 93 | `matchedCount` 1, `modifiedCount` 1; tiempo 30 → 45, spo2 97 → 93 | Ninguna |
| 5 | `deleteOne` { `_id`: TMP-001, `temporal`: true } | `deletedCount` 1 | `deletedCount` 1 | Ninguna |
| 6 | `deleteMany` { `_id` `$in` [TMP-002, TMP-003], `temporal`: true } | `deletedCount` 2 | `deletedCount` 2 | Ninguna |
| 7 | Conteo final | 0 temporales; tenant A = 8 | temporales = 0, tenant A = 8; `verificar.js`: «Resultado: 0 errores» | Ninguna |

## Bitácora de errores

Errores reales encontrados al preparar y ejecutar esta práctica.

| # | Síntoma | Causa | Corrección | Prueba |
|---|---|---|---|---|
| 1 | `docker cp …05_consultas_referencia.js sitbio-mongo:/tmp/semilla/…` falla con «Could not find the file /tmp/semilla in container», y `verificar.js` con `ENOENT`. | El contenedor se había reiniciado y la copia temporal `/tmp/semilla` ya no existía. `docker cp` hacia `carpeta/archivo` no crea la carpeta. | Repetir primero los pasos del README de la semilla: `docker cp seeds/mongo/. sitbio-mongo:/tmp/semilla` (sí crea la carpeta), cargar y verificar. Quedó anotado en «Cómo ejecutar». | Después de copiar, `verificar.js` dio «Resultado: 0 errores» (la carga dejó los 14 documentos «sin cambios») y ambos scripts corrieron. |
| 2 | El plan y el comentario del CRUD afirmaban que `$inc: { tiempo_atencion_min: 15 }` convertiría el campo en double y el validador lo rechazaría. | Suposición sin comprobar: mongosh serializa los enteros de JavaScript como Int32, así que `15` se suma como entero. Solo un incremento con decimales cambia el tipo. | Se corrigió el comentario de `06_crud_temporal.js`; se mantiene `NumberInt(15)` para dejar el tipo explícito. | Con un documento `TMP-099` (luego borrado con filtro `temporal: true`): `$inc` 15 → aceptado y el campo sigue siendo int; `$inc` 1.5 → rechazado con el error 121 «Document failed validation». Temporales restantes: 0. |
