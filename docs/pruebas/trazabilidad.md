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
| `GET /catalogos/{tipo}/{id}` | Detalle de un elemento (AC-01 y AC-02); en proveedores trae además `nombreContacto`, `correo` y `telefono` (HU-025) | 200; 404 si el catálogo o el elemento no existe o es de otra institución |
| `POST /catalogos/{tipo}` | Crear (HU-025): AC-02 en los cuatro catálogos; AC-01 solo en `ubicaciones`. Nace activo | 201 con `Location`; 400 por campo; 403 (queda `ACCESO_DENEGADO` en la bitácora); 404; 409 `CAT-001` (nombre repetido sin distinguir mayúsculas o NIT de proveedor repetido) |
| `PUT /catalogos/{tipo}/{id}` | Editar, activar o inactivar (`activo`; vacío conserva el estado). No hay `DELETE`: los elementos se inactivan (CA-HU-025-02) | 200; 400; 403; 404; 409 `CAT-001` |

Cuerpo de `POST` y `PUT /catalogos/{tipo}` (cada catálogo usa sus campos; los demás se ignoran):

```json
{
  "nombre": "Monitor multiparámetro", "descripcion": null,
  "claseRiesgo": "IIb", "requiereCalibracion": true, "periodicidadCalibracionMeses": 12,
  "nit": null, "tipoProveedor": null, "nombreContacto": null, "correo": null, "telefono": null,
  "activo": null
}
```

En tipos de equipo, sin clase queda «Por definir»; sin `requiereCalibracion` se calibra cada 12 meses al crear y conserva lo que tenía al editar; si no requiere calibración, la periodicidad queda vacía (RN-010).

Cuerpo de `POST` y `PUT /equipos` (fechas ISO 8601):

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

- **Gestión de catálogos (HU-025):** resuelto en los commits 40 y 41: dominio, repositorio, catálogo de tipos de falla (V009) y `POST`/`PUT`/`GET` por id con permisos por rol y bitácora (`CREAR`/`ACTUALIZAR` sobre `tipo_equipo`, `ubicacion`, `proveedor` o `tipo_falla`; el detalle lleva el nombre y los campos que cambiaron, nunca el contacto del proveedor). La pantalla UI-020 quedó en el commit 43.
- **DOC-06 (DAT-CATALOGOS):** debe incluir la tabla `tipo_falla` de la V009 (hoy lista `ubicacion`, `tipo_equipo` y `proveedor`). El .docx lo actualiza el responsable de documentación.
- **Fotografía del equipo (RF-031):** V008 ya tiene `foto_documento_id` y la API devuelve `tieneFoto`; la subida y API-EQ-FOTO llegan con HU-015.
- **Cambio de estado y baja:** la API todavía no ofrece `PATCH` de estado; llega con HU-014.
- **Alerta del plan vencido (RF-020):** el plan se marca vencido y la interfaz lo avisa; la alerta en el módulo de alertas llega con su historia.
- **Numeración de CP:** con esta historia se corrigió la tabla de HU-011 a CP-015, como asigna el DOC-04; antes usaba CP-004 a CP-006, que corresponden a HU-002 y HU-003.

## HU-024 · Configuración de la institución

Caso de uso CU-024 · RF-024 · RN-008 · RN-009 · SEC-003 · Pantalla UI-019 · API-TEN (`GET`/`PUT /api/v1/tenant/actual`).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-027 | CA-HU-024-01: al guardar los días de anticipación de las alertas, las alertas de vencimiento de la institución usan el nuevo valor. | RF-024, RF-020 | Integración (API + PostgreSQL): `CriteriosHu024Tests.CA_HU_024_01_*` (2): el valor queda en `tenant.dias_anticipacion_alerta` y la consulta lo devuelve; fuera de 7 a 90 días responde 400 en el campo sin cambiar nada. Aplicación: `GestionInstitucionTests`. Dominio: `TenantTests`. Cliente: `MiInstitucionViewModelTests`. El uso del valor en el cálculo de las alertas se prueba con HU-020. | Aprobado (parcial: alertas con HU-020) |
| CP-027 | CA-HU-024-02: el Administrador solo ve y edita los datos de su propio tenant; el alta, la suspensión y la desactivación las hace el proveedor. | RN-008, SEC-003, RNF-003 | Integración: `CriteriosHu024Tests.CA_HU_024_02_*` (2): el cuerpo no elige la institución ni cambia el plan o sus límites, la otra institución no cambia, no hay ruta `/tenant/{id}`; Ingeniero, Gerente y Técnico reciben 403 y sin token 401. API: `InstitucionEndpointTests`. Infraestructura: `RepositorioInstitucionPostgresTests`. | Aprobado |
| CP-027 | CA-HU-024-03: se ven el plan, su vigencia y el consumo de usuarios de gestión, equipos activos y almacenamiento, con aviso desde el 80 %. | RN-009, RF-024 | Integración: `CriteriosHu024Tests.CA_HU_024_03_MuestraPlanVigenciaYConsumo_ConAvisoDesdeEl80PorCiento` (plan Consultorio: 3 de 3 usuarios con aviso y límite, AC-04 no cuenta; 239 de 300 equipos = 79 % sin aviso y 240 = 80 % con aviso). Aplicación: `GestionInstitucionTests`. Cliente: `MiInstitucionViewModelTests`, `ServicioInstitucionHttpTests`. | Aprobado |

## HU-025 · Catálogos maestros

Caso de uso CU-025 · RF-025 · RF-030 · RN-008 · SEC-003 · Pantalla UI-020 · API-CAT (`/api/v1/catalogos/{tipo}`) · Migración V009.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-028 | CA-HU-025-01: un elemento agregado a un catálogo aparece como opción en los formularios de la institución. | RF-025, RN-008 | Integración (API + PostgreSQL): `CriteriosHu025Tests.CA_HU_025_01_*` (2): tipo de equipo, ubicación, proveedor y tipo de falla nacen activos, se listan solo en su institución y el tipo, la ubicación y el proveedor se usan al registrar un equipo; un nombre repetido responde 409 `CAT-001`. Aplicación: `GestionCatalogosTests`, `ConsultaCatalogosTests`. Dominio: `CatalogosTests`. Infraestructura: `RepositorioCatalogosPostgresTests`. | Aprobado |
| CP-028 | CA-HU-025-02: al desactivar un elemento ya utilizado, los registros anteriores conservan su valor. | RF-025 | Integración: `CriteriosHu025Tests.CA_HU_025_02_InactivarUnElementoUsado_LosRegistrosAnterioresConservanSuValor`: la hoja de vida conserva tipo y ubicación con sus nombres, el elemento sigue listado como inactivo, no hay `DELETE` (405), el equipo se sigue editando y un equipo nuevo no puede usarlos (400). Aplicación: `GestionCatalogosTests`. | Aprobado |
| CP-028 | CA-HU-025-03: el Administrador del Sistema crea, edita y desactiva áreas y ubicaciones y solo consulta los catálogos técnicos. | RF-025, RNF-003 | Integración: `CriteriosHu025Tests.CA_HU_025_03_*`: AC-01 crea, renombra e inactiva una ubicación con nomenclatura de TI; en tipos de equipo, proveedores y tipos de falla consulta (200) pero crear o editar responde 403 sin cambiar nada. API: `CatalogosEndpointTests`. | Aprobado |

- **Permisos y aislamiento:** `CriteriosHu025Tests.Permisos_OtrosRolesReciben403YSinToken401` (Técnico, Gerente y Personal Clínico: 403; sin token: 401) y `Aislamiento_OtraInstitucionNoVeNiEditaElElemento` (404 al consultar o editar un elemento de otra institución).

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` definida y V009 aplicada: `dotnet test SITBIO.sln --filter "FullyQualifiedName~CriteriosHu024Tests|FullyQualifiedName~CriteriosHu025Tests"`. Estas pruebas no necesitan MongoDB.

### Pendientes

- **Alertas con los días de anticipación (CA-HU-024-01):** el cálculo de las alertas de vencimiento llega con HU-020 (Bloque 2); entonces se agrega la prueba de que usa el valor guardado.
- **Pantalla UI-020:** resuelta en el commit 43 como pestaña «Catálogos» de UI-019 Configuración (AC-01 y AC-02). AC-02 gestiona los cuatro catálogos y edita la clase de riesgo y la calibración por defecto de los tipos de equipo; AC-01 gestiona solo ubicaciones y consulta el resto. Pruebas de cliente en `CatalogosViewModelTests` y `ServicioCatalogosHttpTests`.

## HU-006 · Motor de campos dinámicos

Caso de uso CU-006 · RF-006 · RN-008 · SEC-003 · RNF-003 · Pantalla UI-018 · API-CFG (`GET`/`POST /api/v1/configuracion-campos/{tipoEquipoId}`) · Colección `configuracion_campos` (script 02).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-009 | CA-HU-006-01: un campo nuevo (por ejemplo «Tasa de goteo») aparece en el formulario de mantenimiento de ese tipo de equipo y su valor se guarda en `camposDinamicos`. | RF-006 | Integración (API + PostgreSQL + MongoDB): `CriteriosHu006Tests.CA_HU_006_01_*` (3): el Técnico recibe nombre, etiqueta, tipo de dato, obligatoriedad y unidad solo para ese tipo; el documento pasa el `$jsonSchema` del script 02; una versión nueva reemplaza la activa y la anterior queda histórica sin editarse; publicar lo mismo o campos inválidos responde 400 sin guardar; la publicación queda en la bitácora. Aplicación: `GestionConfiguracionCamposTests`. Dominio: `ReglasCamposDinamicosTests`. Infraestructura: `ConfiguracionCamposMongoTests`. Escritorio (UI-018): `CamposDinamicosViewModelTests` (borrador por tipo, publicación de la versión nueva y conflicto CFG-001) y `ServicioConfiguracionCamposHttpTests`. El guardado del valor en la intervención: `CriteriosHu005Tests.CA_HU_005_02_*` (HU-005). Formulario (UI-013): `IntervencionViewModelTests.Cargar_ArmaUnControlPorTipoDeDato` (texto, número, decimal, fecha, sí/no y selección con su unidad y obligatoriedad) y `Guardar_EnviaCadaValorConSuTipo_YVuelveALaLista`. | Aprobado |
| CP-010 | CA-HU-006-02: un usuario de otra institución no ve los campos definidos en la mía. | RN-008, SEC-003 | Integración: `CriteriosHu006Tests.CA_HU_006_02_OtraInstitucion_NoVeNiModificaLosCampos`: el tipo ajeno responde 404 al consultar y al publicar; su propio tipo con el mismo nombre no trae campos; en MongoDB cada institución solo tiene sus versiones. Infraestructura: `ConfiguracionCamposMongoTests.CA_HU_006_02_*`. | Aprobado |
| CP-010 | CA-HU-006-03: el Administrador del Sistema solo consulta; definir y publicar versiones corresponde al Ingeniero Biomédico. | RNF-003 | Integración: `CriteriosHu006Tests.CA_HU_006_03_Administrador_SoloConsulta` (consulta 200, publicar 403 sin cambios) y `Permisos_OtrosRolesReciben403YSinToken401` (Técnico no publica; AC-04, AC-05 y AC-06 no consultan; sin token 401). API: `ConfiguracionCamposEndpointTests`. Escritorio: `CamposDinamicosViewModelTests.Administrador_SoloConsulta` (UI-018 sin agregar, quitar ni publicar). | Aprobado |

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~CriteriosHu006Tests|FullyQualifiedName~ConfiguracionCamposMongoTests"`. La base temporal de MongoDB copia el validador de `sitbio_dev.configuracion_campos` y crea el índice único `uq_config_tenant_tipo_version`.

### Pendientes

- **Formulario de intervención (CA-HU-006-01):** resuelto en B1-12. UI-013 genera los controles según `tipoDato` y la API valida `camposDinamicos` contra la versión activa (HU-005).
- **IDX-004 (DT-004):** el índice `camposDinamicos.tipo` de `intervenciones` no sirve porque las claves varían; sigue pendiente en el DOC-06.

## HU-003 y HU-018 · Reporte de falla con tiquete único y criticidad

Caso de uso CU-003 · RF-004 · RF-018 · RN-005 · RN-008 · SEC-003 · RNF-003 · Pantalla UI-009 · API-TIQ (`POST`/`GET /api/v1/tiquetes`, `GET /api/v1/tiquetes/{codigo}`) · Tablas `tiquete`, `historial_tiquete` e `historial_estado_equipo` (script 01) · Evento EVT-TIQUETE-CRITICO.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-006 | CA-HU-003-01: con el equipo y el tipo de falla, al enviar se genera un código de tiquete único y se notifica al panel del Ingeniero Biomédico. | RF-004 | Integración (API + PostgreSQL + MongoDB): `CriteriosHu003Tests.CA_HU_003_01_*`: el Personal Clínico recibe `TQT-000001` y `TQT-000002` en estado «recibido» a su nombre (sale del token); el Ingeniero los ve en `GET /tiquetes?estado=recibido` del más reciente al más antiguo; en la base quedan el tiquete y su primer registro de `historial_tiquete`, y la bitácora tiene `CREAR` con el código. Dominio: `TiqueteTests`. Aplicación: `GestionTiquetesTests.Reportar_*`. API: `TiquetesEndpointTests`. Infraestructura: `RepositorioTiquetesPostgresTests` (consecutivo por institución y 6 reportes simultáneos sin códigos repetidos). Cliente (UI-009): `ReportarFallaViewModelTests` (búsqueda del equipo por placa y serie sin equipos de baja, envío de los cuatro datos y MSG-005 con el código), `ServicioTiquetesHttpTests`. | Aprobado (parcial: la bandeja UI-011 llega con HU-004) |
| CP-006 | CA-HU-003-02: sin equipo, al enviar se muestra un mensaje de validación en español y no se genera tiquete. | RF-004, RNF-002 | Integración: `CriteriosHu003Tests.CA_HU_003_02_*`: 400 con «Selecciona el equipo que presenta la falla.» en `equipoId` y ningún tiquete en la base; también 400 con un tipo de falla fuera del catálogo o sin criticidad. La descripción es opcional (DOC-05, UI-009: máximo 500 caracteres). Aplicación: `GestionTiquetesTests.Reportar_SinEquipo_NoGeneraTiquete`, `Reportar_ConVariosErrores_LosDevuelveTodosJuntos`. Cliente: `ReportarFallaViewModelTests.Enviar_SinEquipo_MuestraLaValidacionYNoLlamaALaApi`, `Enviar_SinNada_MarcaLosTresObligatorios`. | Aprobado |
| CP-022 | CA-HU-018-01: la criticidad elegida (crítica, alta, media o baja) se guarda con el tiquete y sugiere la prioridad de la orden. | RF-018 | Integración: `CriteriosHu003Tests.CA_HU_018_01_*`: alta, media y baja quedan en `tiquete.criticidad` y la respuesta trae `prioridadSugerida` igual; el equipo sigue «Operativo» y sin historial; otra criticidad responde 400. Aplicación: `GestionTiquetesTests.Reportar_NoCritica_*` (3). | Aprobado |
| CP-022 | CA-HU-018-02: con una falla crítica, el equipo pasa a «Fuera de servicio» hasta el cierre de la orden (RN-005). | RN-005 | Integración: `CriteriosHu003Tests.CA_HU_018_02_*`: el equipo queda `FUERA_DE_SERVICIO` en la misma transacción del tiquete, `historial_estado_equipo` registra el cambio (Operativo → Fuera de servicio, motivo RN-005, usuario del token), el inventario lo muestra y la bitácora tiene `ACTUALIZAR` del equipo con el código del tiquete; una segunda falla crítica no repite el cambio. Aplicación: `GestionTiquetesTests.Reportar_Critica_*` (2). Infraestructura: `RepositorioTiquetesPostgresTests.Registrar_Critica_GuardaTiqueteEquipoEHistorialesJuntos`. Cliente: `ReportarFallaViewModelTests.Enviar_Critica_PrimeroPideConfirmacion` (MSG-006 antes de enviar), `CambiarCriticidad_CierraMsg006SinEnviar`. | Aprobado (el regreso a «Operativo» al aprobar la orden: `CriteriosHu021Tests.CA_HU_021_02_*`, HU-021) |
| CP-036 | Aislamiento entre instituciones: una institución no reporta sobre equipos ajenos ni ve sus tiquetes. | RN-008, SEC-003 | Integración: `CriteriosHu003Tests.CP_036_OtraInstitucion_NoReportaNiVeTiquetesAjenos`: reportar sobre el equipo de otra institución responde 404 sin crear tiquete ni cambiar el equipo; el código ajeno responde 404 y la lista no lo trae; cada institución numera desde `TQT-000001`. Infraestructura: `RepositorioTiquetesPostgresTests.Consultas_RespetanInstitucionYReportante`. | Aprobado |

- **Permisos:** `CriteriosHu003Tests.Permisos_SoloIngenieroYClinicoReportan_YElClinicoSoloVeLosSuyos`: Administrador, Técnico, Gerente y Directiva reciben 403 al reportar y sin token 401; el Técnico consulta todos los tiquetes y cada Personal Clínico solo los que reportó (404 para los de otros, DOC-06 Tabla 37). API: `TiquetesEndpointTests` (19).

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~CriteriosHu003Tests|FullyQualifiedName~RepositorioTiquetesPostgresTests"`. La institución de cada prueba registra un tipo de falla en su catálogo (V009): sin tipos de falla activos no se pueden reportar fallas.

### Pendientes

- **Pantalla UI-009 Reportar falla:** resuelta en B1-03. Es la pantalla de inicio del Personal Clínico (AC-04) y también la usa AC-02. Los tipos de falla salen de `GET /tiquetes/tipos-falla` (AC-02 y AC-04), porque quien reporta no consulta `/catalogos`. MSG-005 ofrece «Copiar código», «Ver seguimiento» (abre UI-010 con el tiquete, B1-06) y «Reportar otra falla».
- **Tiquete abierto del mismo equipo (FLU-003, A2):** avisar el código existente y ofrecer agregar la observación queda pendiente; hoy se crea un tiquete nuevo.
- **Notificación al panel (CA-HU-003-01):** hoy el Ingeniero ve los tiquetes recibidos al consultar `GET /tiquetes?estado=recibido`; la bandeja UI-011 y el aviso en su panel llegan con HU-004 (B1-07 a B1-09).
- **Seguimiento por código e historial (HU-017):** resuelto en B1-04 a B1-06 (ver HU-017).
- **Fin de RN-005:** desde B1-16, al aprobar la orden el equipo toma el estado de salida de la intervención (por ejemplo, de «Fuera de servicio» a «Operativo») con su registro en `historial_estado_equipo` (`GestionAprobacionTests`, `RepositorioOrdenesPostgresTests.GuardarAprobacion_*`). De punta a punta: `CriteriosHu021Tests.CA_HU_021_02_*` (B1-17).
- **HU-014:** este bloque trae del commit de HU-014 (rama sin fusionar) `HistorialEstadoEquipo` y los métodos de estado del equipo sin cambios, para que las dos ramas se fusionen sin conflictos nuevos.

## HU-017 · Seguimiento de tiquetes

Caso de uso CU-003 · RF-017 · RN-008 · SEC-003 · RNF-003 · Pantalla UI-010 · API-TIQ (`GET /api/v1/tiquetes?mios=true`, `GET /api/v1/tiquetes/{codigo}`, `GET /api/v1/tiquetes/{codigo}/historial`) · Tablas `tiquete` e `historial_tiquete` (script 01).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-021 | CA-HU-017-01: con el código de un tiquete veo su estado (recibido, asignado, en ejecución o cerrado) y su historial de cambios. | RF-017 | Integración (API + PostgreSQL): `CriteriosHu017Tests.CA_HU_017_01_ConElCodigo_VeElEstadoYElHistorialDeCambios`: el Personal Clínico consulta su tiquete por código (sin distinguir mayúsculas) y lo ve pasar por recibido, asignado, en ejecución y cerrado (con fecha de cierre); el historial trae los cuatro cambios del más antiguo al más reciente, con estado anterior, comentario y quién hizo cada uno; un código inexistente responde 404. Cliente (UI-010): `SeguimientoViewModelTests` (consulta por código con o sin el prefijo, detalle con el estado actual y la línea de tiempo del más reciente al más antiguo, aviso de RN-005 y «Ver seguimiento» desde MSG-005), `ServicioTiquetesHttpTests`. Aplicación: `GestionTiquetesTests.Historial_*` (el código se normaliza y el historial trae estado anterior, nuevo estado, fecha y quién hizo el cambio; un código que no ve responde 404 sin leer el historial). API: `TiquetesEndpointTests.Historial_PorCodigo_TraeLosCambiosYSinTokenResponde401`. Infraestructura: `RepositorioTiquetesPostgresTests.Historial_EnOrdenConQuienHizoCadaCambio_YSoloDeLaInstitucion` (del más antiguo al más reciente; otra institución no lo lee). | Aprobado (parcial: los cambios de estado los escribirán HU-004, HU-005 y HU-021; la prueba los escribe en la base) |
| CP-021 | CA-HU-017-02: el Personal Clínico solo ve los tiquetes que reportó. | RF-017, RNF-003 | Integración: `CriteriosHu017Tests.CA_HU_017_02_ElPersonalClinico_SoloVeLosTiquetesQueReporto`: la lista del Personal Clínico trae solo lo suyo, con o sin `mios=true`; los tiquetes de otro clínico y del Ingeniero responden 404, también su historial; el Ingeniero ve todos y con `mios=true` solo los que él reportó. Cliente: `SeguimientoViewModelTests.PersonalClinico_VeMisTiquetesConEstadoYCriticidadEnTexto` («Mis tiquetes»), `Ingeniero_VeTodosYPuedeFiltrarLosQueReporto`, `ConsultarCodigo_QueNoPuedeVer_ExplicaJuntoAlCampoYSigueEnLaLista`. Aplicación: `GestionTiquetesTests.Listar_PersonalClinico_SoloConsultaLosSuyos`, `Listar_Mios_ConsultaSoloLosQueReportoQuienConsulta`, `Obtener_NormalizaElCodigoYAplicaElAlcanceDelRol`. API: `TiquetesEndpointTests.Mios_ListaSoloLosQueReportoQuienConsulta`, `QuienHaceSeguimiento_Consulta`, `ObtenerPorCodigo_PersonalClinicoSoloLoSuyo`. | Aprobado |
| CP-036 | Aislamiento entre instituciones: una institución no sigue los tiquetes de otra. | RN-008, SEC-003 | Integración: `CriteriosHu017Tests.OtraInstitucion_NoSigueTiquetesAjenos`: el código y el historial ajenos responden 404 y la lista no los trae; el `TQT-000001` de cada institución es un tiquete distinto con su propio historial. | Aprobado |

- **Permisos:** según la matriz de permisos del DOC-03 (Seguimiento de Tiquetes), consultan AC-01, AC-02 y AC-03, AC-04 solo los propios y AC-07 en solo consulta; AC-05 y AC-06 reciben 403 (`TiquetesEndpointTests.OtrosRoles_NoConsultan`). Integración: `CriteriosHu017Tests.Permisos_ConsultanAdministradorTecnicoYEnteRegulador_GerenteYDirectivaNo`: Administrador, Técnico y Ente Regulador ven la lista, el tiquete y su historial; el Ente Regulador no reporta (403); Gerente y Directiva reciben 403 y sin token 401. B1-01 dejaba fuera a AC-01 y AC-07; se corrigió en B1-04.

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~CriteriosHu017Tests|FullyQualifiedName~RepositorioTiquetesPostgresTests"`. Mientras no existan la asignación (HU-004), la ejecución (HU-005) y el cierre (HU-021), `ApiConBaseRealFixture.CambiarEstadoTiqueteAsync` cambia el estado en la base y deja el registro en `historial_tiquete`.

### Pendientes

- **Pantalla UI-010:** resuelta en B1-06. Para el Personal Clínico es «Mis tiquetes» y para AC-01, AC-02, AC-03 y AC-07 es «Seguimiento»; el Ingeniero Biomédico puede ver solo los que reportó. Consulta por código (acepta solo el número), lista por estado y detalle con la línea de tiempo; solo lectura.
- **Notificaciones del cambio de estado:** avisar al Personal Clínico cuando su tiquete cambia de estado no es parte de CA-HU-017; queda para cuando existan la asignación (HU-004) y el cierre (HU-021).

## HU-004 · Asignación de órdenes de trabajo

Caso de uso CU-004 · RF-007 · RF-018 · RN-003 · RN-008 · SEC-003 · RNF-003 · Pantalla UI-011 · API-OT (`POST /api/v1/ordenes`, `GET /api/v1/ordenes?asignadas=me`, `GET /api/v1/ordenes/{id}`, `GET /api/v1/ordenes/tecnicos`) · API-TIQ (`GET /api/v1/tiquetes?estado=recibido&orden=criticidad`) · Tablas `orden_trabajo` y `historial_tiquete` (script 01).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-007 | CA-HU-004-01: al asignar un tiquete pendiente a un técnico activo, el tiquete pasa a «Asignado» y la orden aparece en el panel del técnico. | RF-007 | Integración (API + PostgreSQL + MongoDB): `CriteriosHu004Tests.CA_HU_004_01_AsignarATecnicoActivo_ElTiquetePasaAAsignadoYLaOrdenLlegaAlPanelDelTecnico`: el Personal Clínico reporta una falla crítica, el Ingeniero la asigna con fecha programada y recibe 201 con la orden «asignada» y prioridad crítica; el Personal Clínico ve el tiquete «asignado» con el cambio a nombre del Ingeniero; el técnico la ve con `?asignadas=me` y la abre; la bitácora guarda CREAR en `orden_trabajo` con el código del tiquete y la prioridad. `CA_HU_004_01_UnTiqueteYaAsignado_NoRecibeOtraOrden` (409 `OT-001`, una sola orden). Cliente (UI-011): `BandejaViewModelTests.Asignar_CreaLaOrden_CierraElPanel_YLaFilaPasaAAsignado` (envía tiquete, técnico, prioridad y fecha; el panel se cierra, la fila pasa a «Asignado a …» y la carga del técnico sube), `Asignar_SinFecha_NoLaEnviaYUsaLaPrioridadSugerida`, `Asignar_ConDatosIncompletos_ExplicaJuntoAlCampoYNoLlamaALaApi`, `Asignar_OtroIngenieroLoAsignoPrimero_AvisaYLaFilaYaNoSeAsigna`, `ServicioOrdenesHttpTests`. Aplicación: `GestionOrdenesTests.Asignar_CreaLaOrdenDelTecnicoYElTiquetePasaAAsignado` (orden correctiva «asignada» con la prioridad sugerida, fecha programada y quién asignó; historial recibido → asignado), `Asignar_ConOtraPrioridad_LaUsaEnLugarDeLaSugerida`, `Asignar_DejaLaOrdenEnLaBitacoraSinDatosPersonales` (CREAR en `orden_trabajo`), `Listar_ElTecnicoSoloVeLasSuyasYLosDemasConAsignadasAMi`. Dominio: `OrdenTrabajoTests`. API: `OrdenesEndpointTests.Ingeniero_Asigna_Y_RecibeLaOrdenConElTiqueteAsignado`, `AsignadasMe_ListaLasDelUsuarioDelToken`, `ObtenerPorId_ElTecnicoSoloLaSuya`. Infraestructura: `RepositorioOrdenesPostgresTests.Registrar_GuardaLaOrdenElHistorialYElTiqueteAsignadoJuntos`, `Listar_FiltraPorTecnicoYOrdenaPorPrioridad` (críticas primero; otra institución no las ve). | Aprobado |
| CP-007 | CA-HU-004-02: asignar a un usuario inactivo o sin rol de Técnico se rechaza (RN-003). | RN-003 | Integración: `CriteriosHu004Tests.CA_HU_004_02_AsignarAUnInactivoOSinRolDeTecnico_SeRechazaYElTiqueteSigueRecibido`: un técnico desactivado por el Administrador, un Personal Clínico, el propio Ingeniero y un técnico de otra institución reciben 400 con el mensaje de RN-003 en `tecnicoId`; el tiquete sigue «recibido» y no se crea ninguna orden. Aplicación: `GestionOrdenesTests.Asignar_AQuienNoEsTecnicoActivo_SeRechazaSinTocarElTiquete` (Ingeniero, Personal Clínico y técnico inactivo), `Asignar_AUnUsuarioDeOtraInstitucion_SeRechazaComoRn003`. Cliente: `BandejaViewModelTests.Asignar_RechazoRn003DeLaApi_SeMuestraJuntoAlTecnico`, `SinTecnicosActivos_LoExplicaConRn003`. API: `OrdenesEndpointTests.AQuienNoEsTecnico_Responde400ConRn003` (400 en `tecnicoId`). Infraestructura: `RepositorioOrdenesPostgresTests.Tecnico_TraeRolYEstadoSoloDentroDeLaInstitucion`. | Aprobado |
| CP-036 | Aislamiento entre instituciones: una institución no asigna ni ve tiquetes u órdenes de otra. | RN-008, SEC-003 | Integración: `CriteriosHu004Tests.OtraInstitucion_NoAsignaNiVeTiquetesUOrdenesAjenos`: el Ingeniero de otra institución recibe 404 con el tiquete ajeno, 400 (RN-003) con un técnico ajeno, 404 con la orden ajena y su lista no la trae. | Aprobado |

- **Un tiquete, una orden:** asignar un tiquete que ya no está «recibido» responde 409 con código `OT-001` (`GestionOrdenesTests.Asignar_TiqueteYaAsignado_Responde409`, `OrdenesEndpointTests.TiqueteYaAsignado_Responde409ConCodigo`); si dos ingenieros asignan a la vez, el índice `uq_orden_tiquete` deja pasar solo una (`RepositorioOrdenesPostgresTests.Registrar_AsignacionesSimultaneasDelMismoTiquete_SoloUnaGana`).
- **Bandeja UI-011 (RF-018):** los tiquetes «recibidos» salen de lo más crítico a lo menos crítico y, a igual criticidad, del más antiguo al más reciente (`GET /tiquetes?orden=criticidad`); el panel solo ofrece técnicos activos con sus órdenes abiertas, del menos al más ocupado (`GET /ordenes/tecnicos`, solo AC-02). Integración: `CriteriosHu004Tests.Bandeja_PriorizaPorCriticidad_YElPanelMuestraTecnicosActivosConSuCarga`. Infraestructura: `RepositorioTiquetesPostgresTests.Consultas_RespetanInstitucionYReportante`, `RepositorioOrdenesPostgresTests.Tecnicos_SoloActivosConSuCargaDelMenosAlMasOcupado`. API: `TiquetesEndpointTests.OrdenCriticidad_PideLaBandejaPriorizada_YOtroOrdenResponde400`, `OrdenesEndpointTests.Tecnicos_SoloParaQuienAsigna`. Cliente: `BandejaViewModelTests.Abrir_PideLosRecibidosPorCriticidad_YMuestraTextoNoSoloColor` (criticidad con texto, no solo color, ACC-001), `Panel_MuestraLosTecnicosActivosConSuCargaYLaPrioridadSugerida`, `Fabrica_UI011_AbreLaBandejaYLaCarga`.
- **Permisos:** solo el Ingeniero Biomédico (AC-02) asigna; consultan AC-01, AC-02, AC-03 (solo las suyas) y AC-07; AC-04, AC-05 y AC-06 reciben 403 y sin token 401 (`OrdenesEndpointTests.OtrosRoles_NoAsignan`, `QuienSigueLasOrdenes_Consulta`, `OtrosRoles_NoConsultan`, `SinToken_Responde401`). Integración: `CriteriosHu004Tests.Permisos_SoloElIngenieroAsigna_YElTecnicoSoloVeLasSuyas`: el Técnico y el Ente Regulador no asignan (403), el Personal Clínico no consulta (403), sin token 401; un técnico no lista ni abre la orden de otro técnico aunque no pida `?asignadas=me` y el Ente Regulador la consulta.

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~Orden|FullyQualifiedName~CriteriosHu004Tests"`.

### Pendientes

- **Criterios de punta a punta (CP-007):** resueltos en B1-08 (`CriteriosHu004Tests`).
- **Pantalla UI-011 (bandeja y asignación):** resuelta en B1-09. Lista de tiquetes por asignar, panel lateral con técnicos activos y su carga, prioridad sugerida editable, fecha programada opcional y «Asignar y crear orden».
- **Panel del técnico (UI-012):** resuelto en B1-12 con la pantalla «Mis órdenes» (HU-005).
- **Preventivas:** las órdenes de un plan (`plan_id`) llegan con el mantenimiento preventivo; por ahora solo se crean correctivas desde un tiquete.

## HU-005 · Ejecución de la orden y registro de la intervención

Caso de uso CU-005 · RF-005 · RF-006 · RN-004 · RN-008 · RT-002 · SEC-003 · RNF-003 · API-OT-ESTADO (`PATCH /api/v1/ordenes/{id}/estado`) · API-INT (`GET` y `PUT /api/v1/ordenes/{id}/intervencion`) · Tabla `orden_trabajo` (`fecha_inicio`, `intervencion_id`, script 01) · Colecciones `intervenciones` y `configuracion_campos` (script 02).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-008 | El técnico asignado inicia y pausa su orden; al iniciarla por primera vez el tiquete pasa a «en ejecución». | RF-005 | Integración (API + PostgreSQL + MongoDB): `CriteriosHu005Tests.CA_HU_005_01_ElTecnicoIniciaYPausaSuOrden_YElTiquetePasaAEnEjecucion`: el técnico inicia su orden asignada y recibe «en ejecución» con la fecha de inicio; el Personal Clínico ve su tiquete «en ejecución» con el cambio a nombre del técnico; pausar y reanudar no repiten el cambio del tiquete ni mueven la fecha de inicio; una segunda pausa responde 409 `OT-002`; cada cambio queda en la bitácora a nombre del técnico. Aplicación: `GestionEjecucionTests.Iniciar_LaPrimeraVez_LaOrdenYElTiquetePasanAEnEjecucion` (historial a nombre del técnico y bitácora), `PausarYReanudar_NoTocanElTiqueteNiLaFechaDeInicio`, `Pausar_UnaOrdenQueNoEstaEnEjecucion_Responde409SinGuardar` (`OT-002`), `AccionDesconocida_Responde400`, `OrdenDeOtroTecnico_SeRespondeComoNoEncontrada`. Dominio: `OrdenTrabajoTests.Iniciar_LaPrimeraVezFijaLaFechaDeInicio_YReanudarNoLaCambia`, `Transiciones_FueraDeOrden_SeRechazan`, `Tiquete_IniciarEjecucion_SoloDesdeAsignado`. Infraestructura: `RepositorioOrdenesPostgresTests.Guardar_IniciaLaOrdenEnlazaLaIntervencion_YElTiqueteQuedaEnEjecucion`. | Aprobado |
| CP-008 | La intervención se guarda en MongoDB con los `camposDinamicos` validados contra la versión activa del tipo de equipo y queda enlazada a la orden. | RF-005, RF-006 | Integración: `CriteriosHu005Tests.CA_HU_005_02_LaIntervencionSeGuardaEnMongoConLosCamposDeLaVersionActiva_YQuedaEnlazada`: sin iniciar la orden responde 409 `OT-002`; con dos versiones publicadas se valida contra la activa (versión 2) y el documento pasa el `$jsonSchema` del script 02 con `tasa_de_goteo` como número y la opción en su forma canónica; la orden queda con `tieneIntervencion`, el Ingeniero la consulta y una corrección reemplaza el mismo documento; la bitácora registra creación y corrección con el ObjectId. `CA_HU_005_02_CamposDinamicosInvalidos_SeRechazanJuntoAlCampoSinGuardarNada`: tipo incorrecto, obligatorio ausente y campo desconocido responden 400 con la clave `camposDinamicos.{nombre}`, sin documento ni enlace. Aplicación: `GestionEjecucionTests.Registrar_GuardaEnMongoConValoresTipados_Y_EnlazaLaOrden` (valores tipados, versión de campos, costo total y `intervencion_id`), `Registrar_CamposDinamicosInvalidos_ExplicaCadaCampo_YNoGuardaNada`, `Registrar_CamposFijosInvalidos_Responde400`, `Registrar_Correccion_ReemplazaElMismoDocumento_ConservaIdYFecha`, `Registrar_SinIniciarLaOrden_Responde409`, `Registrar_TipoSinCamposDinamicos_GuardaSinVersionYRechazaClavesExtra`; `ValoresCamposDinamicosTests` (tipo de dato, obligatorios, opciones y campos desconocidos). Infraestructura: `IntervencionesMongoTests.Guardar_PasaElValidador_YSeLeeIgualConSuObjectId` (el documento pasa el `$jsonSchema` del script 02), `Reemplazar_CorrigeElMismoDocumento_YOtraInstitucionNoLoVe`. | Aprobado |
| CP-008 | RT-002: si MongoDB no responde, la orden no cambia. | RT-002 | Sin MongoDB caído en la integración, se prueba con dobles y con un servidor inaccesible. Aplicación: `GestionEjecucionTests.Registrar_SiMongoNoResponde_LaOrdenNoCambia` (sin enlace, sin guardar en PostgreSQL y sin bitácora). Infraestructura: `IntervencionesMongoTests.MongoSinRespuesta_Responde503`. API: `EjecucionOrdenesEndpointTests.ErroresDeLaIntervencion_SeRespondenConSuCodigo` (503). | Aprobado |

- **Firmada no se modifica (RN-004):** una intervención con `cerrada_firmada` o una orden aprobada responde 409 `RN-004`; el reemplazo en MongoDB exige `cerrada_firmada` en falso (`GestionEjecucionTests.Registrar_SobreUnaIntervencionFirmada_Responde409Rn004`, `IntervencionesMongoTests.Reemplazar_UnaIntervencionFirmada_SeRechazaRn004`). Integración: `CriteriosHu005Tests.IntervencionFirmada_NoSeModifica_Rn004` (la prueba firma el documento como lo hará HU-021). Una orden cerrada que espera aprobación responde 409 `OT-003`.
- **Permisos:** solo el Técnico de Mantenimiento (AC-03) inicia, pausa y registra, y solo en sus órdenes (otra orden responde 404); consultan la intervención AC-01, AC-02, AC-03 (solo las suyas) y AC-07; los demás roles reciben 403 y sin token 401 (`EjecucionOrdenesEndpointTests.SoloElTecnico_IniciaPausaYRegistra`, `ConsultarIntervencion_PorRol`, `SinToken_Responde401`, `Tecnico_IniciaYRegistraLaIntervencion`). Integración: `CriteriosHu005Tests.Permisos_SoloElTecnicoAsignadoEjecuta_YOtraInstitucionNoLaVe`: otro técnico de la institución o de otra recibe 404; el Ingeniero no inicia ni registra (403) y el Personal Clínico no consulta la intervención (403); la otra institución no tiene documentos.
- **Cliente (UI-012 «Mis órdenes» y UI-013 «Intervención»):** `OrdenesViewModelTests` (el técnico ve sus órdenes con `?asignadas=me` y los demás roles las consultan todas sin ejecutar; cada estado ofrece Iniciar, Reanudar, Pausar y Registrar, Editar o Ver intervención; filtro por estado desde la primera página; OT-002 sin cambiar la fila; sin conexión con reintento; al guardar vuelve a la lista con la fila marcada), `IntervencionViewModelTests` (un control por `tipoDato` de la versión activa; cada valor viaja con su tipo JSON; validación del cliente junto a cada campo sin llamar a la API; errores `camposDinamicos.{nombre}` de la API junto a su campo; RT-002 avisa que la orden no cambió y deja reintentar; una intervención existente se precarga y avisa si cambió la versión; firmada queda en solo lectura con RN-004; pesos con puntos de miles y coma decimal), `ServicioOrdenesHttpTests` (`Listar_*`, `CambiarEstado_EnviaLaAccion`, `ObtenerIntervencion_*`, `Registrar_*`).

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~Ejecucion|FullyQualifiedName~Intervenciones|FullyQualifiedName~ValoresCamposDinamicos|FullyQualifiedName~OrdenTrabajoTests|FullyQualifiedName~CriteriosHu005Tests|FullyQualifiedName~OrdenesViewModelTests|FullyQualifiedName~IntervencionViewModelTests|FullyQualifiedName~ServicioOrdenesHttpTests"`.

### Pendientes

- **Criterios de punta a punta (CP-008):** resueltos en B1-11 (`CriteriosHu005Tests`).
- **Pantallas UI-012 (Mis órdenes) y UI-013 (Intervención):** resueltas en B1-12. UI-013 genera los controles según `tipoDato` de la versión activa.
- **Cierre, firma y aprobación:** con HU-021 (Esteban, B1-16), que fija `cerrada_firmada` y pasa la orden a «cerrada» y «aprobada».

## HU-021 · Cierre y firma de órdenes de trabajo

Caso de uso CU-004 · RF-021 · RN-004 · RN-005 · RN-007 · RN-010 · API-OT-ESTADO (`PATCH /api/v1/ordenes/{id}/estado`, acción `cerrar`) · API-OT-APROB (`POST /api/v1/ordenes/{id}/aprobacion`) · Tabla `orden_trabajo` (`fecha_cierre`, `aprobado_por_id`, `fecha_aprobacion`, `firma_aprobacion`, `ck_orden_firma`) · Colección `intervenciones` (`cerrada_firmada`, `estado_equipo_salida`, `equipo_snapshot`) · Tabla `calibracion` (vigencia, RN-007).

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-025 | CA-HU-021-01: con el diagnóstico y las acciones registrados, el técnico cierra la orden y queda pendiente de aprobación del Ingeniero. | RF-021 | Aplicación: `GestionEjecucionTests.Cerrar_ConLaIntervencionRegistrada_QuedaCerradaPendienteDeAprobacion` (estado «cerrada» con fecha de cierre, bitácora `ACTUALIZAR` con la acción; otro cierre responde 409 `OT-003` y la intervención ya no se edita), `Cerrar_SinIntervencionOSinIniciar_Responde409SinGuardar` (`OT-002`), `Cerrar_SinEstadoDeSalida_Responde400JuntoAlCampo`. Dominio: `OrdenTrabajoTests.Cerrar_*`. API: `EjecucionOrdenesEndpointTests.Tecnico_CierraSuOrden_YElIngenieroLaApruebaConSuFirma`. Infraestructura: `RepositorioOrdenesPostgresTests.GuardarAprobacion_*` (la fecha de cierre se guarda). Integración (API + PostgreSQL + MongoDB): `CriteriosHu021Tests.CA_HU_021_01_ConDiagnosticoYAcciones_ElTecnicoCierra_YQuedaPendienteDeAprobacion`: sin intervención responde 409 `OT-002`; con ella la orden queda «cerrada» con fecha de cierre y sin firma, el Ingeniero la ve con `?estado=cerrada`, el técnico ya no la corrige (409 `OT-003`) ni la reanuda, el tiquete sigue «en ejecución» y el cierre queda en la bitácora a nombre del técnico. `CA_HU_021_01_SinEstadoDeSalidaDelEquipo_NoSeCierra` (400 junto a `estadoEquipoSalida`). Cliente (UI-013, FLU-005): `IntervencionViewModelTests.Cerrar_PideConfirmarElResumen_GuardaLaHoja_YVuelveConLaOrdenCerrada` (resumen con equipo, estado de salida, repuestos, tiempo y costo; al confirmar se guarda la hoja y luego se cierra), `Cerrar_SinEstadoDeSalida_LoMarcaYNoAbreElResumen`, `Cerrar_CalibracionVencida_MuestraMsg013_YLaHojaQuedaGuardada` (RN-007), `Cerrar_SiFallaElGuardado_NoIntentaCerrar`, `SoloLectura_NoOfreceCerrar`; `OrdenesViewModelTests.Tecnico_AlCerrarDesdeLaIntervencion_VuelveConLaOrdenCerrada`; `ServicioOrdenesHttpTests.CambiarEstado_Cerrar_EnviaLaAccion`. | Aprobado |
| CP-025 | CA-HU-021-02: el Ingeniero aprueba con su firma la orden cerrada; la orden y su intervención quedan inalterables (RN-004). | RF-021, RN-004 | Aplicación: `GestionAprobacionTests.Aprobar_FirmaLaIntervencion_LaOrdenQuedaAprobada_ElTiqueteCerrado_YElEquipoConSuEstadoDeSalida` (primero se firma el documento en MongoDB con la copia del equipo; luego, en una transacción, la orden «aprobada» con firma, fecha y quién aprueba, el tiquete «cerrado» con su historial y el equipo con su estado de salida; bitácora `CIERRE_OT`), `Aprobar_OrdenQueNoEstaCerrada_Responde409_YLaYaAprobadaRn004`, `Aprobar_SinFirmaOMuyLarga_Responde400SinTocarNada`, `Aprobar_SiMongoNoResponde_LaOrdenSigueCerrada_YNoSeGuardaNada`, `Aprobar_EquipoYaEnSuEstadoDeSalida_NoCambiaElEquipo`. Infraestructura: `IntervencionesMongoTests.Firmar_MarcaCerradaFirmadaConSalidaYCopiaDelEquipo_YYaNoSeModifica` (pasa el validador del script 02; repetir la firma no cambia nada; el reemplazo posterior responde `RN-004`), `RepositorioOrdenesPostgresTests.GuardarAprobacion_LaOrdenAprobadaElTiqueteCerradoYElEstadoDelEquipoQuedanJuntos`. Dominio: `OrdenTrabajoTests.Aprobar_*`, `Tiquete_Cerrar_DesdeAsignadoOEnEjecucion_GuardaLaFechaDeCierre`. API: `EjecucionOrdenesEndpointTests.Tecnico_CierraSuOrden_YElIngenieroLaApruebaConSuFirma` (409 con código `RN-004` al aprobar de nuevo). Integración: `CriteriosHu021Tests.CA_HU_021_02_ElIngenieroApruebaConSuFirma_YLaOrdenYSuIntervencionQuedanInalterables`: tras una falla crítica (equipo «Fuera de servicio», RN-005) el Ingeniero aprueba con su firma; la orden queda «aprobada» con firma, fecha y quién aprueba; el documento de `intervenciones` queda con `cerrada_firmada`, el estado de salida y `equipo_snapshot`; el tiquete pasa a «cerrado» a nombre del Ingeniero y el equipo vuelve a «Operativo» con su historial; la bitácora guarda `CIERRE_OT` con el ObjectId; corregir la intervención, cerrar de nuevo o volver a aprobar responden 409 `RN-004` y el documento no cambia. `CA_HU_021_02_SoloSeApruebaUnaOrdenCerrada_YConFirma` (409 `OT-002` sin cerrar; 400 sin firma; nada se firma). Cliente (UI-014, FLU-006, NAV-010): `RevisionViewModelTests` (la intervención en solo lectura y sin su barra; «Aprobar y firmar» valida la firma y pide MSG-018 antes de llamar a la API; al confirmar vuelve con la orden «aprobada»; MSG-013 con `RN-007`, `RN-004` deja de permitir aprobar, la firma rechazada va junto al campo y sin conexión avisa con MSG-011), `OrdenesViewModelTests.Ingeniero_RevisaYApruebaLaCerrada_YVuelveConLaOrdenAprobada`, `SoloElIngeniero_VeRevisarYAprobar` (AC-01, AC-03 y AC-07 no lo ven); `ServicioOrdenesHttpTests.Aprobar_*`. | Aprobado |

- **Calibración vencida (RN-007, MSG-013):** un equipo que requiere calibración, con la última `fecha_vencimiento` anterior a hoy (hora de Colombia), no se cierra ni se aprueba como «Operativo»; responde 409 `RN-007` con el texto de MSG-013. Con otro estado de salida, o si el equipo no requiere calibración (RN-010), sí se cierra (`GestionEjecucionTests.Cerrar_ComoOperativoConLaCalibracionVencida_SeImpideConRn007`, `Cerrar_CalibracionVencidaPeroSaleFueraDeServicio_OEquipoQueNoRequiereCalibracion_SiCierra`, `GestionAprobacionTests.Aprobar_ComoOperativoConLaCalibracionVencida_SeImpideConRn007`). La vigencia se lee de `calibracion` con el tenant del equipo (`RepositorioOrdenesPostgresTests.Equipo_TraeTipoUbicacionYElVencimientoDeLaUltimaCalibracion_SoloDeLaInstitucion`). Integración: `CriteriosHu021Tests.Rn007_ConLaCalibracionVencida_NoSaleOperativo_PeroSiConOtroEstado` (409 `RN-007` con la fecha de vencimiento; con «Fuera de servicio» se cierra, se aprueba y el equipo queda así) y `Rn007_ConLaCalibracionVigente_SaleOperativo`; la calibración se inserta en la base con `ApiConBaseRealFixture.RegistrarCalibracionAsync` mientras llega HU-022.
- **Permisos:** solo el Técnico asignado cierra (AC-03; la orden de otro responde 404); solo el Ingeniero Biomédico (AC-02) aprueba; AC-01, AC-03, AC-04 y AC-07 reciben 403 y sin token 401 (`EjecucionOrdenesEndpointTests.SoloElIngeniero_ApruebaYFirma`). Integración: `CriteriosHu021Tests.Permisos_SoloElTecnicoCierraYSoloElIngenieroAprueba_YOtraInstitucionNoLaVe` (otro técnico 404 al cerrar, el Ingeniero 403; el técnico y el Personal Clínico 403 al aprobar; el Ingeniero de otra institución 404; la orden sigue «cerrada» sin firmar).
- **Si falla PostgreSQL después de firmar en MongoDB:** la orden sigue «cerrada» con la intervención ya firmada; repetir la aprobación termina el cambio porque la firma en MongoDB no toca un documento ya firmado.

### Cómo reproducir

Con `SITBIO_PRUEBAS_POSTGRES` y `SITBIO_PRUEBAS_MONGO` definidas: `dotnet test SITBIO.sln --filter "FullyQualifiedName~GestionAprobacionTests|FullyQualifiedName~GestionEjecucionTests|FullyQualifiedName~OrdenTrabajoTests|FullyQualifiedName~IntervencionesMongoTests|FullyQualifiedName~RepositorioOrdenesPostgresTests|FullyQualifiedName~EjecucionOrdenesEndpointTests|FullyQualifiedName~CriteriosHu021Tests"`. Las pantallas, sin bases de datos: `dotnet test tests/SITBIO.Desktop.Tests --filter "FullyQualifiedName~RevisionViewModelTests|FullyQualifiedName~IntervencionViewModelTests|FullyQualifiedName~OrdenesViewModelTests|FullyQualifiedName~ServicioOrdenesHttpTests"`.

### Pendientes

- **Criterios de punta a punta (CP-025):** resueltos en B1-17 (`CriteriosHu021Tests`).
- **Pantalla UI-014 (Revisión y firma):** resuelta en B1-18 junto con «Cerrar orden» en UI-013 (`RevisionViewModelTests`, `IntervencionViewModelTests.Cerrar_*`).
- **Equipo que requiere calibración sin ningún registro:** hoy no bloquea el cierre (RN-007 habla de calibración vencida). Decidir con HU-022 (B2-04) si debe bloquearse.
- **Aprobaciones simultáneas:** dos ingenieros que aprueban la misma orden a la vez pueden pasar ambos la revisión de estado; la última firma gana. Pendiente un control de concurrencia en `orden_trabajo`.
