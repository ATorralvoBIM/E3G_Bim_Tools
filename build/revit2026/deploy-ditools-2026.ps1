# Compila DiTools (net8, carga en Revit 2025 y 2026) + el modulo Saneamiento
# (net10 contra la API de Revit 2026) y los despliega en la carpeta a la que
# apunta %APPDATA%\Autodesk\Revit\Addins\2026\e3gbimtools.DiTools.addin.
# Saneamiento NO tiene .addin propio: lo carga DiTools (SaneamientoBridge) y
# aparece como primera app de la pestania «e3gbimtools».
# Ejecutar con Revit 2026 cerrado (las DLL instaladas quedan bloqueadas).
$ErrorActionPreference = 'Stop'
$repo    = Resolve-Path "$PSScriptRoot\..\.."
$proj    = Join-Path $repo 'src\e3gbimtools.DiTools\e3gbimtools.DiTools.csproj'
$out     = Join-Path $repo 'src\e3gbimtools.DiTools\bin\Release\net8.0-windows'
$sanProj = Join-Path $repo 'src\e3gbimtools.Saneamiento\e3gbimtools.Saneamiento.csproj'
$sanOut  = Join-Path $repo 'src\e3gbimtools.Saneamiento\bin\R2026\Release\net10.0-windows'
$dest    = Join-Path $env:LOCALAPPDATA 'Programs\e3g\BimTools\Revit2026'
$addins  = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like '*Revit 2026*' }) {
    throw 'Revit 2026 está abierto y bloquea las DLL. Ciérralo y reintenta.'
}

dotnet build $sanProj -c Release --nologo /p:SanRevitVersion=2026
if ($LASTEXITCODE -ne 0) { throw 'Build de Saneamiento fallido' }
dotnet build $proj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build de DiTools fallido' }

New-Item -ItemType Directory -Force $dest | Out-Null
$excluir = '*.pdb', 'RevitAPI*.dll', 'AdWindows.dll'

# 1) Saneamiento primero; 2) DiTools despues, para que las DLL compartidas
#    (DiTools.UI/Core, ClosedXML, OpenXml...) queden en la version de DiTools.
# El mapeo de aparatos HS5 se edita desde la app: no se pisa el ya instalado.
$mapeo = 'Hs5Mapeo.json'
Copy-Item "$sanOut\*" $dest -Recurse -Force -Exclude ($excluir + $mapeo)
if (-not (Test-Path (Join-Path $dest $mapeo))) {
    # Primera instalacion: hereda el mapeo del add-in antiguo si existe.
    $viejo = Join-Path $addins "E3G_Saneamiento\$mapeo"
    if (Test-Path $viejo) { Copy-Item $viejo $dest } else { Copy-Item (Join-Path $sanOut $mapeo) $dest }
}
Copy-Item "$out\*" $dest -Recurse -Force -Exclude $excluir

# El add-in antiguo E3G_Saneamiento (pestania «E3G BIM Tools») registra los
# mismos paneles acoplables: si sigue activo, choca.  Se desactiva (reversible).
$viejoAddin = Join-Path $addins 'E3G_Saneamiento.addin'
if (Test-Path $viejoAddin) {
    Move-Item $viejoAddin "$viejoAddin.disabled" -Force
    Write-Host "Desactivado $viejoAddin -> .addin.disabled"
}

Write-Host "Desplegado en $dest"
