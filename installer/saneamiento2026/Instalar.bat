@echo off
REM Lanzador del instalador portable de E3G Saneamiento (Revit 2026).
REM Ejecuta el script de PowerShell sorteando la directiva de ejecucion.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1" %*
echo.
pause
