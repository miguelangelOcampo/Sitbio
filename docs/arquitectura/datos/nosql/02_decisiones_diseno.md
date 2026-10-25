# Justificación de Decisiones Arquitectónicas NoSQL - SITBIO

## Decisión 1. Embedding 1:1 de `camposDinamicos`

* **Técnica:** Embebido del subdocumento `camposDinamicos` dentro de `intervenciones`.

* **Justificación:** Los valores heterogéneos (por ejemplo, el voltaje en monitores y el caudal en bombas de infusión) se consultan y registran junto con el diagnóstico de la intervención. Al compartir el mismo ciclo de vida, el embebido elimina la necesidad de realizar *joins* y optimiza las operaciones de lectura.

## Decisión 2. Referenciación 1:N entre PostgreSQL y MongoDB

* **Técnica:** Uso de referencias mediante UUID (`tenant_id`, `equipo_id` y `orden_id`).

* **Justificación:** Un equipo biomédico puede acumular cientos de intervenciones a lo largo de su vida útil. Almacenar un arreglo de intervenciones dentro de la entidad del equipo generaría un arreglo sin límite (*unbounded array*), con el riesgo de superar el límite de 16 MiB por documento BSON y degradar el rendimiento de las operaciones de entrada y salida (I/O).

## Decisión 3. Desnormalización con Snapshot Histórico (`equipo_snapshot`)

* **Técnica:** Copia inalterable de los atributos `nombre`, `marca`, `modelo` y `ubicacion_al_intervenir` dentro del documento de la intervención.

* **Justificación:** Si un equipo es reubicado o renombrado en PostgreSQL meses después de una intervención, el registro histórico debe conservar la información del activo tal como se encontraba en el momento del mantenimiento. Esta desnormalización garantiza la trazabilidad histórica y evita que los cambios posteriores en los datos maestros alteren el contexto de las intervenciones ya registradas.
