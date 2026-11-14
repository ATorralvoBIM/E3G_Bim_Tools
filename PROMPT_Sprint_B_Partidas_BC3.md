# Prompt para ejecutar Sprint B — Partidas BC3

Copia el bloque de abajo en una sesion nueva.  Esta autocontenido: no
asume conocimiento de la sesion donde se ejecuto Sprint A.

---

## Prompt

Ejecuta el **Sprint B** del briefing de mejoras de UX en Partidas BC3 del
proyecto E3GBIMTools (Revit -> BC3).

**Briefing (obligatorio leerlo entero antes de tocar codigo):**
`C:\Dev\E3GBIMTools\BRIEFING_Sprints_Partidas_BC3_UX.md`
Solo la seccion "Sprint B — Capitulos y subcapitulos como entidades de
primera clase + drag-and-drop".  El Sprint A ya esta hecho y mergeado:
no toques nada del validador, solo reutilizalo.

**Contexto rapido (tres frases):**
- Hoy `MappingRule` lleva `ChapterCode`, `ChapterName`, `SubChapterCode`,
  `SubChapterName` como strings; el arbol de capitulos del BC3 se
  reconstruye en cada export agrupando reglas por esos campos.
- Eso impide: crear capitulos vacios, mover reglas/subcapitulos de un
  capitulo a otro sin retipear, reordenar capitulos, y renombrar sin
  riesgo de typos que generan "capitulos fantasma".
- Sprint B promueve `Chapter` y `SubChapter` a entidades de primera
  clase con `Id` (GUID), `Order` y FKs en `MappingRule`, y anade
  drag-and-drop visual sobre un arbol de capitulos.

**Que esta hecho ya (Sprint A, NO tocar):**
- `src/e3gbimtools.RevitBC3/Mapping/MappingRuleValidator.cs` (detecta
  reglas habilitadas que comparten terna ChapterCode/SubChapterCode/BC3Code).
- Propiedades `HasConflict` y `ConflictTooltip` en `MappingRule.cs`
  (`[JsonIgnore]`).
- `RecomputeRuleConflicts()`, `ConfirmEditorSaveDespiteConflict()` y
  `ConfirmExportDespiteConflicts()` en
  `src/e3gbimtools.RevitBC3/Views/RevitBC3MainWindow.xaml.cs`.
- Tests en `Tests/e3gbimtools.DiTools.Tests/RevitBC3/MappingRuleValidatorTests.cs`.

Sprint B debe **reutilizar** `MappingRuleValidator.DetectConflicts` y
`FindConflictFor` tras cada drop (AC2.3 del briefing).

**Alcance (lee el briefing para detalle, esto es solo el indice):**
- B1 Crear capitulo vacio (boton "+ Capitulo").
- B2 Subcapitulos y mover subcapitulos entre capitulos (drag-and-drop).
- B3 Regla "huerfana" (sin capitulo) + nodo "Sin asignar" en el arbol.
- B4 Drag-and-drop de regla entre nodos del arbol.
- B5 Reordenar capitulos (up/down o drag dentro del mismo nivel),
  `Chapter.Order` se persiste y el export respeta ese orden.
- B6 Migracion automatica de mapping files antiguos (Chapter/SubChapter
  derivados de los strings unicos en las reglas).  Boton para exportar
  en formato legacy.

**Modelo de datos propuesto (ver briefing seccion "Modelo de datos
propuesto"):** `Chapter { Id, Code, Name, Order, SubChapterIds }`,
`SubChapter { Id, ChapterId, Code, Name, Order }`, `MappingRule` gana
`ChapterId` y `SubChapterId` (FKs nullable, los strings actuales se
deprecan pero se conservan temporalmente para legacy export).

**Ficheros principales a tocar (lista no exhaustiva, sigue el grafo):**
- `src/e3gbimtools.RevitBC3/Mapping/`: nuevos `Chapter.cs`, `SubChapter.cs`.
- `src/e3gbimtools.RevitBC3/Mapping/MappingFile.cs` (modelo persistido).
- `src/e3gbimtools.RevitBC3/Mapping/MappingRule.cs` (anade FKs, deprecar
  campos string).
- `src/e3gbimtools.RevitBC3/Services/MappingPersistenceService.cs`
  (load/save + migracion legacy -> nuevo, y exporter legacy).
- `src/e3gbimtools.RevitBC3/Services/BC3WriterService.cs` (`BuildChapters`
  desaparece; el writer recorre Chapter -> SubChapter -> Rule).
- `src/e3gbimtools.RevitBC3/Services/BimlinkExporter.cs` (capitulo y
  subcapitulo via FKs).
- `src/e3gbimtools.RevitBC3/Views/RevitBC3MainWindow.xaml(.cs)`: rediseno
  con tres paneles (arbol de capitulos / reglas del nodo seleccionado /
  detalle de regla), botones "+ Capitulo" y "+ Subcapitulo", nodo
  especial "Sin asignar", drag-and-drop entre nodos.
- Tests nuevos: `Tests/e3gbimtools.DiTools.Tests/RevitBC3/MappingMigrationTests.cs`
  (round-trip: cargar mapping antiguo -> guardar -> mismo BC3 que antes).

**Tecnologia clave:**
- WPF drag-and-drop entre TreeView nodes: usa `gong-wpf-dragdrop`
  (libreria probada, licencia BSD-3) en vez de implementar a mano.
  Anade el package al `Directory.Packages.props` (CPM) o al csproj de
  RevitBC3 directamente.  Si la licencia no encaja con el proyecto,
  documentalo y propon alternativa antes de seguir.
- C# 12 (LangVersion 12.0, ya en Directory.Build.props).  Records y
  pattern matching disponibles.
- Target: net8.0-windows, x64.  Revit 2025 en
  `E:\APPS\Autodesk\Revit 2025\` (segun `Directory.Build.props`).

**Restricciones / no-objetivos:**
- Mantener compatibilidad con mapping files antiguos.  No romper a
  usuarios que carguen un `.json` de Sprint anterior.
- No anadir multi-mapping ni plantillas compartidas entre proyectos
  (fuera de alcance, sprint propio si llega a hacer falta).
- Codigos de capitulo: los introduce el usuario, no auto-numerar.
- Reutilizar el validador del Sprint A tras cada drop; un drop que
  genere conflicto NO se cancela automaticamente, solo marca la regla
  movida como conflictiva (AC4.2).

**Flujo de trabajo esperado:**
1. Lee el briefing (sprint B completo) y los ficheros listados arriba
   antes de planificar.
2. Crea una lista de tareas (TaskCreate) cubriendo: modelo, persistencia
   con migracion, writer, UI tres paneles, drag-and-drop con
   gong-wpf-dragdrop, integracion con validador, tests de migracion,
   build + tests al final.
3. Implementa.  Itera con builds parciales si puedes.
4. Tests obligatorios al final:
   ```
   dotnet build C:\Dev\E3GBIMTools\e3gbimtools.sln
   dotnet test  C:\Dev\E3GBIMTools\Tests\e3gbimtools.DiTools.Tests\e3gbimtools.DiTools.Tests.csproj
   ```
   Los tests existentes (MappingRuleValidatorTests, BimLinkingValidationTests,
   etc.) deben seguir pasando.  Si rompes alguno, arreglalo.

**Importante para el handoff visual:**
La ventana afectada es `RevitBC3MainWindow.xaml` (NO `AjustesWindow`,
ese archivo es del proyecto legacy E3G_Saneamiento y usa otro modelo
distinto).  Las reglas con `ChapterCode`/`BC3Code` SOLO viven en el
editor inline de `RevitBC3MainWindow` (controles `EditorChapterCodeBox`,
`EditorSubChapterCodeBox`, `EditorBC3CodeCombo`).  El mockup conceptual
de la nueva UI esta en el briefing seccion "Alcance UI".

**Cuando termines:**
- Resume cada user story (B1..B6) con un check de "implementado / con
  caveats / no hecho y por que".
- Indica si el formato persistido cambio y como se prueba la migracion
  desde un fichero antiguo.
- Lista los puntos pendientes para Sprint C si los hay.

Empieza llamando a `AskUserQuestion` solo si encuentras una decision
ambigua que el briefing no resuelva (por ejemplo, si gong-wpf-dragdrop
tiene un conflicto de licencia o si la migracion requiere preguntar al
usuario que hacer con datos malformados).  En caso contrario, lanzate.
