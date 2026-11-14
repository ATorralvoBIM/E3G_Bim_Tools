#Requires -Version 5.1
<#
.SYNOPSIS
    Instalador portable (por usuario, sin administrador) de E3G Saneamiento
    para Autodesk Revit 2026.

.DESCRIPTION
    - Comprueba que el Revit 2026 del equipo corre sobre .NET 10 (build 26.5+):
      el complemento está compilado para net10.0 y no carga en builds 2026
      anteriores (.NET 8).
    - Copia la carga útil (carpeta "app") a
        %LOCALAPPDATA%\Programs\e3g\Saneamiento\Revit2026\
    - Escribe E3G_Saneamiento.addin en %APPDATA%\Autodesk\Revit\Addins\2026\
      con la ruta ABSOLUTA al ensamblado.

    Cierra Revit antes de instalar.
#>
[CmdletBinding()]
param(
    # Instalar aunque no se detecte Revit 2026 o su versión de .NET no cuadre.
    [switch] $Force,
    # Carpetas destino alternativas (sólo para probar el instalador).
    [string] $RaizLocal,
    [string] $RaizAddins
)

$ErrorActionPreference = 'Stop'

$Ver           = '2026'
$AssemblyName  = 'E3G_Saneamiento.dll'
$AddInFileName = 'E3G_Saneamiento.addin'
$TemplateName  = 'E3G_Saneamiento.addin.template'

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$PayloadDir = Join-Path $ScriptRoot 'app'
$LocalAppData = if ($RaizLocal)  { $RaizLocal }  else { [Environment]::GetFolderPath('LocalApplicationData') }
$AppData      = if ($RaizAddins) { $RaizAddins } else { [Environment]::GetFolderPath('ApplicationData') }

function Write-Step($msg)  { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "  [!]  $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== Instalador portable: E3G Saneamiento - Revit $Ver ===" -ForegroundColor White
Write-Host ""

# --- Comprobaciones previas --------------------------------------------------
if (-not (Test-Path (Join-Path $PayloadDir $AssemblyName))) {
    throw "No se encontró '$AssemblyName' en '$PayloadDir'. ¿Descomprimiste el ZIP completo?"
}

# Localizar Revit 2026 por registro y comprobar el runtime (.NET 10).
$installDir = Get-ChildItem "HKLM:\SOFTWARE\Autodesk\Revit\$Ver" -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { $_.GetValue('InstallationLocation') } | Where-Object { $_ } | Select-Object -First 1
if (-not $installDir) {
    if (-not $Force) { throw "No se detectó Revit $Ver en este equipo. Usa -Force para instalar igualmente." }
    Write-Warn2 "No se detectó Revit $Ver; se instala igualmente (-Force)."
} else {
    Write-Step "Revit $Ver encontrado en $installDir"
    $rc = Join-Path $installDir 'RevitAPI.runtimeconfig.json'
    $tfm = if (Test-Path $rc) { (Get-Content $rc -Raw | ConvertFrom-Json).runtimeOptions.tfm } else { 'net8.0' }
    if ($tfm -ne 'net10.0') {
        $msg = "Este Revit $Ver usa $tfm y el complemento requiere net10.0 (Revit 2026.5 o posterior). Actualiza Revit desde Autodesk Access."
        if (-not $Force) { throw $msg }
        Write-Warn2 $msg
    } else {
        Write-Ok "Runtime $tfm compatible."
    }
}

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Warn2 "Revit está en ejecución. El complemento no se cargará hasta que lo reinicies."
    $answer = Read-Host "  ¿Continuar? (s/N)"
    if ($answer -notmatch '^[sS]') { Write-Host "Cancelado."; return }
}

# --- 1) Copiar la carga útil ------------------------------------------------
$targetDir = Join-Path $LocalAppData "Programs\e3g\Saneamiento\Revit$Ver"
Write-Step "Copiando archivos a $targetDir"
# El mapeo de aparatos HS5 (Hs5Mapeo.json) lo edita el usuario desde la app: se conserva al actualizar.
$mapeo = Join-Path $targetDir 'Hs5Mapeo.json'
$mapeoPrevio = if (Test-Path $mapeo) { Get-Content $mapeo -Raw } else { $null }
if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir }
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -Path (Join-Path $PayloadDir '*') -Destination $targetDir -Recurse -Force
if ($mapeoPrevio) {
    [IO.File]::WriteAllText($mapeo, $mapeoPrevio, (New-Object Text.UTF8Encoding $false))
    Write-Ok "Archivos copiados (se conserva tu mapeo de aparatos)."
} else {
    Write-Ok "Archivos copiados."
}

# --- 2) Manifiesto .addin con ruta absoluta ---------------------------------
$addinsDir = Join-Path $AppData "Autodesk\Revit\Addins\$Ver"
if (-not (Test-Path $addinsDir)) { New-Item -ItemType Directory -Force -Path $addinsDir | Out-Null }
$assemblyPath = Join-Path $targetDir $AssemblyName
$addinXml = (Get-Content (Join-Path $PayloadDir $TemplateName) -Raw).Replace('{ASSEMBLY}', $assemblyPath)
$addinPath = Join-Path $addinsDir $AddInFileName
[IO.File]::WriteAllText($addinPath, $addinXml, (New-Object Text.UTF8Encoding $false))
Write-Ok "Manifiesto escrito: $addinPath"

Write-Host ""
Write-Host "=== Instalación completada ===" -ForegroundColor Green
Write-Host "Abre (o reinicia) Revit $Ver y busca la pestaña 'E3G BIM Tools' > Saneamiento." -ForegroundColor White
Write-Host ""
