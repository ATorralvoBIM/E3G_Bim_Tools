# Sprint 4 — ViewManager

> Fase 1 — Núcleo · Duración: 2 semanas · Dependencias: Sprint 0 · Estado: **EN CURSO**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 4 — ViewManager".
Prompt original: `Prompts_Sprints_DiTools.html` → sección "Sprint 4".
Este documento expande el roadmap con el plan de ejecución concreto en el repo.

## 1. Objetivo

Gestión centralizada de **View Templates** y **View Ranges** con edición masiva, comparación entre plantillas y aplicación a múltiples vistas en una sola operación.

## 2. Épicas

1. **Listado de View Templates** — inventario por nombre, tipo de vista soportado, vistas asociadas y huérfanas.
2. **Aplicación masiva de plantillas** — aplicar una plantilla a N vistas seleccionadas en una sola `Transaction`.
3. **Editor de View Range multi-vista** — modificar top / cut plane / bottom / view depth en lote, con validación.
4. **Diff entre View Templates** — comparar dos plantillas lado a lado, resaltando diferencias en VG Overrides, escala, detail level, filtros.
5. **Detector de plantillas huérfanas** — plantillas sin vistas asociadas, con opción de borrado seguro.
6. **Export matriz Vista × Template a Excel** — usando `IExcelService` existente (ClosedXML).

## 3. Historias de usuario

| ID | Como usuario… | Quiero… | Para… |
|----|---------------|---------|-------|
| VM-1 | BIM Manager | ver todas las View Templates del proyecto | tener inventario global |
| VM-2 | BIM Manager | aplicar una plantilla a N vistas en una operación | acelerar normalización |
| VM-3 | Modelador | editar View Range de varias vistas a la vez | homogeneizar plantas |
| VM-4 | BIM Manager | comparar 2 plantillas lado a lado | auditar configuraciones |
| VM-5 | BIM Manager | duplicar / renombrar / eliminar plantillas comprobando uso previo | evitar romper vistas |
| VM-6 | BIM Manager | detectar plantillas huérfanas | limpiar el modelo |
| VM-7 | BIM Manager | exportar matriz Vista × Template a Excel | reporting fuera de Revit |

## 4. Criterios de aceptación

- La aplicación masiva de plantilla agrupa todos los cambios en una **única `Transaction`** descriptiva (`"ViewManager: aplicar plantilla 'X' a N vistas"`).
- La comparación de plantillas resalta diferencias en: VG Overrides por categoría, escala, detail level, discipline, filtros aplicados, parámetros gráficos.
- Los cambios en View Range se validan (`top > cut plane > bottom`, view depth ≤ bottom) antes de aplicar; si fallan se cancela la `Transaction`.
- Tests sobre modelo sample con 100 vistas pasan en **< 5 s**.
- Botón "ViewManager" visible en el panel **DiTools** de la pestaña **e3gbimtools** del ribbon.
- La ventana usa estilo consistente con `CopyLevelsWindow`, `OneFilterWindow`, `ParaManagerWindow`.

## 5. Tareas técnicas

### 5.1 Capa Core (`e3gbimtools.DiTools.Core`)

- [ ] Modelos en `Models/Views/`:
  - `ViewTemplateInfo` (Id, Name, ViewType, IsOrphan, AssociatedViewIds, UsageCount).
  - `ViewRangeInfo` (TopLevelId, TopOffset, CutPlaneOffset, BottomLevelId, BottomOffset, ViewDepthLevelId, ViewDepthOffset).
  - `ViewTemplateDiff` (TemplateAId, TemplateBId, FieldDiffs: lista de `ViewTemplateFieldDiff`).
  - `ViewTemplateApplyResult` (Success, AppliedCount, FailedCount, Warnings).
- [ ] Abstracciones en `Abstractions/`:
  - `IViewTemplateService` — lista, aplica, duplica, renombra, elimina, detecta huérfanas, exporta matriz.
  - `IViewRangeService` — lee, valida y aplica View Range a N vistas.
  - `IViewTemplateComparer` — calcula diff entre 2 templates.
  - `IViewRangeCompat` (capa de compatibilidad, ver 5.2).
- [ ] Servicios en `Services/Views/`:
  - `ViewTemplateService` — usa `ITransactionService` + `ILogger`.
  - `ViewRangeService` — usa `IViewRangeCompat`.
  - `ViewTemplateComparer` — puro, sin transacciones.

### 5.2 Capa de compatibilidad multi-Revit (riesgo principal)

> La API de `PlanViewRange` y los métodos `GetViewRange` / `SetViewRange` cambiaron entre Revit 2022, 2023, 2024 y 2025/2026. Aislamos las diferencias detrás de una interfaz.

- [ ] `Abstractions/IViewRangeCompat.cs` — contrato versionado.
- [ ] `Services/Views/Compat/ViewRangeCompat_Default.cs` — implementación común.
- [ ] Si hay divergencia: usar `#if REVIT2022 ... #elif REVIT2025 ...` (las constantes ya existen en `Directory.Build.props`).

### 5.3 Comando + UI (`e3gbimtools.DiTools` + `e3gbimtools.DiTools.UI`)

- [ ] `Commands/ViewManagerCommand.cs` siguiendo el patrón de `ParaManagerCommand`.
- [ ] `Views/ViewManager/ViewManagerWindow.xaml` con 4 pestañas:
  1. **Listado** — DataGrid de plantillas, columnas (Nombre, Tipo, Vistas asociadas, Huérfana).
  2. **Aplicar a vistas** — ComboBox plantilla + ListBox vistas multi-selección + botón Aplicar.
  3. **View Range** — ComboBox vista filtro + editor numérico top/cut/bottom/depth + multi-selección de vistas destino.
  4. **Diff** — 2 ComboBox de plantilla + DataGrid de diferencias.
- [ ] Botón "Aplicar a huérfanas" en pestaña Listado.
- [ ] Botón "Exportar matriz a Excel" (reusa `IExcelService`).

### 5.4 Ribbon

- [ ] Actualizar `Ribbon/RibbonBuilder.cs`:
  - Añadir `AddViewManagerButton(panelDiTools)`.
  - Reusar `icono_panel_32.png` (placeholder).

### 5.5 Tests (`Tests/e3gbimtools.DiTools.Tests`)

> El proyecto de tests está actualmente vacío. Sprint 4 lo activa.

- [ ] Añadir paquetes en `Directory.Packages.props`: `xunit`, `xunit.runner.visualstudio`, `FluentAssertions`, `Moq`.
- [ ] Activar `<PackageReference>` en `.csproj` de Tests.
- [ ] Test placeholder `ViewTemplateServiceTests.cs` — verifica que el contenedor DI resuelve `IViewTemplateService`.
- [ ] Test `ViewTemplateComparerTests.cs` — diff de 2 templates fake.
- [ ] (Si hay tiempo) Mock de modelo Revit con 100 vistas para perfilar < 5 s.

### 5.6 Registro DI

- [ ] Actualizar `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` con el bloque `// ViewManager (Sprint 4)` registrando los 3 servicios + el adapter de compatibilidad.

## 6. Riesgos

| # | Riesgo | Mitigación |
|---|--------|------------|
| R1 | Diferencias API View Range entre Revit 2022 y 2026 | Capa `IViewRangeCompat` con implementación por versión |
| R2 | Aplicar plantilla rompe vistas con dependencias filtradas | Pre-flight check + transacción con `RollBack` en error |
| R3 | Diff de VG Overrides extenso (50+ categorías) | UI con expander por sección, no aplanar |
| R4 | El proyecto Tests no se restaura al añadir xUnit (visto en Sprint 0) | Comprobar `nuget.config` y `Directory.Packages.props` antes de activar tests |

## 7. Definición de hecho (DoD)

- [ ] Código compila para `RevitVersion=2025` sin warnings nuevos.
- [ ] Botón ViewManager aparece en ribbon y abre la ventana.
- [ ] Las 4 pestañas funcionan en modelo sample.
- [ ] Tests xUnit pasan en local (`dotnet test`).
- [ ] `docs/ARCHITECTURE.md` actualizado con bloque ViewManager.
- [ ] README actualizado: ✓ junto a ViewManager en la lista de módulos.
- [ ] PR description con: capturas, lista de archivos nuevos, breaking changes (si hay), checklist DoD.

## 8. Plan de ejecución sugerido (10 días hábiles)

| Día | Tarea | Entregable |
|-----|-------|------------|
| 1 | Scaffolding (abstracciones, modelos, command vacío, ventana vacía con tabs, ribbon, DI) | Compila y abre ventana vacía |
| 2 | `ViewTemplateService.ListAll` + pestaña Listado funcional | DataGrid muestra plantillas |
| 3 | `IViewRangeCompat` + `ViewRangeService.GetViewRange` | Lectura de View Range OK en 2022/2025 |
| 4 | Aplicación masiva (épica 2) + Transaction única | Aplica a N vistas |
| 5 | Editor View Range multi-vista + validación | Bloque editor funcional |
| 6 | Diff entre 2 templates (épica 4) | DataGrid de diferencias |
| 7 | Huérfanas + duplicar/renombrar/eliminar | Botones operativos |
| 8 | Export matriz Excel + tests xUnit base | xUnit verde |
| 9 | Pulido UI, FriendlyErrorDialog, logging | UX consistente con ParaManager |
| 10 | QA en modelo sample + docs + PR | PR mergeable |

## 9. Archivos que crea / modifica este sprint

**Nuevos:**
- `src/e3gbimtools.DiTools.Core/Abstractions/IViewTemplateService.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/IViewRangeService.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/IViewTemplateComparer.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/IViewRangeCompat.cs`
- `src/e3gbimtools.DiTools.Core/Models/Views/ViewTemplateInfo.cs`
- `src/e3gbimtools.DiTools.Core/Models/Views/ViewRangeInfo.cs`
- `src/e3gbimtools.DiTools.Core/Models/Views/ViewTemplateDiff.cs`
- `src/e3gbimtools.DiTools.Core/Models/Views/ViewTemplateApplyResult.cs`
- `src/e3gbimtools.DiTools.Core/Services/Views/ViewTemplateService.cs`
- `src/e3gbimtools.DiTools.Core/Services/Views/ViewRangeService.cs`
- `src/e3gbimtools.DiTools.Core/Services/Views/ViewTemplateComparer.cs`
- `src/e3gbimtools.DiTools.Core/Services/Views/Compat/ViewRangeCompat_Default.cs`
- `src/e3gbimtools.DiTools/Commands/ViewManagerCommand.cs`
- `src/e3gbimtools.DiTools.UI/Views/ViewManager/ViewManagerWindow.xaml`
- `src/e3gbimtools.DiTools.UI/Views/ViewManager/ViewManagerWindow.xaml.cs`
- `Tests/e3gbimtools.DiTools.Tests/ViewTemplateServiceTests.cs`
- `Tests/e3gbimtools.DiTools.Tests/ViewTemplateComparerTests.cs`

**Modificados:**
- `src/e3gbimtools.DiTools/Ribbon/RibbonBuilder.cs` (+1 botón)
- `src/e3gbimtools.DiTools.Core/DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` (+3 servicios)
- `Tests/e3gbimtools.DiTools.Tests/e3gbimtools.DiTools.Tests.csproj` (xUnit, FluentAssertions, Moq)
- `Directory.Packages.props` (versiones xUnit + Moq)
- `docs/ARCHITECTURE.md` (sección ViewManager al final)
- `README.md` (✓ junto a ViewManager)

## 10. Cómo empezar (para el desarrollador o agente)

1. Verificar que `dotnet build /p:RevitVersion=2025 -c Debug` compila limpio en `main`.
2. Crear rama `feature/sprint4-viewmanager`.
3. Leer este documento y la sección Sprint 4 del Roadmap maestro.
4. Aplicar el scaffolding (ver §9 lista de archivos nuevos).
5. Ir cerrando tareas por día siguiendo §8.
