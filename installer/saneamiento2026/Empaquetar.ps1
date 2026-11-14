#Requires -Version 5.1
<#
.SYNOPSIS
    Compila E3G Saneamiento para Revit 2026 y genera el ZIP portable para
    pasarlo a otro usuario (installer\dist\E3G_Saneamiento-Revit2026-portable-<fecha>.zip).

.DESCRIPTION
    1. dotnet build -c Release /p:SanRevitVersion=2026
    2. Refresca la carpeta "app" con la salida (DLL, recursos, Hs5Mapeo.json)
       y la plantilla del manifiesto.
    3. Empaqueta Instalar/Desinstalar + LEEME + app en un ZIP.
#>
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repo = Resolve-Path (Join-Path $here '..\..')
$out  = Join-Path $repo 'bin\R2026\Release\net10.0-windows'
$app  = Join-Path $here 'app'
$dist = Join-Path $repo 'installer\dist'

Write-Host "Compilando Revit 2026 (Release)..." -ForegroundColor Cyan
dotnet build (Join-Path $repo 'E3G_Saneamiento.csproj') -c Release /p:SanRevitVersion=2026 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "La compilación ha fallado." }

Write-Host "Refrescando la carga útil..." -ForegroundColor Cyan
$plantilla = Get-Content (Join-Path $app 'E3G_Saneamiento.addin.template') -Raw
Remove-Item -Recurse -Force $app
New-Item -ItemType Directory -Force $app | Out-Null
Copy-Item "$out\*" $app -Recurse -Force -Exclude '*.pdb'
[IO.File]::WriteAllText((Join-Path $app 'E3G_Saneamiento.addin.template'), $plantilla, (New-Object Text.UTF8Encoding $false))

$version = (Get-Item (Join-Path $app 'E3G_Saneamiento.dll')).LastWriteTime.ToString('yyyyMMdd')
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "E3G_Saneamiento-Revit2026-portable-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }

$staging = Join-Path ([IO.Path]::GetTempPath()) "E3G_Saneamiento-Revit2026"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null
foreach ($f in 'Instalar.bat', 'Instalar.ps1', 'Desinstalar.bat', 'Desinstalar.ps1', 'LEEME.txt') {
    Copy-Item (Join-Path $here $f) $staging
}
Copy-Item $app (Join-Path $staging 'app') -Recurse
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging

Write-Host "ZIP generado: $zip" -ForegroundColor Green
