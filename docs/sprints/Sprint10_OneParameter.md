# Sprint 10 — OneParameter: Edición masiva con scope refinado

> Fase 3 — Datos · Duración: 2 semanas · Dependencias: Sprint 2, Sprint 3 · Estado: **IMPLEMENTADO**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 10 — OneParameter".

## 1. Objetivo

Permitir editar parámetros de N elementos a la vez, limitando el scope a vista activa,
selección actual o todo el modelo, con preview Before/After y operaciones de transformación
tipo Prefix, Suffix, Concat y Regex.

## 2. Épicas implementadas

| # | Épica | Descripción |
|---|-------|-------------|
| P10-1 | Búsqueda por parametro | `GetParameterNames` muestrea el modelo; `Search` recorre todos los elementos (o los del scope/categoría) y retorna filas con `CurrentValue` |
| P10-2 | Selector de scope | `OneParameterScope` (AllModel, CurrentView, CurrentSelection); `BuildSearchRequest` en la UI lee el ComboBox y pasa `ActiveViewId` / `SelectedElementIds` al servicio |
| P10-3 | Transformaciones | `PreviewTransform` (sin tx) + `ApplyTransform` (tx única); operaciones: `SetValue`, `Prefix`, `Suffix`, `Concat`, `RegexReplace` con timeout anti-ReDos |
| P10-4 | Edición inline + lote | DataGrid editable; `OnBulkEdit` vía `TextInputDialog`; `ApplyEdits` en tx única |
| P10-5 | Find & Replace | `FindReplace` — solo parámetros `String`; opción match-case; tx única |
| P10-6 | Exportar CSV | `OnExportCsv` — UTF-8 BOM, todos los resultados cargados |
| P10-7 | Seleccionar en Revit | `OnSelectInRevit` — llama a `_uidoc.Selection.SetElementIds` |

## 3. Archivos creados / modificados

### Core (`e3gbimtools.DiTools.Core`)

| Archivo | Estado | Descripción |
|---------|--------|-------------|
| `Models/OneParameter/OneParameterScope.cs` | **Nuevo** | Enum de scope (AllModel, CurrentView, CurrentSelection) |
| `Models/OneParameter/OneParameterSearchRequest.cs` | **Modificado** | + `Scope`, `ActiveViewId`, `SelectedElementIds` |
| `Models/OneParameter/TransformOperation.cs` | **Nuevo** | Enum de operaciones (SetValue, Prefix, Suffix, Concat, RegexReplace) |
| `Models/OneParameter/TransformRequest.cs` | **Nuevo** | Parámetros de la transformación (Prefix, Suffix, Pattern, Replacement, MatchCase) |
| `Models/OneParameter/TransformPreviewRow.cs` | **Nuevo** | Fila Before/After con `WillChange` y `Error` |
| `Models/OneParameter/TransformPreviewResult.cs` | **Nuevo** | Colección de filas + `WillChangeCount`, `ErrorCount` |
| `Abstractions/IOneParameterService.cs` | **Modificado** | + `PreviewTransform`, `ApplyTransform` |
| `Services/OneParameter/OneParameterService.cs` | **Modificado** | Refactor `CollectElements` con scope; + `PreviewTransform`, `ApplyTransform`, `ComputeTransform`, `ApplyRegex` |

### UI (`e3gbimtools.DiTools.UI`)

| Archivo | Estado | Descripción |
|---------|--------|-------------|
| `Views/OneParameter/OneParameterWindow.xaml` | **Reescrito** | + ComboBox Scope en barra de búsqueda; TabControl con Tab1 (Ver/Editar) y Tab2 (Transformaciones) |
| `Views/OneParameter/OneParameterWindow.xaml.cs` | **Reescrito** | + `BuildSearchRequest` (scope), `OnOperationChanged`, `OnTransformPreview`, `OnApplyTransform`, `BuildTransformRequest`, `GetTransformTargets` |

### Sin cambios

- `FindReplaceDialog.xaml` / `.cs` — sin tocar
- `OneParameterCommand.cs` — sin tocar
- `RibbonBuilder.cs` — botón ya existía
- `DiToolsCoreServiceCollectionExtensions.cs` — registro ya existía

## 4. Interfaz de usuario

### Barra de búsqueda (compartida)

```
[Parametro: ___________] [Scope: Todo el modelo ▼] [Categorias: Todas ▼] [☐ Incl. tipos] [Buscar] [Limpiar]
```

Scopes disponibles: **Todo el modelo** · **Vista actual** (usa `ActiveView.Id`) · **Selección actual** (usa `Selection.GetElementIds()`).

### Tab 1 — Ver / Editar

- DataGrid con columnas: ID, Categoría, Familia, Tipo, Nombre, Nivel, **Valor** (editable).
- Filas modificadas en amarillo claro; filas readonly en gris.
- Barra superior: Seleccionar en Revit · Editar en lote · Find & Replace · Exportar CSV.
- Barra derecha: **Aplicar cambios (N)** · Descartar cambios.

### Tab 2 — Transformaciones

Panel izquierdo (configuración):

| Operación | Campos visibles | Resultado |
|-----------|-----------------|-----------|
| Establecer valor | Valor nuevo | `nuevoValor` |
| Prefix | Prefijo | `prefijo + valorActual` |
| Suffix | Sufijo | `valorActual + sufijo` |
| Concat | Prefijo + Sufijo | `prefijo + valorActual + sufijo` |
| Regex replace | Patrón · Sustitución · Match-case | `Regex.Replace(valorActual, patron, sustitucion)` |

Opciones: aplicar sobre **todos los resultados** o solo **los seleccionados** en la tabla.

Botones: `Preview (Before/After)` → Preview Grid se rellena · `Aplicar transformación` (habilitado sólo tras preview con cambios).

Panel derecho (preview grid): Nombre · Categoría · Nivel · **Antes** · **Después** · **Cambia** · Error. Filas verdes si `WillChange = true`.

## 5. Criterio de aceptación — caso de prueba canónico

> "Cambiar el Comentario de todas las puertas en la planta baja a `[PB] + valor previo`"

1. Buscar parámetro `Comentarios` · Scope: `Vista actual` (con la planta baja abierta) · Categoría: `Puertas`.
2. Tab 2 → Operación `Prefix` · Prefijo `[PB] `.
3. Pulsar **Preview** — la columna "Después" debe mostrar `[PB] {valor original}`.
4. Pulsar **Aplicar transformación** — confirmar → se modifican N elementos.
5. Ctrl-Z en Revit → deshace la operación completa.

## 6. Pendiente Ángel

Compilar en Visual Studio (`Compilación > Recompilar solución`) y probar en Revit 2025:

1. Pestaña **e3gbimtools > DiTools > OneParameter**.
2. **Tab 1 / Scope "Todo el modelo"**: buscar `Comentarios` → editar celdas → Aplicar cambios → Ctrl-Z.
3. **Tab 1 / Scope "Vista actual"**: abrir una planta → buscar → verificar que solo aparecen elementos de esa vista.
4. **Tab 1 / Scope "Selección actual"**: seleccionar 3–5 puertas en Revit → buscar → verificar que solo aparecen esas puertas.
5. **Tab 2 / Prefix**: buscar `Comentarios` en puertas → Prefix `[PB] ` → Preview → Aplicar → Ctrl-Z.
6. **Tab 2 / RegexReplace**: patrón `^(\w+)` sustitución `($1)` → Preview → Aplicar.
7. **Find & Replace**: buscar un texto parcial en un parámetro y reemplazarlo.

## 7. Why / How to apply

**Why:** El Sprint 10 cubre la épica OneParameter del roadmap: edición masiva de parámetros con scope refinado, operaciones de transformación y preview Before/After, que son los flujos de producción real que no cubre la edición cel-a-cel nativa de Revit.

**How to apply:** El siguiente sprint según el roadmap es **Sprint 11 — SheetLink P1** (Export Revit → Excel): exportar datos del modelo por categorías, vista, schedule o selección a un `.xlsx`, con hoja oculta de metadatos para el re-import del Sprint 12.
