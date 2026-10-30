# ADR-011: Tenant en el inicio de sesión

- **Estado:** Aceptado
- **Fecha:** 30/10/2026
- **Drivers:** DRV-001, DRV-003

## Contexto

El inicio de sesión ocurre antes de tener token, así que el servidor todavía no conoce la institución. La pantalla UI-001 (DOC-05) pide solo correo y contraseña, y el cliente es el mismo para todas las instituciones (ADR-007), de modo que no puede llevar la institución configurada. El script 01 hace el correo único solo dentro de cada tenant (`uq_usuario_tenant_correo`): el mismo correo podría existir en dos instituciones y el inicio de sesión no sabría a cuál entrar.

Alternativas consideradas:

1. Correo único en todo el sistema y tenant deducido por el servidor.
2. Campo «Institución» (NIT o código) en UI-001.

## Decisión

Correo único en todo el sistema, sin distinguir mayúsculas (migración V005, índice `uq_usuario_correo_global`). Al iniciar sesión, el servidor busca el usuario por correo en todos los tenants y fija como tenant de la solicitud el del usuario encontrado; el cliente nunca lo envía (RN-008). Esa búsqueda (`RepositorioAcceso.BuscarPorCorreoAsync`) es la única consulta del sistema sin filtro de tenant.

## Consecuencias

- UI-001 queda como la define el DOC-05, sin pasos adicionales.
- Una persona que trabaje en dos instituciones necesita un correo distinto en cada una.
- Al crear usuarios (HU-011), un correo ya registrado en otra institución se rechaza; el mensaje no debe revelar a qué institución pertenece.
- Se conserva `uq_usuario_tenant_correo` del script 01 (ADR-005).
