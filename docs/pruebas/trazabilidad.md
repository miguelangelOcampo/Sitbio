# Trazabilidad de pruebas

Relaciona cada caso de prueba (CP) con su criterio de aceptación (DOC-04), los requisitos (DOC-03) y las pruebas automáticas que lo verifican. Se actualiza en el commit de documentación de cada historia.

## HU-001 · Autenticación JWT y acceso por rol

Caso de uso CU-001 · RF-002 · RN-006 · RNF-004 · ADR-003 · ADR-011 · Pantalla UI-001.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-001 | CA-HU-001-01: con usuario y contraseña válidos, la API emite un JWT y el cliente habilita el menú del rol. | RF-002, RNF-004, RN-008 | Integración (API + PostgreSQL): `CriteriosHu001Tests.CA_HU_001_01_*` (2). API: `LoginEndpointTests.CredencialesValidas_Responde200ConTokenYRol`, `ContextoUsuarioHttpTests` (8). Aplicación: `IniciarSesionTests.CredencialesValidas_EmiteTokenYRegistraLaSesion`. Infraestructura: `GeneradorJwtTests` (6), `HasherBCryptTests` (4). Cliente: `LoginViewModelTests.CredencialesValidas_*`, `MenuPorRolTests` (12), `ShellViewModelTests` (5), `ManejadorTokenTests` (3). | Aprobado |
| CP-002 | CA-HU-001-02: con credenciales inválidas se muestra «Credenciales incorrectas» sin detallar el fallo. | RF-002, MSG-001, DD-005 | Integración: `CriteriosHu001Tests.CA_HU_001_02_*` (2), incluida la respuesta idéntica para un correo inexistente. API: `LoginEndpointTests.CredencialesInvalidas_Responde401ConMsg001` (2), `DatosVacios_Responde400`. Aplicación: `IniciarSesionTests.ClaveIncorrecta_*`, `CorreoInexistente_*`, `DatosVacios_*` (2). Cliente: `LoginViewModelTests.Msg001_*`, `DatosInvalidos_*` (4). | Aprobado |
| CP-003 | CA-HU-001-03: tras cinco intentos fallidos consecutivos, el sexto encuentra la cuenta bloqueada; solo el Administrador la reactiva. | RN-006, MSG-002 | Integración: `CriteriosHu001Tests.CA_HU_001_03_*` (2), incluido que los intentos son consecutivos; `CriteriosHu011Tests.CA_HU_001_03_SoloElAdministradorDesbloquea_YElUsuarioVuelveAEntrar` (HU-011). Dominio: `UsuarioTests.RegistrarIntentoFallido_*`, `RegistrarAccesoExitoso_*` (2). Aplicación: `IniciarSesionTests.CincoFallos_*`, `InstitucionInactiva_*`. API: `LoginEndpointTests.MasIntentosQueElLimite_Responde429`. Cliente: `LoginViewModelTests.OtrosFallos_*` (3). | Aprobado |

Última ejecución: 30/10/2026, 07:48 (UTC−5), rama `feature/HU-001-autenticacion`, con PostgreSQL y MongoDB de Docker: 135 pruebas aprobadas, 0 fallidas, 0 omitidas. Las pruebas de integración se omiten en el CI porque allí no hay bases de datos.

### Verificación manual

- Inicio de sesión desde el cliente WPF contra la API real: MSG-001 con el correo conservado, la contraseña vacía y el foco en ella; tema claro y oscuro.
- Menú por rol después de iniciar sesión con usuarios AC-01, AC-02 y AC-04: módulos del riel, panel agrupado (Inventario, Trabajo y Prevención) y panel oculto.

### Contrato implementado: `POST /api/v1/auth/login` (API-AUTH-LOGIN)

Solicitud anónima, con límite de 10 intentos por minuto por IP:

```json
{ "correo": "nombre@institucion.co", "contrasena": "…" }
```

| Respuesta | Cuándo | Cuerpo |
|---|---|---|
| 200 | Credenciales válidas | `{ "token", "expiraEn", "rol", "debeCambiarClave" }` |
| 400 | Correo o contraseña vacíos | Problem Details con `errors` por campo |
| 401 · `codigo: MSG-001` | Contraseña errada o correo inexistente (misma respuesta) | Problem Details |
| 401 · `codigo: MSG-002` | Cuenta bloqueada o inactiva, o institución inactiva | Problem Details |
| 429 | Más intentos que el límite | Problem Details |

### Cómo reproducir

```bash
docker compose up -d
```

Definir `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` (cadenas de conexión a las bases de Docker; ver `.env`) y ejecutar:

```bash
dotnet test SITBIO.sln
```

La base local debe tener aplicadas V004 y V005 (`database/README.md`).

### Pendientes

- ~~**Reactivación por el Administrador (CP-003)**~~: cerrado con HU-011. El Administrador desbloquea la cuenta con `PATCH /usuarios/{id}/estado` y el usuario vuelve a entrar (prueba de integración en `CriteriosHu011Tests`).
- **MSG-020:** el DOC-05 muestra «Servicio no activo» cuando la institución está inactiva y la contraseña es correcta; la API responde MSG-002 genérico en ese caso.
- **DOC-06:** ubica el componente de autenticación en `src/SITBIO.Api/Modules/Auth`; el código usa `Modules/Autenticacion` (rutas en español).

## HU-011 · Gestión de usuarios y roles

RF-001 · RF-012 · RN-002 · RN-006 · RN-008 · RN-009 · SEC-003 · ADR-011 · Pantalla UI-004 · Mensajes MSG-008, MSG-011 y MSG-019.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-004 | CA-HU-011-01: al completar los datos y elegir un rol, la cuenta se crea con la contraseña cifrada con BCrypt. | RF-001, RF-012, RNF-004 | Integración (API + PostgreSQL): `CriteriosHu011Tests.CA_HU_011_01_AdministradorCreaUsuario_ConHashBCryptYClaveTemporalQueDebeCambiar` (hash `$2…$12$` en la base y la clave temporal permite entrar con `debeCambiarClave`). Aplicación: `GestionUsuariosTests.Crear_*` (4), `ClaveTemporal_*`. API: `UsuariosEndpointTests.Administrador_CreaUsuarioConClaveTemporalYSinHash`. Cliente: `UsuariosViewModelTests.CA_HU_011_01_*` (2), `ServicioUsuariosHttpTests.Crear_*`. | Aprobado |
| CP-005 | CA-HU-011-02: sin rol de Administrador, crear o desactivar cuentas se deniega (RN-002). | RN-002, RNF-003 | Integración: `CriteriosHu011Tests.CA_HU_011_02_*` (2): el Ingeniero (AC-02) y el Técnico (AC-03), con sesión real, reciben 403 en las cinco operaciones y la base no cambia; sin token, 401. API: `UsuariosEndpointTests.OtroRol_Responde403EnTodasLasOperaciones` (AC-02, AC-03 y AC-07), `SinToken_Responde401`. Cliente: `GuiaRolesTests.ElMenuDeCadaRolEsElDelShell`. | Aprobado |
| CP-006 | CA-HU-011-03: con el cupo de usuarios de gestión lleno no se crean ni reactivan cuentas de gestión; Personal Clínico y ente regulador sí (RN-009). | RN-009, MSG-019 | Integración: `CriteriosHu011Tests.CA_HU_011_03_*` (2), con un tenant del plan Consultorio (3 cupos): 409 `RN-009` con «plan Consultorio», 201 para AC-04 y AC-07, desactivar libera cupo y reactivar con el cupo lleno responde 409. Aplicación: `GestionUsuariosTests.Rn009_*` (10), incluido el cambio de AC-04 a un rol de gestión. Cliente: `UsuariosViewModelTests.CA_HU_011_03_*` (2), `GuiaRolesTests.Rn009_*` (7). | Aprobado |

Pruebas adicionales de la historia:

- **Aislamiento (RN-008, SEC-003):** `CriteriosHu011Tests.Aislamiento_AdministradorNoVeNiModificaUsuariosDeOtroTenant` (404 en consultar, editar, cambiar estado y clave temporal; la lista solo trae usuarios propios) y `UsuariosEndpointTests.Administrador_UsuarioDeOtroTenant_Responde404`.
- **Correo de otra institución (ADR-011):** `CriteriosHu011Tests.CorreoDeOtraInstitucion_Responde409SinRevelarElTenant`.
- **Propia cuenta (USR-002):** `GestionUsuariosTests.Desactivar_LaPropiaCuenta_Responde409`, `Actualizar_QuitarseElRolDeAdministrador_Responde409` y `UsuariosViewModelTests.PropiaCuenta_NoOfreceDesactivar`.
- **Guía de roles (ajuste del 01/11/2026):** `GuiaRolesTests` (16 casos): siete roles con el AC-05 «Gerente de Tecnología Biomédica», y campos dinámicos y catálogos técnicos gestionados por AC-02.
- **Cliente:** `UsuariosViewModelTests` (15), `ServicioUsuariosHttpTests` (6) y `ShellViewModelTests.Navegar_MuestraLaPantallaDeLaOpcionOElAvisoDeConstruccion`.

Última ejecución: 02/11/2026, 18:28 (UTC−5), rama `feature/HU-011-usuarios`, con PostgreSQL y MongoDB de Docker: 222 pruebas aprobadas, 0 fallidas, 0 omitidas. Sin las bases (como en el CI) se omiten las 14 de integración de la API.

### Verificación manual

En el cliente WPF contra la API real, con el Administrador de la institución de demostración: la lista con sus indicadores y «(tú)» en la propia cuenta, el menú de acciones de cada fila, el panel para editar, la validación al crear (MSG-008 y error en cada campo), la creación de un usuario con el diálogo de la contraseña temporal y la pestaña «Roles y permisos». Sin revisar a la vista: la matriz de permisos y el tema oscuro (el cambio de tema llega con Mi perfil).

### Contrato implementado: `/api/v1/usuarios` (API-USR)

Todas las rutas exigen un token con rol AC-01 (sin token 401; con otro rol 403). El tenant sale del token.

| Método y ruta | Cuerpo | Respuesta correcta |
|---|---|---|
| `GET /usuarios?page=1&pageSize=20` | — | 200 `{ items, page, pageSize, total }` (`pageSize` de 1 a 100) |
| `GET /usuarios/{id}` | — | 200 usuario |
| `POST /usuarios` | `{ nombres, apellidos, correo, telefono, rol }` | 201 `{ usuario, claveTemporal }` con `Location` |
| `PUT /usuarios/{id}` | `{ nombres, apellidos, correo, telefono, rol }` | 200 usuario |
| `PATCH /usuarios/{id}/estado` | `{ accion }`: `desactivar`, `reactivar` o `desbloquear` | 200 usuario |
| `POST /usuarios/{id}/clave-temporal` | — | 200 `{ claveTemporal }` |

El usuario se devuelve como `{ id, nombres, apellidos, correo, telefono, rol, estado, debeCambiarClave, ultimoAcceso }`, con `estado` Activo, Inactivo o Bloqueado y nunca con el hash.

| Error | Cuándo |
|---|---|
| 400 con `errors` por campo | Datos inválidos, rol inexistente, acción desconocida o paginación fuera de rango |
| 404 | El usuario no existe o es de otra institución |
| 409 · `codigo: USR-001` | El correo ya lo usa otra cuenta (de cualquier institución, sin decir cuál) |
| 409 · `codigo: USR-002` | Desactivar la propia cuenta o quitarse el rol de Administrador |
| 409 · `codigo: RN-009` | Cupo de usuarios de gestión del plan lleno (texto de MSG-019) |

### Pendientes

- **Consumo del plan en UI-004:** la barra «X de Y usuarios de gestión» necesita un endpoint que lo exponga (HU-024); hoy el límite se informa cuando la API responde 409.
- **Bitácora (UI-024):** las acciones sobre usuarios todavía no se registran en la bitácora de auditoría.
- **Envío de la contraseña temporal por correo:** la API la devuelve una sola vez y el Administrador la entrega por un canal seguro.

## HU-012 · Control de sesión y cierre por inactividad

Caso de uso CU-001 · RF-011 · RNF-004 · SEC-002 · ADR-003 · Pantalla UI-001 (regreso al inicio de sesión) · Mensajes MSG-002, MSG-003 y MSG-011.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-016 | CA-HU-012-01: tras 30 minutos sin actividad, al intentar una acción la aplicación cierra la sesión, invalida el token JWT y lleva al inicio de sesión. | RF-011, RNF-004, MSG-003 | Integración (API + PostgreSQL): `CriteriosHu012Tests.CA_HU_012_01_CierrePorInactividad_GuardaElMotivoEInvalidaElToken`. API: `SesionEndpointTests.CA_HU_012_01_LogoutPorInactividad_GuardaEseMotivo`. Aplicación: `SesionesTests.CA_HU_012_01_CerrarPorInactividad_GuardaEseMotivo`. Cliente: `ControlSesionTests.CA_HU_012_01_*` (4): no cierra antes de 30 minutos, a los 30 revoca el token y muestra MSG-003, la actividad reinicia el conteo y cierra una sola vez; `MainViewModelTests.CA_HU_012_01_*` (2): oculta los datos y «Ingresar de nuevo» vuelve al inicio de sesión. | Aprobado |
| CP-016 | CA-HU-012-02: al presionar «Cerrar sesión» y confirmar, el token queda invalidado y no puede reutilizarse. | RF-011, RNF-004 | Integración: `CriteriosHu012Tests.CA_HU_012_02_CerrarSesion_RevocaElTokenYNoPuedeReutilizarse`. API: `SesionEndpointTests.CA_HU_012_02_*` (2): 204 y el mismo token se rechaza en cualquier ruta. Aplicación: `SesionesTests.CA_HU_012_02_CerrarSesion_RevocaElJtiDelToken`. Cliente: `ControlSesionTests.CA_HU_012_02_CierreVoluntario_RevocaYOlvidaElTokenSinAvisoDeExpiracion`, `MainViewModelTests.CA_HU_012_02_CerrarSesion_PideConfirmacionYVuelveAlInicioDeSesion`, `ServicioSesionHttpTests.Cerrar_*` (2). | Aprobado |

Pruebas adicionales de la historia:

- **Renovación (SEC-002):** `CriteriosHu012Tests.Renovar_EntregaOtroJtiYElTokenAnteriorQuedaRevocado`, `SesionEndpointTests.Refresh_DevuelveOtroTokenYRevocaElAnterior`, `SesionesTests.Renovar_*` (6) y `ControlSesionTests`: renueva solo con el token por vencer y el usuario activo, y sin conexión conserva la sesión y reintenta.
- **Cuenta o institución no disponibles:** `CriteriosHu012Tests.CuentaDesactivadaDuranteLaSesion_NoPuedeRenovarYSuSesionSeCierra` y `SesionesTests.Renovar_CuentaBloqueada_*`, `Renovar_InstitucionInactiva_*` (MSG-002).
- **Aislamiento y tokens sin sesión:** `CriteriosHu012Tests.JtiDeUnaSesionDeOtraInstitucion_Responde401`, `TokenExpirado_ConSuSesionSinRevocar_Responde401` y `SesionEndpointTests.TokenSinSesionRegistrada_Responde401`.
- **Dominio:** `SesionTokenTests` (7): la revocación conserva la primera fecha y el primer motivo.
- **Cliente:** `ManejadorTokenTests` (5): un 401 de la API termina la sesión; `ControlSesionTests.LimiteDesdeEntorno_SoloAceptaDe1A30Minutos`.

### Verificación manual

En el cliente WPF contra la API real: con `SITBIO_MINUTOS_INACTIVIDAD=1` la sesión se cerró sola y apareció MSG-003 sobre la ventana a los 93 segundos (el control revisa cada 30 s); `sesion_token` quedó revocada con el motivo de inactividad. La confirmación de «Cerrar sesión» se cubre con las pruebas del cliente.

### Contrato implementado: `/api/v1/auth/refresh` y `/api/v1/auth/logout` (API-AUTH-REFRESH, API-AUTH-LOGOUT)

Ambas rutas exigen un token vigente cuyo `jti` no esté revocado.

| Método y ruta | Cuerpo | Respuesta correcta |
|---|---|---|
| `POST /auth/refresh` | — | 200 `{ token, expiraEn, rol, debeCambiarClave }` con otro `jti`; el anterior queda revocado |
| `POST /auth/logout` | `{ motivo }` opcional: `voluntario` (por defecto) o `inactividad` | 204 |

| Error | Cuándo |
|---|---|
| 400 con `errors.motivo` | Motivo distinto de `voluntario` o `inactividad` (el texto no se guarda) |
| 401 | Sin token, token revocado o expirado, o `jti` sin sesión registrada |
| 401 · `codigo: MSG-002` | Al renovar, la cuenta está bloqueada o inactiva o la institución está inactiva; la sesión se cierra |

## HU-010 · Auditoría de acceso y denegación de permisos RBAC

Caso de uso CU-001 · RF-013 · RN-008 · RNF-003 · RNF-015 · ADR-002 · Pantalla UI-024.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-014 | CA-HU-010-01: si un usuario intenta una acción sin permiso, la API responde 403 y registra el evento con usuario, fecha, hora y acción. | RNF-003, RNF-015 | Integración (API + PostgreSQL + MongoDB con el validador real): `CriteriosHu010Tests.CA_HU_010_01_UnaAccionSinPermiso_Responde403YQuedaEnLaBitacora` (método, ruta, rol y hora; sin el correo del usuario ni el del formulario). API: `AuditoriaEndpointTests.OtroRol_Responde403YQuedaRegistrado`, `SinToken_Responde401SinRegistrar`, `SiLaBitacoraFalla_El403SeRespondeIgual`. Infraestructura: `RegistroAuditoriaTests` (4), `BitacoraAuditoriaMongoTests.Registrar_*` (2), `ElValidadorRechazaUnaAccionFueraDelCatalogo`. Aplicación: `EventoAuditoriaTests` (4), incluido que las acciones son las del validador de V007. | Aprobado |
| CP-014 | CA-HU-010-02: una consulta con un tenant distinto al del usuario no devuelve datos de otro tenant. | RN-008, SEC-003 | Integración: `CriteriosHu010Tests.CA_HU_010_02_CadaAdministradorSoloVeLaBitacoraDeSuInstitucion` (ni la lista ni el filtro por un usuario de otra institución devuelven sus eventos). Infraestructura: `BitacoraAuditoriaMongoTests.CA_HU_010_02_CadaTenantSoloVeSuBitacora`. | Aprobado |
| CP-014 | CA-HU-010-03: el Administrador o el Auditor consultan la bitácora con filtros por usuario y fecha y ven los eventos registrados. | RF-013, RNF-003 | Integración: `CriteriosHu010Tests.CA_HU_010_03_*` (2): AC-01 y AC-07 filtran por usuario, fechas y acción; el Técnico recibe 403 y el intento queda registrado. API: `AuditoriaEndpointTests.CA_HU_010_03_AdministradorYAuditor_ConsultanConNombreDelUsuario`, `FiltroInvalido_Responde400`. Infraestructura: `BitacoraAuditoriaMongoTests.CA_HU_010_03_*`, `Consultar_PaginaConElTotalCompleto`. Cliente: `BitacoraViewModelTests` (12), `ServicioBitacoraHttpTests` (6). | Aprobado |

Pruebas adicionales de la historia:

- **Inicios y cierres de sesión en la bitácora:** `CriteriosHu010Tests.LoginYCierreDeSesion_QuedanEnLaBitacoraDelTenant`; `IniciarSesionTests.HU010_*` (6): `LOGIN_OK` y `LOGIN_FALLIDO` con el motivo, sin correo ni clave, y nada para un correo inexistente; `SesionesTests.HU010_*` (2).
- **Tolerancia a fallos:** si MongoDB no responde, el inicio de sesión, el cierre y el 403 siguen funcionando (`RegistroAuditoriaTests`, `AuditoriaEndpointTests.SiLaBitacoraFalla_El403SeRespondeIgual`).

Última ejecución: 05/11/2026 (UTC−5), rama `feature/HU-010-auditoria`, con PostgreSQL y MongoDB de Docker: 336 pruebas aprobadas, 0 fallidas, 0 omitidas. Las pruebas de integración usan una base temporal de MongoDB con el validador de `sitbio_dev` y la borran al terminar; en el CI se omiten porque allí no hay bases de datos.

### Verificación manual

En el cliente WPF contra la API real, con la institución de demostración: un inicio de sesión fallido, un 403 al consultar la bitácora y un cierre de sesión del Técnico aparecieron en UI-024 con su detalle en español. Como Auditor (AC-07) y como Administrador (AC-01): indicadores, filtro por usuario y por acción, fechas (incluidos el aviso de fecha mal escrita y el de fecha final anterior a la inicial), estado vacío y «Limpiar».

### Contrato implementado: `GET /api/v1/bitacora` (API-AUD)

Solo para AC-01 y AC-07 (sin token 401; con otro rol 403 y el intento queda registrado). El tenant sale del token.

| Parámetro | Uso |
|---|---|
| `usuarioId` | Eventos de un usuario |
| `desde`, `hasta` | Rango de fechas ISO 8601 (`hasta` exclusivo y posterior a `desde`) |
| `accion` | Una acción del catálogo: `LOGIN_OK`, `LOGIN_FALLIDO`, `CIERRE_SESION`, `ACCESO_DENEGADO`, `CREAR`, `ACTUALIZAR`, `ELIMINAR`, `EXPORTAR`, `CIERRE_OT`, `CAMBIO_ROL`, `CAMBIO_TENANT`, `IMPORTAR_CSV` |
| `page`, `pageSize` | Paginación (`pageSize` de 1 a 100, por defecto 50) |

Respuesta 200 `{ items, page, pageSize, total }`, del más reciente al más antiguo; cada evento es `{ id, fechaHora, usuarioId, usuario, accion, entidad, entidadId, ipOrigen, detalle }`. Con fechas o acción inválidas responde 400 con `errors` por campo.

### Pendientes

- **Acciones de cada módulo:** por ahora se registran los inicios y cierres de sesión y los 403. Crear, editar, desactivar, exportar o importar se registran a medida que llegue cada historia (el pendiente de HU-011 sobre la bitácora sigue abierto).
- **UI-024 frente al prototipo:** faltan la exportación a CSV, el indicador de integridad con hash encadenado y el filtro por entidad; ninguna historia ni la API los incluye todavía.
- **Validación de filtros:** se prueba en la API (`FiltroInvalido_Responde400`), no con pruebas propias de `ConsultarBitacora` en la capa de aplicación.
