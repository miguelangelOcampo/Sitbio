# Diccionario de Datos BSON y Reglas de Integridad - SITBIO

Los nombres y tipos coinciden con el validador `$jsonSchema` de `database/02_MongoDB_SITBIO.js`. La columna «Validador» indica si MongoDB lo exige (`required` del script 02); la columna «Contrato» indica si la API y la semilla lo garantizan aunque el validador no lo exija (ver `04_modelo_y_decisiones.md`, sección 3).

## 1. Colección: `intervenciones`

| Ruta del campo BSON | Tipo BSON | Validador | Contrato | ¿Admite null? | Descripción y regla de dominio |
| --- | --- | --- | --- | --- | --- |
| `_id` | ObjectId | Automático | Sí | No | Identificador interno que genera MongoDB. La aplicación usa `id_intervencion`. |
| `id_intervencion` | String (UUID) | Sí | Sí | No | Identificador de la intervención; correlaciona con PostgreSQL. |
| `tenant_id` | String (UUID) | Sí | Sí | No | Referencia lógica a `tenant.id_tenant`. Sale del JWT, nunca del cuerpo (RN-008). |
| `equipo_id` | String (UUID) | Sí | Sí | No | Referencia lógica a `equipo_biomedico.id_equipo`. |
| `orden_id` | String (UUID) | Sí | Sí | No | Referencia lógica a `orden_trabajo.id_orden`; una intervención por orden. |
| `tecnico_id` | String (UUID) | Sí | Sí | No | Referencia lógica a `usuario.id_usuario` del técnico que ejecuta (AC-03). |
| `tipo_mantenimiento` | String (enum) | Sí | Sí | No | `correctivo`, `preventivo`, `calibracion` o `predictivo`. |
| `fecha_intervencion` | Date | Sí | Sí | No | Fecha y hora de la intervención en UTC. Ordena la línea de tiempo (Q02). |
| `diagnostico` | String | No | Sí | No | Descripción técnica de la falla o la revisión. |
| `acciones_realizadas` | String | No | Sí | No | Texto con las acciones que ejecutó el técnico. |
| `repuestos` | Array de Object | No | Sí | No | Repuestos usados; `[]` si no hubo. Máximo 20 elementos (contrato). |
| `repuestos[].nombre` | String | Sí, dentro de cada elemento | Sí | No | Nombre del repuesto. |
| `repuestos[].cantidad` | Int32 | Sí, dentro de cada elemento | Sí | No | Cantidad usada; mayor que 0. |
| `repuestos[].costo_unitario` | Double | No | No | No (se omite si no aplica) | Costo por unidad en COP. |
| `costo_total` | Double | No | Sí | No | Costo total en COP, mayor o igual a 0. El script usa `double`, no Decimal128: suficiente para sumar costos en pesos, pero no para operaciones contables exactas. |
| `tiempo_atencion_min` | Int32 | No | Sí | No | Duración de la atención en minutos; mayor que 0. |
| `estado_equipo_salida` | String (enum) | No | Sí al firmar | No: ausente mientras no esté firmada (el `enum` del validador no acepta null) | `Operativo`, `En mantenimiento`, `Fuera de servicio` o `Baja` (RN-005, RN-007). |
| `cerrada_firmada` | Boolean | No | Sí | No | `false` al crear y `true` al firmar; desde ese momento el documento es inalterable (RN-004). |
| `camposDinamicos` | Object | No | Sí | No | Valores técnicos según `configuracion_campos` del tenant y tipo de equipo; `{}` si no hay campos configurados. |
| `equipo_snapshot` | Object | No declarado | Sí | Sí, mientras no esté firmada | Copia histórica del equipo, capturada al firmar (HU-021). |
| `equipo_snapshot.nombre` | String | No declarado | Sí | No | Nombre del equipo al firmar. |
| `equipo_snapshot.marca` | String | No declarado | Sí | No | Marca. |
| `equipo_snapshot.modelo` | String | No declarado | Sí | No | Modelo. |
| `equipo_snapshot.tipo_equipo` | String | No declarado | Sí | No | Tipo de equipo; lo usa Q06 para agrupar costos. |
| `equipo_snapshot.ubicacion` | String | No declarado | Sí | No | Ubicación del equipo al firmar. |
| `equipo_snapshot.capturado_en` | Date | No declarado | Sí | No | Momento de la captura en UTC. |

«No declarado» significa que el validador del script 02 no menciona el campo; MongoDB lo acepta porque el esquema no usa `additionalProperties: false`, y su forma la garantiza la API.

## 2. Reglas de Integridad Referencial

### 2.1. Integridad por Aplicación

MongoDB no tiene claves foráneas hacia PostgreSQL. Antes de insertar el documento, la API (ASP.NET Core) comprueba con Entity Framework Core que `equipo_id` y `orden_id` existan en PostgreSQL **dentro del tenant del token**; `tenant_id` no se valida porque sale del JWT.

### 2.2. Inalterabilidad (RN-004)

Se prohíbe el borrado físico (*hard delete*) de los documentos de intervención. Una vez firmada la orden (`cerrada_firmada: true`), el registro debe permanecer inalterable para garantizar la trazabilidad y la conservación del historial de mantenimiento. La política completa por operación está en `04_modelo_y_decisiones.md`, sección 4.
