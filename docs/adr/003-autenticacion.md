# ADR-003: Autenticación

- **Estado:** Aceptado
- **Fecha:** 22/10/2026
- **Drivers:** DRV-003

## Contexto

Ver el DOC-06 (secciones de drivers y del componente relacionado).

## Decisión

JWT de 30 min con jti en sesion_token y BCrypt.

## Consecuencias

- Revocación posible; consulta por solicitud.
