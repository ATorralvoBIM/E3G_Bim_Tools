# PR Checklist — Sprint 6: SheetGen

> Rama: `feature/sprint6-sheetgen` → `main`

## 1. Build

- [ ] `dotnet restore e3gbimtools.sln` (descarga ClosedXML 0.102.3)
- [ ] `dotnet build /p:RevitVersion=2025 -c Release` sin warnings nuevos

## 2. Tests unitarios

Ejecutar desde la raíz:

```powershell
dotnet test Tests/e3gbimtools.DiTools.Tests/ --logger "console;verbosity=normal"
```

Tests nuevos que deben pasar:

- [ ] `Sheets/SheetDescriptorTests` — 5 casos (validación de campos)
- [ ] `Sheets/SheetRenumberTests` — 8 casos (patrón de numeración)
- [ ] `Sheets/SheetLayoutRepositoryTests` — 5 casos (round-trip JSON)
- [ ] `Sheets/SheetAuditTests` — 6 casos (detección huérfanas y duplicados)
- [ ] `DependencyInjection/ServiceContainerTests.Build_registra_los_servicios_del_Sprint6`

## 3. QA en modelo sample (requiere Revit 2025)

### 3.1 Arranque

- [ ] Abrir un modelo con al menos 1 title block cargado
- [ ] Verificar que el botón **SheetGen** aparece en el panel DiTools del ribbon
- [ ] Abrir la ventana — las 4 pestañas se renderizan sin errores

### 3.2 Pestaña Crear / Importar

- [ ] Añadir 5 filas manualmente (número, nombre, title block)
- [ ] Pulsar "Crear sheets" → 5 sheets aparecen en el Project Browser
- [ ] Ctrl-Z deshace la creación de las 5 sheets en un solo paso
- [ ] Intentar crear con un Sheet Number duplicado → aparece el aviso y se omite esa fila
- [ ] Preparar un Excel con 50 filas (columnas: SheetNumber, SheetName, TitleBlockFamilyTypeName, Discipline)
- [ ] Importar desde Excel → se crean 50 sheets en < 30 s
- [ ] Exportar a Excel → el archivo .xlsx contiene las sheets del proyecto con las columnas correctas

### 3.3 Pestaña Layouts

- [ ] Crear un layout nuevo, añadir 2 vistas con offset X/Y
- [ ] Guardar → el archivo JSON aparece en `%APPDATA%\e3g\DiTools\SheetLayouts\`
- [ ] Cerrar y reabrir Revit → el layout sigue disponible
- [ ] Crear 3 sheets usando el layout → las vistas se colocan en la posición indicada
- [ ] Eliminar el layout → desaparece de la lista

### 3.4 Pestaña Revisiones

- [ ] Seleccionar 10 sheets y asignar una revisión → el revision tag aparece en el title block
- [ ] Quitar la revisión de las mismas sheets → revision tag desaparece
- [ ] Ctrl-Z en cada acción la deshace correctamente

### 3.5 Pestaña Auditoría / Renumerar

- [ ] Pulsar "Auditar" con sheets sin vistas → aparecen en la lista Huérfanas
- [ ] Pulsar "Auditar" sin problemas → mensaje "Juego de planos limpio"
- [ ] Seleccionar 5 sheets, definir patrón `A-` + 2 dígitos, pulsar "Previsualizar" → tabla con números antes/después
- [ ] "Aplicar" → los Sheet Numbers cambian y los Drawing List Schedules se actualizan
- [ ] Ctrl-Z → renumeración deshecha

## 4. Definición de hecho (DoD)

- [ ] Código compila sin warnings nuevos para RevitVersion=2025
- [ ] Botón SheetGen aparece en ribbon y abre la ventana
- [ ] Creación manual de 5 sheets funciona en modelo sample
- [ ] Import desde Excel de 50 sheets pasa en < 30 s
- [ ] Export a Excel genera columnas correctas
- [ ] Auditoría detecta huérfanas y duplicados correctamente
- [ ] Renumeración actualiza Sheet Numbers
- [ ] Tests xUnit pasan (`dotnet test`)
- [ ] `docs/ARCHITECTURE.md` actualizado con sección SheetGen ✓
- [ ] `README.md` actualizado con ✓ SheetGen ✓
- [ ] PR con capturas (ventana, sheets en Revit, Excel generado)

## 5. Archivos del PR

**Nuevos (22):**
```
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetDescriptor.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/ViewPlacementSpec.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetLayoutDefinition.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetRevisionRequest.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetRenumberRequest.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetGenResult.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetAuditResult.cs
src/e3gbimtools.DiTools.Core/Models/Sheets/SheetInfo.cs
src/e3gbimtools.DiTools.Core/Abstractions/ISheetGenService.cs
src/e3gbimtools.DiTools.Core/Services/Sheets/SheetGenService.cs
src/e3gbimtools.DiTools.Core/Services/Sheets/SheetLayoutRepository.cs
src/e3gbimtools.DiTools/Commands/SheetGenCommand.cs
src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenWindow.xaml
src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenWindow.xaml.cs
src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenViewModel.cs
Tests/e3gbimtools.DiTools.Tests/Sheets/SheetDescriptorTests.cs
Tests/e3gbimtools.DiTools.Tests/Sheets/SheetRenumberTests.cs
Tests/e3gbimtools.DiTools.Tests/Sheets/SheetLayoutRepositoryTests.cs
Tests/e3gbimtools.DiTools.Tests/Sheets/SheetAuditTests.cs
docs/sprints/Sprint6_SheetGen.md
docs/sprints/PR_CHECKLIST_Sprint6.md  ← este archivo
```

**Modificados (6):**
```
src/e3gbimtools.DiTools.Core/e3gbimtools.DiTools.Core.csproj  (+ClosedXML)
src/e3gbimtools.DiTools.Core/Services/ExcelService.cs          (implementación completa)
src/e3gbimtools.DiTools/Ribbon/RibbonBuilder.cs                (+AddSheetGenButton)
src/e3gbimtools.DiTools.Core/DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs (+ISheetGenService)
Tests/e3gbimtools.DiTools.Tests/DependencyInjection/ServiceContainerTests.cs (+Sprint6 assert)
docs/ARCHITECTURE.md                                            (+sección SheetGen)
README.md                                                       (+✓ SheetGen)
```

## 6. Próximo sprint

**Sprint 7 — FamilyReviser** (depende de Sprint 2 — OneFilter)
- Renombrado masivo de familias (prefijo/sufijo, find/replace)
- Export organizado a disco con reglas custom `{Categoria}/{Disciplina}/{Familia}.rfa`
- Gestión de worksets: crear, renombrar, asignar elementos por categoría/filtro
