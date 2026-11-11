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
| CP-015 | CA-HU-011-01: al completar los datos y elegir un rol, la cuenta se crea con la contraseña cifrada con BCrypt. | RF-001, RF-012, RNF-004 | Integración (API + PostgreSQL): `CriteriosHu011Tests.CA_HU_011_01_AdministradorCreaUsuario_ConHashBCryptYClaveTemporalQueDebeCambiar` (hash `$2…$12$` en la base y la clave temporal permite entrar con `debeCambiarClave`). Aplicación: `GestionUsuariosTests.Crear_*` (4), `ClaveTemporal_*`. API: `UsuariosEndpointTests.Administrador_CreaUsuarioConClaveTemporalYSinHash`. Cliente: `UsuariosViewModelTests.CA_HU_011_01_*` (2), `ServicioUsuariosHttpTests.Crear_*`. | Aprobado |
| CP-015 | CA-HU-011-02: sin rol de Administrador, crear o desactivar cuentas se deniega (RN-002). | RN-002, RNF-003 | Integración: `CriteriosHu011Tests.CA_HU_011_02_*` (2): el Ingeniero (AC-02) y el Técnico (AC-03), con sesión real, reciben 403 en las cinco operaciones y la base no cambia; sin token, 401. API: `UsuariosEndpointTests.OtroRol_Responde403EnTodasLasOperaciones` (AC-02, AC-03 y AC-07), `SinToken_Responde401`. Cliente: `GuiaRolesTests.ElMenuDeCadaRolEsElDelShell`. | Aprobado |
| CP-015 | CA-HU-011-03: con el cupo de usuarios de gestión lleno no se crean ni reactivan cuentas de gestión; Personal Clínico y ente regulador sí (RN-009). | RN-009, MSG-019 | Integración: `CriteriosHu011Tests.CA_HU_011_03_*` (2), con un tenant del plan Consultorio (3 cupos): 409 `RN-009` con «plan Consultorio», 201 para AC-04 y AC-07, desactivar libera cupo y reactivar con el cupo lleno responde 409. Aplicación: `GestionUsuariosTests.Rn009_*` (10), incluido el cambio de AC-04 a un rol de gestión. Cliente: `UsuariosViewModelTests.CA_HU_011_03_*` (2), `GuiaRolesTests.Rn009_*` (7). | Aprobado |

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

## HU-002 · Registro de hoja de vida biomédica y placa única

Caso de uso CU-002 · RF-003 · RF-019 · RF-020 · RF-030 · RN-001 · RN-008 · RN-009 · RN-010 · SEC-003 · Pantallas UI-005, UI-006 y UI-007 · Mensajes MSG-008, MSG-011 y MSG-019 · Migración V008.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-004 | CA-HU-002-01: con los datos obligatorios (serie, placa, marca, modelo y ubicación), el equipo se guarda en PostgreSQL con estado «Operativo». | RF-003 | Integración (API + PostgreSQL): `CriteriosHu002Tests.CA_HU_002_01_*` (2): 201 con el equipo «Operativo» leído de la base, y 400 en el campo que falta sin guardar nada. Dominio: `EquipoBiomedicoTests.Crear_*` (4). Aplicación: `GestionEquiposTests.Crear_DatosCompletos_GuardaOperativoYDevuelveElDto`, `Crear_TipoInexistenteOUbicacionInactiva_Responde400`. API: `EquiposEndpointTests.IngenieroBiomedico_RegistraEquipo201`. Infraestructura: `MapeoInventarioTests` (5), `InventarioPostgresTests.GuardarEquipoConInformacionRegulatoriaYPlan_*`. Cliente: `EquiposViewModelTests.Registrar_EnviaLosDatosCierraYConfirma`, `FormularioIncompleto_NoLlamaALaApi`; `FormularioEquipoViewModelTests.Validar_MarcaLosCamposObligatoriosConMsg008`, `Datos_ArmaElCuerpoParaLaApi`; `ServicioEquiposHttpTests.Registrar_*` (3). | Aprobado |
| CP-004 | CA-HU-002-02: una placa o un número de serie repetidos en la misma institución se rechazan (RN-001). | RN-001, RN-008 | Integración: `CriteriosHu002Tests.CA_HU_002_02_*` (3): 409 `RN-001` al registrar y al editar con la placa de otro equipo (sin cambiar nada), y la misma placa en otra institución se acepta. Aplicación: `GestionEquiposTests.Crear_PlacaOSerieRepetida_Responde409Rn001`. Infraestructura: `InventarioPostgresTests.PlacaRepetidaEnLaMismaInstitucion_*`, `MismaPlacaEnOtraInstitucion_SeAcepta`; `RepositorioEquiposPostgresTests.Duplicados_*`. Cliente: `EquiposViewModelTests.PlacaRepetida_DejaElFormularioAbiertoConElError`, `FormularioEquipoViewModelTests.ErrorRn001_SeMuestraEnLaPlaca`. | Aprobado |
| CP-004 | CA-HU-002-03: con el máximo de equipos activos del plan no se registra uno nuevo, se muestra el consumo y se conservan la consulta y la edición (RN-009). | RN-009, MSG-019 | Integración: `CriteriosHu002Tests.CA_HU_002_03_*` (2), con un tenant del plan Consultorio: 409 `RN-009` con el consumo, la lista y la edición siguen respondiendo, y un equipo dado de baja libera cupo. Aplicación: `GestionEquiposTests.Crear_ConElCupoDelPlanLleno_Responde409Rn009ConElConsumo`, `Actualizar_ConElCupoLleno_SiSePermite`, `Capacidad`. API: `EquiposEndpointTests.IngenieroBiomedico_CupoLleno_Responde409ConCodigoRn009`. Infraestructura: `RepositorioEquiposPostgresTests.Capacidad_CuentaSoloLosEquiposQueNoEstanDeBaja`. Cliente: `FormularioEquipoViewModelTests.ErrorRn009_EsElLimiteDelPlanMsg019`. | Aprobado |
| CP-004 | CA-HU-002-04: al elegir el tipo se proponen la clase de riesgo y la calibración, se pueden ajustar y se exigen el número y la vigencia de la autorización INVIMA (RF-030, RN-010). | RF-030, RN-010 | Integración: `CriteriosHu002Tests.CA_HU_002_04_*` (4): 400 sin número ni vigencia, periodicidad de calibración de 1 a 60 meses, estetoscopio clase I sin calibración ni periodicidad, y el ajuste frente al tipo queda en la bitácora. Dominio: `EquipoBiomedicoTests.InformacionRegulatoria_*` (6), `TipoEquipoYPlanTests.TipoEquipo_*` (2). Aplicación: `GestionEquiposTests.Crear_FaltanDatosRegulatorios_*`, `Crear_ConCalibracionYPeriodicidadFueraDeRango_*`, `Crear_SinCalibracion_*`, `Crear_IgualAlTipo_NoAnotaDiferencias`. Catálogos: `ConsultaCatalogosTests` (3), `CatalogosEndpointTests` (4), `RepositorioCatalogosPostgresTests` (3). Cliente: `FormularioEquipoViewModelTests.AlElegirElTipo_ProponeClaseYCalibracion`, `SiElEquipoDifiereDelTipo_Avisa`, `CalibracionFueraDeRango_SeRechaza` (3), `SinCalibracion_NoEnviaPeriodicidad`, `AutorizacionPorVencer_AvisaSinBloquear`; `EquiposViewModelTests.NuevoEquipo_CargaLosCatalogosYAbreElFormulario`, `SiNoCarganLosCatalogos_NoAbreElFormulario`. | Aprobado |
| CP-005 | CA-HU-002-05: al filtrar el inventario por clase de riesgo o por calibración se ven solo los equipos que cumplen, y la hoja de vida muestra la información regulatoria con el aviso de registro vencido o por vencer (RN-010). | RF-030, RN-010 | Integración: `CriteriosHu002Tests.CA_HU_002_05_*` (2). Dominio: `EquipoBiomedicoTests.AvisoDelRegistro_SegunLosDiasQueFaltan`. Aplicación: `GestionEquiposTests.Listar_*` (2), `Obtener_CalculaElAvisoConLaFechaDeColombia`. Infraestructura: `RepositorioEquiposPostgresTests.Listar_*` (2). Cliente: `EquiposViewModelTests` (lista, filtros y paginación, entre ellas `FiltroDeRiesgo_DistingueClaseYCalibracion`), `DetalleEquipoViewModelTests` (11, entre ellas `RN010_*` (3)), `ServicioEquiposHttpTests` (7 de consulta). | Aprobado |
| CP-004 | CA-HU-002-06: con la periodicidad y el último preventivo se calcula el próximo (ajustable) y se crea el plan activo; si ya pasó, nace vencido (RF-020); si no se conoce el último, se indica el próximo. | RF-019, RF-020 | Integración: `CriteriosHu002Tests.CA_HU_002_06_*` (5): cálculo y plan activo, plan vencido, próximo ajustado, próximo sin último que no puede haber pasado y reprogramación del mismo plan al editar. Dominio: `TipoEquipoYPlanTests.Plan_*` (4). Aplicación: `GestionEquiposTests.Preventivo_*` (4). Cliente: `FormularioEquipoViewModelTests.ProximoPreventivo_*`, `ProximoAjustadoAMano_NoSeRecalcula`, `SiElProximoCalculadoYaPaso_AvisaQueNaceVencido`, `NoSeConoceElUltimo_*`, `Editar_*` (2); `EquiposViewModelTests.EditarDesdeLaHojaDeVida_ActualizaElDetalle`. | Aprobado |

Pruebas adicionales de la historia:

- **Permisos (RNF-003):** `CriteriosHu002Tests.Permisos_ElTecnicoConsultaPeroNoRegistraYLaDirectivaNoConsulta`; `EquiposEndpointTests.SinToken_Responde401`, `RolesDeConsulta_ListanEquipos`, `Directiva_NoConsultaElInventario`, `SoloElIngenieroBiomedico_RegistraYEdita`; en el cliente, `EquiposViewModelTests.SoloElIngenieroBiomedico_VeRegistrar` y `ServicioEquiposHttpTests.Registrar_ConOtroRol_DaElMensajeDeGestion`.
- **Aislamiento entre instituciones (RN-008, SEC-003):** `CriteriosHu002Tests.Aislamiento_OtraInstitucionNoVeNiEditaElEquipo` (404); `EquiposEndpointTests.EquipoDeOtroTenant_Responde404`; `GestionEquiposTests.Actualizar_EquipoDeOtroTenant_Responde404`; `RepositorioEquiposPostgresTests.Listar_FiltraSoloLosEquiposDeLaInstitucion`, `ObtenerParaEditar_*`; `RepositorioCatalogosPostgresTests` (catálogos de cada institución).

Última ejecución: 06/11/2026 (UTC−5), rama `feature/HU-002-hoja-de-vida`, con PostgreSQL y MongoDB de Docker: 524 pruebas aprobadas, 0 fallidas, 0 omitidas (Dominio 52, Aplicación 88, Infraestructura 71, API 127, Cliente 186). En el CI se omiten las de integración porque allí no hay bases de datos.

### Verificación manual

En el cliente WPF contra la API real, como Ingeniero Biomédico (AC-02) de la institución de demostración, con datos de prueba que se borraron al terminar:

- Lista UI-005 con indicadores, búsqueda por serie, filtros por estado, clase y calibración, y paginación (enlaces desactivados en gris); hoja de vida UI-006 con el aviso «Autorización INVIMA por vencer» y la pestaña de información técnica.
- Formulario UI-007: al elegir «Monitor de signos vitales» se propone la clase IIb; con 6 meses de calibración aparece el aviso de diferencia con el tipo; el aviso de vencimiento cuenta los días; con un último preventivo antiguo, el próximo se calcula y aparece «Preventivo atrasado»; «Sin proveedor» como opción; guardar vacío marca los campos con MSG-008; la edición abre con los datos y el plan del equipo. No se guardó desde la interfaz (la bitácora es solo de inserción); el guardado lo cubren las pruebas de integración.

### Contrato implementado: `/api/v1/equipos` (API-EQ-LIST, API-EQ) y `GET /api/v1/catalogos/{tipo}` (API-CAT)

El tenant sale del token. Consultan AC-01, AC-02, AC-03, AC-04, AC-05 y AC-07; registran y editan solo AC-02 (otro rol 403, sin token 401). Un equipo de otra institución responde 404.

| Método y ruta | Uso | Respuestas |
|---|---|---|
| `GET /equipos` | Lista con `placa`, `serie` (coincidencia parcial), `estado`, `clase` (I, IIa, IIb, III), `calibracion` (true/false), `page` y `pageSize` (1 a 100, por defecto 20) | 200 `{ items, page, pageSize, total }`; 400 con `errors` por filtro |
| `GET /equipos/{id}` | Hoja de vida | 200; 404 |
| `POST /equipos` | Registrar | 201 con `Location`; 400 por campo; 409 `RN-001` (placa o serie repetida) o `RN-009` (cupo del plan) |
| `PUT /equipos/{id}` | Editar (el cupo del plan no la impide) | 200; 400; 404; 409 `RN-001` |
| `GET /catalogos/{tipo}` | `tipos-equipo`, `ubicaciones`, `proveedores` o `tipos-falla` (este último desde HU-025, V009), con los inactivos, ordenados por nombre (solo AC-01 y AC-02) | 200 `[{ id, nombre, activo, claseRiesgo, requiereCalibracion, periodicidadCalibracionMeses, descripcion, nit, tipoProveedor }]` (clase y calibración solo en tipos de equipo; `descripcion` en tipos de equipo y ubicaciones; `nit` y `tipoProveedor` en proveedores); 404 si el catálogo no existe |

Cuerpo de `POST` y `PUT` (fechas ISO 8601):

```json
{
  "tipoEquipoId": "…", "ubicacionId": "…", "proveedorId": null,
  "numeroSerie": "SN-100", "placaInventario": "CSR-EQ-0100", "marca": "Philips", "modelo": "MX450",
  "fechaAdquisicion": "2022-03-15",
  "tipoAutorizacion": "registro_sanitario", "registroInvima": "INVIMA 2019DM-0019801", "venceRegistro": "2027-02-10",
  "claseRiesgo": "IIb", "requiereCalibracion": true, "periodicidadCalibracionMeses": 12,
  "preventivo": { "periodicidadDias": 90, "ultimaEjecucion": "2026-09-02", "proximaFecha": null }
}
```

Cada equipo en la respuesta trae además `estado`, `estadoNombre`, los nombres de tipo, ubicación y proveedor, `avisoRegistro` (`Vigente`, `PorVencer`, `Vencido`), `tieneFoto` y `preventivo { id, periodicidadDias, ultimaEjecucion, proximaFecha, vencido }`.

### Cómo reproducir

La base local debe tener aplicada V008 (`database/README.md`). Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas, `dotnet test SITBIO.sln`.

### Pendientes

- **Gestión de catálogos (HU-025):** el commit 40 dejó listos el dominio y el repositorio (crear, editar, activar e inactivar; nombres únicos sin distinguir mayúsculas; NIT único por proveedor; 409 `CAT-001`) y agregó el catálogo de tipos de falla (V009). Los endpoints `POST`/`PUT` con permisos y bitácora llegan con el commit 41, en el mismo módulo `Modules/Catalogos`.
- **DOC-06 (DAT-CATALOGOS):** debe incluir la tabla `tipo_falla` de la V009 (hoy lista `ubicacion`, `tipo_equipo` y `proveedor`). El .docx lo actualiza el responsable de documentación.
- **Fotografía del equipo (RF-031):** V008 ya tiene `foto_documento_id` y la API devuelve `tieneFoto`; la subida y API-EQ-FOTO llegan con HU-015.
- **Cambio de estado y baja:** la API todavía no ofrece `PATCH` de estado; llega con HU-014.
- **Alerta del plan vencido (RF-020):** el plan se marca vencido y la interfaz lo avisa; la alerta en el módulo de alertas llega con su historia.
- **Numeración de CP:** con esta historia se corrigió la tabla de HU-011 a CP-015, como asigna el DOC-04; antes usaba CP-004 a CP-006, que corresponden a HU-002 y HU-003.
