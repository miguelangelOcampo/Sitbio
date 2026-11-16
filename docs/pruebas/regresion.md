# Regresión final (C-01 e I-03)

Ejecución de toda la suite sobre `main` con todas las historias del alcance integradas, con PostgreSQL y MongoDB reales en Docker.

- **Fecha:** 16/11/2026
- **Commit base:** `46bf36c` (`main`, PR #29: HU-014), después de I-01 (PR #27, pantallas de HU-005 y HU-021) e I-02 (PR #29).
- **Cómo reproducir:** `scripts/regresion.sh`. Levanta `sitbio-postgres` y `sitbio-mongo` con `docker compose`, lee las credenciales de `.env` (o de `$SITBIO_ENV`) sin mostrarlas, compila en Release y corre cada proyecto de pruebas por separado. Deja los TRX en `TestResults/regresion-<fecha>/`.
- **Primera ejecución (C-01):** 15/11/2026 sobre `ec14c6a` (PR #25), 1 401 de 1 401.

## Resultado

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

## Cobertura de líneas (coverlet, Release)

| Ensamblado | Líneas cubiertas | Cobertura |
|---|---|---|
| SITBIO.Api | 704 de 713 | 98,7 % |
| SITBIO.Application | 2 252 de 2 278 | 98,9 % |
| SITBIO.Infrastructure | 1 544 de 1 599 | 96,6 % |
| SITBIO.Domain | 1 430 de 1 572 | 91,0 % |
| **Backend** | **5 930 de 6 162** | **96,2 %** |
| SITBIO (cliente WPF, referencia) | 4 192 de 5 089 | 82,4 % |

La cobertura une las pruebas unitarias y las de integración de cada proyecto; se excluyen los archivos generados (`obj/`, `*.g.cs`).

## Casos de prueba

El estado de cada CP, con sus pruebas, está en la matriz de [go-no-go.md](go-no-go.md). En esta ejecución todos los CP del alcance que tienen prueba automática quedaron aprobados, incluido CP-018 (HU-014, `CriteriosHu014Tests`), que entró con I-02.

## CP-036 · Aislamiento entre instituciones

`RegresionCp036Tests.CP_036_NingunRolDeOtraInstitucion_VeLosDatosDeA_EnNingunaConsulta`:

1. La institución A registra un equipo y reporta una falla.
2. Cada uno de los siete roles de la institución B recorre los listados de la API: equipos, tiquetes, órdenes, técnicos, usuarios, planes preventivos, calibraciones, alertas, bitácora, indicadores, institución actual y analítica.
3. Ninguna respuesta contiene el tenant, el equipo, la placa ni el correo de A, y ninguna responde 500.
4. Las consultas directas por id de A (hoja de vida, línea de tiempo, PDF e historial del tiquete) responden 404 o 403.

La bitácora de B sí guarda esos intentos denegados con la ruta pedida. Son registros de sus propios usuarios, no una fuga; por eso la prueba revisa los listados antes de las consultas por id.

## Observaciones

- **Integración de HU-014:** el registro del equipo ahora guarda su estado inicial en `historial_estado_equipo`. La línea de tiempo (HU-023) no lo repite, porque ya aparece como «Equipo registrado», y las pruebas de HU-003 cuentan solo los cambios de estado.
- **Pruebas de tiempo y concurrencia:** con todos los proyectos en paralelo en un mismo equipo fallaron por carga la de 1 000 registros en PDF (RNF-007), la del Excel del historial y la de asignaciones simultáneas. Corriendo un proyecto a la vez pasan, y por eso el script corre los proyectos uno por uno.
- **Duración:** `SITBIO.Api.Tests` tarda entre 1 y 7 minutos con las bases reales, según la carga del equipo.
