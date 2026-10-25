# ADR-006: Integración de la analítica

- **Estado:** Propuesto
- **Fecha:** 22/10/2026
- **Drivers:** DRV-014

## Contexto

Ver el DOC-06 (secciones de drivers y del componente relacionado).

## Decisión

Python por lotes escribe en la colección resultados_analitica; la API la sirve a UI-023.

## Consecuencias

- Desacople; resultados no en tiempo real.
