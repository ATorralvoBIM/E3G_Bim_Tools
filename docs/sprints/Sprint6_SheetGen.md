# Sprint 6 — SheetGen

> Fase 2 — Modelo · Duración: 2 semanas · Dependencias: Sprint 4 (ViewManager) · Estado: **EN PREPARACIÓN**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 6 — SheetGen".
Prompt original: `Prompts_Sprints_DiTools.html` → sección "Sprint 6".

## 1. Objetivo

Crear múltiples **Drawing Sheets** a partir de title blocks del proyecto, importar y exportar listados desde/hacia Excel, definir y persistir **layouts** de colocación de vistas, gestionar **revisiones** en bulk y auditar el estado del juego de planos (huérfanas, duplicados, renumeración).

## 2. Épicas

1. **Creación masiva de sheets** — crear N sheets especificando número, nombre, title block y disciplina.
2. **Import/Export Excel** — leer un `.xlsx` con la lista de sheets y generarlos; exportar el estado actual a Excel.
3. **Layouts de colocación de vistas** — definir posiciones y escalas de vistas en la sheet, persistir como JSON reutilizable.
4. **Gestión de revisiones** — asignar/quitar revisiones a múltiples sheets en una sola transacción.
5. **Renumeración en bulk** — renumerar sheets siguiendo patrón prefijo + secuencia, respetando referencias.
6. **Auditoría** — detectar sheets sin vistas (huérfanas) y números duplicados.

## 3. Historias de usuario

| ID | Como usuario… | Quiero… | Para… |
|----|---------------|---------|-------|
| SG-1 | BIM Manager | crear N sheets de una vez especificando título, número y title block | generar el juego de planos inicial sin hacerlo uno a uno |
| SG-2 | BIM Manager | definir un layout (posiciones y escalas de vistas) y aplicarlo al generar las sheets | estandarizar la composición de planos del proyecto |
| SG-3 | BIM Manager | importar el listado de sheets desde Excel (Sheet Number, Sheet Name, Title Block, Discipline) | usar la planificación de entregas ya elaborada en Excel |
| SG-4 | BIM Manager | exportar el listado actual de sheets a Excel | auditar el estado del juego de planos fuera de Revit |
| SG-5 | BIM Manager | asignar o quitar revisiones a múltiples sheets en bulk | actualizar el estado de revisión de toda una entrega de golpe |
| SG-6 | BIM Manager | renumerar sheets siguiendo un patrón (prefijo + secuencia) | reorganizar el juego de planos sin romper referencias |
| SG-7 | BIM Manager | detectar sheets sin vistas (huérfanas) y sheets con número duplicado | mantener limpio el listado de planos |

## 4. Criterios de aceptación

- El import desde Excel **valida unicidad** de Sheet Number antes de crear ninguna sheet; si hay duplicados muestra lista de conflictos y aborta.
- Los layouts se **persisten como JSON** en `%APPDATA%\e3g\DiTools\SheetLayouts\` y se pueden reutilizar entre sesiones.
- La renumeración en bulk **respeta los Drawing List Schedules y Tagged elements** que referencian las sheets (Revit actualiza las referencias automáticamente al cambiar el parámetro nativo `Sheet Number`).
- Tests con un Excel sample de 50 sheets pasan en **< 30 s** en una máquina de desarrollo estándar.
- Toda operación que crea o modifica sheets se ejecuta en **una sola `Transaction`** descriptiva y es reversible con Ctrl-Z.

## 5. Tareas técnicas

### 5.1 Capa Core (`e3gbimtools.DiTools.Core`)

#### Modelos en `Models/Sheets/`

- [ ] `SheetDescriptor.cs` — `SheetNumber` (string), `SheetName` (string), `TitleBlockFamilyTypeName` (string), `Discipline` (string?), `LayoutId` (Guid?).
- [ ] `ViewPlacementSpec.cs` — `ViewName` (string), `XOffsetMm` (double), `YOffsetMm` (double), `ScaleOverride` (int?, null = mantener escala de la vista).
- [ ] `SheetLayoutDefinition.cs` — `Id` (Guid), `Name` (string), `Description` (string?), `Placements` (List\<ViewPlacementSpec\>).  Serializable a JSON.
- [ ] `SheetRevisionRequest.cs` — `SheetIds` (IList\<ElementId\>), `RevisionId` (ElementId), `Add` (bool — true = asignar, false = quitar).
- [ ] `SheetRenumberRequest.cs` — `SheetIds` (IList\<ElementId\>), `Prefix` (string), `Suffix` (string), `StartIndex` (int), `PadDigits` (int).
- [ ] `SheetGenResult.cs` — `CreatedSheetIds` (IList\<ElementId\>), `SkippedNumbers` (IList\<string\>), `Warnings` (IList\<string\>), `ElapsedMs` (long).
- [ ] `SheetAuditResult.cs` — `OrphanSheets` (IList\<SheetInfo\>), `DuplicateNumbers` (IList\<string\>).
- [ ] `SheetInfo.cs` — `ElementId`, `SheetNumber`, `SheetName`, `ViewCount` (int).

#### Abstracciones en `Abstractions/`

- [ ] `ISheetGenService.cs`:
  ```csharp
  Task<SheetGenResult>   BulkCreateAsync(Document doc, IList<SheetDescriptor> descriptors, SheetLayoutDefinition? layout, IProgress<int>? progress);
  SheetGenResult         ImportFromExcel(Document doc, string filePath, SheetLayoutDefinition? layout);
  void                   ExportToExcel(Document doc, string filePath);
  void                   ApplyRevisions(Document doc, SheetRevisionRequest request);
  void                   RenumberSheets(Document doc, SheetRenumberRequest request);
  SheetAuditResult       Audit(Document doc);
  IList<SheetLayoutDefinition> GetLayouts();
  void                   SaveLayout(SheetLayoutDefinition layout);
  void                   DeleteLayout(Guid layoutId);
  ```

#### Servicios en `Services/Sheets/`

- [ ] `SheetGenService.cs` — implementa `ISheetGenService`.  Usa `ITransactionService`, `IExcelService` y `ILoggerFactory`.
  - `BulkCreateAsync`: itera descriptors, resuelve `FamilySymbol` del title block por nombre, crea `ViewSheet.Create(doc, titleBlockTypeId)`, asigna `Sheet Number` y `Sheet Name` como parámetros de instancia.  Si `layout != null`, coloca las vistas con `Viewport.Create`.
  - `ImportFromExcel`: llama a `IExcelService.ReadSheet<SheetDescriptor>`, valida unicidad, llama a `BulkCreateAsync`.
  - `ExportToExcel`: recoge todos los `ViewSheet` del documento y los escribe con `IExcelService.WriteSheet<SheetInfo>`.
  - `ApplyRevisions`: en una sola transacción modifica el parámetro de revisión de cada sheet.
  - `RenumberSheets`: en una sola transacción renombra `Sheet Number` con `Prefix + (StartIndex + i).ToString("D{PadDigits}") + Suffix`.
  - `Audit`: recorre los ViewSheet y devuelve huérfanas (0 viewports) y números duplicados.
- [ ] `SheetLayoutRepository.cs` — carga/guarda JSON de `SheetLayoutDefinition` en `%APPDATA%\e3g\DiTools\SheetLayouts\`.  Inyectado en `SheetGenService`.

### 5.2 Comando + UI (`e3gbimtools.DiTools` + `e3gbimtools.DiTools.UI`)

- [ ] `Commands/SheetGenCommand.cs` — sigue el patrón de `ViewManagerCommand`: adjunta `UIApplication`, valida documento activo, resuelve `ISheetGenService` del contenedor, instancia `SheetGenWindow` y llama a `ShowDialog()`.
- [ ] `Views/SheetGen/SheetGenWindow.xaml` — ventana tabbed con 4 pestañas:

  **Pestaña 1 — Crear / Importar**
  - Grid de edición manual: columnas SheetNumber, SheetName, TitleBlock (ComboBox), Discipline.  Botón "+ Fila", "- Fila", "Importar desde Excel…", "Exportar a Excel…".
  - ComboBox "Layout" (lista de layouts guardados + "Sin layout").
  - Botón "Crear sheets" (progreso con `ProgressBar` + mensaje de estado).

  **Pestaña 2 — Layouts**
  - Lista de layouts (Nombre, Descripción, Nº vistas).  Botones "Nuevo", "Editar", "Eliminar".
  - Editor de layout inline: grid con columnas ViewName, X (mm), Y (mm), Escala.  Botón "+ Vista", "- Vista".
  - Botón "Guardar layout".

  **Pestaña 3 — Revisiones**
  - Lista multi-selección de sheets del proyecto.  ComboBox de revisiones disponibles.
  - Radio buttons "Asignar" / "Quitar".  Botón "Aplicar".

  **Pestaña 4 — Auditoría / Renumerar**
  - Sección "Auditoría": botón "Auditar juego de planos".  Dos listas de resultados: Huérfanas | Duplicados.
  - Sección "Renumeración": TextBox Prefijo, TextBox Sufijo, NumericUpDown Inicio, NumericUpDown Dígitos.  Lista de sheets a renumerar.  Botón "Previsualizar" (muestra mapeo antes/después), "Aplicar".

- [ ] `Views/SheetGen/SheetGenWindow.xaml.cs` — code-behind mínimo; toda la lógica en el ViewModel.
- [ ] `Views/SheetGen/SheetGenViewModel.cs` — hereda de `ViewModelBase` (si existe), expone propiedades observables para todas las pestañas.

### 5.3 Ribbon

- [ ] `Ribbon/RibbonBuilder.cs`:
  - Añadir llamada `AddSheetGenButton(panelDiTools)` en `Build()`, después de `AddSectionBoxerButton`.
  - Implementar:
    ```csharp
    private static void AddSheetGenButton(RibbonPanel panel)
    {
        var data = new PushButtonData(
            "SheetGen", "SheetGen",
            Assembly.GetExecutingAssembly().Location,
            typeof(SheetGenCommand).FullName);
        if (panel.AddItem(data) is not PushButton btn) return;
        btn.ToolTip = "Generacion masiva de Drawing Sheets, layouts y gestion de revisiones.";
        btn.LongDescription = "SheetGen replica las funciones esenciales de DiRootsOne SheetGen: " +
            "crea sheets en bulk desde una grilla o un Excel, aplica layouts de colocacion " +
            "de vistas, gestiona revisiones en lote y audita el juego de planos " +
            "(huerfanas, duplicados, renumeracion con patron).";
        var icon32 = TryLoadIcon("icono_panel_32.png");
        var icon16 = TryLoadIcon("icono_small_16.png");
        if (icon32 is not null) btn.LargeImage = icon32;
        if (icon16 is not null) btn.Image = icon16;
    }
    ```

### 5.4 Registro DI

- [ ] `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` — añadir bloque al final de `DiToolsCoreBuilder.Build`:
  ```csharp
  // SheetGen (Sprint 6)
  var sheetLayoutRepo = new SheetLayoutRepository(loggerFactory);
  container.Register<ISheetGenService>(
      new SheetGenService(txService, excelService, sheetLayoutRepo, loggerFactory));
  ```

### 5.5 Tests (`Tests/e3gbimtools.DiTools.Tests`)

- [ ] `Sheets/SheetDescriptorTests.cs` — validaciones: SheetNumber no vacío, TitleBlockFamilyTypeName no vacío.
- [ ] `Sheets/SheetRenumberTests.cs` — verifica patrón de renumeración (prefijo, sufijo, padding, índice de inicio).
- [ ] `Sheets/SheetLayoutRepositoryTests.cs` — round-trip JSON: guardar un layout y releerlo devuelve el mismo objeto.
- [ ] `Sheets/SheetAuditTests.cs` — mock con lista de SheetInfo, verifica detección de huérfanas y duplicados.
- [ ] `DependencyInjection/ServiceContainerTests.cs` — añadir assert para `ISheetGenService`.

## 6. Riesgos

| # | Riesgo | Mitigación |
|---|--------|------------|
| R1 | Title block no presente en el proyecto al importar desde Excel | `SheetGenService` busca el `FamilySymbol` por nombre antes de crear nada; si falta, añade la sheet a `SkippedNumbers` con aviso "title block no cargado" y continúa con las demás |
| R2 | Colocación de vistas con `Viewport.Create` requiere que la vista no esté ya en otra sheet | Validar con `view.Sheet` antes de colocar; si ya está colocada, omitir y registrar warning |
| R3 | `Sheet Number` es un parámetro built-in de Revit con restricciones de unicidad | Recoger todos los `Sheet Number` existentes antes de la transacción y rechazar duplicados dentro del batch de import |
| R4 | `Viewport.Create` necesita coordenadas en pies (unidades internas de Revit) | Convertir `XOffsetMm` / `YOffsetMm` con `UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters)` |
| R5 | JSON de layouts en `%APPDATA%` puede no existir en primera ejecución | `SheetLayoutRepository` crea el directorio si no existe (`Directory.CreateDirectory`) |
| R6 | Operación de 50 sheets puede ser lenta si cada sheet crea su propia transacción | Toda la creación masiva va en una sola `Transaction` con progreso via `IProgress<int>` |

## 7. Definición de hecho (DoD)

- [ ] Código compila para `RevitVersion=2025` sin warnings nuevos.
- [ ] Botón SheetGen aparece en el panel DiTools del ribbon y abre la ventana.
- [ ] Creación manual de 5 sheets desde la pestaña Crear funciona en modelo sample.
- [ ] Import desde Excel de 50 sheets pasa en < 30 s (medido con `Stopwatch`).
- [ ] Export a Excel genera un `.xlsx` con las columnas correctas.
- [ ] Auditoría detecta correctamente huérfanas y duplicados en modelo sample.
- [ ] Renumeración cambia los Sheet Numbers y los Drawing List Schedules se actualizan.
- [ ] Tests xUnit pasan (`dotnet test`).
- [ ] `docs/ARCHITECTURE.md` actualizado con sección SheetGen.
- [ ] README actualizado: ✓ junto a SheetGen en la lista de módulos.
- [ ] PR con: capturas (ventana, sheets creadas en Revit, Excel generado), lista de archivos, checklist DoD.

## 8. Plan de ejecución sugerido (10 días hábiles)

| Día | Tarea | Entregable |
|-----|-------|------------|
| 1 | Scaffolding: modelos, abstracciones, `SheetGenService` vacío, command, window (4 tabs vacíos), ribbon, DI | Compila y abre ventana vacía con 4 pestañas |
| 2 | `SheetGenService.BulkCreate` + `SheetLayoutRepository` (JSON round-trip) + tests de models | Creación manual de sheets funcional |
| 3 | Pestaña Crear: grid editable + ComboBox title blocks + ComboBox layouts + botón "Crear" con progress | UI pestaña 1 completa |
| 4 | `IExcelService.ReadSheet<SheetDescriptor>` adaptado + `ImportFromExcel` + validación duplicados | Import desde Excel funciona |
| 5 | `ExportToExcel` + botones Import/Export en UI | Export a Excel funciona |
| 6 | Pestaña Layouts: lista + editor inline + guardar/eliminar | Layouts persistidos en JSON |
| 7 | `ApplyRevisions` + pestaña Revisiones (multi-selección sheets + combobox revisiones) | Revisiones en bulk funcional |
| 8 | `RenumberSheets` + sección Auditoría (`Audit`) + pestaña Auditoría/Renumerar con preview | Renumeración y auditoría funcionales |
| 9 | Colocación de vistas con `Viewport.Create` cuando layout está seleccionado | Layouts aplicados al crear |
| 10 | QA en modelo sample · tests · docs · PR | PR mergeable |

## 9. Archivos que crea / modifica este sprint

**Nuevos:**
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetDescriptor.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/ViewPlacementSpec.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetLayoutDefinition.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetRevisionRequest.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetRenumberRequest.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetGenResult.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetAuditResult.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sheets/SheetInfo.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/ISheetGenService.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sheets/SheetGenService.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sheets/SheetLayoutRepository.cs`
- `src/e3gbimtools.DiTools/Commands/SheetGenCommand.cs`
- `src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenWindow.xaml`
- `src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenWindow.xaml.cs`
- `src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenViewModel.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sheets/SheetDescriptorTests.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sheets/SheetRenumberTests.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sheets/SheetLayoutRepositoryTests.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sheets/SheetAuditTests.cs`

**Modificados:**
- `src/e3gbimtools.DiTools/Ribbon/RibbonBuilder.cs` (+1 botón `AddSheetGenButton`)
- `src/e3gbimtools.DiTools.Core/DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` (+1 servicio SheetGen)
- `Tests/e3gbimtools.DiTools.Tests/DependencyInjection/ServiceContainerTests.cs` (assert ISheetGenService)
- `docs/ARCHITECTURE.md`
- `README.md`

## 10. Cómo empezar (para el desarrollador o agente)

1. Verificar que la rama `feature/sprint5-sectionboxer` (o `main` si ya mergeada) compila limpio.
2. Crear rama `feature/sprint6-sheetgen`.
3. Leer este documento y la sección Sprint 6 del Roadmap maestro (`Roadmap_DiTools_e3gbimtools.html`).
4. Aplicar el scaffolding del día 1 (§9 archivos nuevos).
5. Ir cerrando tareas por día siguiendo §8.
