# BRIEFING — Mejoras de UX en Partidas BC3

**Modulo afectado:** `e3gbimtools.RevitBC3` (ventana Ajustes -> Partidas BC3, export BC3 desde Saneamiento -> Mediciones).
**Estado:** Pendiente de planificacion. No codificar hasta tener sprints aprobados.
**Origen:** Sesion 25/05/2026, tras desbloquear conversor BC3 -> TCQ nativo.

---

## Contexto

El export BC3 funciona y TCQ lo lee y lo convierte a su formato nativo. En la verificacion del lado TCQ aparecen dos hallazgos que no son bugs del writer sino huecos del modelo de configuracion en la app:

1. **Duplicados silenciosos de partida dentro del mismo capitulo.** Cuando el usuario crea dos reglas habilitadas que apuntan al mismo codigo de catalogo (p.ej. `ED15N711.001`) y las dos viven en el mismo capitulo (p.ej. `01 SANEAMIENTO EDIFICIO A`), el writer las sufija como `ED15N711.001.001`, `ED15N711.001.002` para cumplir FIEBDC-3 (codigos `~C` unicos). TCQ admite el BC3 pero en su modelo de obra no puede tener dos lineas de presupuesto con el mismo codigo de partida bajo un mismo subcapitulo, asi que el sufijo es un parche al sintoma y no resuelve el problema real: el usuario quiso meter dos veces la misma partida en el mismo capitulo sin darse cuenta.

2. **Capitulos derivados implicitamente de las reglas.** Hoy el capitulo y el subcapitulo viven en campos de la propia regla (`ChapterCode`, `ChapterName`, `SubChapterCode`, `SubChapterName`). El arbol de capitulos del BC3 se reconstruye en cada export agrupando reglas por esos campos. Esto se rompe cuando el usuario quiere:
   - Crear un capitulo "vacio" para colocar reglas mas tarde.
   - Mover una regla de un capitulo a otro (hoy hay que editar dos campos de la regla y recordarse del codigo y nombre exactos del capitulo destino).
   - Mover un subcapitulo entero de un capitulo a otro.
   - Reordenar capitulos para que el BC3 salga con la ordenacion deseada.

Ambos hallazgos se abordan en sprints separados porque son cambios de naturaleza muy distinta (validacion ligera vs. refactor de modelo de datos y UI).

---

## Sprint A — Protocolo de avisos contra duplicados de partida

### Problema

Dos reglas habilitadas con el mismo `(ChapterCode, SubChapterCode, BC3Code)` generan dos partidas con el mismo codigo de catalogo en el mismo capitulo. Eso solo tiene sentido si el usuario quiere medir lo mismo dos veces (improbable, casi siempre es un descuido). El export "soluciona" el conflicto con sufijo `.001` y TCQ pierde la trazabilidad limpia al banco de precios.

### Objetivo

Que la app no permita que el usuario llegue al export con duplicados. La regla de oro: dentro de un mismo `(ChapterCode, SubChapterCode)`, el codigo de catalogo BC3 debe ser unico entre las reglas habilitadas.

### Alcance

- Validacion **en tiempo real** en `AjustesWindow` -> Partidas BC3:
  - Al crear o editar una regla.
  - Al cambiar el capitulo o subcapitulo de una regla existente.
  - Al habilitar/deshabilitar una regla (una regla deshabilitada no cuenta).
- Validacion **previa al export** en `RevitBC3MainWindow` (botones "Exportar BC3" y "Exportar CSV").
- Indicador visual permanente en el grid de reglas para las que estan en conflicto.

### User stories y criterios de aceptacion

- **A1.** *Como usuario, al editar el codigo BC3 de una regla, quiero que si choca con otra regla habilitada del mismo capitulo/subcapitulo me lo avise antes de guardar.*
  - **AC1.1**  Al cerrar el dialogo de edicion de regla con un BC3Code que choca con otra regla habilitada en el mismo `(ChapterCode, SubChapterCode)`, aparece un mensaje claro con el texto: "Ya existe otra regla habilitada con codigo `ED15N711.001` en el capitulo `01 SANEAMIENTO` -> subcapitulo `<nombre>`. ¿Continuar igual (se sufijara como `.001`/`.002` en el BC3 y TCQ no la importara), o cancelar para revisarlo?"
  - **AC1.2**  El usuario puede elegir "Cancelar" (vuelve al dialogo de edicion sin guardar) o "Guardar de todas formas" (se guarda con el conflicto marcado).
  - **AC1.3**  La validacion ignora reglas deshabilitadas.

- **A2.** *Como usuario, quiero ver de un vistazo que reglas estan en conflicto sin tener que abrirlas una a una.*
  - **AC2.1**  Las filas del grid de reglas en conflicto muestran un icono o un fondo (amarillo o rojo) en la columna "Estado" / "Avisos".
  - **AC2.2**  Tooltip al pasar el raton: "Conflicto: misma partida (`ED15N711.001`) en el mismo capitulo que la regla `<nombre>`."

- **A3.** *Como usuario, al pulsar "Exportar BC3" con conflictos pendientes, quiero un resumen y opcion de cancelar.*
  - **AC3.1**  Si al pulsar exportar hay 1 o mas conflictos, antes de generar el BC3 aparece un dialogo modal con la lista de conflictos: capitulo, codigo catalogo, reglas implicadas.
  - **AC3.2**  Opciones del dialogo:
    1. *Cancelar y revisar*: cierra sin exportar.
    2. *Exportar igual con sufijo automatico*: continua el export como hoy (genera `.001`/`.002`), util para no bloquear si el usuario sabe lo que hace.
  - **AC3.3**  No bloqueamos el export por defecto: el usuario sigue siendo el dueño de la decision. Solo informamos.

### Notas tecnicas

- La logica de deteccion vive en el dominio (no en la UI). Modulo recomendado: `e3gbimtools.RevitBC3/Mapping/MappingRuleValidator.cs` (nuevo). Recibe la coleccion completa de reglas y devuelve `IReadOnlyList<RuleConflict>` con `{ChapterKey, SubChapterKey, BC3Code, RuleIds}`.
- Reutilizar el mismo validador en (a) `AjustesWindow` en tiempo real y (b) `RevitBC3MainWindow` antes del export.
- El `MappingPersistenceService` no cambia: el conflicto puede persistirse, simplemente el usuario lo ve.
- Los tests existentes (`BimLinkingValidationTests`) no se ven afectados. Anadir tests unitarios nuevos en `tests/e3gbimtools.RevitBC3.Tests/MappingRuleValidatorTests.cs` con cases:
  - Dos reglas, distinto capitulo, mismo codigo -> no conflicto.
  - Dos reglas, mismo capitulo, mismo codigo, una deshabilitada -> no conflicto.
  - Dos reglas, mismo capitulo, mismo codigo, ambas habilitadas -> conflicto.
  - Dos reglas, mismo capitulo+subcap, mismo codigo -> conflicto.

### Fuera de alcance

- Auto-fusion de reglas duplicadas (lo que el usuario realmente queria al crear dos reglas iguales): mas complejo, requiere decidir como combinar selectores Revit. Si surge, sprint propio.
- Validacion de duplicados *entre proyectos*: no aplica, cada proyecto Revit lleva su propio set.

### Estimacion grosso modo

1 sprint pequeño (3-5 dias) si se hace por separado, asumiendo que `AjustesWindow.xaml` permite anadir una columna de aviso sin reorganizarlo entero.

---

## Sprint B — Capitulos y subcapitulos como entidades de primera clase + drag-and-drop

### Problema

El modelo actual:

```
Rule {
  Id, Name, Enabled, BC3Code,
  ChapterCode, ChapterName,
  SubChapterCode, SubChapterName,
  ...selectores Revit
}
```

derive el arbol de capitulos en cada export agrupando reglas por `ChapterCode`. Esto fuerza una serie de fricciones:

- No existen capitulos sin reglas (no se puede preparar la estructura del presupuesto antes de mapear).
- Renombrar un capitulo obliga a editar todas las reglas que lo usan (riesgo de typos -> capitulos fantasma "00.1 SANEAMINETO" por una `.` o un acento mal puesto).
- Mover una regla de capitulo es editar dos campos a mano (codigo y nombre) y rezar para que coincidan con el destino.
- Mover un subcapitulo entero entre capitulos no se puede sin tocar todas sus reglas.
- El orden de capitulos en el BC3 sale del orden alfabetico de codigos, no del orden que quiere el usuario.

### Objetivo

Promover capitulos y subcapitulos a entidades nombradas con identidad propia, persistidas explicitamente, y permitir reorganizar reglas y subcapitulos visualmente con drag-and-drop.

### Modelo de datos propuesto

```
Chapter {
  Id (GUID),
  Code (p.ej. "01"),
  Name (p.ej. "SANEAMIENTO EDIFICIO A"),
  Order (int, para reordenar),
  SubChapterIds (List<Guid>)
}

SubChapter {
  Id (GUID),
  ChapterId (FK),
  Code (p.ej. "AFS"),
  Name (p.ej. "Aigua Freda Sanitaria"),
  Order (int)
}

Rule {
  ... (existente)
  ChapterId (FK, GUID, nullable -> sin asignar)
  SubChapterId (FK, GUID, nullable)
  // ChapterCode/Name/SubChapterCode/SubChapterName desaparecen como
  // campos editables; se derivan via FK en tiempo de export.
}
```

Migracion: al cargar un fichero de mapping antiguo, crear Chapters/SubChapters a partir de los strings `ChapterCode`/`ChapterName` distintos que aparecen en las reglas (un capitulo por valor unico de `ChapterCode`).

### Alcance UI

Nueva organizacion de `AjustesWindow` -> Partidas BC3, tres paneles:

```
+------------------------------------------------------------------+
| [+ Capitulo] [+ Subcapitulo]                                     |
+----------------------+----------------------+--------------------+
|  Arbol de capitulos  |  Reglas del nodo     |  Detalle regla     |
|                      |  seleccionado        |                    |
|  > 01 SANEAMIENTO    |                      |  Nombre: ...       |
|    > Subcap AFS      |  ED15N711.001  ...   |  BC3 Code: ...     |
|    > Subcap PLUV     |  ED15N711.002  ...   |  Categoria Revit:  |
|  > 02 BAJANTES       |  EFA12345      ...   |  Filtros: ...      |
|                      |                      |                    |
|  [Sin asignar (3)]   |                      |                    |
+----------------------+----------------------+--------------------+
```

### User stories y criterios de aceptacion

- **B1.** *Como usuario, puedo crear un capitulo vacio antes de crear reglas.*
  - **AC1.1**  Boton "+ Capitulo" abre dialogo con Code y Name.
  - **AC1.2**  El capitulo aparece en el arbol con la marca "(0 reglas)".
  - **AC1.3**  Al exportar BC3 un capitulo sin reglas se omite (no contamina el BC3 final con capitulos vacios), pero permanece en la configuracion para uso futuro.

- **B2.** *Como usuario, puedo crear subcapitulos dentro de un capitulo y mover subcapitulos entre capitulos.*
  - **AC2.1**  Boton "+ Subcapitulo" solo activo si hay un capitulo seleccionado.
  - **AC2.2**  Arrastrar un nodo subcapitulo del arbol a otro nodo capitulo lo mueve (y todas sus reglas).
  - **AC2.3**  La validacion del Sprint A se reevalua tras el drop: si al mover un subcapitulo a un capitulo destino donde ya hay reglas con el mismo BC3Code, aparece el aviso del Sprint A.

- **B3.** *Como usuario, puedo crear una regla "huerfana" sin capitulo, y asignarla despues arrastrandola al capitulo o subcapitulo destino.*
  - **AC3.1**  El nodo especial "Sin asignar" en el arbol agrupa reglas con `ChapterId == null`.
  - **AC3.2**  Arrastrar una regla de "Sin asignar" a un capitulo o subcapitulo le asigna las FKs correspondientes.
  - **AC3.3**  Una regla en "Sin asignar" no se exporta al BC3 hasta que tenga capitulo.

- **B4.** *Como usuario, puedo mover una regla de un capitulo a otro arrastrandola.*
  - **AC4.1**  Drag and drop de regla entre nodos del arbol cambia su `ChapterId`/`SubChapterId`.
  - **AC4.2**  Si el destino genera conflicto (Sprint A), aparece el aviso, no se cancela el move automaticamente: queda la regla movida pero marcada como conflictiva.

- **B5.** *Como usuario, puedo reordenar capitulos arriba/abajo para controlar el orden del BC3.*
  - **AC5.1**  Botones up/down en el header del arbol, o drag-and-drop dentro del mismo nivel.
  - **AC5.2**  El export BC3 emite los capitulos en el orden persistido (campo `Chapter.Order`), no por codigo alfabetico.

- **B6.** *Migracion automatica de configuraciones existentes.*
  - **AC6.1**  Al abrir un mapping guardado con el modelo antiguo (`ChapterCode` en la regla), la app crea entities `Chapter` y `SubChapter` derivadas de los strings unicos y asigna las FKs.
  - **AC6.2**  Al guardar, el formato persistido pasa a la nueva forma. Boton "Exportar mapping en formato legacy" para compatibilidad temporal.

### Notas tecnicas

- Ficheros mas tocados: `AjustesWindow.xaml(.cs)` (mucho), `MappingFile.cs`, `MappingPersistenceService.cs`, `MappingRule.cs` (deprecar campos), nuevos `Chapter.cs` y `SubChapter.cs` en `Mapping/`.
- El export BC3 (`BC3WriterService`) se simplifica: la funcion `BuildChapters` desaparece (la jerarquia ya viene resuelta del modelo); el writer solo recorre `Chapter -> SubChapter -> Rule -> ExportPartida`.
- El `BimlinkExporter` actualiza `Capitulo`/`Subcapitulo` para que escriban `Chapter.Code` y `SubChapter.Code` desde las FKs.
- WPF drag-and-drop entre TreeView nodes es propenso a bugs visuales; conviene usar una libreria probada (gong-wpf-dragdrop) en lugar de implementar `MouseDown`+`DragDrop.DoDragDrop` a mano. Hay que evaluar la licencia.
- Tests: anadir `MappingMigrationTests.cs` para validar que mapping files antiguos se cargan con la nueva forma y producen el mismo BC3 que antes (round-trip).

### Fuera de alcance

- Multi-proyecto / multi-mapping en la misma sesion. Sigue siendo un mapping a la vez.
- Compartir capitulos entre proyectos (plantillas de capitulos). Si llega a ser util, sprint propio.
- Codigos de capitulo automaticos (auto-numeracion). Lo deja decidir el usuario.

### Estimacion grosso modo

Sprint mediano-grande (2 semanas). El refactor del modelo de datos es la parte cara; la UI con tres paneles es estandar WPF pero con cuidado en el drag-and-drop.

---

## Orden recomendado y dependencias

1. **Sprint A primero.** Es chico, da valor inmediato y no requiere refactor de datos. Ademas la logica del validador (`MappingRuleValidator`) se reutiliza en Sprint B (los conflictos se vuelven a chequear despues de cada drag-and-drop).
2. **Sprint B despues.** Hereda el validador y lo enchufa en los handlers de drop. El refactor de modelo aprovecha que el usuario ya tiene una herramienta para limpiar duplicados antes de migrar.

No se recomienda fusionar los dos en un solo sprint: el riesgo de regresion en Sprint B es alto y el aviso del A se quiere tener ya.

---

## Anexo: estado actual del export y por que aparece el sufijo `.001`

El writer `BC3WriterService.cs` cuenta, antes de emitir nada, cuantas veces se repite cada codigo de catalogo entre las mediciones no-vacias:

```
var codeUsageCount = nonZero
    .GroupBy(m => m.BC3Code, StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
```

Si `count > 1`, sufija las repeticiones con un secuencial: `ED15N711.001`, `ED15N711.001.001`, `ED15N711.001.002`...  Esto es necesario por dos motivos:

- FIEBDC-3 exige que los codigos `~C` sean unicos en todo el archivo. Sin sufijo el BC3 estaria malformado.
- Las dos partidas son conceptualmente distintas en la app: cada una tiene su propio set de elementos Revit, su propia medicion, posiblemente filtros distintos. Mantenerlas como dos partidas separadas preserva la trazabilidad BIM <-> presupuesto.

El problema es que **TCQ no admite dos lineas con el mismo codigo de partida en un mismo subcapitulo** en su modelo nativo, asi que ese sufijo `.001`/`.002` se importa pero rompe la regla de "una partida del banco = un codigo TCQ unico". El Sprint A no toca el writer: hace que el usuario nunca llegue al export con esta configuracion.
