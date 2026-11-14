#Requires -Version 5.1
<#
.SYNOPSIS
    Desinstalador portable de E3G Saneamiento para Revit 2026.
    Borra el manifiesto .addin y la carpeta por usuario. Sin administrador.
#>
$ErrorActionPreference = 'Stop'

$Ver       = '2026'
$addinPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) "Autodesk\Revit\Addins\$Ver\E3G_Saneamiento.addin"
$targetDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs\e3g\Saneamiento\Revit$Ver"

Write-Host ""
Write-Host "=== Desinstalador: E3G Saneamiento - Revit $Ver ===" -ForegroundColor White
if (Test-Path $addinPath) { Remove-Item -Force $addinPath; Write-Host "  [OK] Eliminado $addinPath" -ForegroundColor Green }
if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir; Write-Host "  [OK] Eliminado $targetDir" -ForegroundColor Green }
Write-Host "Reinicia Revit para completar la desinstalación." -ForegroundColor White
Write-Host ""
