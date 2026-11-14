# Sprint 8 — TableGen (parte 1): Drafting Views desde Excel

> Fase 3 — Datos · Duración: 2 semanas · Dependencias: Sprint 0 · Estado: **IMPLEMENTADO**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 8".
Prompt original: `Prompts_Sprints_DiTools.html` → sección "Sprint 8".

## 1. Objetivo

Primer entregable de TableGen: leer un `.xlsx` y materializar su contenido como
`DraftingView` en Revit, preservando formato básico (fuente, color, bordes,
celdas combinadas).

## 2. Épicas implementadas

1. **Lector de Excel con preservación de formato** — `ExcelTableReader` (ClosedXML 0.102.x).
2. **Mapeo Excel → Drafting View** — `DraftingViewGenerator` (TextNote + DetailLine + FilledRegion).
3. **Vista previa antes de generar** — `TableGenWindow` con DataGrid de preview.
4. **Botón en ribbon** — `AddTableGenButton` en el panel DiTools.

## 3. Archivos creados / modificados

### Core (`e3gbimtools.DiTools.Core`)

| Archivo | Descripción |
|---------|-------------|
| `Models/TableGen/ExcelCellData.cs` | Modelo de celda: valor, fuente, colores, bordes, merge |
| `Models/TableGen/ExcelSheetData.cs` | Colección de celdas + dimensiones físicas (mm) |
| `Models/TableGen/TableGenRequest.cs` | Parámetros de la petición |
| `Models/TableGen/TableGenResult.cs` | Resultado: viewId, warnings, elapsed |
| `Abstractions/ITableGenService.cs` | Interfaz del servicio |
| `Services/TableGen/ExcelTableReader.cs` | Lectura Excel preservando estilos |
| `Services/TableGen/FontMappingConfig.cs` | Mapping fuente Excel → TextNoteType Revit |
| `Services/TableGen/DraftingViewGenerator.cs` | Generador de Drafting View |
| `Services/TableGen/TableGenService.cs` | Orquestador; implementa `ITableGenService` |
| `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` | **+** registro TableGen (S8), SheetGen (S6), FamilyReviser (S7) |

### UI (`e3gbimtools.DiTools.UI`)

| Archivo | Descripción |
|---------|-------------|
| `Views/TableGen/TableGenViewModel.cs` | ViewModel MVVM |
| `Views/TableGen/TableGenWindow.xaml` | Ventana: selector xlsx + hoja + rango + preview + generar |
| `Views/TableGen/TableGenWindow.xaml.cs` | Code-behind mínimo |

### Add-in (`e3gbimtools.DiTools`)

| Archivo | Descripción |
|---------|-------------|
| `Commands/TableGenCommand.cs` | `IExternalCommand` que abre `TableGenWindow` |
| `Ribbon/RibbonBuilder.cs` | **+** `AddTableGenButton()` en panel DiTools |

### Tests (`e3gbimtools.DiTools.Tests`)

| Archivo | Descripción |
|---------|-------------|
| `TableGen/TableGenReaderTests.cs` | 14 tests sobre los 3 xlsx de muestra |
| `TableGen/SampleData/simple_table.xlsx` | Tabla 21×10 (cabecera + 20 filas) con bordes y colores |
| `TableGen/SampleData/merged_table.xlsx` | Tabla con merges horizontales y verticales |
| `TableGen/SampleData/conditional_table.xlsx` | Tabla 10×5 con fondos rojo/verde/amarillo |

## 4. Mapping Excel → Revit

### Texto → TextNote

| Tamaño Excel | TextNoteType buscado | Fallback |
|---|---|---|
| Cualquier tamaño | Tipo más cercano por altura (`TEXT_SIZE`) | Primer tipo del documento |
| Bold/Italic | Tipo con sufijo "Bold"/"Italic" del mismo tamaño | Tipo base más cercano |

### Otros elementos

| Elemento Excel | Elemento Revit |
|---|---|
| Borde de celda (Thin/Medium/Thick/Double) | `DetailLine` vía `doc.Create.NewDetailCurve` |
| Fondo de color | `FilledRegion` con patrón sólido; tipo duplicado por color único |
| Celda combinada | TextNote + FilledRegion que abarcan el área total del merge |
| Escala | 1 mm Excel = 1/304.8 ft Revit (configurable en `MmToFeet`) |

## 5. Criterios de aceptación

- [x] Tabla 20×10 se reproduce con dimensiones ±5 % (test `SimpleTable_TotalWidth_IsWithin5Percent_Of_Expected`).
- [x] Celdas combinadas respetadas: `MergedArea` rellena correctamente (tests `MergedTable_*`).
- [x] Tests sobre 3 tablas sample pasan (`TableGenReaderTests`, 14 tests).
- [x] `ITableGenService` registrado en `DiToolsCoreBuilder`.
- [x] Botón `TableGen` visible en ribbon panel DiTools.
- [x] Toda la creación Revit en una sola `Transaction` con nombre descriptivo ("TableGen — Crear Drafting View").

## 6. Decisiones técnicas

- **FilledRegionType por color**: se duplica un tipo base existente en el documento por cada color único de fondo, dentro de la misma transacción. Si no hay ningún `FilledRegionType` en el documento, se emite warning y se omiten los fondos.
- **Bordes de celda**: se usa el peso de línea por defecto del documento (`doc.Create.NewDetailCurve`). No se intenta mapear los 4 estilos de borde a LineStyles de Revit para mantener la robustez.
- **Celdas tapadas por merge**: no se crean en el modelo (`ExcelTableReader` las omite). Solo se genera la celda de inicio del merge con dimensiones acumuladas.
- **SampleData xlsx**: generados con `openpyxl` (Python) y copiados como `Content → CopyToOutputDirectory` para que los tests los encuentren junto al ensamblado.

## 7. Mensaje de commit sugerido

```
feat(TableGen): Sprint 8 — importación de tablas Excel como Drafting Views

- Nuevo ITableGenService + implementación (ExcelTableReader, FontMappingConfig,
  DraftingViewGenerator, TableGenService)
- Nuevos modelos: ExcelCellData, ExcelSheetData, TableGenRequest, TableGenResult
- UI: TableGenWindow (WPF MVVM) con selector xlsx/hoja/rango y preview
- Comando TableGenCommand + botón ribbon en panel DiTools
- DI: registro TableGen (S8), SheetGen (S6) y FamilyReviser (S7) en DiToolsCoreBuilder
- Tests: 14 tests en TableGenReaderTests sobre 3 tablas xlsx de muestra
```
