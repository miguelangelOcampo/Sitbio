# Base de datos

| Archivo | Contenido |
|---|---|
| `01_PostgreSQL_SITBIO.sql` | Esquema relacional `sitbio` (16 tablas, vistas y catálogos iniciales). |
| `02_MongoDB_SITBIO.js` | Colecciones `intervenciones`, `configuracion_campos` y `bitacora_auditoria` con validación e índices. |
| `V002__planes_y_solicitudes.sql` | Campos del plan en `tenant` (RN-009, DT-009) y tabla `solicitud_servicio` (RF-029, DT-010). |
| `V003__plan_consultorio.sql` | Plan Consultorio y nuevo almacenamiento: Clínica 25 GB, Hospital 100 GB (RN-009, DOC-01 Tabla 4). |
| `V004__codigo_roles.sql` | Columna `rol.codigo` (AC-01 a AC-07), nombres según el DOC-01 Tabla 3 y rol AC-07 Ente Regulador. La aplicación usa `rol.codigo`, nunca `id_rol`. |
| `V005__correo_unico.sql` | Correo único en todo el sistema sin distinguir mayúsculas (HU-001): el inicio de sesión deduce el tenant a partir del correo. |
| `V006__nombres_y_alcance_roles.sql` | Ajuste de roles del 01/11/2026: AC-05 «Gerente de Tecnología Biomédica»; campos dinámicos y catálogos técnicos para AC-02; ubicaciones para AC-01 y AC-02. Solo nombres y descripciones; los códigos no cambian. |
| `V007__bitacora_acciones.js` | MongoDB: `bitacora_auditoria` acepta las acciones `ACCESO_DENEGADO` (403, CA-HU-010-01) y `CIERRE_SESION` (HU-012). Reemplaza el validador con `collMod`; idempotente. |
| `V008__informacion_regulatoria_equipo.sql` | Información regulatoria del equipo (RF-030, RN-010): autorización INVIMA (registro sanitario o permiso de comercialización), número y vigencia; clase de riesgo (I, IIa, IIb, III); si requiere calibración y su periodicidad (1 a 60 meses). Valores por defecto en `tipo_equipo`; `plan_preventivo.ultima_ejecucion` (último preventivo conocido) y `equipo_biomedico.foto_documento_id` (RF-031). |
| `V009__catalogo_tipo_falla.sql` | Catálogo de tipos de falla por institución (HU-025, RF-025): tabla `tipo_falla` con nombre único por tenant sin distinguir mayúsculas. Sin valores iniciales; `tiquete.tipo_falla` sigue siendo texto, así que inactivar un tipo no cambia los tiquetes anteriores. |
| `docker/postgres-init.sh` | Ejecuta el script 01 y las migraciones al crear el contenedor. |

Reglas (ADR-005):

- Los scripts son la fuente de verdad; EF Core no genera migraciones automáticas.
- Un cambio de esquema es un archivo nuevo `V###__descripcion.sql` (nunca se edita uno ya integrado en `main`).
- Commit con el tipo `db`, por ejemplo `db(HU-002): índice por placa en equipo_biomedico`.

Para recrear las bases desde cero: `docker compose down -v` y luego `docker compose up -d`.

Cuando llega una migración nueva, una base ya creada no la ejecuta sola (los scripts de inicio solo corren con la base vacía). Hay dos opciones: recrearla con los comandos anteriores (se pierden los datos locales; la semilla de MongoDB se vuelve a cargar con `seeds/mongo/README.md`) o aplicar solo el archivo nuevo, por ejemplo:

```bash
docker exec sitbio-postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d sitbio_dev -f /sitbio-db/V004__codigo_roles.sql'
```

Las migraciones de MongoDB (`V###__*.js`) se aplican con `mongosh`; Docker las ejecuta solas al crear el contenedor (`docker-entrypoint-initdb.d`, después del script 02). En una base ya creada:

```bash
docker exec -i sitbio-mongo sh -c 'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin' < database/V007__bitacora_acciones.js
```
