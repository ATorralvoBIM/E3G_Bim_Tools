# Instalador `.msi` — e3gbimtools

Proyecto WiX Toolset v5 que genera un instalador `.msi` para distribuir
el add-in `e3gbimtools` a otros usuarios sin que tengan que copiar archivos
a mano.

## Qué hace el instalador

1. Crea la carpeta `%LOCALAPPDATA%\Programs\e3g\BimTools\Revit2025\` y deja allí:
   - `e3gbimtools.DiTools.dll`
   - `e3gbimtools.DiTools.Core.dll`
   - `e3gbimtools.DiTools.UI.dll`
   - Iconos del ribbon
2. Copia el manifest `.addin` a `%APPDATA%\Autodesk\Revit\Addins\2025\`
   para que Revit cargue automáticamente el add-in al arrancar.
3. Es **per-user**: no requiere permisos de administrador. Cualquier usuario
   puede instalarlo en su propia máquina sin pasar por IT.

## Cómo construir el `.msi` localmente

Sprint 0 — requisitos:

1. Instalar la herramienta WiX Toolset 5 (la primera vez):
   ```
   dotnet tool install --global wix
   ```
2. Construir el instalador (después de haber compilado el add-in):
   ```
   dotnet build installer\e3gbimtools.Installer.wixproj -c Release
   ```
3. El `.msi` resultante aparece en `installer\bin\Release\`. Doble-click
   para instalarlo, o pásalo a cualquier compañero.

## Cuándo se actualiza

Cada vez que se publica una nueva versión del add-in:

1. Sube el número de `Version="..."` en `Product.wxs`.
2. Recompila el proyecto (`dotnet build installer\...`).
3. El `MajorUpgrade` está activado: el instalador nuevo desinstala el viejo
   automáticamente si está presente.

**NO cambies el `UpgradeCode`** (`A12B3C4D-5E60-4F71-8A92-1B2C3D4E5F60`).
Es el identificador estable del producto entre versiones.

## Multi-versión Revit (pendiente)

Sprint 0 solo cubre Revit 2025 porque es la única versión con DLL real.
Para añadir Revit 2022/2023/2024/2026/2027 cuando estén compilables:

1. Construir las DLLs de cada versión (con su `RevitInstallDir` apuntando
   a la instalación local).
2. Añadir `ComponentGroup` y `Feature` para cada versión nueva en
   `Product.wxs`, copiando el bloque de Revit 2025 y ajustando rutas.
3. Cada versión usa su `.addin` correspondiente de `build\manifests\`.
