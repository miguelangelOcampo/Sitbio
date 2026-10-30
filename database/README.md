# Base de datos

| Archivo | Contenido |
|---|---|
| `01_PostgreSQL_SITBIO.sql` | Esquema relacional `sitbio` (16 tablas, vistas y catálogos iniciales). |
| `02_MongoDB_SITBIO.js` | Colecciones `intervenciones`, `configuracion_campos` y `bitacora_auditoria` con validación e índices. |
| `V002__planes_y_solicitudes.sql` | Campos del plan en `tenant` (RN-009, DT-009) y tabla `solicitud_servicio` (RF-029, DT-010). |
| `V003__plan_consultorio.sql` | Plan Consultorio y nuevo almacenamiento: Clínica 25 GB, Hospital 100 GB (RN-009, DOC-01 Tabla 4). |
| `V004__codigo_roles.sql` | Columna `rol.codigo` (AC-01 a AC-07), nombres según el DOC-01 Tabla 3 y rol AC-07 Ente Regulador. La aplicación usa `rol.codigo`, nunca `id_rol`. |
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
