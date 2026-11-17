<#
.SYNOPSIS
  Construye el instalador del cliente de escritorio SITBIO (RNF-011, DT-013 del DOC-09).

.DESCRIPTION
  1. Publica el cliente WPF autocontenido para win-x64 en publish/desktop.
  2. Si encuentra Inno Setup 6 (ISCC.exe), compila installer/SITBIO.iss y deja
     dist/SITBIO-Setup-<versión>.exe. Si no lo encuentra, deja la publicación lista y lo avisa.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/instalador.ps1
  powershell -ExecutionPolicy Bypass -File scripts/instalador.ps1 -ApiUrl https://api.sitbio.co/api/v1/
#>
param(
    # Dirección de la API que el asistente propone (SITBIO_API_URL).
    [string]$ApiUrl = 'http://localhost:5080/api/v1/',
    # Versión del instalador; por defecto la de Directory.Build.props.
    [string]$Version,
    # Ruta de ISCC.exe si no está en el PATH ni en la carpeta habitual.
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $raiz 'Directory.Build.props'))).Project.PropertyGroup.Version
}

$publicado = Join-Path $raiz 'publish\desktop'
Write-Host "Publicando el cliente $Version en $publicado..."
if (Test-Path $publicado) { Remove-Item -Recurse -Force $publicado }
dotnet publish src/SITBIO.Desktop -c Release -r win-x64 --self-contained true -o $publicado -p:Version=$Version -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló dotnet publish.' }
if (-not (Test-Path (Join-Path $publicado 'SITBIO.exe'))) { throw 'La publicación no contiene SITBIO.exe.' }

if (-not $Iscc) {
    $candidatos = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $Iscc = $candidatos | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

if (-not $Iscc) {
    Write-Warning ('No se encontró Inno Setup 6 (ISCC.exe). La publicación quedó en publish\desktop. ' +
        'Instala Inno Setup 6 desde https://jrsoftware.org/isdl.php y vuelve a ejecutar este script, o pasa -Iscc <ruta>.')
    exit 0
}

& $Iscc "/DVersion=$Version" "/DApiUrl=$ApiUrl" "/DPublicado=$publicado" (Join-Path $raiz 'installer\SITBIO.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del instalador.' }
Write-Host "Instalador listo: dist\SITBIO-Setup-$Version.exe"
