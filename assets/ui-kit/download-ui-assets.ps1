<#
    download-ui-assets.ps1
    Prepara assets externos para el reskin de e3gbimtools (Revit 2025 / net8.0-windows).

    Baja:
      - Fuente Inter (OFL)           -> assets\ui-kit\fonts\Inter\
      - Iconos Lucide (ISC, SVG)     -> assets\ui-kit\icons\lucide\
    Imprime (NO instala) el comando NuGet para la libreria de controles.

    Uso:
      pwsh -ExecutionPolicy Bypass -File .\download-ui-assets.ps1
      pwsh ... -File .\download-ui-assets.ps1 -SkipFonts -SkipIcons   # selectivo

    Nada se conecta a Revit. Solo descarga ficheros a disco.
#>

[CmdletBinding()]
param(
    [switch]$SkipFonts,
    [switch]$SkipIcons
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ProgressPreference = 'SilentlyContinue'   # acelera Invoke-WebRequest

function Get-File($url, $out) {
    Write-Host "  -> $url" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
}

function Expand-Clean($zip, $dest) {
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Expand-Archive -Path $zip -DestinationPath $dest -Force
}

Write-Host "== e3gbimtools UI asset prep ==" -ForegroundColor Cyan
Write-Host "Destino: $root`n"

# ----------------------------------------------------------------------
# 1) Fuente Inter (OFL 1.1)  -- release oficial rsms/inter en GitHub
# ----------------------------------------------------------------------
if (-not $SkipFonts) {
    Write-Host "[1/2] Fuente Inter..." -ForegroundColor Yellow
    $interVer = '4.1'
    $interUrl = "https://github.com/rsms/inter/releases/download/v$interVer/Inter-$interVer.zip"
    $tmpZip   = Join-Path $env:TEMP 'Inter.zip'
    $fontDir  = Join-Path $root 'fonts\Inter'
    try {
        Get-File $interUrl $tmpZip
        Expand-Clean $tmpZip $fontDir
        Remove-Item $tmpZip -Force
        Write-Host "    OK -> $fontDir" -ForegroundColor Green
        Write-Host "    (usa los .ttf de la subcarpeta 'extras\ttf' o 'web' para embeber en WPF)" -ForegroundColor DarkGray
    } catch {
        Write-Warning "    Fallo bajando Inter: $($_.Exception.Message)"
        Write-Host    "    Manual: https://github.com/rsms/inter/releases" -ForegroundColor DarkGray
    }
}

# ----------------------------------------------------------------------
# 2) Iconos Lucide (ISC)  -- release oficial lucide-icons/lucide
# ----------------------------------------------------------------------
if (-not $SkipIcons) {
    Write-Host "`n[2/2] Iconos Lucide (SVG)..." -ForegroundColor Yellow
    # 'latest' redirige al ultimo release; el zip de SVGs se llama lucide-icons-<ver>.zip
    $lucideUrl = 'https://github.com/lucide-icons/lucide/releases/latest/download/lucide-icons.zip'
    $tmpZip    = Join-Path $env:TEMP 'lucide.zip'
    $iconDir   = Join-Path $root 'icons\lucide'
    try {
        Get-File $lucideUrl $tmpZip
        Expand-Clean $tmpZip $iconDir
        Remove-Item $tmpZip -Force
        $svgCount = (Get-ChildItem $iconDir -Recurse -Filter *.svg).Count
        Write-Host "    OK -> $iconDir ($svgCount SVGs)" -ForegroundColor Green
        Write-Host "    (SVG -> WPF: convierte a Geometry/PathData, o usa SharpVectors NuGet)" -ForegroundColor DarkGray
    } catch {
        Write-Warning "    Fallo bajando Lucide: $($_.Exception.Message)"
        Write-Host    "    Manual: https://github.com/lucide-icons/lucide/releases" -ForegroundColor DarkGray
    }
}

# ----------------------------------------------------------------------
# 3) Libreria de controles -- NO se baja aqui (es NuGet). Solo info.
# ----------------------------------------------------------------------
Write-Host "`n[controles] NuGet (instalacion manual, decide alcance primero):" -ForegroundColor Yellow
Write-Host "    cd src\e3gbimtools.DiTools.UI"
Write-Host "    dotnet add package iNKORE.UI.WPF.Modern" -ForegroundColor White
Write-Host "    # alternativas: HandyControl  |  MaterialDesignThemes" -ForegroundColor DarkGray
Write-Host "    AVISO: DLL compartida con Revit/otros addins -> posible conflicto de version." -ForegroundColor Red

Write-Host "`n== Listo. Assets en $root ==" -ForegroundColor Cyan
