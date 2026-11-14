@echo off
REM Retira el add-in antiguo E3G BIM Tools sin tocar el plugin nuevo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Limpiar_E3G_Antiguo.ps1" %*
echo.
pause
