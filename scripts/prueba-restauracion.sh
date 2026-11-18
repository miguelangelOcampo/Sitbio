#!/usr/bin/env bash
# Prueba de restauración (DOC-09, Tablas 32 y 33; RNF-010): respalda PostgreSQL y MongoDB de DEP-DEV con los
# comandos de la Tabla 32, los restaura en un DEP-QA desechable (contenedores nuevos, sin volúmenes), compara
# los conteos tabla por tabla y colección por colección, y corre las pruebas de humo de la API contra DEP-QA.
# Uso: scripts/prueba-restauracion.sh   (desde la raíz; lee .env, o $SITBIO_ENV, sin mostrarlo)
# Deja el respaldo y el registro en TestResults/restauracion-<fecha>/ (ignorado por git).
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1 # Git Bash en Windows: no convertir las rutas /tmp de los contenedores.

ENV_FILE="${SITBIO_ENV:-.env}"
[[ -f "$ENV_FILE" ]] || { echo "Falta $ENV_FILE (copia .env.example y llénalo)." >&2; exit 1; }
set -a; . "$ENV_FILE"; set +a

BASE="${POSTGRES_DB:-sitbio_dev}"
BASE_MONGO="${SITBIO_MONGO_DB:-sitbio_dev}"
FECHA="$(date +%Y%m%d)"
SALIDA="TestResults/restauracion-$(date +%Y%m%d-%H%M)"
QA_PG=sitbio-qa-postgres
QA_MONGO=sitbio-qa-mongo
QA_PG_PUERTO=55432
QA_MONGO_PUERTO=27018
API_PUERTO=5199
mkdir -p "$SALIDA"
REGISTRO="$SALIDA/registro.txt"
: > "$REGISTRO"

ocultar() { sed -E 's#(mongodb://|Password=)[^ ;@]*#\1***#g'; }
log() { echo "$*" | tee -a "$REGISTRO"; }
ahora() { date +%s.%N; }
segundos() { awk -v a="$1" -v b="$2" 'BEGIN { printf "%.1f", b - a }'; }

API_PID=""
limpiar() {
  [[ -n "$API_PID" ]] && kill "$API_PID" >/dev/null 2>&1 || true
  docker rm -f "$QA_PG" "$QA_MONGO" >/dev/null 2>&1 || true
}
trap limpiar EXIT

MONGO_AUTH=(--username "$MONGO_ROOT_USER" --password "$MONGO_ROOT_PASSWORD" --authenticationDatabase admin)

conteos_pg() { # contenedor
  docker exec "$1" psql -U "$POSTGRES_USER" -d "$BASE" -At -F ' ' -c "
    SELECT format('SELECT %L, count(*) FROM %I.%I', schemaname || '.' || relname, schemaname, relname)
    FROM pg_stat_user_tables ORDER BY schemaname, relname" \
  | while read -r consulta; do docker exec "$1" psql -U "$POSTGRES_USER" -d "$BASE" -At -F ' ' -c "$consulta"; done
}

conteos_mongo() { # contenedor
  docker exec "$1" mongosh --quiet "${MONGO_AUTH[@]}" --eval "
    const d = db.getSiblingDB('$BASE_MONGO');
    d.getCollectionNames().sort().forEach(c => print(c + ' ' + d.getCollection(c).countDocuments()));"
}

docker compose up -d postgres mongo >/dev/null
log "Prueba de restauración · $(date '+%d/%m/%Y %H:%M') · commit $(git rev-parse --short HEAD)"

# 1. Respaldo con los comandos de la Tabla 32 (dentro de los contenedores de DEP-DEV).
t0=$(ahora)
docker exec sitbio-postgres pg_dump -U "$POSTGRES_USER" -Fc -d "$BASE" -f "/tmp/sitbio_$FECHA.dump"
docker cp -q "sitbio-postgres:/tmp/sitbio_$FECHA.dump" "$SALIDA/"
t1=$(ahora)
docker exec sitbio-mongo mongodump --quiet "${MONGO_AUTH[@]}" --db "$BASE_MONGO" --archive="/tmp/sitbio_$FECHA.gz" --gzip
docker cp -q "sitbio-mongo:/tmp/sitbio_$FECHA.gz" "$SALIDA/"
t2=$(ahora)
docker exec sitbio-postgres rm -f "/tmp/sitbio_$FECHA.dump"
docker exec sitbio-mongo rm -f "/tmp/sitbio_$FECHA.gz"
log "Respaldo PostgreSQL: sitbio_$FECHA.dump, $(du -h "$SALIDA/sitbio_$FECHA.dump" | cut -f1), $(segundos "$t0" "$t1") s"
log "Respaldo MongoDB:    sitbio_$FECHA.gz, $(du -h "$SALIDA/sitbio_$FECHA.gz" | cut -f1), $(segundos "$t1" "$t2") s"
conteos_pg sitbio-postgres > "$SALIDA/origen-postgres.txt"
conteos_mongo sitbio-mongo > "$SALIDA/origen-mongo.txt"

# 2. DEP-QA desechable: mismas imágenes que docker-compose.yml, sin volúmenes y solo en 127.0.0.1.
docker rm -f "$QA_PG" "$QA_MONGO" >/dev/null 2>&1 || true
docker run -d --name "$QA_PG" -e POSTGRES_USER="$POSTGRES_USER" -e POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
  -p "127.0.0.1:$QA_PG_PUERTO:5432" postgres:16 >/dev/null
docker run -d --name "$QA_MONGO" -e MONGO_INITDB_ROOT_USERNAME="$MONGO_ROOT_USER" -e MONGO_INITDB_ROOT_PASSWORD="$MONGO_ROOT_PASSWORD" \
  -p "127.0.0.1:$QA_MONGO_PUERTO:27017" mongo:7 >/dev/null
for _ in $(seq 1 60); do
  if docker exec "$QA_PG" psql -U "$POSTGRES_USER" -d postgres -c 'SELECT 1' >/dev/null 2>&1 \
     && docker exec "$QA_MONGO" mongosh --quiet "${MONGO_AUTH[@]}" --eval 'db.runCommand({ping:1}).ok' >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

# 3. Restauración (Nota de la Tabla 32), cronometrada desde que se copia el respaldo hasta que termina.
t3=$(ahora)
docker cp -q "$SALIDA/sitbio_$FECHA.dump" "$QA_PG:/tmp/"
docker exec "$QA_PG" psql -U "$POSTGRES_USER" -d postgres -q -c "CREATE DATABASE $BASE ENCODING 'UTF8' TEMPLATE template0;"
docker exec "$QA_PG" pg_restore -U "$POSTGRES_USER" -d "$BASE" -c --if-exists --exit-on-error "/tmp/sitbio_$FECHA.dump"
t4=$(ahora)
docker cp -q "$SALIDA/sitbio_$FECHA.gz" "$QA_MONGO:/tmp/"
docker exec "$QA_MONGO" mongorestore --quiet "${MONGO_AUTH[@]}" --archive="/tmp/sitbio_$FECHA.gz" --gzip --drop
t5=$(ahora)
log "Restauración PostgreSQL en DEP-QA: $(segundos "$t3" "$t4") s"
log "Restauración MongoDB en DEP-QA:    $(segundos "$t4" "$t5") s"
log "Tiempo real de restauración (ambas): $(segundos "$t3" "$t5") s (RTO de la Tabla 32: 30 min)"

# 4. Verificación: mismos conteos en cada tabla y colección.
conteos_pg "$QA_PG" > "$SALIDA/qa-postgres.txt"
conteos_mongo "$QA_MONGO" > "$SALIDA/qa-mongo.txt"
fallos=0
if diff -q "$SALIDA/origen-postgres.txt" "$SALIDA/qa-postgres.txt" >/dev/null; then
  log "PostgreSQL: $(wc -l < "$SALIDA/qa-postgres.txt") tablas, $(awk '{s+=$2} END {print s}' "$SALIDA/qa-postgres.txt") filas; conteos iguales al origen"
else
  log "PostgreSQL: los conteos NO coinciden"; diff "$SALIDA/origen-postgres.txt" "$SALIDA/qa-postgres.txt" | tee -a "$REGISTRO"; fallos=1
fi
if diff -q "$SALIDA/origen-mongo.txt" "$SALIDA/qa-mongo.txt" >/dev/null; then
  log "MongoDB: $(wc -l < "$SALIDA/qa-mongo.txt") colecciones, $(awk '{s+=$2} END {print s}' "$SALIDA/qa-mongo.txt") documentos; conteos iguales al origen"
else
  log "MongoDB: los conteos NO coinciden"; diff "$SALIDA/origen-mongo.txt" "$SALIDA/qa-mongo.txt" | tee -a "$REGISTRO"; fallos=1
fi

# 5. Pruebas de humo: la API arranca contra DEP-QA, /salud responde 200 y el inicio de sesión de un correo
#    inexistente responde 401 (no 500), lo que confirma que la API consulta la base restaurada.
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$QA_PG_PUERTO;Database=$BASE;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
export ConnectionStrings__Mongo="mongodb://$MONGO_ROOT_USER:$MONGO_ROOT_PASSWORD@127.0.0.1:$QA_MONGO_PUERTO/?authSource=admin"
export Mongo__Database="$BASE_MONGO"
export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$API_PUERTO"
dotnet run --project src/SITBIO.Api -c Release --no-launch-profile > "$SALIDA/api.log" 2>&1 &
API_PID=$!
salud=000
for _ in $(seq 1 90); do
  salud=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$API_PUERTO/api/v1/salud" || true)
  [[ "$salud" == 200 ]] && break
  sleep 2
done
login=$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' \
  -d '{"correo":"humo-restauracion@sitbio.invalid","contrasena":"no-es-una-clave"}' \
  "http://127.0.0.1:$API_PUERTO/api/v1/auth/login" || true)
log "Humo: GET /api/v1/salud → $salud; POST /api/v1/auth/login (correo inexistente) → $login"
[[ "$salud" == 200 && "$login" == 401 ]] || fallos=1
sed -i -E 's#(mongodb://|Password=)[^ ;@]*#\1***#g' "$SALIDA/api.log"

if [[ $fallos -eq 0 ]]; then log "Resultado: restauración correcta"; else log "Resultado: restauración con diferencias"; fi
echo "Registro en $REGISTRO" | ocultar
exit "$fallos"
