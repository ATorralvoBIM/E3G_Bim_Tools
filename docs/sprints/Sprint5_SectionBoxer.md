# Sprint 5 — SectionBoxer

> Fase 2 — Modelo · Duración: 2 semanas · Dependencias: Sprint 0, Sprint 2 (OneFilter) · Estado: **EN PREPARACIÓN**

Fuente maestra: `Roadmap_DiTools_e3gbimtools.html` → sección "Sprint 5 — SectionBoxer".
Prompt original: `Prompts_Sprints_DiTools.html` → sección "Sprint 5".

## 1. Objetivo

Generar **Section Boxes 3D** a partir de cuatro fuentes posibles (selección, habitación/espacio, nivel, filtro guardado de OneFilter), con padding configurable, alineación al proyecto o a la vista, soporte opcional de modelos vinculados y creación de vistas 3D dedicadas con el section box aplicado. Incluir reset/clear de la vista activa.

## 2. Épicas

1. **Origen unificado** — abstracción `ISectionBoxSource` con 4 implementaciones (selección, habitación, nivel, filtro guardado).
2. **Cálculo del bounding** — bounding mínimo de los elementos del origen + padding configurable; opción "incluir linked models".
3. **Aplicación a vista** — aplicar section box a vista 3D existente, o crear una nueva vista 3D dedicada.
4. **Reset / clear** — restaurar la vista al estado previo sin section box.
5. **Alineación** — section box alineado al sistema de coordenadas del proyecto o al de la vista.

## 3. Historias de usuario

| ID | Como usuario… | Quiero… | Para… |
|----|---------------|---------|-------|
| SB-1 | Modelador | seleccionar elementos y generar un section box con padding | aislar visualmente partes del modelo |
| SB-2 | Modelador | generar section box desde habitación/espacio | revisar instalaciones de un cuarto |
| SB-3 | BIM Manager | generar section box por nivel (rango de elevaciones) | revisar una planta entera |
| SB-4 | BIM Manager | reutilizar un filtro guardado de OneFilter como fuente | acelerar workflows recurrentes |
| SB-5 | Modelador | crear vista 3D dedicada que conserve el section box | navegar entre zonas sin perder el corte |
| SB-6 | Modelador | limpiar el section box de la vista activa | volver al estado sin corte |
| SB-7 | BIM Manager | alinear section box al proyecto o a la vista | cortes ortogonales coherentes |
| SB-8 | Modelador | incluir o no linked models en el cálculo | trabajar con coordinación |

## 4. Criterios de aceptación

- El bounding box generado es el **mínimo** que envuelve todos los elementos del origen aplicando el padding configurado.
- Funciona en vistas 3D **ortogonales y en perspectiva**.
- El reset deja la vista en su **estado previo** (sin section box), no en su default original — guardamos el snapshot antes de aplicar.
- La opción "incluir linked models" es **explícita** en el diálogo y, cuando se activa, incorpora bounding boxes de instancias de RevitLinkInstance al cálculo.
- Aplicar el section box va en **una sola `Transaction`** descriptiva (`"SectionBoxer: aplicar a vista 'X'"`).
- Botón "SectionBoxer" visible en el panel **DiTools** de la pestaña **e3gbimtools** del ribbon.
- Estilo de la ventana consistente con `CopyLevelsWindow`, `OneFilterWindow`, `ParaManagerWindow`, `ViewManagerWindow`.

## 5. Tareas técnicas

### 5.1 Capa Core (`e3gbimtools.DiTools.Core`)

- [ ] Modelos en `Models/Sections/`:
  - `SectionBoxSourceKind` (enum: `Selection`, `Room`, `Level`, `SavedFilter`).
  - `SectionBoxRequest` (Kind, ElementIds | RoomId | LevelId | FilterTemplateId, Padding, IncludeLinks, Alignment).
  - `SectionBoxAlignment` (enum: `Project`, `View`).
  - `SectionBoxResult` (Success, ViewId, AppliedBox: `BoundingBoxXYZ`, Warnings).
- [ ] Abstracciones en `Abstractions/`:
  - `ISectionBoxSource` — `IList<Element> Resolve(Document doc)` (estrategia por origen).
  - `ISectionBoxService` — `Apply(Document, SectionBoxRequest, View3D? target) → SectionBoxResult`; `Clear(View3D)`; `CreateDedicatedView(Document, SectionBoxRequest)`.
  - `IBoundingBoxCalculator` — `BoundingBoxXYZ Compute(IList<Element>, double padding, bool includeLinks, SectionBoxAlignment)`.
- [ ] Servicios en `Services/Sections/`:
  - `SectionBoxService` — orquesta, usa `ITransactionService` + `IBoundingBoxCalculator`.
  - `BoundingBoxCalculator` — implementa el cálculo puro.
  - `Sources/SelectionSource.cs`, `Sources/RoomSource.cs`, `Sources/LevelSource.cs`, `Sources/SavedFilterSource.cs` (reusa `IFilterTemplateService` del Sprint 2).
- [ ] **Snapshot para Reset:** mantenemos un diccionario `Dictionary<ElementId, BoundingBoxXYZ?>` en memoria por sesión del ViewManager para devolver al estado previo. No persistimos en disco.

### 5.2 Comando + UI (`e3gbimtools.DiTools` + `e3gbimtools.DiTools.UI`)

- [ ] `Commands/SectionBoxerCommand.cs` siguiendo el patrón de `ViewManagerCommand`.
- [ ] `Views/SectionBoxer/SectionBoxerWindow.xaml` con 1 sola página (no tabs — flujo lineal):
  - Selector de origen (radio buttons: Selección / Habitación / Nivel / Filtro guardado).
  - Editor del padding (TextBox numérico en metros).
  - ComboBox de alineación (Proyecto / Vista).
  - CheckBox "Incluir modelos vinculados".
  - CheckBox "Crear vista 3D dedicada" + TextBox nombre vista.
  - Botones "Aplicar", "Reset vista activa", "Cerrar".
- [ ] Cuando el origen es "Selección" usa la `ISelectionService` actual; cuando es "Habitación" abre un picker dedicado.

### 5.3 Ribbon

- [ ] `Ribbon/RibbonBuilder.cs`:
  - Añadir `AddSectionBoxerButton(panelDiTools)`. Reusa `icono_panel_32.png`.

### 5.4 Registro DI

- [ ] Actualizar `DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` con bloque `// SectionBoxer (Sprint 5)` registrando:
  - `IBoundingBoxCalculator` → `BoundingBoxCalculator`.
  - `ISectionBoxService` → `SectionBoxService(txService, calculator, loggerFactory)`.

### 5.5 Tests (`Tests/e3gbimtools.DiTools.Tests`)

- [ ] `Sections/BoundingBoxCalculatorTests.cs` — verifica padding y `min == 0` cuando lista vacía.
- [ ] `Sections/SectionBoxRequestTests.cs` — validaciones del request (padding ≥ 0, IDs no nulos, etc.).
- [ ] `DependencyInjection/ServiceContainerTests.cs` — añadir asserts para `ISectionBoxService` y `IBoundingBoxCalculator`.

## 6. Riesgos

| # | Riesgo | Mitigación |
|---|--------|------------|
| R1 | Linked models no contribuyen al bounding box por defecto | Opción explícita en UI + recorrer `RevitLinkInstance` y transformar el bounding al CS del host |
| R2 | Vistas 3D en perspectiva tienen API distinta para section box | Comprobar `View3D.IsPerspective`; aplicar `SetSectionBox` por igual (la API es la misma desde Revit 2022) |
| R3 | El "estado previo" para Reset requiere persistencia entre sesiones | MVP: solo en memoria. Sprint 5b: persistir en `extensibleStorage` por vista |
| R4 | El origen "Filtro guardado" depende del repositorio de OneFilter (Sprint 2) — comprobar que `IFilterTemplateService` expone el método de listado | Si no expone, añadir método en Sprint 5 sin romper compatibilidad |
| R5 | Padding en metros: confusion con unidades internas de Revit (pies) | Conversión explícita `UnitUtils.ConvertToInternalUnits(padding, UnitTypeId.Meters)` |

## 7. Definición de hecho (DoD)

- [ ] Código compila para `RevitVersion=2025` sin warnings nuevos.
- [ ] Botón SectionBoxer aparece en ribbon y abre la ventana.
- [ ] Los 4 orígenes funcionan en modelo sample.
- [ ] Reset restaura el estado previo en la misma sesión.
- [ ] Tests xUnit pasan (`dotnet test`).
- [ ] `docs/ARCHITECTURE.md` actualizado con sección SectionBoxer.
- [ ] README actualizado: ✓ junto a SectionBoxer en la lista de módulos.
- [ ] PR con: capturas (3D antes/después), lista de archivos, checklist DoD.

## 8. Plan de ejecución sugerido (10 días hábiles)

| Día | Tarea | Entregable |
|-----|-------|------------|
| 1 | Scaffolding (modelos, abstracciones, command vacío, window, ribbon, DI) | Compila y abre ventana vacía |
| 2 | `BoundingBoxCalculator` + tests xUnit del cálculo puro | Cobertura del cálculo |
| 3 | `SelectionSource` + flujo Aplicar para origen Selección | Funciona origen 1 |
| 4 | `RoomSource` + picker de habitación | Funciona origen 2 |
| 5 | `LevelSource` + selector de nivel | Funciona origen 3 |
| 6 | `SavedFilterSource` + integración con `IFilterTemplateService` | Funciona origen 4 |
| 7 | Crear vista 3D dedicada + alineación Proyecto/Vista | UI completa |
| 8 | Reset / Clear + snapshot en memoria | Reset funcional |
| 9 | Opción "incluir linked models" + tests adicionales | Cobertura linked |
| 10 | QA en modelo sample + docs + PR | PR mergeable |

## 9. Archivos que crea / modifica este sprint

**Nuevos:**
- `src/e3gbimtools.DiTools.Core/Models/Sections/SectionBoxSourceKind.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sections/SectionBoxRequest.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sections/SectionBoxAlignment.cs`
- `src/e3gbimtools.DiTools.Core/Models/Sections/SectionBoxResult.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/ISectionBoxSource.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/ISectionBoxService.cs`
- `src/e3gbimtools.DiTools.Core/Abstractions/IBoundingBoxCalculator.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/SectionBoxService.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/BoundingBoxCalculator.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/Sources/SelectionSource.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/Sources/RoomSource.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/Sources/LevelSource.cs`
- `src/e3gbimtools.DiTools.Core/Services/Sections/Sources/SavedFilterSource.cs`
- `src/e3gbimtools.DiTools/Commands/SectionBoxerCommand.cs`
- `src/e3gbimtools.DiTools.UI/Views/SectionBoxer/SectionBoxerWindow.xaml`
- `src/e3gbimtools.DiTools.UI/Views/SectionBoxer/SectionBoxerWindow.xaml.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sections/BoundingBoxCalculatorTests.cs`
- `Tests/e3gbimtools.DiTools.Tests/Sections/SectionBoxRequestTests.cs`

**Modificados:**
- `src/e3gbimtools.DiTools/Ribbon/RibbonBuilder.cs` (+1 botón)
- `src/e3gbimtools.DiTools.Core/DependencyInjection/DiToolsCoreServiceCollectionExtensions.cs` (+2 servicios)
- `Tests/e3gbimtools.DiTools.Tests/DependencyInjection/ServiceContainerTests.cs` (asserts SectionBoxer)
- `docs/ARCHITECTURE.md`
- `README.md`

## 10. Cómo empezar (para el desarrollador o agente)

1. Verificar que la rama `feature/sprint4-viewmanager` (o `main` si ya mergeada) compila limpio.
2. Crear rama `feature/sprint5-sectionboxer`.
3. Leer este documento y la sección Sprint 5 del Roadmap maestro.
4. Aplicar el scaffolding (§9 archivos nuevos).
5. Ir cerrando tareas por día siguiendo §8.
