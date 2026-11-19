# Analítica SITBIO (CMP-ANA)

Proceso por lotes que lee las intervenciones de MongoDB, calcula métricas con Pandas y agrupa los equipos con K-Means (ADR-006, HU-008, HU-009). Escribe los resultados en la colección `resultados_analitica`, que la API entrega a UI-023.

Variables de entorno:

| Variable | Uso | Ejemplo |
|---|---|---|
| `SITBIO_MONGO_URI` | Conexión a MongoDB (usuario de solo lectura más escritura en su colección, INT-002) | `mongodb://localhost:27017` |
| `SITBIO_MONGO_DB` | Base de datos | `sitbio_dev` |

## Ejecución por lotes (HU-008, HU-009)

Con los contenedores arriba y el `.env` lleno, desde la raíz del repositorio:

```bash
scripts/analitica.sh                                             # Git Bash, Linux o macOS
powershell -ExecutionPolicy Bypass -File scripts/analitica.ps1   # Windows
```

Los scripts arman `SITBIO_MONGO_URI` con el `.env` si no está definida (sin mostrar la contraseña), usan `SITBIO_MONGO_DB` (por defecto `sitbio_dev`), revisan que estén las dependencias y pasan los demás argumentos al lote. Sobre la semilla de demostración (`scripts/semilla-demo.sh`) dejan resultados para la pantalla «Analítica» (UI-023); mientras no se ejecute, la pantalla muestra un aviso informativo.

Ejecución manual:

```bash
pip install -r analytics/requirements.txt
cd analytics
python -m sitbio_analytics                      # lee intervenciones y escribe resultados_analitica
python -m sitbio_analytics --fuente ../seeds/mongo/data/intervenciones.ejson.json --sin-escribir
```

Opciones: `--fecha-corte` (ISO-8601, por defecto ahora en UTC), `-k` (grupos de K-Means, por defecto 3) y `--sin-escribir` (imprime el resultado en Extended JSON).

Módulos: `metricas.py` (indicadores con universo, numerador y denominador, como en el laboratorio L4), `agrupacion.py` (K-Means sobre variables estandarizadas por equipo, semilla fija) y `lote.py` (lectura, cálculo y escritura).

### Documento de `resultados_analitica`

Un documento por tenant (`_id` = `tenant_id`), reemplazado en cada ejecución:

| Campo | Contenido |
|---|---|
| `generado_en`, `fecha_corte` | Fechas UTC de la ejecución y del corte |
| `version`, `huella_entrada` | Versión del pipeline y SHA-256 de las intervenciones leídas |
| `intervenciones_analizadas` | Total de intervenciones del tenant |
| `indicadores[]` | `indicador`, `universo`, `numerador`, `denominador`, `valor` (`null` si el universo está vacío) |
| `agrupacion` | `algoritmo`, `k`, `semilla`, `caracteristicas`, `grupos[]` (promedios por grupo) y `equipos[]` (`equipo_id`, `grupo` y sus variables) |

Los grupos se numeran por carga creciente (0 = menor número de intervenciones y costo confirmado). La agrupación nunca mezcla equipos de distintos tenants.

Pruebas: `pytest` y `ruff check .` desde la raíz del repositorio.
