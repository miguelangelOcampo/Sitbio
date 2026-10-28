# Semilla de laboratorio de MongoDB

Datos 100 % ficticios para desarrollo y para las prácticas de la guía NoSQL (semanas 2 y 3). Siguen el modelo y el contrato de `docs/arquitectura/datos/nosql/04_modelo_y_decisiones.md` y pasan el validador estricto del script `database/02_MongoDB_SITBIO.js`.

| Archivo | Contenido |
|---|---|
| `data/intervenciones.ejson.json` | 12 intervenciones en Extended JSON; `_id` es igual a `id_intervencion`. |
| `data/configuracion_campos.ejson.json` | 2 configuraciones activas del tenant A: monitor multiparamétrico y bomba de infusión. |
| `data/referencias_postgres.json` | Equipos (con su ubicación actual), órdenes y usuarios ficticios que «existen» en PostgreSQL. No se cargan en MongoDB. |
| `scripts/cargar_semilla.js` | Carga idempotente con `replaceOne({ _id }, doc, { upsert: true })`. |
| `scripts/verificar.js` | Conteos, referencias rotas, tipos, regla snapshot ↔ firmada, repuestos ≤ 20 y campos dinámicos obligatorios. |

## Tenants de laboratorio

| Tenant | ID | Intervenciones | Firmadas | Sin firmar | Configuraciones |
|---|---|---|---|---|---|
| Clínica Demo A | `11111111-1111-4111-8111-111111111111` | 8 (2 monitores y 2 bombas) | 5 | 3 | 2 |
| Clínica Demo B | `22222222-2222-4222-8222-222222222222` | 4 (electrocardiógrafo y desfibrilador) | 2 | 2 | 0 |

- Las firmadas tienen `equipo_snapshot` completo y `estado_equipo_salida`; las que no están firmadas tienen `equipo_snapshot: null` y no tienen `estado_equipo_salida` (el validador no acepta null en ese campo).
- Tres intervenciones tienen repuestos (máximo 20 por intervención).
- El tenant B no tiene configuración de campos: sus intervenciones usan `camposDinamicos: {}`.
- **Cambio histórico:** la intervención `a1a10000-0000-4000-8000-000000000001` tiene el monitor en «Urgencias» en el snapshot, pero hoy el equipo está en «UCI Adultos» (`referencias_postgres.json`). Los reportes usan el snapshot; la hoja de vida, el dato actual.

## Requisitos

- Docker con los contenedores del proyecto en marcha (`docker compose up -d`) y el archivo `.env` creado a partir de `.env.example`.
- No hace falta instalar mongosh: los scripts se ejecutan **dentro** del contenedor `sitbio-mongo`, con las credenciales que el contenedor ya recibió del `.env`. Así tampoco interfiere un MongoDB instalado en Windows que ocupe `127.0.0.1:27017`.

## Comandos

Desde la raíz del repositorio (funcionan igual en PowerShell y en Git Bash):

```bash
docker cp seeds/mongo/. sitbio-mongo:/tmp/semilla
```

```bash
docker exec sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin sitbio_dev /tmp/semilla/scripts/cargar_semilla.js'
```

```bash
docker exec sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin sitbio_dev /tmp/semilla/scripts/verificar.js'
```

Si cambia algún archivo de `seeds/mongo`, se vuelve a ejecutar el `docker cp` (sobrescribe la copia del contenedor) antes de cargar.

En Git Bash, si `docker cp` o `docker exec` convierten `/tmp/...` en una ruta de Windows, ejecute antes `export MSYS_NO_PATHCONV=1`.

## Resultados esperados

Primera carga:

```text
configuracion_campos: 2 documentos -> 2 insertados, 0 reemplazados, 0 sin cambios
intervenciones: 12 documentos -> 12 insertados, 0 reemplazados, 0 sin cambios
```

Repetir la carga no aumenta los conteos, porque cada documento se repone por `_id`:

```text
configuracion_campos: 2 documentos -> 0 insertados, 0 reemplazados, 2 sin cambios
intervenciones: 12 documentos -> 0 insertados, 0 reemplazados, 12 sin cambios
```

Verificación:

```text
Conteos por tenant:
  Clínica Demo A: 8 intervenciones (5 firmadas, 3 sin firmar), 2 configuraciones
  Clínica Demo B: 4 intervenciones (2 firmadas, 2 sin firmar), 0 configuraciones
Avisos (no son errores):
  - a1a10000-0000-4000-8000-000000000001: cambio histórico, snapshot en «Urgencias», hoy en «UCI Adultos»
Resultado: 0 errores
```

## Prueba de error

1. Romper una referencia: cambiar el equipo de una intervención del tenant B por uno que no existe.

   ```bash
   docker exec sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin sitbio_dev --eval "db.intervenciones.updateOne({ _id: \"b2a10000-0000-4000-8000-000000000002\" }, { \$set: { equipo_id: \"b2c30000-0000-4000-8000-000000000099\" } })"'
   ```

2. Ejecutar `verificar.js`. Debe terminar con código 1 y mostrar:

   ```text
   Errores:
     - b2a10000-0000-4000-8000-000000000002: referencia rota, equipo_id b2c30000-0000-4000-8000-000000000099 no existe
   Resultado: 1 errores
   ```

3. Restaurar ejecutando otra vez `cargar_semilla.js` (repone el documento: «1 reemplazados») y comprobar con `verificar.js` que vuelve a «0 errores».

El validador del servidor también protege los tipos: un `updateOne` que pone `costo_total: NumberInt(5)` falla con el error 121 «Document failed validation» y el documento no cambia.

## Conceptos

- **Extended JSON:** en JSON plano no existe la diferencia entre entero y decimal ni el tipo fecha. `{"$numberInt": "1"}`, `{"$numberDouble": "48000.0"}` y `{"$date": "2026-09-01T13:00:00Z"}` indican el tipo BSON exacto. Por eso el script lee con `EJSON.parse(texto, { relaxed: false })`: en modo relajado, `0.0` se convertiría en el número `0` y se guardaría como Int32, y el validador rechazaría `costo_total`.
- **Idempotencia:** `insertMany` duplicaría los documentos (o fallaría por `_id`) al repetir la carga; `replaceOne` con `upsert` inserta si no existe y reemplaza si existe, así que el resultado es el mismo sin importar cuántas veces se ejecute.
- **Colisiones:** antes de escribir, el script comprueba que ningún `_id` de la semilla pertenezca a otro tenant y que no haya otra configuración con el mismo tenant, tipo y versión (índice único del script 02). Si encuentra alguna, no carga nada.
- **Validación `strict`:** el servidor rechaza cualquier documento que no cumpla el `$jsonSchema`, venga de la API, de la semilla o de mongosh.
- **Integridad referencial verificada por fuera:** MongoDB no tiene claves foráneas hacia PostgreSQL; `verificar.js` compara `equipo_id`, `orden_id` y `tecnico_id` con `referencias_postgres.json`, que hace el papel de PostgreSQL.
