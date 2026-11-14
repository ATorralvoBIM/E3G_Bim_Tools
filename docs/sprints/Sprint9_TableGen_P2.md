# Sprint 9 — TableGen P2: Lote, Colocar en Hoja y Schedule → Vista

> Fase 3 — Datos · Duración: 1 semana · Dependencias: Sprint 8 (TableGen P1) · Estado: **IMPLEMENTADO**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 9 — TableGen".
Este documento cubre la segunda parte de TableGen implementada el 2026-05-22.

## 1. Objetivo

Completar TableGen con tres capacidades nuevas que complementan la generación
individual de Drafting Views (P1):

1. **Generación en lote** — una vista por cada hoja del Excel en una sola operación.
2. **Colocar en hoja** — colocar cualquier Drafting View sobre un Drawing Sheet con
   control de posición XY.
3. **Schedule → Vista** — convertir un ViewSchedule de Revit en una Drafting View
   con el mismo generador gráfico de P1 (texto, bordes, fondos de color).

## 2. Épicas implementadas

| # | Épica | Descripción |
|---|-------|-------------|
| P2-1 | Generación en lote | `GenerateBatch` — procesa todas las hojas del .xlsx; patrón de nombre configurable `{file}_{sheet}`; sobreescritura opcional |
| P2-2 | Sobreescritura | Flag `OverwriteExisting` en `TableGenRequest` (Tab 1 y lote) elimina la vista preexistente antes de regenerar |
| P2-3 | Colocar en hoja | `PlaceOnSheet` — crea `Viewport.Create` en el sheet; elimina viewport anterior si ya estaba colocada; offset XY configurable en mm |
| P2-4 | Schedule → Vista | `ExportScheduleToView` + `ScheduleTableReader`; cabecera azul claro (#BDD7EE) con negrita; filas alternas blanco/#F2F2F2; anchos de columna desde el schedule |
| P2-5 | Consultas nuevas | `GetSchedules`, `GetDrawingSheets`, `GetDraftingViews` — pob lan los combos de la UI sin necesidad de tocar Revit manualmente |

## 3. Archivos creados / modificados

### Core (`e3gbimtools.DiTools.Core`)

| Archivo | Descripción |
|---------|-------------|
| `Models/TableGen/BatchTableGenRequest.cs` | Parámetros del lote |
| `Models/TableGen/BatchTableGenResult.cs` | Resultado del lote (lista de TableGenResult) |
| `Models/TableGen/PlaceOnSheetRequest.cs` | Vista + hoja + XY offset |
| `Models/TableGen/PlaceOnSheetResult.cs` | Success + ViewportId + Warnings |
| `Models/TableGen/ScheduleToViewRequest.cs` | ScheduleId + opciones de formato |
| `Models/TableGen/ScheduleExportResult.cs` | Success + ViewId + rowCount/colCount |
| `Models/TableGen/ScheduleInfo.cs` | DTO para el ComboBox de schedules |
| `Models/TableGen/SheetPickerInfo.cs` | DTO para el ComboBox de Drawing Sheets |
| `Models/TableGen/DraftingViewInfo.cs` | DTO con flag `IsPlacedOnSheet` |
| `Models/TableGen/TableGenRequest.cs` | **+** propiedad `OverwriteExisting` |
| `Abstractions/ITableGenService.cs` | **+** 7 métodos nuevos P2 |
| `Services/TableGen/ScheduleTableReader.cs` | Nuevo: lee ViewSchedule → ExcelSheetData |
| `Services/TableGen/TableGenService.cs` | Implementa los métodos P2 |

### UI (`e3gbimtools.DiTools.UI`)

| Archivo | Descripción |
|---------|-------------|
| `Views/TableGen/TableGenWindow.xaml` | Reescrito con TabControl de 3 pestañas |
| `Views/TableGen/TableGenViewModel.cs` | Ampliado con lógica de Tab 2 y Tab 3 |
| `Views/TableGen/TableGenWindow.xaml.cs` | Comentario actualizado; constructor sin cambios |

### Sin cambios necesarios

- `DiToolsCoreServiceCollectionExtensions.cs` — TableGen ya estaba registrado en Sprint 8.
- `RibbonBuilder.cs` — botón TableGen ya existe.
- `TableGenCommand.cs` — abre la misma ventana.
- `DraftingViewGenerator.cs` — reutilizado sin modificaciones.
- `ExcelTableReader.cs` — reutilizado sin modificaciones.

## 4. Interfaz de usuario (3 pestañas)

```
[Excel → Vista]  [Lote + Colocar en hoja]  [Schedule → Vista]
```

**Tab 1 — Excel → Vista** (P1 sin cambios funcionales):
- Añadido checkbox "Sobreescribir si ya existe".
- Añadido checkbox opcional "Colocar en hoja después de generar" con combo de hoja y offset XY.

**Tab 2 — Lote + Colocar en hoja**:
- Sección superior: file picker + patrón de nombre + sobreescribir + botón "Generar lote".
- DataGrid con resultado por hoja (nombre de hoja, vista creada, estado, ms).
- Sección inferior: combo Drafting View + combo Drawing Sheet + XY + botón "Colocar en hoja".

**Tab 3 — Schedule → Vista**:
- Combo con todos los ViewSchedules del documento (nombre + categoría + nº filas).
- Campo nombre de la vista destino (auto-rellena con "TableGen_{schedule}").
- Parámetros: multiplicador de ancho de columna, alto de fila cabecera/cuerpo.
- Checkbox "Sobreescribir si ya existe".
- Panel informativo con categoría y nº de filas del schedule seleccionado.
- Botón "Generar desde schedule" + barra de progreso.

## 5. ScheduleTableReader — esquema visual del schedule exportado

| Sección | FontSize | Bold | Fondo | Borde exterior | Borde interior |
|---------|----------|------|-------|----------------|----------------|
| Cabecera | 9 pt | Sí | #BDD7EE (azul claro) | Medium | Thin |
| Filas pares | 8 pt | No | Blanco | Medium (exteriores) | Thin |
| Filas impares | 8 pt | No | #F2F2F2 (gris) | Medium (exteriores) | Thin |

Los anchos de columna se leen directamente de `TableSectionData.GetColumnWidth(col)` (ft → mm) y se escalan con `ColumnWidthMultiplier`.

## 6. Pendiente Angel

Compilar en Visual Studio (Compilación > Recompilar solución) y probar en Revit 2025:

1. Pestaña **e3gbimtools > DiTools > TableGen**.
2. **Tab 1**: abrir un .xlsx, preview, generar — igual que antes. Probar "Sobreescribir" y "Colocar en hoja".
3. **Tab 2**: abrir el mismo .xlsx, pulsar "Generar lote" — deben aparecer tantas vistas como hojas tiene el Excel. Luego probar "Colocar en hoja" seleccionando una vista y una hoja.
4. **Tab 3**: seleccionar un schedule del modelo (p. ej. "Lista de ventanas"), pulsar "Generar desde schedule" — debe crear una Drafting View con la tabla formateada.

## 7. Why / How to apply

**Why:** P1 (Sprint 8) cubría el caso de uso básico (una tabla = una vista). P2 añade los flujos de producción real: lotes de decenas de tablas desde un Excel, colocar tablas directamente en planos y convertir schedules existentes en tablas editables de Drafting View.

**How to apply:** Siguiente sprint según roadmap es **Sprint 10 — OneParameter** (replica de OneParameter de DiRootsOne: modificar el valor de un parámetro en múltiples elementos a la vez, con búsqueda global y edición masiva).
