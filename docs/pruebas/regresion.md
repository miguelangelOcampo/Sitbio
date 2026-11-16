# Regresión final (C-01)

Ejecución de toda la suite sobre `main` con todas las historias integradas (hasta el PR #25), con PostgreSQL y MongoDB reales en Docker.

- **Fecha:** 15/11/2026
- **Commit base:** `ec14c6a` (`main`, PR #25)
- **Cómo reproducir:** `scripts/regresion.sh`. Levanta `sitbio-postgres` y `sitbio-mongo` con `docker compose`, lee las credenciales de `.env` (o de `$SITBIO_ENV`) sin mostrarlas, compila en Release y corre cada proyecto de pruebas por separado. Deja los TRX en `TestResults/regresion-<fecha>/`.

## Resultado

| Proyecto | Pruebas | Correctas | Con error | Omitidas |
|---|---|---|---|---|
| SITBIO.Domain.Tests | 177 | 177 | 0 | 0 |
| SITBIO.Application.Tests | 310 | 310 | 0 | 0 |
| SITBIO.Desktop.Tests | 426 | 426 | 0 | 0 |
| SITBIO.Infrastructure.Tests | 133 | 133 | 0 | 0 |
| SITBIO.Api.Tests | 355 | 355 | 0 | 0 |
| **Total .NET** | **1 401** | **1 401** | **0** | **0** |
| tests/analytics (pytest) | — | — | — | No ejecutado: pytest no está instalado en el equipo de la regresión |

Con las bases activas no se omite ninguna prueba de integración.

## Casos de prueba

| CP | Historia | Pruebas automáticas | Resultado |
|---|---|---|---|
| CP-001 a CP-003 | HU-001 | `CriteriosHu001Tests` | Aprobado |
| CP-004, CP-005 | HU-002 | `CriteriosHu002Tests` | Aprobado |
| CP-006 | HU-003, HU-018 | `CriteriosHu003Tests` | Aprobado |
| CP-007 | HU-004 | `CriteriosHu004Tests` | Aprobado |
| CP-008 | HU-005 | `CriteriosHu005Tests` | Aprobado |
| CP-009, CP-010 | HU-006 | `CriteriosHu006Tests` | Aprobado |
| CP-011, CP-012 | HU-008, HU-009 | `ConsultaAnaliticaTests`, `AnaliticaViewModelTests`; el lote K-Means con `tests/analytics` | Aprobado en .NET; **Pendiente** la ejecución de pytest |
| CP-013 | HU-007, HU-027 | `CriteriosHu007Tests`, `GeneradorReportesTests` | Aprobado |
| CP-014 | HU-010 | `CriteriosHu010Tests` | Aprobado |
| CP-015 | HU-011 | `CriteriosHu011Tests` | Aprobado |
| CP-016 | HU-012 | `CriteriosHu012Tests` | Aprobado |
| CP-017 a CP-020 | — | Sin prueba asociada en la trazabilidad | **Pendiente**: confirmar a qué criterio corresponden en el DOC-07 |
| CP-021 | HU-017 | `CriteriosHu017Tests` | Aprobado |
| CP-022 | HU-003, HU-018 | `CriteriosHu003Tests` | Aprobado |
| CP-023, CP-024 | HU-019, HU-020 | `CriteriosHu019Tests` | Aprobado |
| CP-025 | HU-021 | `CriteriosHu021Tests` | Aprobado |
| CP-026 | HU-022 | `CriteriosHu022Tests` | Aprobado |
| CP-027 | HU-023, HU-024 | `CriteriosHu023Tests`, `CriteriosHu024Tests` | Aprobado |
| CP-028 | HU-025 | `CriteriosHu025Tests` | Aprobado |
| CP-029 | — | Sin prueba asociada en la trazabilidad | **Pendiente** |
| CP-030 | HU-026 | `CriteriosHu026Tests` | Aprobado |
| CP-031 | HU-007, HU-027 | `CriteriosHu007Tests` | Aprobado |
| CP-032 | HU-028 (probable) | `CriteriosHu028Tests` | Aprobado; **por confirmar** el número de CP, que la trazabilidad de HU-028 no asigna |
| CP-033 | — | Sin prueba asociada | **Pendiente** |
| CP-034 | HU-019, HU-020 (según el plan de commits) | No definido en DOC-04 a DOC-06 | **Pendiente**: confirmar el criterio |
| CP-035 | — | Sin prueba asociada | **Pendiente** |
| CP-036 | Todas | `RegresionCp036Tests` (transversal) y las pruebas de aislamiento de cada historia | Aprobado |
| CP-037 | — | Sin prueba asociada | **Pendiente** |

## CP-036 · Aislamiento entre instituciones

`RegresionCp036Tests.CP_036_NingunRolDeOtraInstitucion_VeLosDatosDeA_EnNingunaConsulta`:

1. La institución A registra un equipo y reporta una falla.
2. Cada uno de los siete roles de la institución B recorre los listados de la API: equipos, tiquetes, órdenes, técnicos, usuarios, planes preventivos, calibraciones, alertas, bitácora, indicadores, institución actual y analítica.
3. Ninguna respuesta contiene el tenant, el equipo, la placa ni el correo de A, y ninguna responde 500.
4. Las consultas directas por id de A (hoja de vida, línea de tiempo, PDF e historial del tiquete) responden 404 o 403.

La bitácora de B sí guarda esos intentos denegados con la ruta pedida. Son registros de sus propios usuarios, no una fuga; por eso la prueba revisa los listados antes de las consultas por id.

## Observaciones

- **Pruebas de tiempo y concurrencia:** con todos los proyectos corriendo en paralelo en un mismo equipo fallaron tres pruebas: la de 1 000 registros en PDF (RNF-007), la del Excel del historial y la de asignaciones simultáneas. Fue por la carga del equipo: corriendo un proyecto a la vez pasan, con Infraestructura en 3 a 14 s. Por eso el script corre los proyectos uno por uno.
- **Duración:** `SITBIO.Api.Tests` tarda unos 7 minutos con las bases reales.
