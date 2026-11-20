# Regresión y prueba de restauración

## v1.2.0 (Z-04)

Ejecución de toda la suite sobre `main` con las filas de la v1.2.0 integradas (I-00 a I-14), con PostgreSQL y MongoDB reales en Docker y la semilla de demostración «Hospital Demo SITBIO» cargada en `sitbio_dev`.

- **Fecha:** 20/11/2026
- **Commit base:** `20fa169` (`main`, PR #57: panel de inicio como el prototipo, I-12 a I-14).
- **Cómo reproducir:** `scripts/regresion.sh`, que corre los proyectos de prueba uno a la vez. Los TRX quedan en `TestResults/regresion-20261120-0900/`.

### Resultado

| Proyecto | Pruebas | Correctas | Con error | Omitidas |
|---|---|---|---|---|
| SITBIO.Domain.Tests | 200 | 200 | 0 | 0 |
| SITBIO.Application.Tests | 372 | 372 | 0 | 0 |
| SITBIO.Infrastructure.Tests | 148 | 148 | 0 | 0 |
| SITBIO.Api.Tests | 428 | 428 | 0 | 0 |
| SITBIO.Desktop.Tests | 894 | 894 | 0 | 0 |
| **Total .NET** | **2 042** | **2 042** | **0** | **0** |
| tests/analytics (pytest) | — | — | — | Se ejecuta en la CI (job «Analytics»), con las pruebas nuevas del lote de 2 a 6 grupos |

Pasó a la primera; no hubo defectos nuevos. Con las bases activas no se omite ninguna prueba de integración. Frente a la v1.1.1 hay 176 pruebas .NET más (1 866 → 2 042).

### Pruebas nuevas de la v1.2.0

| Fila | Cambio | Pruebas automáticas | CP | Estado |
|---|---|---|---|---|
| I-00 | Avisos vacíos de la pestaña Mantenimientos de la hoja de vida (sin la plantilla roja de error) | `AvisosVaciosTests` | CP-004 | Aprobado |
| I-01 | Componente de gráficos (línea, área, barras, dispersión y arco) con etiqueta al pasar el mouse | `GraficoTests` | — | Aprobado |
| I-02 | Gráficos de Reportes e indicadores (HU-026): mini gráficos, mantenimientos por mes, estado del parque y fuera de servicio | `CriteriosHu026Tests`, `ConsultaIndicadoresTests`, `ReportesViewModelTests`, `PanelViewModelTests` | CP-030 | Aprobado |
| I-03 | El lote calcula de 2 a 6 grupos y les da nombre (HU-008) | `tests/analytics/test_lote.py` (CI), `EjecutorAnaliticaPythonTests` | CP-011 | Aprobado en la CI |
| I-04 | Recálculo de la analítica bajo demanda desde la API | `AnaliticaEndpointTests`, `RecalculoAnaliticaTests`, `ConsultaAnaliticaTests` | CP-011 | Aprobado |
| I-05 | Pantalla de Analítica con número de grupos, ejes y leyenda con nombres | `AnaliticaViewModelTests`, `GeneradorReportesTests` | CP-011 | Aprobado |
| I-06 | Textos clave seleccionables para copiar | `CopiarTextoTests` | — | Aprobado |
| I-07 | Calendario en todos los campos de fecha | `CampoFechaTests` | — | Aprobado |
| I-08 | Búsqueda incremental del equipo en Exportación | `ReportesViewModelTests` (exportación) | CP-031 | Aprobado |
| I-09 | Filas que se ven clicables (un clic o Enter abre el detalle) | `FilaClicableTests` | — | Aprobado |
| I-10 | Foto del equipo en la vista previa de la hoja de vida | `AccionesHojaVidaTests` | CP-035 | Aprobado |
| I-11 | Alertas de vencimiento con el diseño del prototipo, también en Mantenimientos (HU-020) | `CentroAlertasViewModelTests`, `MantenimientosViewModelTests` | CP-024 | Aprobado |
| I-12 | Indicadores del Panel con ícono y mini gráfico | `PanelViewModelTests` | — | Aprobado |
| I-13 | Actividad reciente y «Atención requerida» con íconos y prioridad | `PanelViewModelTests` | — | Aprobado |
| I-14 | Ilustraciones de estados vacíos, punto de aviso en el riel y animación de entrada | `ShellViewModelTests`, `AnimacionTests` | — | Aprobado |

Las filas sin CP son cambios de la interfaz sin un caso de prueba propio en la matriz; se verificaron además con capturas de pantalla contra el prototipo. El resto de los CP (CP-001 a CP-037) siguen aprobados con las mismas pruebas de la matriz de [go-no-go.md](go-no-go.md). CP-037 sigue sin iniciar (falta la prueba de carga con k6).

## v1.1.1 (Z-01)

Ejecución de toda la suite sobre `main` con los arreglos de la v1.1.1 integrados (G-01 a G-09 y la fotografía en la hoja de vida), con PostgreSQL y MongoDB reales en Docker y la semilla de demostración «Hospital Demo SITBIO» cargada en `sitbio_dev`.

- **Fecha:** 19/11/2026
- **Commit base:** `e8cf1b3` (`main`, PR #49: fotografía del equipo en la hoja de vida), después del PR #50 (historial de intervenciones, HU-005).
- **Cómo reproducir:** `scripts/regresion.sh`, que corre los proyectos de prueba uno a la vez. Los TRX quedan en `TestResults/regresion-20261119-1410/`.

### Resultado

| Proyecto | Pruebas | Correctas | Con error | Omitidas |
|---|---|---|---|---|
| SITBIO.Domain.Tests | 200 | 200 | 0 | 0 |
| SITBIO.Application.Tests | 365 | 365 | 0 | 0 |
| SITBIO.Infrastructure.Tests | 145 | 145 | 0 | 0 |
| SITBIO.Api.Tests | 418 | 418 | 0 | 0 |
| SITBIO.Desktop.Tests | 738 | 738 | 0 | 0 |
| **Total .NET** | **1 866** | **1 866** | **0** | **0** |
| tests/analytics (pytest) | — | — | — | Se ejecuta en la CI (job «Analytics») |

Pasó a la primera; no hubo defectos nuevos. Con las bases activas no se omite ninguna prueba de integración.

### Pruebas nuevas de la v1.1.1

| Fila | Cambio | Pruebas automáticas | CP | Estado |
|---|---|---|---|---|
| G-01 | Descripción de la falla visible en la bandeja, la orden y la intervención (HU-004) | `OrdenesEndpointTests`, `RepositorioOrdenesPostgresTests`, `BandejaViewModelTests`, `OrdenesViewModelTests`, `IntervencionViewModelTests` | CP-007 | Aprobado |
| G-03 | Filtro por tipo de equipo y búsqueda por nombre o modelo (UI-005) | `EquiposEndpointTests`, `RepositorioEquiposPostgresTests` (con aislamiento), `ServicioEquiposHttpTests`, `EquiposViewModelTests` | CP-004 | Aprobado |
| G-07 a G-09 | Historial de intervenciones de la institución (HU-005): consulta paginada con filtros y totales, aislamiento entre instituciones y pantalla | `HistorialIntervencionesTests` (integración con PostgreSQL), `ConsultaHistorialIntervencionesTests`, `HistorialIntervencionesViewModelTests`, `MenuFuncionesFuturasTests` | CP-008 | Aprobado |
| PR #49 | Fotografía del equipo en la hoja de vida en PDF (HU-027, RF-031) | `GeneradorReportesTests` (con foto y con imagen ilegible), `CriteriosRf031Tests`, `ReportesEndpointTests` | CP-035 | Aprobado |

El resto de los CP (CP-001 a CP-037) siguen aprobados con las mismas pruebas de la matriz de [go-no-go.md](go-no-go.md). CP-037 sigue sin iniciar (falta la prueba de carga con k6).

### Observación sobre la CI

Las pruebas HTTP que reemplazan los repositorios con sustitutos deben cubrir todas las dependencias del servicio. En la CI no hay cadenas de conexión, así que una dependencia real responde 500. Así falló primero el PR #49. Para reproducir la CI en local, corre las pruebas con `ASPNETCORE_ENVIRONMENT=Production`: en ese entorno no se cargan los user-secrets.

## v1.1.0 (F-01)

Ejecución de toda la suite sobre `main` con las historias de la v1.1.0 integradas (D-01 a D-07 y E-01 a E-11), con PostgreSQL y MongoDB reales en Docker y la semilla de demostración «Hospital Demo SITBIO» cargada en `sitbio_dev`.

- **Fecha:** 18/11/2026
- **Commit base:** `3fc2bd7` (`main`, PR #43: D-07, paridad con el prototipo v0.24).
- **Cómo reproducir:** `scripts/semilla-demo.sh` (si la institución de demostración no existe) y luego `scripts/regresion.sh`. Desde esta entrega el script corre los proyectos de prueba uno a la vez y termina con error si alguno falla.

### Resultado

| Proyecto | Pruebas | Correctas | Con error | Omitidas |
|---|---|---|---|---|
| SITBIO.Domain.Tests | 200 | 200 | 0 | 0 |
| SITBIO.Application.Tests | 357 | 357 | 0 | 0 |
| SITBIO.Infrastructure.Tests | 142 | 142 | 0 | 0 |
| SITBIO.Api.Tests | 407 | 407 | 0 | 0 |
| SITBIO.Desktop.Tests | 678 | 678 | 0 | 0 |
| **Total .NET** | **1 784** | **1 784** | **0** | **0** |
| tests/analytics (pytest) | — | — | — | Se ejecuta en la CI (job «Analytics»); el entorno local no tiene scikit-learn |

Con las bases activas no se omite ninguna prueba de integración. La cobertura de líneas no se volvió a medir en esta ejecución; la última medición es la de la v1.0.0 (más abajo).

### Defecto encontrado y corregido

En la primera ejecución (solución completa en paralelo) falló 1 de 1 784: `RepositorioOrdenesPostgresTests.Registrar_AsignacionesSimultaneasDelMismoTiquete_SoloUnaGana`. El defecto estaba en la prueba, no en el producto: su ayudante `Asignar` decía asignar «como lo hace `GestionOrdenes`», pero omitía la verificación previa del estado del tiquete. Cuando un hilo leía el tiquete después de que otro ya lo había asignado, el dominio lanzaba `InvalidOperationException` en lugar del conflicto OT-001 que devuelve la API. `GestionOrdenes` sí hace esa verificación, así que el usuario recibe el 409 esperado. La prueba ahora repite la verificación, y pasó 10 de 10 veces seguidas; la regresión completa, un proyecto a la vez, quedó en 1 784 de 1 784.

### Casos de prueba nuevos de la v1.1.0

| CP | Historia | Pruebas automáticas | Estado |
|---|---|---|---|
| CP-019 | HU-015 (documentos del equipo, RF-015) | `CriteriosHu015Tests` (9 casos: PDF, JPG y PNG; otros formatos y tamaños con 400; nombre repetido; roles; aislamiento; plan lleno con MSG-019; desactivación con bitácora) | Aprobado |
| CP-020 | HU-016 (importación masiva, RF-016) | `CriteriosHu016Tests` (7 casos: CSV y Excel; vista previa sin guardar; duplicados y filas inválidas con informe por fila; cupo del plan; roles; aislamiento), `PlanillaImportacionTests`, `ImportacionViewModelTests` | Aprobado |
| CP-035 | HU-015 (fotografía, RF-031) | `CriteriosRf031Tests` (7 casos: solo las dos versiones reducidas; rechazo con 400; plan lleno; hoja de vida con foto por tamaño; cambiar y quitar; roles; aislamiento), `FotoEquipoViewModelTests` | Aprobado |

El resto de los CP (CP-001 a CP-037) siguen aprobados con las mismas pruebas de la matriz de [go-no-go.md](go-no-go.md). Siguen igual que en la v1.0.0 CP-011 (aprobado en la CI, no en local) y CP-037 (sin iniciar: falta la prueba de carga con k6).

### Prueba de restauración (DOC-09, Tablas 32 y 33)

`scripts/prueba-restauracion.sh` respalda DEP-DEV con los comandos de la Tabla 32 (`pg_dump -Fc` y `mongodump --archive --gzip`) y los restaura en un DEP-QA desechable: contenedores nuevos de `postgres:16` y `mongo:7`, sin volúmenes y publicados solo en 127.0.0.1. Restaura con `pg_restore -c` y `mongorestore --drop`, compara el conteo de cada tabla y colección, y corre las pruebas de humo con la API apuntando a DEP-QA. Deja el respaldo y el registro en `TestResults/restauracion-<fecha>/`.

| Fecha | Recurso | Respaldo | Tiempo real | Resultado |
|---|---|---|---|---|
| 18/11/2026 | PostgreSQL (DEP-QA) | `sitbio_20261118.dump`, 208 KB, 0,7 s | 1,3 s | Correcto: 18 tablas y 2 409 filas, iguales al origen |
| 18/11/2026 | MongoDB (DEP-QA) | `sitbio_20261118.gz`, 152 KB, 0,7 s | 0,9 s | Correcto: 4 colecciones y 2 795 documentos, iguales al origen |
| 18/11/2026 | Ambas, con pruebas de humo | — | 2,1 s (RTO: 30 min) | `GET /api/v1/salud` → 200; `POST /api/v1/auth/login` con un correo inexistente → 401, no 500, lo que confirma que la API consulta la base restaurada |

Los tiempos corresponden al volumen de la semilla de demostración (seis meses simulados de una institución) más las instituciones de prueba; con el volumen de una IPS en producción serán mayores, pero el margen frente al RTO de 30 minutos es amplio. Esta fila es la que se copia en la Tabla 33 del DOC-09 (F-02).

## v1.0.0 (C-01 e I-03)

Ejecución de toda la suite sobre `main` con todas las historias del alcance integradas, con PostgreSQL y MongoDB reales en Docker.

- **Fecha:** 16/11/2026
- **Commit base:** `46bf36c` (`main`, PR #29: HU-014), después de I-01 (PR #27, pantallas de HU-005 y HU-021) e I-02 (PR #29).
- **Cómo reproducir:** `scripts/regresion.sh`. Levanta `sitbio-postgres` y `sitbio-mongo` con `docker compose`, lee las credenciales de `.env` (o de `$SITBIO_ENV`) sin mostrarlas, compila en Release y corre cada proyecto de pruebas por separado. Deja los TRX en `TestResults/regresion-<fecha>/`.
- **Primera ejecución (C-01):** 15/11/2026 sobre `ec14c6a` (PR #25), 1 401 de 1 401.

### Resultado

| Proyecto | Pruebas | Correctas | Con error | Omitidas |
|---|---|---|---|---|
| SITBIO.Domain.Tests | 192 | 192 | 0 | 0 |
| SITBIO.Application.Tests | 328 | 328 | 0 | 0 |
| SITBIO.Infrastructure.Tests | 136 | 136 | 0 | 0 |
| SITBIO.Api.Tests | 378 | 378 | 0 | 0 |
| SITBIO.Desktop.Tests | 509 | 509 | 0 | 0 |
| **Total .NET** | **1 543** | **1 543** | **0** | **0** |
| tests/analytics (pytest) | — | — | — | Se ejecuta en la CI (job «Analytics»); no en este equipo, que no tiene pytest |

Con las bases activas no se omite ninguna prueba de integración.

### Cobertura de líneas (coverlet, Release)

| Ensamblado | Líneas cubiertas | Cobertura |
|---|---|---|
| SITBIO.Api | 704 de 713 | 98,7 % |
| SITBIO.Application | 2 252 de 2 278 | 98,9 % |
| SITBIO.Infrastructure | 1 544 de 1 599 | 96,6 % |
| SITBIO.Domain | 1 430 de 1 572 | 91,0 % |
| **Backend** | **5 930 de 6 162** | **96,2 %** |
| SITBIO (cliente WPF, referencia) | 4 192 de 5 089 | 82,4 % |

La cobertura une las pruebas unitarias y las de integración de cada proyecto; se excluyen los archivos generados (`obj/`, `*.g.cs`).

### Casos de prueba

El estado de cada CP, con sus pruebas, está en la matriz de [go-no-go.md](go-no-go.md). En esta ejecución todos los CP del alcance que tienen prueba automática quedaron aprobados, incluido CP-018 (HU-014, `CriteriosHu014Tests`), que entró con I-02.

### CP-036 · Aislamiento entre instituciones

`RegresionCp036Tests.CP_036_NingunRolDeOtraInstitucion_VeLosDatosDeA_EnNingunaConsulta`:

1. La institución A registra un equipo y reporta una falla.
2. Cada uno de los siete roles de la institución B recorre los listados de la API: equipos, tiquetes, órdenes, técnicos, usuarios, planes preventivos, calibraciones, alertas, bitácora, indicadores, institución actual y analítica.
3. Ninguna respuesta contiene el tenant, el equipo, la placa ni el correo de A, y ninguna responde 500.
4. Las consultas directas por id de A (hoja de vida, línea de tiempo, PDF e historial del tiquete) responden 404 o 403.

La bitácora de B sí guarda esos intentos denegados con la ruta pedida. Son registros de sus propios usuarios, no una fuga; por eso la prueba revisa los listados antes de las consultas por id.

### Observaciones

- **Integración de HU-014:** el registro del equipo ahora guarda su estado inicial en `historial_estado_equipo`. La línea de tiempo (HU-023) no lo repite, porque ya aparece como «Equipo registrado», y las pruebas de HU-003 cuentan solo los cambios de estado.
- **Pruebas de tiempo y concurrencia:** con todos los proyectos en paralelo en un mismo equipo fallaron por carga la de 1 000 registros en PDF (RNF-007), la del Excel del historial y la de asignaciones simultáneas. Corriendo un proyecto a la vez pasan, y por eso el script corre los proyectos uno por uno.
- **Duración:** `SITBIO.Api.Tests` tarda entre 1 y 7 minutos con las bases reales, según la carga del equipo.
