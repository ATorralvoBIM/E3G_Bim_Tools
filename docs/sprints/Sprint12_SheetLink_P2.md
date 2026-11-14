# Sprint 12 — SheetLink P2: Diff Before/After + Importacion selectiva

> Fase 4 — SheetLink · Duracion: 1 semana · Dependencias: Sprint 11 · Estado: **IMPLEMENTADO**

Fuente maestra: `docs/sprints/Sprint11_SheetLink_P1.md` → sección "How to apply (Sprint 12)".

---

## 1. Objetivo

Ampliar SheetLink con un flujo de importacion inteligente: el usuario selecciona el `.xlsx` editado, la herramienta valida que el archivo es compatible (via `ColumnHash` de la hoja `_Meta`), calcula el diff campo a campo entre los valores del Excel y los valores actuales en el modelo Revit, muestra un preview Before/After con coloreado por estado (amarillo=modificado, rojo=no encontrado, verde=aplicado), y permite aplicar **selectivamente** solo las filas elegidas en una unica Transaction (un solo Ctrl-Z).

---

## 2. Epicas implementadas

| # | Epica | Descripcion |
|---|-------|-------------|
| P12-1 | Validacion de esquema | `ValidateXlsxSchema` lee la hoja `_Meta`, comprueba presencia de "Planos" y "_Meta" y que el `ColumnHash` coincide con el esquema actual |
| P12-2 | Calculo del diff | `ComputeDiff` compara los valores del .xlsx con el modelo Revit campo a campo y devuelve `SheetLinkDiffRow` con status Unchanged/Modified/Missing |
| P12-3 | Aplicacion selectiva | `ApplyDiff` aplica solo las filas con `IsSelected=true` y `Status==Modified` en una unica transaccion |
| P12-4 | Preview Before/After | DataGrid en Tab 2 con columnas "Numero (Antes→Despues)", "Nombre (Antes→Despues)", "# Cambios", coloreado por estado y tooltip con detalle de todos los campos |
| P12-5 | Panel de metadatos | Muestra fecha de exportacion, titulo del documento y numero de planos exportados tras validar el archivo |
| P12-6 | Filtros y seleccion | Checkbox "Solo filas con cambios" (activado por defecto), botones "Seleccionar modificados" y "Deseleccionar todos" |
| P12-7 | Resumen post-aplicacion | Panel verde/rojo con "N planos actualizados, M omitidos, K errores" tras aplicar |

---

## 3. Archivos creados / modificados

### Core (`e3gbimtools.DiTools.Core`)

| Archivo | Estado | Descripcion |
|---------|--------|-------------|
| `Models/SheetLink/SheetLinkDiffRow.cs` | **Nuevo** | Modelo con Before/After por campo, `DiffStatus` (Unchanged/Modified/Missing), `HasChanges`, `ChangedFieldCount`, `ApplyStatus` |
| `Models/SheetLink/XlsxValidationResult.cs` | **Nuevo** | Resultado de validacion: `IsValid`, `ErrorMessage`, `ExportDate`, `DocumentTitle`, `SheetsExported`, `ColumnHash` |
| `Abstractions/ISheetLinkService.cs` | **Modificado** | Añadidos `ValidateXlsxSchema`, `ComputeDiff`, `ApplyDiff` con docstrings completos |
| `Services/SheetLink/SheetLinkService.cs` | **Modificado** | Implementacion de los 3 nuevos metodos en secciones "Sprint 12" |

### UI (`e3gbimtools.DiTools.UI`)

| Archivo | Estado | Descripcion |
|---------|--------|-------------|
| `Views/SheetLink/SheetLinkWindow.xaml` | **Modificado** | Tab 2 completamente rediseñada: selector archivo, panel _Meta, barra de opciones diff, DataGrid diff, panel resumen |
| `Views/SheetLink/SheetLinkWindow.xaml.cs` | **Modificado** | `OnBrowseImport` (valida esquema), `OnComputeDiff` (calcula diff), `OnApply` (aplica diff); VMs `SheetLinkDiffRowVm` con `NumberDisplay`, `NameDisplay`, `ChangesTooltip`, `ApplyStatusCode` |

### Sin cambios

- `Models/SheetLink/SheetLinkRow.cs`
- `Models/SheetLink/SheetLinkImportResult.cs`
- `Models/SheetLink/SheetLinkExportOptions.cs`
- `Commands/SheetLinkCommand.cs`
- `RibbonBuilder.cs`
- `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` (SheetLink ya registrado en Sprint 11)

---

## 4. Flujo de usuario (Tab 2 — Importar con Diff)

```
[Tab "2. Importar / Aplicar cambios"]

  Paso 1 — Seleccionar y validar archivo:
  → [Examinar...]  → OpenFileDialog (.xlsx)
  → ValidateXlsxSchema: comprueba hojas "Planos" y "_Meta", valida ColumnHash
  → Si invalido: MessageBox con error → flujo abortado
  → Si valido: panel de metadatos visible (fecha exportacion | documento | N planos)
  → Boton "Calcular diff →" habilitado

  Paso 2 — Calcular diff:
  → [Calcular diff →]
  → ComputeDiff: lee .xlsx, lee modelo Revit, compara campo a campo
  → DataGrid aparece con filas coloreadas:
      - Amarillo (#FFF8E1): Modified — al menos un campo difiere
      - Rojo (#FFEBEE):    Missing  — ElementId no encontrado en el modelo
      - Blanco:            Unchanged — todos los campos coinciden
  → Filtro "Solo filas con cambios" activo por defecto (oculta Unchanged)
  → Filas Modified preseleccionadas (checkbox marcado); Missing no seleccionadas
  → Tooltip por fila: detalle de cada campo que cambia ("Numero: '01' → '01-A'")
  → Boton "Aplicar cambios seleccionados al modelo" habilitado

  Paso 3 — Aplicar:
  → [Aplicar cambios seleccionados al modelo]
  → Dialogo de confirmacion: "Se aplicaran N cambios. Ctrl-Z deshace todo."
  → ApplyDiff: Transaction unica "SheetLink — Aplicar diff"
  → Filas actualizadas: fondo verde (#E8F5E9), columna "Resultado" = "✓ Aplicado"
  → Filas con error: fondo rojo (#FFEBEE), columna "Resultado" = "⚠ Error"
  → Panel resumen: "N planos actualizados, M omitidos, K errores."
  → Ctrl-Z en Revit deshace toda la operacion
```

---

## 5. Modelo de datos — SheetLinkDiffRow

```csharp
// Por cada plano del .xlsx:
ElementId       // Id del ViewSheet en Revit (clave de busqueda)
Status          // Unchanged | Modified | Missing
IsSelected      // true para Modified (preseleccion automatica); false para el resto
HasChanges      // true si al menos un campo difiere
ChangedFieldCount // numero de campos que cambian

// Campos Before (= valor actual en Revit) / After (= valor en el Excel)
SheetNumber_Before / After
SheetName_Before / After
RevisionDate_Before / After
RevisionNumber_Before / After
RevisionDescription_Before / After
IssuedBy_Before / After
IssuedTo_Before / After
ApprovedBy_Before / After
DesignedBy_Before / After
CheckedBy_Before / After
DrawnBy_Before / After

// Estado post-aplicacion
ApplyStatus     // Pending | Updated | Skipped | Error
ApplyMessage    // mensaje de error si fallo
```

---

## 6. Validacion de esquema — ColumnHash

El `ColumnHash` (SHA-256 8 chars de los nombres de columna de `DataColumns`) actua como version del esquema de exportacion. Si el usuario importa un .xlsx exportado con una version anterior de SheetLink que tenia columnas diferentes, la validacion falla con un mensaje claro antes de intentar parsear nada.

---

## 7. Criterio de aceptacion — caso de prueba canonico

> "Editar numero, nombre y firmante de 3 planos en Excel y aplicar solo 2 de ellos"

1. Exportar planos (Tab 1) → abrir el .xlsx en Excel.
2. Editar `SheetNumber`, `SheetName` y `ApprovedBy` en 3 filas → guardar y cerrar Excel.
3. Tab 2 → Examinar → seleccionar el .xlsx editado.
4. Panel metadatos aparece con fecha y titulo de documento correctos.
5. Calcular diff → DataGrid muestra 3 filas en amarillo, las demas en blanco.
6. Desmarcar uno de los 3 checkboxes (deseleccionar 1 fila).
7. Aplicar → confirmar → 2 planos actualizados (verde), 1 omitido (gris).
8. Verificar en Revit que los 2 planos seleccionados tienen los nuevos datos.
9. Ctrl-Z en Revit → deshace los 2 cambios de una vez.
10. Verificar que el plano deseleccionado NO fue modificado.

---

## 8. Pendiente Angel

Compilar en Visual Studio (`Compilacion > Recompilar solucion`) y probar en Revit 2025:

1. **Abrir SheetLink**: e3gbimtools > DiTools > SheetLink — Tab 1 debe cargar los planos.
2. **Exportar**: seleccionar 5-10 planos → Exportar Excel → comprobar que abre con hoja "_Meta" oculta.
3. **Editar**: en Excel, cambiar `SheetNumber` en 2 planos y `ApprovedBy` en 1 → guardar.
4. **Tab 2 / Examinar**: seleccionar el .xlsx editado → verificar que aparece el panel de metadatos.
5. **Calcular diff**: pulsar "Calcular diff →" → verificar filas amarillas para los 3 planos editados.
6. **Seleccion parcial**: desmarcar 1 checkbox → pulsar "Aplicar" → confirmar.
7. **Resultado**: 2 filas verdes + 1 gris. Verificar en Revit los planos actualizados.
8. **Ctrl-Z**: deshace todo a la vez — planos vuelven a los valores originales.
9. **Archivo invalido**: importar un .xlsx cualquiera (no exportado por SheetLink) → debe salir aviso claro.

---

## 9. Why / How to apply

**Why:** Sprint 12 cierra el loop Export → Edit → Import con inspeccion explícita de los cambios antes de aplicar. Sin el diff, el usuario tendria que confiar a ciegas en que el Excel solo tiene los cambios que el queria; con el diff ve exactamente que va a cambiar y puede deseleccionar filas antes de confirmar.

**How to apply (Sprint 13):** El siguiente sprint es **SheetLink P3 — Google Sheets + sync bidireccional avanzado**: exportar directamente a Google Sheets, sincronizar cambios de vuelta al modelo, y soporte de sync incremental (solo planos modificados desde la ultima sync). Requiere cuenta de Google y OAuth — evaluar si Angel lo necesita o si SheetLink se da por completo con Sprints 11+12.
