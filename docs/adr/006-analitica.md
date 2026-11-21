# ADR-006: Integración de la analítica

- **Estado:** Aceptado (implementado en la v1.0.0; ampliado en la v1.2.0)
- **Fecha:** 22/10/2026 (ampliación: 20/11/2026)
- **Drivers:** DRV-014

## Contexto

Ver el DOC-06 (secciones de drivers y del componente CMP-ANA).

## Decisión

Python por lotes escribe en la colección resultados_analitica; la API la sirve a UI-023.

Ampliación de la v1.2.0: el lote calcula el agrupamiento K-Means para 2 a 6 grupos y nombra cada grupo según su centroide, así que la pantalla cambia de número de grupos sin volver a ejecutar Python. Además del lote programado, el Ingeniero Biomédico (AC-02) puede pedir un recálculo con `POST /api/v1/analitica/agrupamiento`: la API ejecuta `python -m sitbio_analytics --tenant <institución>` en segundo plano, uno por institución a la vez y con un máximo de 10 minutos, y `GET` del mismo recurso informa el estado.

## Consecuencias

- Desacople; resultados no en tiempo real.
- El servidor de la API necesita Python 3.12 con las dependencias de `analytics/requirements.txt` (DOC-09).
- El estado del recálculo vive en la memoria de la API; antes de escalarla a varias instancias hay que guardarlo en MongoDB (DT-015 del DOC-06).
