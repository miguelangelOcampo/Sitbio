# Trazabilidad de pruebas

Relaciona cada caso de prueba (CP) con su criterio de aceptación (DOC-04), los requisitos (DOC-03) y las pruebas automáticas que lo verifican. Se actualiza en el commit de documentación de cada historia.

## HU-001 · Autenticación JWT y acceso por rol

Caso de uso CU-001 · RF-002 · RN-006 · RNF-004 · ADR-003 · ADR-011 · Pantalla UI-001.

| CP | Criterio | Requisitos | Pruebas automáticas | Resultado |
|---|---|---|---|---|
| CP-001 | CA-HU-001-01: con usuario y contraseña válidos, la API emite un JWT y el cliente habilita el menú del rol. | RF-002, RNF-004, RN-008 | Integración (API + PostgreSQL): `CriteriosHu001Tests.CA_HU_001_01_*` (2). API: `LoginEndpointTests.CredencialesValidas_Responde200ConTokenYRol`, `ContextoUsuarioHttpTests` (8). Aplicación: `IniciarSesionTests.CredencialesValidas_EmiteTokenYRegistraLaSesion`. Infraestructura: `GeneradorJwtTests` (6), `HasherBCryptTests` (4). Cliente: `LoginViewModelTests.CredencialesValidas_*`, `MenuPorRolTests` (12), `ShellViewModelTests` (5), `ManejadorTokenTests` (3). | Aprobado |
| CP-002 | CA-HU-001-02: con credenciales inválidas se muestra «Credenciales incorrectas» sin detallar el fallo. | RF-002, MSG-001, DD-005 | Integración: `CriteriosHu001Tests.CA_HU_001_02_*` (2), incluida la respuesta idéntica para un correo inexistente. API: `LoginEndpointTests.CredencialesInvalidas_Responde401ConMsg001` (2), `DatosVacios_Responde400`. Aplicación: `IniciarSesionTests.ClaveIncorrecta_*`, `CorreoInexistente_*`, `DatosVacios_*` (2). Cliente: `LoginViewModelTests.Msg001_*`, `DatosInvalidos_*` (4). | Aprobado |
| CP-003 | CA-HU-001-03: tras cinco intentos fallidos consecutivos, el sexto encuentra la cuenta bloqueada; solo el Administrador la reactiva. | RN-006, MSG-002 | Integración: `CriteriosHu001Tests.CA_HU_001_03_*` (2), incluido que los intentos son consecutivos. Dominio: `UsuarioTests.RegistrarIntentoFallido_*`, `RegistrarAccesoExitoso_*` (2). Aplicación: `IniciarSesionTests.CincoFallos_*`, `InstitucionInactiva_*`. API: `LoginEndpointTests.MasIntentosQueElLimite_Responde429`. Cliente: `LoginViewModelTests.OtrosFallos_*` (3). | Aprobado, con pendiente (ver abajo) |

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

- **Reactivación por el Administrador (CP-003):** el desbloqueo de cuentas llega con HU-011 (commit 13); hoy solo se verifica el bloqueo.
- **MSG-020:** el DOC-05 muestra «Servicio no activo» cuando la institución está inactiva y la contraseña es correcta; la API responde MSG-002 genérico en ese caso.
- **DOC-06:** ubica el componente de autenticación en `src/SITBIO.Api/Modules/Auth`; el código usa `Modules/Autenticacion` (rutas en español).
