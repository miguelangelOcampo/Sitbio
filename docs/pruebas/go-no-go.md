# Matriz de trazabilidad y evaluación Go / No-Go (DOC-07, Tablas 6 y 10)

Evaluación vigente: v1.3.0 (21/11/2026, sección «Evaluación Go / No-Go de la v1.3.0»). Valores medidos el 18/11/2026 para la v1.1.0 sobre `main` (`f0d4d8a`, PR #44, después de D-01 a D-07, E-01 a E-11 y F-01), con PostgreSQL y MongoDB en Docker y la semilla de demostración cargada. La regresión y la prueba de restauración están en [regresion.md](regresion.md); los criterios de aceptación por historia, en [trazabilidad.md](trazabilidad.md). La evaluación de la v1.0.0 (16/11/2026) se conserva al final. Nada se marca como hecho sin una prueba aprobada.

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
| CP-011 | RF-009 | HU-008 | `tests/analytics` (pytest en la CI) | Aprobado en la CI (job «Analytics», corrida #98 sobre `f0d4d8a`); no se ejecuta en local (sin scikit-learn) |
| CP-012 | RF-010, RF-028 | HU-009 | `ConsultaAnaliticaTests`, `AnaliticaViewModelTests`, `tests/analytics` | Aprobado |
| CP-013 | RF-008, RNF-007 | HU-007 | `CriteriosHu007Tests`, `GeneradorReportesTests` | Aprobado |
| CP-014 | RF-013, RNF-003 | HU-010 | `CriteriosHu010Tests` | Aprobado |
| CP-015 | RF-001, RN-002, RN-009 | HU-011 | `CriteriosHu011Tests` | Aprobado |
| CP-016 | RF-011 | HU-012 | `CriteriosHu012Tests` | Aprobado |
| CP-017 | RF-012 | HU-013 | `CambiarContrasenaTests`, `CambioClaveEndpointTests`, `UsuariosEndpointTests` | Aprobado |
| CP-018 | RF-014 | HU-014 | `CriteriosHu014Tests` | Aprobado |
| CP-019 | RF-015 | HU-015 | `CriteriosHu015Tests`, `GestionDocumentosTests`, `DocumentosEquipoViewModelTests` | Aprobado (v1.1) |
| CP-020 | RF-016, RN-001 | HU-016 | `CriteriosHu016Tests`, `PlanillaImportacionTests`, `ImportacionViewModelTests` | Aprobado (v1.1) |
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
| CP-035 | RF-031 | HU-015 | `CriteriosRf031Tests`, `GestionFotoEquipoTests`, `FotografiaEquipoTests`, `ReductorFotoTests`, `FotoEquipoViewModelTests` | Aprobado (v1.1) |
| CP-036 | RNF-005, RN-008 | Todas | `RegresionCp036Tests` y las pruebas de aislamiento de cada historia | Aprobado |
| CP-037 | RNF-006, RNF-008 | — | — | **Sin iniciar**: falta la prueba de carga con k6 |

**Resumen:** de los 37 CP, 36 están aprobados con pruebas automáticas (97,3 %), incluidos CP-019, CP-020 y CP-035 de la v1.1; CP-006 queda aprobado solo en lo funcional (falta la medición de usabilidad). Sin iniciar: CP-037 (prueba de carga con k6).

## Historias del DOC-04 (28 de 28)

| HU | Historia | CP | Estado |
|---|---|---|---|
| HU-001 | Autenticación y acceso por rol | CP-001, CP-002, CP-003 | Aprobado |
| HU-002 | Hoja de vida y placa única | CP-004, CP-005, CP-033, CP-034 | Aprobado |
| HU-003 | Reporte de falla con tiquete | CP-006 | Aprobado |
| HU-004 | Asignación de órdenes | CP-007 | Aprobado |
| HU-005 | Ejecución de la orden | CP-008 | Aprobado |
| HU-006 | Campos dinámicos | CP-009, CP-010 | Aprobado |
| HU-007 | Reportes PDF y Excel | CP-013 | Aprobado |
| HU-008 | Lote analítico K-Means | CP-011 | Aprobado |
| HU-009 | Vista de analítica | CP-012 | Aprobado |
| HU-010 | Auditoría y denegación RBAC | CP-014 | Aprobado |
| HU-011 | Usuarios y roles | CP-015, CP-003 | Aprobado |
| HU-012 | Sesión e inactividad | CP-016 | Aprobado |
| HU-013 | Contraseña temporal | CP-017 | Aprobado |
| HU-014 | Estado y baja lógica | CP-018 | Aprobado |
| HU-015 | Documentos y fotografía del equipo | CP-019, CP-035 | Aprobado |
| HU-016 | Importación masiva | CP-020 | Aprobado |
| HU-017 | Seguimiento de tiquetes | CP-021 | Aprobado |
| HU-018 | Criticidad y fuera de servicio | CP-022 | Aprobado |
| HU-019 | Planes preventivos | CP-023 | Aprobado |
| HU-020 | Alertas de vencimiento | CP-024 | Aprobado |
| HU-021 | Cierre y firma | CP-025 | Aprobado |
| HU-022 | Calibraciones | CP-026 | Aprobado |
| HU-023 | Línea de tiempo | CP-027 | Aprobado |
| HU-024 | Configuración de la institución | CP-028 | Aprobado |
| HU-025 | Catálogos maestros | CP-029 | Aprobado |
| HU-026 | Tablero de indicadores | CP-030 | Aprobado |
| HU-027 | Hoja de vida en PDF | CP-031 | Aprobado |
| HU-028 | Planes y solicitud del servicio | CP-032 | Aprobado |

**28 de 28 historias** con sus criterios de aceptación verificados por pruebas automáticas (la v1.0.0 cerró con 26; la v1.1 agrega HU-015 y HU-016). HU-003 queda aprobada en lo funcional, como CP-006.

## Requisitos funcionales Must y Should

| Prioridad | RF | Con pruebas aprobadas |
|---|---|---|
| Must (12) | RF-001, 002, 003, 004, 005, 006, 011, 013, 017, 019, 021, 030 | 12 de 12 |
| Should (13) | RF-007, 008, 009, 012, 014, 018, 020, 022, 023, 024, 025, 026, 027 | 13 de 13 |
| Could (3) | RF-015, 016, 031 | 3 de 3 |
| **Total Must y Should** | | **25 de 25 (100 %)** |

## Evaluación Go / No-Go de la v1.3.0 (21/11/2026)

Regresión sobre `main` (`15f1db9`, PR #63) con las filas J-01 a J-12 de la v1.3.0 integradas y la migración V011 aplicada ([regresion.md](regresion.md)). La matriz de CP y de historias de arriba no cambia: las mejoras se verificaron con pruebas nuevas asociadas a CP-012, CP-015, CP-021, CP-030, CP-032 y CP-036, todas aprobadas.

| Métrica de calidad | Criterio mínimo | Valor medido | Estado |
|---|---|---|---|
| Cobertura de pruebas unitarias del backend | ≥ 70 % (RNF-012) | 94,7 % de líneas medido en la v1.1.0; la v1.3.0 suma 134 pruebas y no se volvió a medir | Cumple |
| Defectos abiertos S1 / S2 | 0 | 0: regresión 2 176 de 2 176 a la primera (v1.2.0: 2 042 de 2 042) | Cumple |
| Casos de prueba ejecutados | 100 % de CP-001 a CP-037 | 36 de 37 (97,3 %); falta CP-037 | **No cumple** |
| Cobertura de RF Must y Should | > 80 % con pruebas aprobadas (RNF-013) | 25 de 25 (100 %); además RF-015, RF-016 y RF-031 | Cumple |
| Persistencia NoSQL MongoDB | 100 % correcto en camposDinamicos, intervenciones y bitácora | Mismas pruebas aprobadas; la bitácora registra EXPORTAR en los PDF nuevos y ACTUALIZAR en la foto de perfil | Cumple |
| Pipeline analítico Python | K-Means sobre datos sintéticos sin errores | Job «Analytics» en la CI; la v1.3.0 no cambia el lote | Cumple |
| Estabilidad de la app de escritorio | Instalación y operación estable en Windows 10 y 11 (RNF-011) | Operada en Windows 11 hasta la v1.3.0; falta instalarla y recorrerla en Windows 10 | **Pendiente** |

### Decisión de la v1.3.0

**No-Go por ahora**, por los mismos dos puntos: CP-037 (prueba de carga con k6) y la instalación en Windows 10, con el instalador `SITBIO-Setup-1.3.0.exe`. La decisión final la toman el equipo y el instructor asesor del SENA CTMA en la reunión de cierre.

## Evaluación Go / No-Go de la v1.2.0 (20/11/2026)

Regresión sobre `main` (`20fa169`, PR #57) con las filas G-01 a G-09 de la v1.1.1 y I-00 a I-14 de la v1.2.0 integradas ([regresion.md](regresion.md)). La matriz de CP y de historias de arriba no cambia: los arreglos y mejoras se verificaron con pruebas nuevas asociadas a CP-004, CP-007, CP-008, CP-011, CP-024, CP-030, CP-031 y CP-035, todas aprobadas.

| Métrica de calidad | Criterio mínimo | Valor medido | Estado |
|---|---|---|---|
| Cobertura de pruebas unitarias del backend | ≥ 70 % (RNF-012) | 94,7 % de líneas medido en la v1.1.0; la v1.2.0 suma 176 pruebas y no se volvió a medir | Cumple |
| Defectos abiertos S1 / S2 | 0 | 0: regresión 2 042 de 2 042 a la primera (v1.1.1: 1 866 de 1 866) | Cumple |
| Casos de prueba ejecutados | 100 % de CP-001 a CP-037 | 36 de 37 (97,3 %); falta CP-037 | **No cumple** |
| Cobertura de RF Must y Should | > 80 % con pruebas aprobadas (RNF-013) | 25 de 25 (100 %); además RF-015, RF-016 y RF-031 | Cumple |
| Persistencia NoSQL MongoDB | 100 % correcto en camposDinamicos, intervenciones y bitácora | Mismas pruebas aprobadas; resultados_analitica con 2 a 6 grupos probada en `ConsultaAnaliticaTests` y `RecalculoAnaliticaTests` | Cumple |
| Pipeline analítico Python | K-Means sobre datos sintéticos sin errores | Job «Analytics» en la CI con las pruebas del lote de 2 a 6 grupos (`tests/analytics/test_lote.py`) | Cumple |
| Estabilidad de la app de escritorio | Instalación y operación estable en Windows 10 y 11 (RNF-011) | Operada en Windows 11 hasta la v1.2.0; falta instalarla y recorrerla en Windows 10 | **Pendiente** |

### Decisión de la v1.2.0

**No-Go por ahora**, por los mismos dos puntos: CP-037 (prueba de carga con k6) y la instalación en Windows 10. El plan de corrección es el de la v1.1.0, con el instalador `SITBIO-Setup-1.2.0.exe`. La decisión final la toman el equipo y el instructor asesor del SENA CTMA en la reunión de cierre.

## Evaluación Go / No-Go de la v1.1.0 (Tabla 10)

| Métrica de calidad | Criterio mínimo | Valor medido | Estado |
|---|---|---|---|
| Cobertura de pruebas unitarias del backend | ≥ 70 % (RNF-012) | 94,7 % de líneas (Api 98,9 %, Application 97,8 %, Infrastructure 96,7 %, Domain 86,1 %), con coverlet y las bases reales; cliente WPF 82,3 % como referencia | Cumple |
| Defectos abiertos S1 / S2 | 0 | 0: regresión 1 784 de 1 784; el único fallo de la primera vuelta era de una prueba de concurrencia y se corrigió en F-01 | Cumple |
| Casos de prueba ejecutados | 100 % de CP-001 a CP-037 | 36 de 37 (97,3 %); falta CP-037 | **No cumple** |
| Cobertura de RF Must y Should | > 80 % con pruebas aprobadas (RNF-013) | 25 de 25 (100 %); además los Could RF-015, RF-016 y RF-031 | Cumple |
| Persistencia NoSQL MongoDB | 100 % correcto en camposDinamicos, intervenciones y bitácora | `ConfiguracionCamposMongoTests`, `IntervencionesMongoTests`, `BitacoraAuditoriaMongoTests` y `AislamientoTenantMongoTests` aprobadas; restauración con los mismos conteos (DOC-09, Tabla 33) | Cumple |
| Pipeline analítico Python | K-Means sobre datos sintéticos sin errores | Job «Analytics» (pytest y ruff) en verde en la CI #98 de `main` (`f0d4d8a`) | Cumple |
| Estabilidad de la app de escritorio | Instalación y operación estable en Windows 10 y 11 (RNF-011) | Instalador listo (`installer/SITBIO.iss`, E-11); operada en Windows 11; falta instalarla y recorrerla en Windows 10 | **Pendiente** |

### Decisión de la v1.1.0

**No-Go por ahora**, por los mismos dos puntos de la v1.0.0: falta CP-037 (prueba de carga con k6) y la instalación en Windows 10. Los otros cinco criterios se cumplen, y la v1.1 cerró los casos que antes quedaban fuera (CP-019, CP-020 y CP-035) y la prueba de restauración.

Plan de corrección:

1. Prueba de carga con k6 sobre el inventario con 5 000 equipos y 20 usuarios (CP-037).
2. Construir `SITBIO-Setup-1.1.0.exe` con `scripts/instalador.ps1`, instalarlo y recorrer los flujos críticos en Windows 10 y 11 (RNF-011).

La decisión final la toman el equipo y el instructor asesor del SENA CTMA en la reunión de cierre.

## Evaluación de la v1.0.0 (16/11/2026)

| Métrica de calidad | Criterio mínimo | Valor medido | Estado |
|---|---|---|---|
| Cobertura de pruebas unitarias del backend | ≥ 70 % (RNF-012) | 96,2 % de líneas (Api 98,7 %, Application 98,9 %, Infrastructure 96,6 %, Domain 91,0 %), con coverlet y las bases reales | Cumple |
| Defectos abiertos S1 / S2 | 0 | 0: la regresión no dejó fallos (1 543 de 1 543) y no hay defectos S1 o S2 registrados | Cumple |
| Casos de prueba ejecutados | 100 % de CP-001 a CP-037 del MVP | 33 de 34 del alcance (97,1 %); falta CP-037; CP-019, CP-020 y CP-035 fuera del alcance | **No cumple** |
| Cobertura de RF Must y Should | > 80 % con pruebas aprobadas (RNF-013) | 25 de 25 (100 %) | Cumple |
| Persistencia NoSQL MongoDB | 100 % correcto en camposDinamicos, intervenciones y bitácora | Pruebas de `ConfiguracionCamposMongoTests`, `IntervencionesMongoTests`, `BitacoraAuditoriaMongoTests` y `AislamientoTenantMongoTests` aprobadas | Cumple |
| Pipeline analítico Python | K-Means sobre datos sintéticos sin errores | Lote con pytest aprobado en la CI del PR #24; no ejecutado en la regresión local | Cumple, **por confirmar** en la reunión con el reporte de la CI |
| Estabilidad de la app de escritorio | Instalación y operación estable en Windows 10 y 11 (RNF-011) | Operada en Windows 11 durante el desarrollo; sin instalador ni prueba en Windows 10 | **Pendiente** |

### Decisión de la v1.0.0

**No-Go por ahora**: falta CP-037 (prueba de carga) y la estabilidad en Windows 10 no está verificada. El resto de los criterios se cumple.

Según la Figura 5 del DOC-07, se define un plan de corrección con nueva evaluación:

1. Prueba de carga con k6 sobre el inventario con 5 000 equipos y 20 usuarios (CP-037).
2. Instalación y recorrido de la app en Windows 10 y 11 (RNF-011).
3. HU-015 y HU-016 (CP-019, CP-020, CP-035) son Could y quedan como mejora futura, según el plan del 15/11/2026.

La decisión final la toman el equipo y el instructor asesor del SENA CTMA en la reunión de cierre.
