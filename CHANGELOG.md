# Registro de cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y versionado semántico (`vMAJOR.MINOR.PATCH`, DOC-06 12).

## [Sin publicar]

## [0.2.0] - 2026-11-12

Cierre del Sprint 3. Incluye todo lo anterior a esta versión.

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
- HU-011 Gestión de usuarios y roles:
  - `/api/v1/usuarios` solo para el Administrador del Sistema (AC-01, RN-002): listar con paginación, consultar, crear, editar, desactivar, reactivar, desbloquear (RN-006) y emitir una contraseña temporal (RF-012). La contraseña temporal se devuelve una sola vez y se guarda solo su hash BCrypt.
  - Límite del plan (RN-009): con el cupo de usuarios de gestión lleno no se crean ni se reactivan cuentas AC-01, AC-02, AC-03, AC-05 ni AC-06, ni se cambia una cuenta AC-04 o AC-07 a un rol de gestión (409 con el texto de MSG-019). El Personal Clínico (AC-04) y el ente regulador (AC-07) no cuentan.
  - Cliente WPF: pantalla UI-004 Usuarios y roles con indicadores, filtros, tabla abierta, menú de acciones por fila, panel lateral para crear o editar, confirmación al desactivar y diálogo que muestra la contraseña temporal una sola vez; pestaña «Roles y permisos» con la guía de cada rol y la matriz de permisos. El marco de la sesión ya muestra la pantalla de cada opción del menú.
  - Pruebas de los criterios CA-HU-011-01 a CA-HU-011-03 (CP-015) contra la API y PostgreSQL reales, incluido el desbloqueo que cierra el pendiente de CP-003.
- HU-012 Control de sesión y cierre por inactividad:
  - `POST /api/v1/auth/refresh` entrega un token nuevo (otro `jti`) y revoca el anterior; `POST /api/v1/auth/logout` revoca el token con el motivo `voluntario` o `inactividad`, que queda en `sesion_token`.
  - Todo token revocado se rechaza con 401 en cualquier ruta: la API comprueba el `jti` contra `sesion_token` en cada solicitud. Si la cuenta se desactiva o se bloquea durante la sesión, la renovación se rechaza y la sesión se cierra.
  - Cliente WPF: a los 30 minutos sin actividad la sesión se cierra, el token se revoca y se muestra MSG-003 sobre la ventana, sin dejar ver los datos; el token se renueva solo cuando faltan menos de 5 minutos y el usuario está activo; «Cerrar sesión» pide confirmación; un 401 de la API termina la sesión.
  - Pruebas de los criterios CA-HU-012-01 y CA-HU-012-02 (CP-016) contra la API y PostgreSQL reales.
- HU-010 Auditoría de acceso y denegación de permisos:
  - Bitácora de auditoría en MongoDB (`bitacora_auditoria`), solo de inserción: inicios de sesión correctos y fallidos (con el motivo), cierres de sesión y respuestas 403 con método, ruta y rol, siempre con el usuario, la hora del servidor y la IP de origen.
  - `GET /api/v1/bitacora` para el Administrador del Sistema (AC-01) y el Ente Regulador / Auditor (AC-07), con filtros por usuario, fechas y acción, paginado y con el nombre del usuario.
  - Si MongoDB no responde, la operación auditada no se interrumpe: el fallo queda en el log del servidor.
  - Cliente WPF: pantalla UI-024 Bitácora de auditoría, de solo lectura, con indicadores de eventos, accesos denegados e inicios fallidos, filtros por usuario, acción y fechas, y paginación de 50 eventos.
  - Pruebas de los criterios CA-HU-010-01 a CA-HU-010-03 (CP-014) contra la API, PostgreSQL y MongoDB reales, con el validador de la colección.
- HU-002 Registro de hoja de vida biomédica y placa única:
  - `/api/v1/equipos` (API-EQ-LIST, API-EQ): listar con filtros por placa, serie, estado, clase de riesgo y calibración, y paginado; consultar la hoja de vida; registrar y editar solo para el Ingeniero Biomédico (AC-02). Consultan AC-01, AC-02, AC-03, AC-04, AC-05 y AC-07; la Directiva Institucional (AC-06) no.
  - Placa de inventario y número de serie únicos por institución (RN-001): un duplicado responde 409 `RN-001` indicando el campo.
  - Límite de equipos activos del plan (RN-009): con el cupo lleno no se registran equipos (409 con el consumo del plan, MSG-019); la consulta y la edición siguen funcionando y los equipos dados de baja no cuentan.
  - Información regulatoria (RF-030, RN-010): autorización INVIMA (registro sanitario o permiso de comercialización) con número y vigencia, clase de riesgo (I, IIa, IIb, III) y si requiere calibración, con periodicidad de 1 a 60 meses. El tipo de equipo propone los valores; si el equipo difiere, queda en la bitácora. Aviso, sin bloqueo, cuando la autorización está vencida o le faltan 180 días o menos.
  - Plan preventivo (RF-019, CA-HU-002-06): al registrar se crea con la periodicidad y el último preventivo; el próximo es último + periodicidad, ajustable. Si ya pasó, el plan nace vencido (RF-020); si no se conoce el último, se indica el próximo, que no puede haber pasado. Al editar se reprograma el mismo plan. No se pide técnico responsable.
  - `GET /api/v1/catalogos/{tipo}` (API-CAT) de solo consulta para AC-01 y AC-02: `tipos-equipo` (con sus valores regulatorios por defecto), `ubicaciones` y `proveedores`. La gestión de los catálogos llega con HU-025.
  - Cliente WPF: UI-005 Equipos biomédicos (indicadores, búsqueda por serie, filtros por estado, clase y calibración, tabla abierta con los avisos de mantenimiento y de registro, paginación); UI-006 hoja de vida (resumen, información técnica y aviso de la autorización); UI-007 formulario de registro y edición en panel lateral, con la clase y la calibración propuestas por el tipo, el próximo preventivo calculado y los avisos de diferencia con el tipo, de vencimiento y de preventivo atrasado.
  - Pruebas de los criterios CA-HU-002-01 a CA-HU-002-06 (CP-004 y CP-005) contra la API y PostgreSQL reales.
- HU-013 Restablecimiento de contraseña con clave temporal:
  - El Administrador del Sistema (AC-01) emite una clave temporal; quien inicia sesión con ella debe cambiarla antes de continuar (RF-012). Mientras no la cambie, la API solo atiende cambiar la contraseña, renovar y cerrar sesión; cualquier otra ruta responde 403 con MSG-004.
  - `PUT /api/v1/auth/password`: la nueva contraseña debe tener al menos 10 caracteres, mayúscula y minúscula, un número y un símbolo, y no puede contener el nombre, el apellido ni el correo. La API aplica las mismas reglas que muestra el cliente y responde 400 con las reglas incumplidas.
  - Al cambiarla se revocan las demás sesiones abiertas del usuario y el cambio queda en la bitácora (obligatorio o voluntario).
  - Cliente WPF: pantalla UI-002 de cambio de contraseña con las reglas marcadas mientras se escribe; se abre sola al entrar con una clave temporal y también desde el menú de la sesión.
- HU-024 Configuración de la institución:
  - `GET`/`PUT /api/v1/tenant/actual` (API-TEN) solo para el Administrador del Sistema (AC-01): nombre, NIT y días de anticipación de las alertas de vencimiento (7 a 90). El plan, su vigencia y sus límites solo se consultan; los fija el proveedor del servicio.
  - Consumo del plan (RN-009): usuarios de gestión, equipos activos y almacenamiento, con aviso desde el 80 % y aviso de límite alcanzado.
  - Cliente WPF: UI-019 Configuración › «Mi institución», con los datos editables y el plan con sus medidores.
  - Pruebas de los criterios CA-HU-024-01 a CA-HU-024-03 (CP-027) contra la API y PostgreSQL reales. El uso de los días de anticipación en el cálculo de las alertas se prueba con HU-020.
- HU-025 Catálogos maestros:
  - `/api/v1/catalogos/{tipo}` (API-CAT) para `tipos-equipo`, `ubicaciones`, `proveedores` y `tipos-falla`: listar (con activos e inactivos), consultar por id, crear y editar. El Ingeniero Biomédico (AC-02) gestiona los cuatro; el Administrador del Sistema (AC-01) solo las ubicaciones y consulta los demás.
  - Los valores no se borran: se inactivan y los registros que ya los usan los conservan. Un nombre repetido (o, en proveedores, un NIT repetido) responde 409 `CAT-001`.
  - Los tipos de equipo guardan la clase de riesgo y la calibración que se proponen al registrar un equipo (RF-030).
  - Crear y editar quedan en la bitácora con los campos que cambiaron, sin los datos de contacto del proveedor.
  - Cliente WPF: UI-019 Configuración pasa a tener pestañas; «Catálogos» (UI-020) muestra los catálogos con sus activos y una tabla abierta para agregar, activar o inactivar valores. En «Tipos de equipo», AC-02 edita la clase de riesgo y la calibración por defecto. El Ingeniero Biomédico entra a Configuración solo a Catálogos.
  - Pruebas de los criterios CA-HU-025-01 a CA-HU-025-03 (CP-028) contra la API y PostgreSQL reales.

### Cambiado
- DOC-01 actualizado con el plan Consultorio y el nuevo almacenamiento de los planes (Tabla 4: Consultorio 3/300/5 GB, Clínica 7/1.000/25 GB, Hospital 14/3.000/100 GB, Red de Salud a la medida).
- DOC-01 a DOC-06, el diagrama de casos de uso y el mapa de navegación (DIA-IA-01) sincronizados con el ajuste de roles del 01/11/2026: AC-05 pasa a ser «Gerente de Tecnología Biomédica»; los campos dinámicos y los catálogos técnicos los gestiona AC-02, y las ubicaciones, AC-01 y AC-02.
- DOC-01 y DOC-03 a DOC-06 sincronizados con el ajuste del 04/11/2026: información regulatoria del equipo (RF-030, RN-010: autorización INVIMA con número y vigencia, clase de riesgo y calibración independiente de la clase) y fotografía opcional del equipo (RF-031, con HU-015). El DOC-02 no cambia.
- Base de datos (cada base local debe aplicar las migraciones nuevas; ver `database/README.md`):
  - V003: plan Consultorio y nuevo almacenamiento de los planes Clínica (25 GB) y Hospital (100 GB) (RN-009).
  - V004: columna `rol.codigo` (AC-01 a AC-07), nombres de roles según el DOC-01 y rol AC-07; la aplicación identifica los roles por código.
  - V005: correo único en todo el sistema, sin distinguir mayúsculas (ADR-011).
  - V006: el AC-05 se llama «Gerente de Tecnología Biomédica»; descripciones de AC-01 (ubicaciones) y AC-02 (campos dinámicos y catálogos técnicos) según el ajuste de roles del 01/11/2026.
  - V007 (MongoDB): el validador de `bitacora_auditoria` acepta las acciones `ACCESO_DENEGADO` y `CIERRE_SESION`. Docker la aplica al crear el contenedor; en una base ya creada se ejecuta con `mongosh` (comando en `database/README.md`).
  - V008: información regulatoria del equipo (RF-030, RN-010) en `equipo_biomedico`, valores por defecto en `tipo_equipo`, `plan_preventivo.ultima_ejecucion` y `equipo_biomedico.foto_documento_id` (RF-031, para HU-015).
  - V009: catálogo `tipo_falla` por institución (HU-025).
- `CodigosRol.DirectorBiomedico` pasa a llamarse `CodigosRol.GerenteBiomedico` (el código AC-05 no cambia).
- `GET /api/v1/catalogos/{tipo}` acepta también `tipos-falla`, y en proveedores devuelve el NIT y el tipo.
- Versión de la solución: 0.2.0 (`Directory.Build.props`).

### Seguridad
- La clave JWT se configura solo con User Secrets o con la variable `Jwt__Clave`; sin una clave válida la API arranca y rechaza todo token.
- El inicio de sesión responde igual a un correo inexistente que a una contraseña errada, y no revela si una cuenta está bloqueada (MSG-002 genérico).
- Gestión de usuarios: un correo usado en otra institución responde 409 `USR-001` sin revelar a cuál pertenece; un usuario de otra institución responde 404 (SEC-003); el Administrador no puede desactivar su propia cuenta ni quitarse el rol (409 `USR-002`).
- Sesiones (HU-012): un token revocado por cierre de sesión, inactividad o renovación no vuelve a servir aunque su firma y su vigencia sean válidas; un `jti` de otra institución o sin sesión registrada responde 401.
- Bitácora (HU-010): el detalle de cada evento nunca lleva correo, contraseña ni token (Ley 1581 de 2012); cada institución solo consulta sus propios eventos (RN-008) y la API no ofrece forma de modificarlos ni borrarlos.
- Equipos (HU-002): un equipo de otra institución responde 404 al consultarlo o editarlo (SEC-003); la placa y la serie solo se comparan dentro de la institución; registrar y editar se validan en la API (403 para cualquier rol distinto de AC-02), no solo ocultando el botón en WPF.
- Contraseñas (HU-013): la clave temporal solo sirve para cambiarla (403 MSG-004 en cualquier otra ruta, validado en la API); al cambiar la contraseña se cierran las demás sesiones del usuario, y ni la contraseña ni el correo van a la bitácora.
- Institución (HU-024): el Administrador solo consulta y edita su propio tenant (no hay ruta por id) y no puede cambiar el plan ni sus límites; otros roles reciben 403.
- Catálogos (HU-025): cada institución solo ve y edita sus elementos (404 para los de otra); los permisos por rol se validan en la API (403), no solo en la interfaz; no hay `DELETE`.
