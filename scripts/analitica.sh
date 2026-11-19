#!/usr/bin/env bash
# Ejecuta el lote de analítica (HU-008, HU-009): lee las intervenciones de MongoDB, calcula los indicadores y
# los grupos de K-Means y deja el resultado en resultados_analitica para UI-023.
# Uso: scripts/analitica.sh [opciones de python -m sitbio_analytics]   (desde cualquier carpeta)
#   Ejemplos: scripts/analitica.sh
#             scripts/analitica.sh -k 4 --fecha-corte 2026-11-01T00:00:00Z
# Si SITBIO_MONGO_URI no está definida, la arma con el .env (o $SITBIO_ENV) sin mostrar la contraseña.
# SITBIO_MONGO_DB toma su valor del entorno o del .env; por defecto, sitbio_dev.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -z "${SITBIO_MONGO_URI:-}" ]]; then
  ENV_FILE="${SITBIO_ENV:-.env}"
  [[ -f "$ENV_FILE" ]] || { echo "Define SITBIO_MONGO_URI o crea $ENV_FILE (copia .env.example y llénalo)." >&2; exit 1; }
  set -a; . "$ENV_FILE"; set +a
  # MongoDB por IPv6: en algunos equipos 127.0.0.1:27017 lo ocupa un Mongo nativo.
  export SITBIO_MONGO_URI="mongodb://$MONGO_ROOT_USER:$MONGO_ROOT_PASSWORD@[::1]:27017/?authSource=admin"
fi
export SITBIO_MONGO_DB="${SITBIO_MONGO_DB:-sitbio_dev}"

PYTHON="${PYTHON:-python}"
if ! "$PYTHON" -c "import pandas, sklearn, pymongo" >/dev/null 2>&1; then
  echo "Faltan dependencias de Python. Instálalas con: $PYTHON -m pip install -r analytics/requirements.txt" >&2
  exit 1
fi

cd analytics
"$PYTHON" -m sitbio_analytics "$@" 2>&1 | sed -E 's#(mongodb://)[^ @]*@#\1***@#g'
exit "${PIPESTATUS[0]}"
