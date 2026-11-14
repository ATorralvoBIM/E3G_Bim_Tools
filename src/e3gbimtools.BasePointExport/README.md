# e3gbimtools.BasePointExport — Exportador Revit (BasePointView, Sprint 4)

Comando de Revit que exporta la **vista 3D activa** a:

- **`<modelo>.glb`** — geometría (glTF binario), un nodo por elemento etiquetado
  con su `UniqueId` de Revit en `extras`.
- **`<modelo>.json`** — propiedades indexadas por ese mismo `UniqueId`.

Es el puente Revit → web del visor **BasePointView**, **sin dependencia de
Autodesk APS/Forge**: todo el proceso es local con la API de Revit.

## Cómo funciona

- `ExportCommand` (`IExternalCommand`, `TransactionMode.ReadOnly`): resuelve la
  vista 3D, pide la ruta de salida y lanza el `CustomExporter`.
- `GltfExportContext` (`IExportContext`, patrón base Tammik/McCulloch): Revit
  tesela la geometría y la entrega por callbacks; aquí se captura
  (`OnPolymesh`), se acumula por elemento (`OnElementBegin`), se aplican las
  transformaciones de instancia (`OnInstanceBegin`) y los materiales
  (`OnMaterial`), y se serializa a GLB con **SharpGLTF**.
  - Unidades internas (pies) → metros (×0,3048).
  - Eje Z-up de Revit → Y-up de glTF (se abre derecho en visores estándar).
  - Metadato Survey / Project Base Point en `scene.extras` (para la federación
    del Sprint 6).
- `PropertyExtractor` (frontera estilo **RevitBridge**): vuelca cada elemento al
  esquema neutro `RevitElementData` (`name`, `category`, `attributes`,
  `dimensions{value,unit}`, `materials`, `psets`), que coincide con
  `src/viewer/PropertyBinding.ts` del visor. Reutiliza `RevitBridge/RevitUnits`.

Solo se exportan propiedades de elementos **con geometría** (JSON alineado con
el GLB).

## Build

```powershell
dotnet build -c Release
# Revit en otra ruta:
dotnet build -c Release /p:RevitInstallDir="C:\Program Files\Autodesk\Revit 2025\"
```

Salida en `bin\Release\net8.0-windows\` (incluye `SharpGLTF.*.dll`).

## Instalar en Revit

Copia `BasePointExport.addin` a
`%AppData%\Autodesk\Revit\Addins\2025\` (ajusta la ruta `<Assembly>` si tu salida
de build difiere). El comando aparece como **«BasePointView — Exportar a GLB»**.

## Uso

1. Abre una vista 3D del modelo.
2. Ejecuta el comando y elige dónde guardar el `.glb`.
3. Arrastra **el `.glb` y el `.json`** generados a la ventana del visor web.

## Notas

- Dependencias: Revit 2025 (DLLs por HintPath), `SharpGLTF.Toolkit`,
  `RevitBridge/RevitUnits.cs` (enlazado).
- La compresión Draco e instanciación de geometría se abordan en el Sprint 10.
