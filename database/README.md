# Base de datos

| Archivo | Contenido |
|---|---|
| `01_PostgreSQL_SITBIO.sql` | Esquema relacional `sitbio` (16 tablas, vistas y catálogos iniciales). |
| `02_MongoDB_SITBIO.js` | Colecciones `intervenciones`, `configuracion_campos` y `bitacora_auditoria` con validación e índices. |
| `V002__planes_y_solicitudes.sql` | Campos del plan en `tenant` (RN-009, DT-009) y tabla `solicitud_servicio` (RF-029, DT-010). |
| `docker/postgres-init.sh` | Ejecuta el script 01 y las migraciones al crear el contenedor. |

Reglas (ADR-005):

- Los scripts son la fuente de verdad; EF Core no genera migraciones automáticas.
- Un cambio de esquema es un archivo nuevo `V###__descripcion.sql` (nunca se edita uno ya integrado en `main`).
- Commit con el tipo `db`, por ejemplo `db(HU-002): índice por placa en equipo_biomedico`.

Para recrear las bases desde cero: `docker compose down -v` y luego `docker compose up -d`.
