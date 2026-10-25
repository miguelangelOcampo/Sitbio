# Analítica SITBIO (CMP-ANA)

Proceso por lotes que lee las intervenciones de MongoDB, calcula métricas con Pandas y agrupa los equipos con K-Means (ADR-006, HU-008, HU-009). Escribe los resultados en la colección `resultados_analitica`, que la API entrega a UI-023.

Variables de entorno:

| Variable | Uso | Ejemplo |
|---|---|---|
| `SITBIO_MONGO_URI` | Conexión a MongoDB (usuario de solo lectura más escritura en su colección, INT-002) | `mongodb://localhost:27017` |
| `SITBIO_MONGO_DB` | Base de datos | `sitbio_dev` |
