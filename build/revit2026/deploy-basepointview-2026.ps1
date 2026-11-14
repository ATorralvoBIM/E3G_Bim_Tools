# Compila el add-in BasePointView (exportador + conversor de .rvt) para Revit
# 2026 (.NET 10) y lo despliega en %APPDATA%\Autodesk\Revit\Addins\2026. Los
# binarios se COPIAN a la subcarpeta BasePointView\ (ruta relativa en el .addin)
# para no bloquear bin\ al compilar. Ejecutar con Revit 2026 cerrado.
$ErrorActionPreference = 'Stop'
$repo    = Resolve-Path "$PSScriptRoot\..\.."
$project = Join-Path $repo 'src\e3gbimtools.BasePointExport\e3gbimtools.BasePointExport.csproj'
$out     = Join-Path $repo 'src\e3gbimtools.BasePointExport\bin\Release\net10.0-windows'
$addins  = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$dest    = Join-Path $addins 'BasePointView'

if (Get-Process Revit -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*Revit 2026*' }) {
    throw "Cierra Revit 2026 antes de desplegar."
}

dotnet build $project -c Release -f net10.0-windows
if ($LASTEXITCODE -ne 0) { throw "Build fallido" }

New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$out\*" $dest -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'BasePointView.addin') $addins -Force
Write-Host "Desplegado en $dest"
