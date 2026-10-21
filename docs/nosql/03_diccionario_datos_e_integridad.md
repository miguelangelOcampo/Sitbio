# Diccionario de Datos BSON y Reglas de Integridad - SITBIO

## 1. Colección: `intervenciones`

| Ruta del Campo BSON      | Tipo BSON      | Requerido | Descripción y Regla de Dominio                                                            |
| ------------------------ | -------------- | --------- | ----------------------------------------------------------------------------------------- |
| `_id`                    | ObjectId       | Sí        | Identificador único autogenerado en MongoDB.                                              |
| `tenant_id`              | String (UUID)  | Sí        | Clave foránea lógica hacia `tbl_tenant` en PostgreSQL.                                    |
| `orden_id`               | String (UUID)  | Sí        | Clave foránea lógica hacia `tbl_orden_trabajo` en PostgreSQL.                             |
| `equipo_id`              | String (UUID)  | Sí        | Clave foránea lógica hacia `tbl_equipo_biomedico` en PostgreSQL.                          |
| `diagnostico`            | String         | Sí        | Texto descriptivo de la falla o revisión, con una longitud de entre 10 y 2000 caracteres. |
| `actividades_ejecutadas` | Array[String]  | Sí        | Debe contener como mínimo una actividad registrada por el técnico.                        |
| `costo_total`            | Decimal128     | Sí        | Valor monetario mayor o igual a 0,00 COP.                                                 |
| `tiempo_atencion_min`    | Int32          | Sí        | Duración real de la atención, expresada en minutos. Debe ser mayor que 0.                 |
| `camposDinamicos`        | Document       | Sí        | Objeto con variables técnicas específicas según el tipo de equipo biomédico.              |
| `equipo_snapshot`        | Document       | Sí        | Objeto con los datos históricos del activo, conservados al momento de la intervención.    |
| `fecha_registro`         | Date (ISODate) | Sí        | Marca de tiempo de la intervención, almacenada en UTC.                                    |

## 2. Reglas de Integridad Referencial

### 2.1. Integridad por Aplicación

La API backend desarrollada en C# con ASP.NET Core debe validar la existencia de `tenant_id`, `equipo_id` y `orden_id` en PostgreSQL mediante Entity Framework Core antes de ejecutar la inserción del documento en MongoDB.

### 2.2. Inalterabilidad (RN-004)

Se prohíbe el borrado físico (*hard delete*) de los documentos de intervención. Una vez cerrada la orden de trabajo, el registro de la intervención debe permanecer inalterable para garantizar la trazabilidad y la conservación del historial de mantenimiento.
