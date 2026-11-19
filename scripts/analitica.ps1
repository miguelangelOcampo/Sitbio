<#
.SYNOPSIS
  Ejecuta el lote de analítica de SITBIO (HU-008, HU-009) para que UI-023 tenga resultados.

.DESCRIPTION
  Corre "python -m sitbio_analytics" desde la carpeta analytics. Lee las intervenciones de MongoDB, calcula
  los indicadores y los grupos de K-Means y escribe la colección resultados_analitica.
  Si SITBIO_MONGO_URI no está definida, la arma con el .env (o $env:SITBIO_ENV) sin mostrar la contraseña.
  SITBIO_MONGO_DB toma su valor del entorno o del .env; por defecto, sitbio_dev.
  Los argumentos adicionales pasan tal cual al lote (-k, --fecha-corte, --sin-escribir).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/analitica.ps1
  powershell -ExecutionPolicy Bypass -File scripts/analitica.ps1 -k 4
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

function Leer-Env([string]$ruta) {
    $valores = @{}
    foreach ($linea in Get-Content $ruta) {
        if ($linea -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
            $valores[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'")
        }
    }
    return $valores
}

$archivoEnv = if ($env:SITBIO_ENV) { $env:SITBIO_ENV } else { Join-Path $raiz '.env' }
$envLeido = if (Test-Path $archivoEnv) { Leer-Env $archivoEnv } else { @{} }

if (-not $env:SITBIO_MONGO_URI) {
    if (-not $envLeido.ContainsKey('MONGO_ROOT_USER')) {
        throw "Define SITBIO_MONGO_URI o crea $archivoEnv (copia .env.example y llénalo)."
    }
    # MongoDB por IPv6: en algunos equipos 127.0.0.1:27017 lo ocupa un Mongo nativo.
    $env:SITBIO_MONGO_URI = "mongodb://$($envLeido.MONGO_ROOT_USER):$($envLeido.MONGO_ROOT_PASSWORD)@[::1]:27017/?authSource=admin"
}
if (-not $env:SITBIO_MONGO_DB) {
    $env:SITBIO_MONGO_DB = if ($envLeido.ContainsKey('SITBIO_MONGO_DB')) { $envLeido.SITBIO_MONGO_DB } else { 'sitbio_dev' }
}

$python = if ($env:PYTHON) { $env:PYTHON } else { 'python' }
# En Windows PowerShell 5.1 lo que Python escribe en stderr llega como error; desde aquí no detiene el script.
$ErrorActionPreference = 'Continue'
& $python -c "import pandas, sklearn, pymongo" *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Faltan dependencias de Python. Instálalas con: $python -m pip install -r analytics/requirements.txt"
}

Push-Location (Join-Path $raiz 'analytics')
try {
    & $python -m sitbio_analytics @args 2>&1 | ForEach-Object { "$_" -replace 'mongodb://[^ @]*@', 'mongodb://***@' }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
