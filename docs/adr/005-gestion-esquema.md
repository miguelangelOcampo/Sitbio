# ADR-005: Gestión del esquema

- **Estado:** Aceptado
- **Fecha:** 22/10/2026
- **Drivers:** RNF-016

## Contexto

Ver el DOC-06 (secciones de drivers y del componente relacionado).

## Decisión

Scripts SQL/JS versionados como fuente de verdad; EF Core sin migraciones automáticas.

## Consecuencias

- Control explícito; disciplina manual.
