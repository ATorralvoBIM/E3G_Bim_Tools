@echo off
REM Lanzador del desinstalador portable de E3G Saneamiento (Revit 2026).
REM Ejecuta el script de PowerShell sorteando la directiva de ejecucion.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Desinstalar.ps1" %*
echo.
pause
