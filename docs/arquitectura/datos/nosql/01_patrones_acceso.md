# Matriz de Patrones de Acceso NoSQL - Proyecto SITBIO

## 1. Patrones de Lectura (Queries)

* **Q01. Metadatos de Campos Dinámicos:** Obtener la configuración de campos según `tenant_id` y `tipo_equipo_id` (INT) para renderizar el formulario dinámico en C# WPF.

* **Q02. Línea de Tiempo por Equipo:** Consultar el historial de intervenciones de un activo mediante `tenant_id` y `equipo_id` (UUID), ordenado por `fecha_registro` en orden descendente.

* **Q03. Detalle por Orden de Trabajo:** Obtener la intervención asociada a una orden de trabajo mediante `tenant_id` y `orden_id` (UUID).

* **Q04. Extracción para Pipeline Analítico:** Extraer registros de `intervenciones`, filtrados por `tenant_id` y un rango de fechas, para cargar el DataFrame de Pandas y ejecutar el algoritmo de agrupamiento K-Means.

* **Q05. Bitácora de Auditoría:** Consultar la trazabilidad de operaciones críticas mediante `tenant_id` y fecha.

* **Q06. Reporte Histórico de Costos:** Ejecutar agregaciones sobre `equipo_snapshot.tipo_equipo` para consolidar los costos históricos por categoría de equipo.

## 2. Patrones de Escritura (Writes)

* **W01. Parametrizar Campos Dinámicos:** Insertar o actualizar las reglas de los campos personalizados en la colección `configuracion_campos`.

* **W02. Registrar Intervención Técnica:** Insertar un documento BSON en la colección `intervenciones`, registrando el diagnóstico, los repuestos, los tiempos, los costos y la propiedad anidada `camposDinamicos`.

* **W03. Cierre e Inalterabilidad de la Orden de Trabajo (OT):** Actualizar el estado de la orden de trabajo en PostgreSQL y garantizar la inalterabilidad del registro correspondiente en MongoDB, de acuerdo con el requisito RN-004.

* **W04. Registrar Evento de Auditoría:** Insertar de forma asíncrona un evento en la colección `bitacora_auditoria`.
