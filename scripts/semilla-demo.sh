#!/usr/bin/env bash
# Siembra «Hospital Demo SITBIO» con seis meses de operación simulada para revisar la app (no es para producción).
# Uso: scripts/semilla-demo.sh [--reemplazar] [--dias N]   (desde la raíz; lee .env, o $SITBIO_ENV, sin mostrarlo)
# Opcional: SITBIO_DEMO_CLAVE fija la contraseña de los usuarios; si no, se genera una.
# Los usuarios y la contraseña quedan en demo-credenciales.local.txt (ignorado por git).
set -euo pipefail
cd "$(dirname "$0")/.."

ENV_FILE="${SITBIO_ENV:-.env}"
[ -f "$ENV_FILE" ] || { echo "Falta $ENV_FILE (copia .env.example y llénalo)." >&2; exit 1; }
set -a; . "$ENV_FILE"; set +a

docker compose up -d postgres mongo >/dev/null
for _ in $(seq 1 60); do
  if docker exec sitbio-postgres pg_isready -q -U "$POSTGRES_USER" >/dev/null 2>&1; then break; fi
  sleep 2
done

# MongoDB por IPv6: en algunos equipos 127.0.0.1:27017 lo ocupa un Mongo nativo.
export SITBIO_DEMO_MONGO="mongodb://$MONGO_ROOT_USER:$MONGO_ROOT_PASSWORD@[::1]:27017/?authSource=admin"
export SITBIO_DEMO_POSTGRES="Host=localhost;Port=5432;Database=${POSTGRES_DB:-sitbio_dev};Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
export SITBIO_DEMO_MONGO_DB="${SITBIO_MONGO_DB:-sitbio_dev}"

dotnet run --project tools/SITBIO.SemillaDemo -c Release -- "$@" 2>&1 | sed -E 's#(mongodb://|Password=)[^ ;@]*#\1***#g'
exit "${PIPESTATUS[0]}"
