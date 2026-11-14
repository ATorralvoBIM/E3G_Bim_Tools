# Sprint 11 — SheetLink P1: Export Revit → Excel

> Fase 4 — SheetLink · Duración: 2 semanas · Dependencias: Sprint 3, Sprint 10 · Estado: **IMPLEMENTADO**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 11 — SheetLink (parte 1)".

---

## 1. Objetivo

Exportar los datos de todos los planos (ViewSheet) del documento Revit a un archivo Excel `.xlsx` editable, con hoja oculta de metadatos para trazabilidad y posterior re-importación. El usuario puede editar números, nombres, revisiones y firmantes en Excel y luego reimportarlos al modelo en una única transacción (un solo Ctrl-Z).

---

## 2. Épicas implementadas

| # | Épica | Descripción |
|---|-------|-------------|
| P11-1 | Lectura de planos | `ReadSheets` lee todos los `ViewSheet` (no placeholder), ordenados por `SheetNumber` |
| P11-2 | Export a .xlsx | `ExportToXlsx` genera un workbook con hoja "Planos" formateada y hoja "_Meta" oculta |
| P11-3 | Formato visual | Cabecera azul corporativo (#1F4E79), columnas readonly en gris, filas alternas en azul claro, fila fija |
| P11-4 | Hoja de metadatos | "_Meta" (oculta): `ExportDate` (ISO 8601), `DocumentTitle`, `ColumnHash` (SHA-256 8 chars), `SheetsExported`, `Generator` |
| P11-5 | Opciones de exportación | `IncludeRevisionFields`, `IncludeSignatoryFields`, `IncludeViewsPlaced`, `FilteredElementIds` |
| P11-6 | Import desde .xlsx | `ParseXlsx` lee la hoja "Planos", valida cabecera y extrae filas por `ElementId` |
| P11-7 | Aplicar cambios | `ApplyChanges` actualiza en una única Transaction los campos editables de cada `ViewSheet` |
| P11-8 | Registro en DI | `ISheetLinkService` → `SheetLinkService` registrado en `DiToolsCoreBuilder.Build()` |

---

## 3. Archivos creados / modificados

### Core (`e3gbimtools.DiTools.Core`)

| Archivo | Estado | Descripción |
|---------|--------|-------------|
| `Abstractions/ISheetLinkService.cs` | **Modificado** | `ExportToCsv` → `ExportToXlsx`; `ParseCsv` → `ParseXlsx`; docstring actualizado |
| `Models/SheetLink/SheetLinkExportResult.cs` | **Modificado** | Eliminado `CsvContent` (innecesario con XLSX binario) |
| `Services/SheetLink/SheetLinkService.cs` | **Reescrito** | ClosedXML; hoja "Planos" con formato corporativo; hoja "_Meta" oculta con metadatos |
| `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` | **Modificado** | `+using SheetLink;` + registro `ISheetLinkService` → `SheetLinkService` en Sprint 11 |

### UI (`e3gbimtools.DiTools.UI`)

| Archivo | Estado | Descripción |
|---------|--------|-------------|
| `Views/SheetLink/SheetLinkWindow.xaml` | **Modificado** | Tab headers y textos: CSV → Excel |
| `Views/SheetLink/SheetLinkWindow.xaml.cs` | **Modificado** | Filtros de archivo `.csv` → `.xlsx`; `ExportToCsv` → `ExportToXlsx`; `ParseCsv(contenido)` → `ParseXlsx(ruta)` |

### Sin cambios

- `Models/SheetLink/SheetLinkRow.cs` — sin tocar
- `Models/SheetLink/SheetLinkImportResult.cs` — sin tocar
- `Models/SheetLink/SheetLinkExportOptions.cs` — sin tocar
- `Commands/SheetLinkCommand.cs` — sin tocar
- `RibbonBuilder.cs` — botón ya existía

---

## 4. Estructura del .xlsx generado

### Hoja "Planos" (visible, editable)

| Columna | Editable | Descripción |
|---------|----------|-------------|
| `ElementId` | ❌ (gris) | Id interno de Revit — clave de reimportación |
| `SheetNumber` | ✅ | Número del plano |
| `SheetName` | ✅ | Nombre del plano |
| `TitleBlock` | ❌ (gris) | Nombre del cartucho |
| `RevisionDate` | ✅ | Fecha de revisión |
| `RevisionNumber` | ✅ | Número de revisión actual |
| `RevisionDescription` | ✅ | Descripción de revisión |
| `IssuedBy` | ✅ | Emitido por |
| `IssuedTo` | ✅ | Emitido para |
| `ApprovedBy` | ✅ | Aprobado por |
| `DesignedBy` | ✅ | Diseñado por |
| `CheckedBy` | ✅ | Comprobado por |
| `DrawnBy` | ✅ | Dibujado por |
| `ViewsPlaced` | ❌ (gris) | Vistas colocadas (separadas por \` \| \`) |

Formato visual:
- Cabecera: fondo `#1F4E79` (azul corporativo), texto blanco, negrita, fila fija
- Columnas readonly: fondo `#F2F2F2`, texto `#595959`
- Filas alternas: fondo `#DEEBF7` (azul muy claro)
- Autofilter habilitado
- `ElementId` tiene ancho fijo (10); resto autofit con máximo 45

### Hoja "_Meta" (oculta)

| Clave | Valor ejemplo |
|-------|--------------|
| `ExportDate` | `2026-05-22T10:30:00.0000000Z` |
| `DocumentTitle` | `Edificio_A.rvt` |
| `ColumnHash` | `A3F2B1C8` (SHA-256 8 chars de los nombres de columna concatenados) |
| `SheetsExported` | `47` |
| `Generator` | `e3gbimtools.DiTools SheetLink Sprint 11` |

El `ColumnHash` se usará en **Sprint 12** para detectar si el archivo fue exportado con una versión diferente del esquema (protección contra importar un Excel de estructura diferente).

---

## 5. Flujo de usuario (Tab 1 — Exportar)

```
[Abrir SheetLink]
  → Tab "1. Exportar planos a Excel"
  → Lista todos los planos del documento (numerados y nombrados)
  → Checkboxes: ✓ Campos de revisión  ✓ Firmantes  ✓ Vistas colocadas
  → [Filtrar por número/nombre]
  → [✓ Marcar todos / ✗ Desmarcar todos]
  → [Exportar Excel...]  → SaveFileDialog (.xlsx)
  → Se genera el .xlsx con hoja "Planos" + "_Meta" oculta
  → Diálogo: "¿Abrir el archivo ahora?"
```

## 6. Flujo de usuario (Tab 2 — Importar)

```
[Tab "2. Importar / Aplicar cambios"]
  → [Examinar...]  → OpenFileDialog (.xlsx)
  → [Previsualizar]  → ParseXlsx(ruta) → DataGrid con filas a aplicar
  → Revisar cambios
  → [Aplicar cambios al modelo]  → confirm → ApplyChanges → Transaction única
  → Filas coloreadas: verde (Updated), amarillo (Skipped), rojo (Error)
  → Resumen: "N planos actualizados, M omitidos, K errores."
  → Ctrl-Z en Revit deshace toda la operación
```

---

## 7. Criterio de aceptación — caso de prueba canónico

> "Renumerar y cambiar el nombre de 5 planos y actualizar el firmante 'ApprovedBy'"

1. Abrir SheetLink → Tab Exportar → Seleccionar los 5 planos → Exportar Excel.
2. Abrir el .xlsx en Excel → cambiar `SheetNumber`, `SheetName` y `ApprovedBy` en 5 filas.
3. Guardar el .xlsx y cerrarlo.
4. Tab Importar → Examinar → seleccionar el .xlsx editado → Previsualizar.
5. El DataGrid muestra las 5 filas con los nuevos valores.
6. Aplicar cambios → confirmar → en Revit los 5 planos tienen los nuevos datos.
7. Ctrl-Z en Revit → deshace los 5 cambios de una vez.

---

## 8. Pendiente Ángel

Compilar en Visual Studio (`Compilación > Recompilar solución`) y probar en Revit 2025:

1. **Pestaña e3gbimtools > DiTools > SheetLink** — comprobar que abre sin error.
2. **Tab 1 / Exportar todos**: pulsar "Exportar Excel…" → guardar → comprobar que se abre en Excel con hoja "Planos" formateada y hoja "_Meta" oculta.
3. **Opciones de columnas**: desmarcar "Firmantes" → exportar → verificar que esas columnas no aparecen en el Excel.
4. **Tab 2 / Importar**: editar 2–3 celdas en el Excel exportado → Examinar → Previsualizar → Aplicar → verificar cambios en Revit → Ctrl-Z → verificar rollback.
5. **ElementId incorrecto**: modificar manualmente un ElementId en el Excel a un valor inválido → importar → verificar que esa fila aparece como "Skipped" (amarillo).

---

## 9. Why / How to apply

**Why:** Sprint 11 cubre la primera parte de SheetLink: el flujo Export → Edit en Excel → Import, que permite gestionar el listado de planos (numeración, nombres, revisiones, firmantes) fuera de Revit de forma masiva, sin tocar plano a plano.

**How to apply (Sprint 12):** El siguiente sprint es **SheetLink P2 — Import Excel → Revit con diff**: leer el .xlsx editado, detectar qué celdas cambiaron respecto al modelo actual, mostrar un preview Before/After con coloreado, y aplicar solo los cambios seleccionados. Usar el `ColumnHash` de la hoja `_Meta` para validar compatibilidad del archivo.
