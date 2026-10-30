# Registro de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y versionado semántico (`vMAJOR.MINOR.PATCH`, DOC-06 12).

## [Sin publicar]

### Agregado
- Estructura del repositorio según el DOC-06 (Anexo A).
- Solución `SITBIO.sln` con los proyectos Desktop, Api, Application, Domain e Infrastructure y sus pruebas.
- Módulo `analytics` en Python con pruebas en pytest.
- Scripts de base de datos 01 (PostgreSQL) y 02 (MongoDB), migración V002 (DT-009 y DT-010) y entorno con Docker Compose.
- Pipeline de integración continua en GitHub Actions.
- Documentación del proyecto (DOC-01 a DOC-06) y ADR-001 a ADR-010.
- Estilos «clínico editorial» del cliente WPF: secciones abiertas, franja de indicadores, tabla abierta y estados con punto.
- Persistencia multi-tenant (RN-008, SEC-003): `SitbioDbContext` con filtro global por tenant y bloqueo de escrituras de otra institución; repositorio base de MongoDB que agrega `tenant_id` a toda consulta, conteo, actualización e inserción.
- Pruebas de aislamiento entre dos tenants contra PostgreSQL y MongoDB reales (se omiten si no hay base disponible).
- Manejo de errores de la API con Problem Details (400, 401, 403, 404, 409, 429, 503 y 500 genérico), `traceId`, `correlationId` y cabecera `X-Correlation-ID`; rutas bajo `/api/v1`.
- Laboratorio de las guías NoSQL S2–S3: modelo documental y decisiones, semilla reproducible de MongoDB con verificador (`seeds/mongo`), consultas de referencia y CRUD con resultados esperados.
- HU-001 Inicio de sesión:
  - `POST /api/v1/auth/login` con JWT de 30 minutos (claims `sub`, `tenant_id`, `rol` y `jti`, sin datos personales), registro de la sesión en `sesion_token` y BCrypt de costo 12 (ADR-003).
  - Bloqueo de la cuenta al quinto intento fallido consecutivo (RN-006) y límite de 10 intentos por minuto por IP (429).
  - `IContextoUsuario` a partir de los claims del token; el tenant del inicio de sesión lo deduce el servidor a partir del correo (ADR-011).
  - Cliente WPF: pantalla UI-001 con MSG-001, MSG-002 y MSG-011, tema claro y oscuro, y menú «riel con nombres y panel agrupado» según el rol (AC-01 a AC-07); el token se adjunta a cada solicitud.
  - Pruebas de los criterios CA-HU-001-01 a CA-HU-001-03 (CP-001 a CP-003); trazabilidad en `docs/pruebas/trazabilidad.md`.

### Cambiado
- Base de datos (cada base local debe aplicar las migraciones nuevas; ver `database/README.md`):
  - V003: plan Consultorio y nuevo almacenamiento de los planes Clínica (25 GB) y Hospital (100 GB) (RN-009).
  - V004: columna `rol.codigo` (AC-01 a AC-07), nombres de roles según el DOC-01 y rol AC-07; la aplicación identifica los roles por código.
  - V005: correo único en todo el sistema, sin distinguir mayúsculas (ADR-011).

### Seguridad
- La clave JWT se configura solo con User Secrets o con la variable `Jwt__Clave`; sin una clave válida la API arranca y rechaza todo token.
- El inicio de sesión responde igual a un correo inexistente que a una contraseña errada, y no revela si una cuenta está bloqueada (MSG-002 genérico).
