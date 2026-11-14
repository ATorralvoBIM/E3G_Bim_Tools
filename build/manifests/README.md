# Manifests `.addin` por versión de Revit

Cada archivo `e3gbimtools.DiTools.<VERSION>.addin` apunta a la DLL que el
instalador deja en `%LOCALAPPDATA%\Programs\e3g\BimTools\Revit<VERSION>\`.

Versiones cubiertas:

- `e3gbimtools.DiTools.2022.addin` → Revit 2022 (.NET Framework 4.8)
- `e3gbimtools.DiTools.2023.addin` → Revit 2023 (.NET Framework 4.8)
- `e3gbimtools.DiTools.2024.addin` → Revit 2024 (.NET Framework 4.8)
- `e3gbimtools.DiTools.2025.addin` → Revit 2025 (.NET 8) ← **única que tiene DLL real ahora mismo**
- `e3gbimtools.DiTools.2026.addin` → Revit 2026 (.NET 8)
- `e3gbimtools.DiTools.2027.addin` → Revit 2027 (.NET 8)

## Cuándo se usan

El instalador WiX (proyecto `installer/e3gbimtools.Installer.wixproj`) detecta
qué versiones de Revit están instaladas en la máquina (vía registro de Windows)
y copia el `.addin` correspondiente a `%APPDATA%\Autodesk\Revit\Addins\<VERSION>\`.

Si una versión de Revit no está instalada, su `.addin` no se despliega.

## Cuándo se construyen las DLLs

Por ahora **solo se compila la DLL de Revit 2025** porque es la única versión
con `RevitAPI.dll` disponible en disco (`E:\APPS\Autodesk\Revit 2025\`).

Para construir las otras 5 versiones hace falta:

1. Tener instaladas las versiones correspondientes de Revit, o
2. Tener los `RevitAPI.dll` y `RevitAPIUI.dll` de cada versión en una carpeta accesible, o
3. Usar paquetes NuGet `Nice3point.Revit.Api.*` (requiere conexión NuGet funcionando).

Ver `docs/DEV_SETUP.md` para más detalles.

## Identidad común

Todos los manifests comparten:

- `AddInId`: `F4E7C2A1-9B3D-4A12-B6FA-1A2B3C4D5E60` (no cambia entre versiones — un único producto)
- `FullClassName`: `E3G.BimTools.DiTools.DiToolsApplication`
- `VendorId`: `E3G`
- `Name`: `e3gbimtools — DiTools`

**No cambies el `AddInId`** entre versiones ni en futuras actualizaciones. Si lo
cambias, Revit lo trataría como un add-in distinto y conviviría con el viejo.
