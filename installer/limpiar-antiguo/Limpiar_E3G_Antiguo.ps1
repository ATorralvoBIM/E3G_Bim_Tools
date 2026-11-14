#Requires -Version 5.1
<#
.SYNOPSIS
    Retira el add-in antiguo "E3G BIM Tools / E3G Saneamiento" sin tocar el
    plugin nuevo e3gbimtools - DiTools.

.DESCRIPTION
    El add-in antiguo registra los mismos paneles acoplables que el modulo
    Saneamiento nuevo y Revit rechaza el duplicado ("Cannot register the same
    dockable pane ID more than once").

    Busca manifiestos .addin antiguos en las carpetas de usuario y de equipo y
    los MUEVE, junto con sus carpetas conocidas, a una copia de seguridad en
    %LOCALAPPDATA%\Programs\e3g\_Antiguo\<fecha>\. No borra nada.
    Si el mapeo de aparatos HS5 antiguo tiene datos y el nuevo esta vacio, lo
    hereda.

    Este archivo es solo ASCII a proposito: Windows PowerShell 5.1 lee los .ps1
    sin BOM con la codificacion del sistema.
#>
[CmdletBinding()]
param(
    [string[]] $Versions = @('2026'),
    # Muestra lo que haria sin cambiar nada.
    [switch] $Simular,
    # Carpetas alternativas (solo para probar el script).
    [string] $RaizLocal,
    [string] $RaizAddins,
    [string] $RaizProgramData
)

$ErrorActionPreference = 'Stop'

$Local   = if ($RaizLocal)       { $RaizLocal }       else { [Environment]::GetFolderPath('LocalApplicationData') }
$Roaming = if ($RaizAddins)      { $RaizAddins }      else { [Environment]::GetFolderPath('ApplicationData') }
$Comun   = if ($RaizProgramData) { $RaizProgramData } else { [Environment]::GetFolderPath('CommonApplicationData') }

$ManifiestoNuevo = 'e3gbimtools.DiTools.addin'
$NombreMapeo     = 'Hs5Mapeo.json'
$Backup          = Join-Path $Local ("Programs\e3g\_Antiguo\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))

$script:Movidos = 0
$script:Fallos  = 0

function Write-Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Aviso($m)  { Write-Host "  [!]  $m" -ForegroundColor Yellow }
function Write-Fallo($m)  { Write-Host "  [X]  $m" -ForegroundColor Red }

function Mover($origen, $relativoBackup) {
    $destino = Join-Path $Backup $relativoBackup
    if ($Simular) {
        Write-Host "  [simulacion] Se retiraria: $origen"
        $script:Movidos++
        return
    }
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destino) | Out-Null
        Move-Item -LiteralPath $origen -Destination $destino -Force
        Write-Ok "Retirado: $origen"
        $script:Movidos++
    } catch {
        Write-Fallo "No se pudo retirar $origen : $($_.Exception.Message)"
        $script:Fallos++
    }
}

function EntradasMapeo($ruta) {
    if (-not (Test-Path -LiteralPath $ruta)) { return 0 }
    try {
        $json = [IO.File]::ReadAllText($ruta) | ConvertFrom-Json
        if ($null -eq $json) { return 0 }
        return @($json.PSObject.Properties).Count
    } catch {
        return 0
    }
}

function EsAntiguo($archivo, $esCarpetaUsuario) {
    if ($esCarpetaUsuario -and $archivo.Name -ieq $ManifiestoNuevo) { return $false }
    $texto = [IO.File]::ReadAllText($archivo.FullName)
    return ($texto -match 'E3G\.Saneamiento\.' -or $texto -match 'E3G\.BimTools\.DiTools\.')
}

Write-Host ""
Write-Host "=== Limpieza del add-in antiguo E3G BIM Tools ===" -ForegroundColor White

if (-not $RaizLocal -and (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)) {
    Write-Fallo "Revit esta abierto. Cierralo y vuelve a ejecutar este archivo."
    return
}

foreach ($ver in $Versions) {
    Write-Host ""
    Write-Host "--- Revit $ver ---" -ForegroundColor White

    $addinsUsuario = Join-Path $Roaming "Autodesk\Revit\Addins\$ver"
    $addinsEquipo  = Join-Path $Comun   "Autodesk\Revit\Addins\$ver"
    $carpetaVieja1 = Join-Path $addinsUsuario 'E3G_Saneamiento'
    $carpetaVieja2 = Join-Path $Local "Programs\e3g\Saneamiento\Revit$ver"
    $carpetaNueva  = Join-Path $Local "Programs\e3g\BimTools\Revit$ver"

    # 1) Heredar el mapeo antes de retirar las carpetas.
    $mapeoNuevo = Join-Path $carpetaNueva $NombreMapeo
    foreach ($carpeta in @($carpetaVieja2, $carpetaVieja1)) {
        $mapeoViejo = Join-Path $carpeta $NombreMapeo
        if ((EntradasMapeo $mapeoViejo) -gt 0 -and (EntradasMapeo $mapeoNuevo) -eq 0 -and (Test-Path -LiteralPath $carpetaNueva)) {
            if ($Simular) {
                Write-Host "  [simulacion] Se heredaria el mapeo de aparatos de: $mapeoViejo"
            } else {
                Copy-Item -LiteralPath $mapeoViejo -Destination $mapeoNuevo -Force
                Write-Ok "Mapeo de aparatos heredado de: $mapeoViejo"
            }
            break
        }
    }

    # 2) Manifiestos antiguos.
    $lugares = @(
        @{ Dir = $addinsUsuario; Usuario = $true;  Prefijo = 'usuario' },
        @{ Dir = $addinsEquipo;  Usuario = $false; Prefijo = 'equipo'  }
    )
    foreach ($lugar in $lugares) {
        if (-not (Test-Path -LiteralPath $lugar.Dir)) { continue }
        foreach ($archivo in Get-ChildItem -LiteralPath $lugar.Dir -Filter '*.addin' -File) {
            if (EsAntiguo $archivo $lugar.Usuario) {
                Mover $archivo.FullName ("Revit$ver\" + $lugar.Prefijo + "_" + $archivo.Name)
            }
        }
    }

    # 3) Carpetas conocidas del add-in antiguo (nunca BimTools).
    if (Test-Path -LiteralPath $carpetaVieja1) { Mover $carpetaVieja1 "Revit$ver\Addins_E3G_Saneamiento" }
    if (Test-Path -LiteralPath $carpetaVieja2) { Mover $carpetaVieja2 "Revit$ver\Programs_Saneamiento" }

    # 4) Comprobar que el plugin nuevo sigue instalado.
    if (Test-Path -LiteralPath (Join-Path $addinsUsuario $ManifiestoNuevo)) {
        Write-Ok "El plugin nuevo sigue instalado."
    } else {
        Write-Aviso "No se encuentra el plugin nuevo en Revit $ver. Ejecuta Instalar.bat del ZIP nuevo."
    }
}

# 5) Avisar de restos en ApplicationPlugins (solo informa).
Write-Host ""
foreach ($raiz in @((Join-Path $Roaming 'Autodesk\ApplicationPlugins'), (Join-Path $Comun 'Autodesk\ApplicationPlugins'))) {
    if (-not (Test-Path -LiteralPath $raiz)) { continue }
    foreach ($archivo in Get-ChildItem -LiteralPath $raiz -Filter '*.addin' -File -Recurse -ErrorAction SilentlyContinue) {
        if (EsAntiguo $archivo $false) {
            Write-Aviso "Queda un manifiesto antiguo que este script no retira: $($archivo.FullName)"
        }
    }
}

if ($script:Fallos -gt 0) {
    Write-Fallo "Hubo $($script:Fallos) error(es). Si pone acceso denegado, abre PowerShell como administrador y ejecuta este script desde ahi."
} elseif ($script:Movidos -eq 0) {
    Write-Aviso "No se encontro nada antiguo en estas carpetas. No se ha cambiado nada."
} elseif ($Simular) {
    Write-Host "Simulacion terminada: $($script:Movidos) elemento(s). No se ha cambiado nada." -ForegroundColor White
} else {
    Write-Host "=== Limpieza completada: $($script:Movidos) elemento(s) ===" -ForegroundColor Green
    Write-Host "Copia de seguridad en: $Backup"
    Write-Host "Abre Revit y pulsa Saneamiento en la pestana e3gbimtools."
}
Write-Host ""
