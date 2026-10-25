#!/bin/bash
# Inicializa PostgreSQL en Docker con los scripts del repositorio (ADR-005).
# El script 01 crea la base con la configuración regional es_CO, que no existe
# en la imagen oficial; por eso aquí se crea sitbio_dev y se ejecuta el script 01
# desde la línea "\c sitbio_dev", seguido de las migraciones V### en orden.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -c "CREATE DATABASE sitbio_dev ENCODING 'UTF8' TEMPLATE template0;"

sed -n '/^\\c sitbio_dev/,$p' /sitbio-db/01_PostgreSQL_SITBIO.sql | tail -n +2 \
    | psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname sitbio_dev

for f in $(ls /sitbio-db/V*.sql 2>/dev/null | sort); do
    echo "Aplicando $f"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname sitbio_dev -f "$f"
done
