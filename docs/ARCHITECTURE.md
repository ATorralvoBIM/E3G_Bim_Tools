# Arquitectura — e3gbimtools

> Estado: borrador inicial del Sprint 0. Se completara al cierre del sprint.

## Solucion

```
e3gbimtools.sln
├── src/
│   ├── e3gbimtools.DiTools        — App Revit (IExternalApplication, ribbon)
│   ├── e3gbimtools.DiTools.Core   — Servicios + abstracciones Revit (sin WPF)
│   ├── e3gbimtools.DiTools.UI     — WPF base (tema, controles, converters)
│   ├── e3gbimtools.Saneamiento    — Disciplina Saneamiento (migrado del monolito)
│   └── e3gbimtools.Fontaneria     — Disciplina Fontaneria (migrado del monolito)
├── tests/
│   └── e3gbimtools.DiTools.Tests  — xUnit
├── build/                          — manifests .addin, scripts
└── installer/                      — WiX v4 .wixproj
```

## Multi-targeting Revit

| Revit | TFM           | Constante  |
|-------|---------------|------------|
| 2022  | net48         | REVIT2022  |
| 2023  | net48         | REVIT2023  |
| 2024  | net48         | REVIT2024  |
| 2025  | net8.0-windows| REVIT2025  |
| 2026  | net8.0-windows| REVIT2026  |
| 2027  | net8.0-windows| REVIT2027  |

Build local: `dotnet build /p:RevitVersion=2025`.
RevitAPI se resuelve via paquetes `Nice3point.Revit.Api.*` versionados por `$(RevitVersion)`.

## Convenciones

- Namespace raiz: `E3G.BimTools.<Modulo>.*` (PascalCase). Nombre de ensamblado `e3gbimtools.<Modulo>` (lowercase).
- Toda operacion que modifique el modelo va dentro de `ITransactionService.Run(...)`, con nombre descriptivo.
- Logging: `Serilog` con sink a `%APPDATA%/e3g/DiTools/logs/dt-yyyyMMdd.log` rolling diario.
- WPF: `CommunityToolkit.Mvvm` (`ObservableObject` + `RelayCommand`).
- Excepciones no controladas: `GlobalExceptionHandler` -> log + `FriendlyErrorDialog`.

---

## Módulos DiTools

### Sprint 1 — CopyLevels
Copia niveles desde un modelo vinculado al documento anfitrión en una sola `Transaction`.  
Servicio: `ILevelCopyService` → `LevelCopyService`.

### Sprint 2 — OneFilter
Filtrado de elementos por categoría + árbol de condiciones AND/OR. Acciones: seleccionar, aislar, override color, edición en lote, exportar CSV.  
Servicios: `IFilterService`, `IFilterTemplateService`, `IFilterBatchEditService`.  
Plantillas guardadas en `%APPDATA%\e3g\DiTools\FilterTemplates\*.json`.

### Sprint 3 — ParaManager
Gestión de parámetros de proyecto y compartidos: crear, eliminar, recategorizar, editar valores en lote.  
Servicios: `IParaManagerService`, `IParameterValueBulkEditService`.

### Sprint 4 — ViewManager
Gestión de View Templates y View Ranges: inventario, aplicación masiva, diff entre plantillas, exportar matriz a Excel.  
Servicios: `IViewTemplateService`, `IViewRangeService`, `IViewTemplateComparer`.  
Requiere `IExcelService` (ClosedXML).

### Sprint 5 — SectionBoxer
Section Boxes 3D desde selección / habitación / nivel / filtro guardado. Padding, alineación, vista dedicada, reset.  
Servicios: `ISectionBoxService`, `IBoundingBoxCalculator`.  
Fuentes: `ISectionBoxSource` (Strategy): `SelectionSource`, `RoomSource`, `LevelSource`, `SavedFilterSource`.

### Sprint 6 — SheetGen

**Objetivo:** Creación masiva de Drawing Sheets, import/export Excel, layouts de colocación de vistas, revisiones en bulk y auditoría del juego de planos.

**Modelos** (`Core/Models/Sheets/`):

| Clase | Rol |
|-------|-----|
| `SheetDescriptor` | DTO de entrada para crear una sheet (número, nombre, title block, disciplina, layoutId) |
| `ViewPlacementSpec` | Posición (X/Y en mm) y escala de una vista dentro de un layout |
| `SheetLayoutDefinition` | Colección de `ViewPlacementSpec` con nombre e id (Guid); persistida en JSON |
| `SheetRevisionRequest` | Petición para asignar/quitar una revisión a N sheets |
| `SheetRenumberRequest` | Patrón de renumeración: prefijo + secuencia + sufijo + padding |
| `SheetGenResult` | Resultado de creación masiva: creadas, omitidas, warnings, tiempo |
| `SheetAuditResult` | Huérfanas (0 viewports) y números duplicados |
| `SheetInfo` | Snapshot de una ViewSheet existente (usado en export y auditoría) |

**Abstracción:** `ISheetGenService` — 9 métodos: `BulkCreateAsync`, `ImportFromExcel`, `ExportToExcel`, `ApplyRevisions`, `RenumberSheets`, `PreviewRenumber`, `Audit`, `GetAllSheets`, `GetLayouts / SaveLayout / DeleteLayout`.

**Servicios** (`Core/Services/Sheets/`):
- `SheetGenService` — implementación principal. Toda la creación de sheets va en **una sola `Transaction`** para cumplir el criterio de rendimiento (50 sheets < 30 s). Validación de duplicados de `Sheet Number` **antes** de abrir la transacción.
- `SheetLayoutRepository` — persiste layouts como JSON en `%APPDATA%\e3g\DiTools\SheetLayouts\{guid}.json` usando `System.Text.Json`.

**Excel** (`Core/Services/ExcelService.cs`):
Implementación completa de `IExcelService` con ClosedXML. `WriteSheet<T>` y `ReadSheet<T>` usan reflexión para mapear columnas por nombre de propiedad (case-insensitive).

**UI** (`DiTools.UI/Views/SheetGen/`):
Ventana tabbed (`SheetGenWindow`) con 4 pestañas:
1. **Crear / Importar** — grid editable + import/export Excel + selector de layout + progress bar.
2. **Layouts** — lista + editor inline de `ViewPlacementSpec` + guardar/eliminar JSON.
3. **Revisiones** — multi-selección de sheets + combobox de revisiones + asignar/quitar.
4. **Auditoría / Renumerar** — audit (huérfanas + duplicados) + renumeración con preview antes/después.

**Dependencias en runtime:** ClosedXML 0.102.3 (añadido en `e3gbimtools.DiTools.Core.csproj`).
