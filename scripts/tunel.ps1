<#
.SYNOPSIS
  Publica la API local de SITBIO en internet con un túnel rápido de Cloudflare, para usar la aplicación desde otros
  equipos sin abrir puertos (demostración y prueba en Windows 10).

.DESCRIPTION
  1. Levanta PostgreSQL y MongoDB con Docker (docker compose, credenciales del .env).
  2. Inicia la API en modo Production en http://localhost:5080. En Production la API no lee user-secrets: el script
     toma de ellos (o de las variables de entorno que ya existan) las cadenas de conexión, la base de MongoDB y la
     clave del JWT, y las pasa solo a este proceso. No muestra ningún valor ni lo escribe en archivos.
  3. Abre el túnel con cloudflared y muestra la dirección https://….trycloudflare.com. Esa dirección más /api/v1/ es
     la que se escribe en el asistente del instalador (SITBIO_API_URL).
  4. Con Ctrl + C se cierran el túnel y la API. Las bases siguen en Docker.

  Solo queda expuesta la API: las bases, los archivos y el resto del equipo no. La dirección cambia cada vez que se
  abre el túnel; compártela solo con quien la necesite y cierra el túnel al terminar.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/tunel.ps1
#>
param(
    # Archivo con las credenciales de Docker (por defecto, el .env de la raíz).
    [string]$EnvFile,
    # Carpeta de los documentos y fotos (por defecto, la que usa la API en desarrollo).
    [string]$Almacen,
    # Ruta de cloudflared.exe si no está en el PATH ni en la carpeta de instalación.
    [string]$Cloudflared,
    [int]$Puerto = 5080
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
if (-not $EnvFile) { $EnvFile = Join-Path $raiz '.env' }
if (-not $Almacen) { $Almacen = Join-Path $raiz 'src\SITBIO.Api\storage\adjuntos' }

if (-not $Cloudflared) {
    $Cloudflared = @(
        (Get-Command cloudflared.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe",
        "$env:ProgramFiles\cloudflared\cloudflared.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Cloudflared) { throw 'No se encontró cloudflared. Instálalo con: winget install --id Cloudflare.cloudflared -e' }
if (-not (Test-Path $EnvFile)) { throw "Falta $EnvFile (copia .env.example y llénalo)." }
if (Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue) {
    throw "El puerto $Puerto ya está en uso (¿la API de desarrollo está abierta?). Ciérrala y vuelve a ejecutar el script."
}

# ---------- Bases en Docker ----------
Write-Host 'Levantando PostgreSQL y MongoDB...'
docker compose --env-file $EnvFile up -d postgres mongo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron levantar las bases con Docker. ¿Está abierto Docker Desktop?' }

# ---------- Configuración de la API (sin mostrar valores) ----------
$secretos = @{}
$archivoSecretos = Join-Path $env:APPDATA 'Microsoft\UserSecrets\sitbio-api-dev\secrets.json'
if (Test-Path $archivoSecretos) {
    (Get-Content $archivoSecretos -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $secretos[$_.Name] = $_.Value }
}
$obligatorias = 'ConnectionStrings:Postgres', 'ConnectionStrings:Mongo', 'Mongo:Database', 'Jwt:Clave'
# Opcionales: sin ellas la API funciona, pero no envía el aviso por correo de las solicitudes (ADR-012).
$opcionales = 'Smtp:Usuario', 'Smtp:Contrasena', 'Smtp:Remitente'
$faltan = @()
foreach ($clave in $obligatorias + $opcionales) {
    $variable = $clave -replace ':', '__'
    if ([Environment]::GetEnvironmentVariable($variable)) { continue }
    if ($secretos.ContainsKey($clave)) { [Environment]::SetEnvironmentVariable($variable, [string]$secretos[$clave], 'Process') }
    elseif ($obligatorias -contains $clave) { $faltan += $clave }
}
if ($faltan) { throw "Falta configurar: $($faltan -join ', ') (user-secrets del proyecto SITBIO.Api o variables de entorno)." }
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://localhost:$Puerto"
$env:Storage__AttachmentsPath = $Almacen

# ---------- API ----------
Write-Host "Compilando e iniciando la API en modo Production (http://localhost:$Puerto)..."
dotnet build src/SITBIO.Api -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la API.' }
$registroApi = Join-Path $env:TEMP 'sitbio-api-tunel.log'
$api = Start-Process dotnet -ArgumentList 'run', '--project', 'src/SITBIO.Api', '-c', 'Release', '--no-build', '--no-launch-profile' `
    -NoNewWindow -PassThru -RedirectStandardOutput $registroApi -RedirectStandardError "$registroApi.err"

$tunel = $null
$procesoApi = $null
try {
    $lista = $false
    for ($i = 0; $i -lt 60 -and -not $lista; $i++) {
        Start-Sleep -Seconds 1
        if ($api.HasExited) { throw "La API se detuvo al iniciar; revisa $registroApi.err" }
        try { $lista = (Invoke-WebRequest "http://localhost:$Puerto/api/v1/salud" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { }
    }
    if (-not $lista) { throw "La API no respondió en 60 s; revisa $registroApi" }
    # dotnet run deja la API como proceso hijo: se guarda cuál escucha en el puerto para cerrarlo al final.
    $procesoApi = Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty OwningProcess
    Write-Host 'API lista.'

    # ---------- Túnel ----------
    Write-Host 'Abriendo el túnel de Cloudflare...'
    $registroTunel = Join-Path $env:TEMP 'sitbio-tunel.log'
    if (Test-Path $registroTunel) { Remove-Item $registroTunel }
    $tunel = Start-Process $Cloudflared -ArgumentList 'tunnel', '--no-autoupdate', '--url', "http://localhost:$Puerto", '--logfile', $registroTunel `
        -NoNewWindow -PassThru -RedirectStandardError "$registroTunel.err"
    $direccion = $null
    for ($i = 0; $i -lt 60 -and -not $direccion; $i++) {
        Start-Sleep -Seconds 1
        if ($tunel.HasExited) { throw "cloudflared se detuvo; revisa $registroTunel.err" }
        $texto = (Get-Content "$registroTunel.err", $registroTunel -Raw -ErrorAction SilentlyContinue) -join "`n"
        if ($texto -match 'https://[a-z0-9-]+\.trycloudflare\.com') { $direccion = $Matches[0] }
    }
    if (-not $direccion) { throw "No se obtuvo la dirección del túnel; revisa $registroTunel" }

    Write-Host ''
    Write-Host '  Túnel abierto. Dirección de la API para el instalador (SITBIO_API_URL):' -ForegroundColor Green
    Write-Host "    $direccion/api/v1/" -ForegroundColor Green
    Write-Host ''
    Write-Host '  Compártela solo con quien la necesite. Ctrl + C cierra el túnel y la API.'
    Wait-Process -Id $tunel.Id
}
finally {
    foreach ($proceso in $tunel, $api) {
        if ($proceso -and -not $proceso.HasExited) { Stop-Process -Id $proceso.Id -Force -ErrorAction SilentlyContinue }
    }
    if ($procesoApi) { Stop-Process -Id $procesoApi -Force -ErrorAction SilentlyContinue }
    Write-Host 'Túnel y API cerrados.'
}
