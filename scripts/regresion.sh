#!/usr/bin/env bash
# Regresión final (C-01): levanta PostgreSQL y MongoDB con Docker y corre toda la suite, incluidas las
# pruebas de integración de CP-001 a CP-037 y el aislamiento entre instituciones (CP-036).
# Uso: scripts/regresion.sh   (desde la raíz; lee las credenciales de .env, o de $SITBIO_ENV, sin mostrarlas)
set -euo pipefail
cd "$(dirname "$0")/.."

ENV_FILE="${SITBIO_ENV:-.env}"
[ -f "$ENV_FILE" ] || { echo "Falta $ENV_FILE (copia .env.example y llénalo)." >&2; exit 1; }
set -a; . "$ENV_FILE"; set +a

docker compose up -d postgres mongo >/dev/null
echo "Esperando a PostgreSQL y MongoDB..."
for _ in $(seq 1 60); do
  if docker exec sitbio-postgres pg_isready -q -U "$POSTGRES_USER" >/dev/null 2>&1 \
     && docker exec sitbio-mongo mongosh --quiet --eval 'db.runCommand({ping:1}).ok' >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

# MongoDB por IPv6: en algunos equipos 127.0.0.1:27017 lo ocupa un Mongo nativo.
export SITBIO_PRUEBAS_MONGO="mongodb://$MONGO_ROOT_USER:$MONGO_ROOT_PASSWORD@[::1]:27017/?authSource=admin"
export SITBIO_PRUEBAS_POSTGRES="Host=localhost;Port=5432;Database=${POSTGRES_DB:-sitbio_dev};Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"

resultados="TestResults/regresion-$(date +%Y%m%d-%H%M)"
dotnet build SITBIO.sln -c Release -nologo -v q
# Un proyecto a la vez: en paralelo, las pruebas de tiempo y concurrencia fallan por carga del equipo.
fallos=0
for proyecto in tests/SITBIO.*.Tests/SITBIO.*.Tests.csproj; do
  dotnet test "$proyecto" -c Release --no-build -nologo -v q --logger "trx" --results-directory "$resultados" \
    | sed -E 's#(mongodb://|Password=)[^ ;@]*#\1***#g' || fallos=1
done
echo "Resultados TRX en $resultados"
exit "$fallos"
