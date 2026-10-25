# ADR-004: Modelo multi-tenant

- **Estado:** Aceptado
- **Fecha:** 22/10/2026
- **Drivers:** DRV-001

## Contexto

Ver el DOC-06 (secciones de drivers y del componente relacionado).

## Decisión

Base compartida con discriminador tenant_id y filtros globales.

## Consecuencias

- Simple de operar; exige pruebas cruzadas.
