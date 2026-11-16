# Matriz de trazabilidad y evaluación Go / No-Go (DOC-07, Tablas 6 y 10)

Valores medidos el 16/11/2026 sobre `main` (`ec14c6a`, PR #25), con PostgreSQL y MongoDB en Docker. La ejecución y su detalle están en [regresion.md](regresion.md); los criterios de aceptación por historia, en [trazabilidad.md](trazabilidad.md). Lo que no se hizo queda «Sin iniciar» o «Pendiente»; nada se marca como hecho sin una prueba aprobada.

## Matriz de trazabilidad (Tabla 6 del DOC-07)

| CP | Requisitos | Historia | Pruebas automáticas | Estado |
|---|---|---|---|---|
| CP-001 | RF-002 | HU-001 | `CriteriosHu001Tests` | Aprobado |
| CP-002 | RF-002 | HU-001 | `CriteriosHu001Tests` | Aprobado |
| CP-003 | RN-006 | HU-001, HU-011 | `CriteriosHu001Tests`, `CriteriosHu011Tests` (desbloqueo) | Aprobado |
| CP-004 | RF-003 | HU-002 | `CriteriosHu002Tests` (CA-HU-002-01) | Aprobado |
| CP-005 | RN-001 | HU-002 | `CriteriosHu002Tests` (CA-HU-002-02) | Aprobado |
| CP-006 | RF-004, RNF-001 | HU-003 | `CriteriosHu003Tests` | Aprobado en lo funcional; **Pendiente** la medición de usabilidad (90 % sin ayuda, en menos de 2 minutos) |
| CP-007 | RF-007, RN-003 | HU-004 | `CriteriosHu004Tests` | Aprobado |
| CP-008 | RF-005 | HU-005 | `CriteriosHu005Tests` | Aprobado |
| CP-009 | RF-006 | HU-006 | `CriteriosHu006Tests` | Aprobado |
| CP-010 | RF-006, RN-008 | HU-006 | `CriteriosHu006Tests` | Aprobado |
| CP-011 | RF-009 | HU-008 | `tests/analytics` (pytest en la CI) | Aprobado en la CI del PR #24; no ejecutado en la regresión local (sin pytest) |
| CP-012 | RF-010, RF-028 | HU-009 | `ConsultaAnaliticaTests`, `AnaliticaViewModelTests`, `tests/analytics` | Aprobado |
| CP-013 | RF-008, RNF-007 | HU-007 | `CriteriosHu007Tests`, `GeneradorReportesTests` | Aprobado |
| CP-014 | RF-013, RNF-003 | HU-010 | `CriteriosHu010Tests` | Aprobado |
| CP-015 | RF-001, RN-002, RN-009 | HU-011 | `CriteriosHu011Tests` | Aprobado |
| CP-016 | RF-011 | HU-012 | `CriteriosHu012Tests` | Aprobado |
| CP-017 | RF-012 | HU-013 | `CambiarContrasenaTests`, `CambioClaveEndpointTests`, `UsuariosEndpointTests` | Aprobado |
| CP-018 | RF-014 | HU-014 | — | **Pendiente**: hay modelo e historial de estados, pero la API no ofrece el cambio de estado ni la baja |
| CP-019 | RF-015 | HU-015 | — | **Sin iniciar** |
| CP-020 | RF-016, RN-001 | HU-016 | — | **Sin iniciar** |
| CP-021 | RF-017 | HU-017 | `CriteriosHu017Tests` | Aprobado |
| CP-022 | RF-018, RN-005 | HU-018 | `CriteriosHu003Tests` (CA-HU-018) | Aprobado |
| CP-023 | RF-019 | HU-019 | `CriteriosHu019Tests` | Aprobado |
| CP-024 | RF-020 | HU-020 | `CriteriosHu019Tests` (CA-HU-020) | Aprobado |
| CP-025 | RF-021, RN-004 | HU-021 | `CriteriosHu021Tests` | Aprobado |
| CP-026 | RF-022, RN-007 | HU-022 | `CriteriosHu022Tests` | Aprobado |
| CP-027 | RF-023 | HU-023 | `CriteriosHu023Tests` | Aprobado |
| CP-028 | RF-024, RN-009 | HU-024 | `CriteriosHu024Tests` | Aprobado |
| CP-029 | RF-025 | HU-025 | `CriteriosHu025Tests` | Aprobado |
| CP-030 | RF-026 | HU-026 | `CriteriosHu026Tests` | Aprobado |
| CP-031 | RF-027, RNF-007 | HU-027 | `CriteriosHu007Tests` (CA-HU-027), `GeneradorReportesTests` | Aprobado |
| CP-032 | RF-029, RN-009 | HU-028 | `CriteriosHu028Tests` | Aprobado |
| CP-033 | RF-030, RN-010 | HU-002 | `CriteriosHu002Tests` (CA-HU-002-04 y 05) | Aprobado |
| CP-034 | RF-019 | HU-002 | `CriteriosHu002Tests` (CA-HU-002-06) | Aprobado |
| CP-035 | RF-031 | HU-015 | — | **Sin iniciar**: la base ya guarda `foto_documento_id`; falta la subida y la vista |
| CP-036 | RNF-005, RN-008 | Todas | `RegresionCp036Tests` y las pruebas de aislamiento de cada historia | Aprobado |
| CP-037 | RNF-006, RNF-008 | — | — | **Sin iniciar**: falta la prueba de carga con k6 |

**Resumen:** 32 de 37 CP aprobados con pruebas automáticas (86,5 %); CP-006 queda aprobado solo en lo funcional. Pendientes: CP-018; sin iniciar: CP-019, CP-020, CP-035 y CP-037.

## Requisitos funcionales Must y Should

| Prioridad | RF | Con pruebas aprobadas |
|---|---|---|
| Must (12) | RF-001, 002, 003, 004, 005, 006, 011, 013, 017, 019, 021, 030 | 12 de 12 |
| Should (13) | RF-007, 008, 009, 012, 014, 018, 020, 022, 023, 024, 025, 026, 027 | 12 de 13 (falta RF-014) |
| **Total** | | **24 de 25 (96 %)** |

## Evaluación Go / No-Go (Tabla 10)

| Métrica de calidad | Criterio mínimo | Valor medido | Estado |
|---|---|---|---|
| Cobertura de pruebas unitarias del backend | ≥ 70 % (RNF-012) | 94,2 % de líneas (Api 98,4 %, Application 98,0 %, Infrastructure 95,9 %, Domain 86,0 %), con coverlet y las bases reales | Cumple |
| Defectos abiertos S1 / S2 | 0 | 0: la regresión no dejó fallos (1 401 de 1 401) y no hay defectos S1 o S2 registrados | Cumple |
| Casos de prueba ejecutados | 100 % de CP-001 a CP-037 | 32 de 37 (86,5 %) | **No cumple** |
| Cobertura de RF Must y Should | > 80 % con pruebas aprobadas (RNF-013) | 24 de 25 (96 %) | Cumple |
| Persistencia NoSQL MongoDB | 100 % correcto en camposDinamicos, intervenciones y bitácora | Pruebas de `ConfiguracionCamposMongoTests`, `IntervencionesMongoTests`, `BitacoraAuditoriaMongoTests` y `AislamientoTenantMongoTests` aprobadas | Cumple |
| Pipeline analítico Python | K-Means sobre datos sintéticos sin errores | Lote con pytest aprobado en la CI del PR #24; no ejecutado en la regresión local | Cumple, **por confirmar** en la reunión con el reporte de la CI |
| Estabilidad de la app de escritorio | Instalación y operación estable en Windows 10 y 11 (RNF-011) | Operada en Windows 11 durante el desarrollo; sin instalador ni prueba en Windows 10 | **Pendiente** |

## Decisión

**No-Go** para liberar el MVP completo: no se ejecutó el 100 % de los casos de prueba (faltan CP-018 a CP-020, CP-035 y CP-037) y la estabilidad en Windows 10 no está verificada.

Según la Figura 5 del DOC-07, se define un plan de corrección con nueva evaluación:

1. HU-014: `PATCH /equipos/{id}/estado` y baja lógica, con sus pruebas (CP-018).
2. Prueba de carga con k6 sobre el inventario con 5 000 equipos y 20 usuarios (CP-037).
3. Instalación y recorrido de la app en Windows 10 y 11 (RNF-011).
4. HU-015 y HU-016 (CP-019, CP-020, CP-035) son Could: se pueden pasar a una versión posterior si el instructor lo aprueba, y entonces salen del 100 % exigido.

La decisión final la toman el equipo y el instructor asesor del SENA CTMA en la reunión de cierre.
