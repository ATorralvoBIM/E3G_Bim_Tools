# Briefing — Mejoras UX en MEPCalc

**Proyecto:** E3G BIM Tools — módulo de cálculo MEP
**Documento:** Roadmap de mejoras de experiencia de usuario
**Fecha:** 2026-05-13
**Estado:** Borrador para revisión y priorización
**Audiencia:** equipo de producto + desarrollo

---

## 1. Contexto

MEPCalc es el módulo de E3G BIM Tools que dimensiona redes hidráulicas (tuberías) y aeráulicas (conductos) leídas directamente del modelo Revit. La versión actual cumple su función técnica (BFS topológico → cálculo de pérdidas → recomendación de DN → aplicar al modelo) pero la experiencia de uso queda en el mínimo viable: tabla, esquema y botón "Calcular". El objetivo de este documento es identificar las palancas que más aumentan la calidad de vida del calculista y posicionan la herramienta como referencia frente a alternativas como MagiCAD o Cype.

## 2. Resumen ejecutivo

Se proponen **15 mejoras** agrupadas en seis ejes: sincronización con Revit, diagnóstico visual, generación de entregables, navegación, comparación y automatización. La priorización se basa en dos criterios: **frecuencia de uso esperada** (cuántas veces al día/semana un calculista lo tocaría) y **esfuerzo de implementación** (días-persona).

El roadmap recomendado en tres olas (§14):

1. **Ola 1 — Diferenciación rápida** (≈ 4–6 semanas): F-01, F-02, F-04, F-08
2. **Ola 2 — Profesionalización del entregable** (≈ 6–8 semanas): F-03, F-07, F-12
3. **Ola 3 — Inteligencia de la herramienta** (≈ 8–10 semanas): F-05, F-09, F-14

---

## 3. F-01 · Sincronización bidireccional con Revit

**Problema.** Hoy el calculista identifica un tramo con problema en la tabla y tiene que buscarlo a mano en el modelo por su Element Id. Y al revés, si está en Revit viendo un tramo, no puede saltar a su fila del cálculo. La ruptura del contexto rompe el flujo.

**Valor.** El usuario trabaja en un solo "espacio mental": señala en Revit y aparece en MEPCalc, o señala en MEPCalc y se ilumina en Revit. Reduce el tiempo medio de iteración de minutos a segundos.

**Alcance**

Incluido:
- Clic en fila del DataGrid → selecciona la pipe correspondiente en Revit y hace `UIDocument.ShowElements(elementId)` para centrarla en la vista activa.
- Clic en glifo del esquema → mismo efecto que clic en la fila.
- Suscripción a `UIApplication.SelectionChanged` para que cuando el usuario seleccione una pipe en Revit, la fila correspondiente se resalte en el grid y el glifo en el esquema.
- Hover sobre fila → override gráfico temporal del elemento en Revit (color del sistema, grosor doble) durante 2 s.

Excluido:
- Edición geométrica desde el panel.
- Selección múltiple cross-context (más adelante).

**Enfoque técnico**

El panel ya vive dentro de la sesión de Revit (modal abierto desde un `IExternalCommand`). Usar el `UIDocument` recibido en el constructor para hacer `Selection.SetElementIds` y `ShowElements`. Para la suscripción inversa: registrar handler en `UIApplication.SelectionChanged`. Para el override de hover: `OverrideGraphicSettings` con `SetProjectionLineColor`/`SetProjectionLineWeight` dentro de una transacción ligera, y temporizador WPF que la revierte. Alternativa: usar `IUpdater` o `TemporaryGraphicsManager` para evitar transacciones.

**Dependencias.** Acceso al `UIDocument` (ya disponible). Verificar permisos cuando la ventana se abre en modo no-modal (futuro).

**Esfuerzo.** 4–5 días-persona.

**Riesgos.** `SelectionChanged` puede dispararse muy seguido durante drag — añadir throttle. El override temporal exige `Transaction`; si la ventana está modal frente a otra transacción abierta podría dar conflicto — usar `TemporaryGraphicsManager` si está disponible.

**Criterios de aceptación.**

- Seleccionar una pipe en Revit con MEPCalc abierto → la fila correspondiente queda resaltada, el grid hace scroll hasta ella y su glifo en el esquema parpadea.
- Doble-clic en una fila del grid → la vista 3D activa hace zoom-fit a la pipe.
- Hover sobre fila durante > 200 ms → la pipe se resalta en la vista activa hasta que el ratón sale (con un timeout de seguridad de 2 s).

---

## 4. F-02 · Camino crítico y heatmap

**Problema.** En una red de 300 tramos, identificar dónde está el cuello de botella exige leer la tabla columna a columna. El cálculo entrega los datos pero no la lectura.

**Valor.** Un click → el ingeniero ve dónde está el problema. Convierte MEPCalc en herramienta de diagnóstico, no solo de cálculo.

**Alcance**

Incluido:
- Botón "Camino crítico" que recorre desde la fuente hasta el terminal con peor ΔP acumulado y resalta esos tramos en el esquema y en la tabla (fondo amarillo + grosor doble en el dibujo).
- Modo "heatmap": un combo en la cabecera del esquema permite elegir métrica (velocidad, ΔP/m, holgura del criterio, FixtureCount). Los tramos se pintan con un gradiente verde→amarillo→rojo según el valor relativo al rango.
- Etiqueta de presión acumulada en cada nodo del camino crítico.

Excluido:
- Re-routing automático (es otra feature).
- Comparativa entre criterios (F-05).

**Enfoque técnico**

El camino crítico ya está implícito en `CalcReport.WorstCasePressureDropPa`. Reconstruirlo recorriendo el árbol y trazando hacia atrás desde el terminal con ΔP máximo hasta la fuente, almacenando `ResultRow` por el camino. Render: usar un segundo pase tras `BuildSchematic` que aplique override de stroke en los `WpfLine` correspondientes (mantener referencia row → WpfLine en un diccionario).

Heatmap: cambiar `_lastSystemBrush` a un `Func<ResultRow, Brush>` parametrizable. Calcular min/max del campo seleccionado y usar `LinearGradientBrush` o interpolación HSV en código.

**Dependencias.** Ninguna externa.

**Esfuerzo.** 3–4 días-persona.

**Riesgos.** Performance del heatmap al cambiar de métrica si la red es grande — recalcular sobre el `rows` ya cargado, no relanzar el cálculo hidráulico.

**Criterios de aceptación.**

- Botón "Camino crítico" disponible tras Calcular. Al pulsarlo, el recorrido aguas-arriba desde el terminal con peor ΔP se resalta en amarillo en el esquema y las filas correspondientes se marcan en la tabla.
- Combo "Métrica" en el esquema con 4 opciones (Q, v, ΔP/m, holgura). Al cambiar, los tramos se recolorean con gradiente sin volver a calcular.

---

## 5. F-03 · Generador de esquema de principio en Revit

**Problema.** El esquema unifilar que se entrega al cliente (planos CTE/RITE) se dibuja a mano en CAD a partir del modelo. Para una red de 300 tramos son 1–2 días de un delineante.

**Valor.** Feature flagship. Pasa MEPCalc de "calculadora" a "herramienta de entrega de proyecto". Justifica un escalón de precio.

**Alcance**

Incluido:
- Botón "Generar esquema en Revit" que crea (o actualiza) una `DraftingView` con el esquema 2D usando familias del proyecto.
- Layout idéntico al esquema interno de MEPCalc (DFS preorder branch-first, columnas por flujo, glifos TEE/ELBOW/TERMINAL).
- Etiquetas configurables: BfsIndex, NodePath, DN, Q, v, ΔP/m (mismo selector que el grid).
- Línea con el grosor escalado por DN.
- Leyenda automática (sistema, criterio aplicado, fecha de cálculo, autor, totales).
- Camino crítico opcional resaltado.
- Exportable a DWG / PDF directo desde Revit.

Excluido:
- Plantilla por norma (CTE-HS4 vs RITE) — primera versión usa una plantilla genérica configurable, no automática por preset.
- Esquema isométrico (proyección 30°): solo unifilar 2D plano.

**Enfoque técnico**

Crear una `View` de tipo `ViewDrafting`. Iterar el árbol calculado y por cada tramo crear líneas de detalle (`DetailLine`) con `GraphicsStyle` por DN. Glifos como `FamilyInstance` 2D anotativos en familias de detalle (TEE, codo, terminal). Texto como `TextNote`. La X/Y depende del DFS y la indentación parametrizable.

Necesita un set de familias de detalle 2D adjuntas al plugin. Plantilla `.rfa` + `.rvt` con líneas, símbolos y texto-types preconfigurados.

**Dependencias.**

- Familias `.rfa` de símbolos (TEE, ELBOW, CROSS, TERMINAL, REDUCER, FUENTE).
- Plantilla de `DraftingView` con escalas y estilos.
- Decisión de versión Revit objetivo (familias 2024+ vs 2025+).

**Esfuerzo.** 12–18 días-persona (incluye autoría de las familias y plantilla).

**Riesgos.** El layout puede quedar apretado en redes grandes — incluir paginación automática (varias DraftingViews) o escala configurable. Necesita una transacción potente, conviene encapsular en `TransactionGroup` para permitir undo limpio.

**Criterios de aceptación.**

- Tras Calcular, botón "Generar esquema en Revit" crea una `DraftingView` "Esquema [Sistema] [Fecha]" con el árbol calculado dibujado a escala 1:1 sobre el plano.
- Cada línea lleva texto adyacente con BfsIndex + DN + Q (campos configurables).
- Leyenda en la esquina superior derecha con nombre del sistema, criterio, ΔP del peor caso, número de tramos.
- La vista se puede arrastrar a una hoja, imprimir a PDF y exportar a DWG sin postproceso adicional.

---

## 6. F-04 · Búsqueda y filtrado en la tabla

**Problema.** No hay forma rápida de encontrar el tramo #178 ni de aislar todos los DN20 con problema.

**Valor.** Reduce tiempo de exploración en redes grandes. Permite reuniones donde se navega por "muéstrame solo los errores".

**Alcance**

Incluido:
- Caja de búsqueda libre arriba del grid: filtra rows por BFS index, NodePath, Element Id, Size, LevelName, Status. Filtrado incremental al teclear.
- Chips de filtros rápidos: "Solo errores", "Solo warnings", "DN ≥ DN32", "DN ≤ DN25", "Nivel = [combo]", "Tramos del sistema X".
- Limpiar filtros en un click.
- El esquema atenúa al 25 % los tramos no coincidentes (mantiene la topología visible).

Excluido:
- Filtros guardados como presets (futuro).
- Filtros con operadores lógicos complejos (AND/OR explícitos).

**Enfoque técnico**

Cambiar `DgResults.ItemsSource` de la `ObservableCollection` directa a una `CollectionView` con `Filter` configurable. El predicado se construye combinando el texto de la caja con los chips activos.

Para el esquema, añadir un parámetro `IsHighlighted` calculado y modificar `BuildSchematic` para atenuar opacidad de las líneas/glifos correspondientes cuando hay filtros activos.

**Dependencias.** Ninguna.

**Esfuerzo.** 3–4 días-persona.

**Riesgos.** Repintar el esquema en cada cambio de filtro puede ser pesado en redes grandes — usar opacidad sobre `_schemaGlyphs` sin reconstrucción, o un throttle de 150 ms.

**Criterios de aceptación.**

- Caja `Ctrl+F` (foco) sobre el grid filtra al teclear con < 50 ms de latencia en 500 rows.
- Chip "Solo errores" deja visible solo filas con `StatusKind = Error` y atenúa el resto del esquema.
- Botón "Limpiar filtros" devuelve el estado completo.

---

## 7. F-05 · Comparación de snapshots

**Problema.** Tras un cambio en el modelo o en el preset, no hay forma de comparar el resultado anterior con el nuevo. El ingeniero recompila y pierde la referencia.

**Valor.** Permite validar cambios ("¿bajar la temperatura del agua a 10 °C realmente afecta?") y dejar trazabilidad entre versiones.

**Alcance**

Incluido:
- Botón "Guardar snapshot" tras Calcular. Pide nombre y guarda `CalcReport` + metadatos (preset, fecha, ID de versión del documento).
- Vista "Comparar" con dos selectores (snapshot A / snapshot B) y tabla con delta por tramo: ΔDN, ΔQ, Δv, ΔΔP. Las filas con cambio se resaltan.
- Persistencia en disco junto al `.rvt` (carpeta `.e3gcalc/snapshots/`).

Excluido:
- Comparar entre proyectos distintos.
- Versionado tipo Git (más adelante con integración a BIM 360).

**Enfoque técnico**

Serializar `CalcReport` a JSON con `System.Text.Json` (todas las propiedades son tipos simples). Cargar dos snapshots, hacer join por `RevitId` (o por `NodePath` si el Id ha cambiado), calcular deltas, mostrar en un nuevo `DataGrid` con columnas duplicadas A/B/Δ.

Detección automática "el modelo ha cambiado": comparar el `Document.LastSaveModificationStamp` o un hash de los element IDs de la red contra el guardado en el snapshot.

**Dependencias.** Ninguna externa.

**Esfuerzo.** 6–8 días-persona.

**Riesgos.** Si la topología de la red cambia entre snapshots (tramos nuevos o eliminados), el join no es trivial — definir política: por Element Id estricto, mostrando "nuevo"/"eliminado" para los que no casan.

**Criterios de aceptación.**

- Tras Calcular, "Guardar snapshot" persiste el resultado con un nombre dado por el usuario.
- Pestaña "Comparar" lista todos los snapshots del proyecto. Al elegir A y B, una tabla muestra los tramos con cambio en cualquier campo.
- Al abrir MEPCalc con un modelo cuyo timestamp es posterior al último snapshot, el botón Calcular muestra una insignia "Modelo modificado desde el último cálculo".

---

## 8. F-06 · Modo what-if interactivo

**Problema.** Para tantear "¿qué pasa si subo este tramo a DN50?" hay que ir a Revit, cambiar el DN, volver a MEPCalc, recalcular. Iteración lenta.

**Valor.** Iteración instantánea. Hace posible diseñar buscando óptimos en el panel sin tocar el modelo hasta confirmar.

**Alcance**

Incluido:
- Doble-clic en la celda DN de una fila → editor inline con flechas para subir/bajar el DN dentro de la familia de tallas del proyecto.
- Al cambiar, recalcular aguas-abajo en memoria y refrescar todos los campos (v, ΔP/m, ΔP) sin tocar Revit.
- Banner naranja: "Hay 3 cambios sin aplicar al modelo". Botón "Aplicar al modelo" persiste solo los cambios al `.rvt`.
- "Descartar cambios" vuelve al estado calculado.

Excluido:
- Editar otros campos que no sean DN (criterio, simultaneidad — eso ya tiene su sitio en los controles de cabecera).
- Histórico de tanteos.

**Enfoque técnico**

Mantener un `Dictionary<RevitId, double>` de overrides de DN. En lugar de `MakeRowFromPipe(s, ...)` directo, pasar por una función que si hay override aplique el DN forzado al cálculo de pérdidas. Recalcular solo el sub-árbol afectado (los descendientes del tramo modificado).

`ResultWriter` ya hace transacciones para escribir al modelo — recibe la lista filtrada por overrides.

**Dependencias.** F-05 conveniente para "snapshot pre-tanteo".

**Esfuerzo.** 5–7 días-persona.

**Riesgos.** Recalcular solo el sub-árbol exige modificar el motor hidráulico (`HydraulicCalculator`) para aceptar un punto de partida. Si es prohibitivo, recalcular toda la red entera tras cada cambio — aceptable hasta 500 tramos.

**Criterios de aceptación.**

- Doble-clic en DN20 de la fila #5 → aparece editor. Selección de DN25 → la fila y todas sus descendientes recalculan en menos de 500 ms.
- Banner naranja persiste hasta que el usuario aplique o descarte los cambios.
- "Aplicar al modelo" abre una sola transacción y persiste solo los DN forzados (no toca el resto).

---

## 9. F-07 · Diagnóstico explicativo por fila

**Problema.** El mensaje de estado es críptico ("dP/m excede FRICTION_EXCEEDED"). El usuario no sabe qué hacer.

**Valor.** Convierte avisos en acciones. Reduce la curva de aprendizaje y la dependencia de soporte.

**Alcance**

Incluido:
- Clic derecho en fila con warning/error → "¿Por qué?" abre panel lateral con:
  - Texto del aviso en lenguaje natural ("La pérdida unitaria 566 Pa/m supera el límite de 400 Pa/m fijado por el preset CTE-HS4 para agua fría sanitaria").
  - Cadena de cálculo: fórmula utilizada, valores sustituidos, valor obtenido, valor límite.
  - Sugerencia accionable ("Sube a DN32: la pérdida unitaria estimada sería 198 Pa/m, dentro de rango. Coste estimado adicional: +3.20 €/m de tubería").
  - Botón "Aplicar sugerencia" → activa el override de F-06.

Excluido:
- Diagnóstico de errores topológicos (red desconectada, etc.) — usa el botón Diagnóstico existente.

**Enfoque técnico**

Mapa estático `Dictionary<string, ExplainerFunc>` donde la clave es el código del aviso (`FRICTION_EXCEEDED`, `VELOCITY_EXCEEDED`, `UNDERSIZED`, etc.) y el valor una función que recibe la `ResultRow` y devuelve un objeto `{ Texto, Formula, Sugerencia }`.

Cada explainer encapsula la lógica del criterio aplicado y propone el siguiente DN superior calculando la pérdida unitaria con el caudal actual.

**Dependencias.** F-06 conveniente para "aplicar sugerencia".

**Esfuerzo.** 4–6 días-persona (la mitad es escribir los textos explicativos).

**Riesgos.** Mantener los textos actualizados cuando cambian los criterios. Centralizar en un único archivo (resource `.resx` para localización si hace falta).

**Criterios de aceptación.**

- Para cada código de aviso definido en `CalcWarning`, hay un explainer asociado.
- Clic derecho en fila con `StatusKind != OK` → opción "Explicar este aviso" abre el panel.
- El panel muestra fórmula, valor calculado, límite y sugerencia concreta.

---

## 10. F-08 · Atajos de teclado y persistencia de preferencias

**Problema.** Cada apertura el usuario reconfigura columnas visibles, tema, anchos de splitter. Y todo se hace con ratón.

**Valor.** Calidad de vida tangible para el usuario power. Muestra cuidado.

**Alcance**

Incluido:
- Atajos: `Ctrl+F` (foco a búsqueda — depende de F-04), `F5` recalcular, `Ctrl+E` exportar, `Ctrl+S` guardar snapshot (depende de F-05), `Esc` cerrar, `Space` toggle tema oscuro.
- Persistencia entre sesiones en `%AppData%/E3GBIMTools/userprefs.json`: tema, columnas visibles, ancho del splitter, último preset por sistema, posición y tamaño de ventana, ruta del último export.
- Memoria del último sistema seleccionado por proyecto Revit.

Excluido:
- Atajos personalizables por el usuario.

**Enfoque técnico**

`Window.PreviewKeyDown` con `switch` por `Key`. Persistencia con `System.Text.Json` en un struct `UserPreferences`. Cargar en el constructor, guardar en `OnClosing`.

**Dependencias.** Otras features para tener atajos relevantes.

**Esfuerzo.** 2–3 días-persona.

**Riesgos.** Conflicto de atajos con Revit cuando la ventana es no-modal — limitar a teclas no usadas por Revit en sus modos típicos.

**Criterios de aceptación.**

- Cerrar la ventana y reabrir mantiene el tema, las columnas visibles y el tamaño de ventana.
- `F5` lanza un recálculo. `Esc` cierra. `Space` alterna tema.

---

## 11. F-09 · Multi-sistema simultáneo

**Problema.** Para comparar AF y ACS, hay que abrir MEPCalc dos veces o cambiar el combo de sistema y perder el contexto.

**Valor.** Visibilidad inmediata de cómo se reparten las pérdidas entre redes paralelas. Caso de uso recurrente en proyectos residenciales y terciarios.

**Alcance**

Incluido:
- Pestañas paralelas dentro de Tuberías y de Conductos, una por sistema cargado.
- Posibilidad de tener N pestañas a la vez con cada una su tabla y su esquema.
- Modo "side-by-side" opcional que muestra dos sistemas en dos paneles paralelos del esquema, con scroll sincronizado.

Excluido:
- Cálculo conjunto de sistemas acoplados (recirculación, retornos).

**Enfoque técnico**

Refactor de `CalcWindow` para que `_lastReport`, `_lastSchemaRows`, `_lastSystemBrush` pasen a un struct `SystemTab` que se instancia por pestaña. La UI se replica.

**Dependencias.** Ninguna, pero requiere refactor significativo.

**Esfuerzo.** 8–12 días-persona.

**Riesgos.** Memoria si el usuario carga 10 sistemas grandes — limitar a 5 pestañas simultáneas o cerrar las menos usadas.

**Criterios de aceptación.**

- En la pestaña Tuberías se pueden abrir varios sistemas a la vez en sub-pestañas.
- Cada sub-pestaña conserva su estado: rows, esquema, preset.
- Modo "Comparar lado a lado" muestra dos esquemas a la vez con barras de scroll sincronizadas.

---

## 12. F-10 · Mediciones y materiales

**Problema.** Tras calcular, el ingeniero/delineante necesita pasar a presupuesto: cuántos metros de cada DN, cuántos fittings. Hoy se hace fuera de Revit o con un Schedule manual.

**Valor.** Cierra el ciclo cálculo → medición → presupuesto sin salir de la herramienta. Apreciado por estudios pequeños sin Presto/Arquímedes.

**Alcance**

Incluido:
- Pestaña "Mediciones" con dos tablas:
  - Por DN: metros lineales totales, número de tramos, % del sistema.
  - Por tipo de fitting: TEE, ELBOW, CROSS, REDUCER, TERMINAL — recuento.
- Campo opcional "Precio unitario" por DN y por fitting (configurable y persistente). Si hay precios, columna "Importe" y total al pie.
- Exportar a Excel con un click.

Excluido:
- Bibliotecas de precios con descargas automáticas.
- Integración con software de presupuestos.

**Enfoque técnico**

Agregaciones sencillas sobre `_lastSchemaRows`. UI: nuevo `TabItem` con dos `DataGrid`. Export a Excel: librería `ClosedXML` o salida CSV (más ligero).

**Dependencias.** Ninguna.

**Esfuerzo.** 4–5 días-persona.

**Riesgos.** Los precios persistentes deben ser por-proyecto, no globales. Decidir si se guardan en el `.rvt` (extensible storage) o en preferencias.

**Criterios de aceptación.**

- Pestaña "Mediciones" muestra metros totales por DN y recuento de fittings tras Calcular.
- Editar un precio unitario re-totaliza inmediatamente.
- "Exportar a Excel" genera un `.xlsx` con las dos tablas en sus hojas.

---

## 13. F-11 · Inspector lateral colapsable

**Problema.** El tooltip de un tramo es efímero y limitado. Para ver todas las propiedades hay que recorrer las columnas del DataGrid.

**Valor.** Una vista detallada por elemento sin saturar la tabla principal. Permite ocultar columnas en el grid (F-17 ya hecho) sin perder acceso a la información.

**Alcance**

Incluido:
- Panel lateral derecho colapsable que se abre al seleccionar una fila.
- Muestra todas las propiedades del tramo agrupadas: identificación, geometría, hidráulica, criterio, estado.
- Fórmula evaluada con los valores sustituidos del tramo.
- Lista de fittings aguas-arriba y aguas-abajo navegable (clic → selecciona).
- Mini-thumbnail 3D si Revit puede capturarlo.

Excluido:
- Edición desde el panel (eso es F-06).

**Enfoque técnico**

`Grid.ColumnDefinition` extra a la derecha con `Visibility` toggleable. `Binding` directo a la fila seleccionada del DataGrid. El thumbnail 3D requiere `View3D` temporal y `ExportImage` — caro, opcional, detrás de un toggle.

**Dependencias.** F-07 reutiliza este panel para mostrar diagnóstico explicativo.

**Esfuerzo.** 3–4 días-persona (sin thumbnail), +3 con thumbnail.

**Riesgos.** Generar thumbnails 3D ralentiza la UI — generar en background, mostrar placeholder mientras llega.

**Criterios de aceptación.**

- Botón en cabecera "Inspector" alterna visibilidad del panel.
- Seleccionar una fila muestra todas sus propiedades agrupadas y la fórmula del criterio evaluada.
- Navegar a un fitting adyacente desde el panel cambia la fila seleccionada.

---

## 14. F-12 · Informe profesional con plantilla

**Problema.** El export actual es un texto plano. Lo que el cliente quiere es un PDF con membrete, criterios, tabla, esquema y conclusiones.

**Valor.** Cierra el deliverable. Un solo click → memoria de cálculo lista para firmar.

**Alcance**

Incluido:
- Exportador a PDF (y opcionalmente DOCX) con plantilla CTE-HS4 / RITE seleccionable.
- Estructura: portada (proyecto, sistema, fecha, autor, logo), datos de partida, criterios aplicados, tabla de resultados, esquema renderizado, camino crítico, anexo de fórmulas, conclusiones (auto-pobladas con totales).
- Configurable: membrete, logo, autor, datos del proyecto.

Excluido:
- Edición WYSIWYG de la plantilla.

**Enfoque técnico**

Usar `QuestPDF` o `iTextSharp` (LGPL). Render del esquema a SVG/PNG desde el `Canvas` actual mediante `RenderTargetBitmap`. Datos de cabecera en preferencias del proyecto.

**Dependencias.** F-02 conveniente para incluir el camino crítico en el informe.

**Esfuerzo.** 8–10 días-persona (plantilla + maquetación).

**Riesgos.** La maquetación del esquema cuando es grande — generar en varias páginas con paginación automática o reducir escala manteniendo legibilidad.

**Criterios de aceptación.**

- Botón "Exportar informe" abre un diálogo con selección de plantilla (CTE-HS4, RITE-Supply, RITE-Return, genérico).
- El PDF resultante es válido para entregar como anexo de proyecto: portada, criterios, tabla, esquema, conclusiones.
- Datos del despacho (logo, nombre, dirección) se configuran una sola vez y persisten entre proyectos.

---

## 15. F-13 · Animación de flujo en el esquema

**Problema.** El esquema es estático. No transmite el sentido del flujo de un vistazo.

**Valor.** Feature de "wow" — ayuda a la lectura intuitiva y diferencia visualmente la herramienta en demos comerciales.

**Alcance**

Incluido:
- Toggle "Animación de flujo" en el esquema. Cuando se activa, puntos blancos se desplazan a lo largo de los tramos en sentido del flujo, a velocidad proporcional al caudal del tramo.
- Detener automáticamente cuando la ventana pierde foco para no consumir CPU.

Excluido:
- Animación 3D en Revit (otra feature).

**Enfoque técnico**

`CompositionTarget.Rendering` para frame loop ligero. Por cada tramo activo, mantener una lista de "partículas" con posición normalizada; en cada frame avanzar `dt * vQ` y reposicionar `WpfEllipse` correspondientes.

**Dependencias.** Ninguna.

**Esfuerzo.** 2–3 días-persona.

**Riesgos.** Performance en redes grandes — limitar el número de partículas concurrentes y/o desactivar para tramos con v < threshold.

**Criterios de aceptación.**

- Toggle visible cerca del esquema; al activarse, los puntos animados aparecen sobre los tramos.
- 60 fps estables con 300 tramos en una máquina media.
- Apagado cuando la ventana se minimiza.

---

## 16. F-14 · Sugerencias de optimización automáticas

**Problema.** El cálculo dice "OK" pero no avisa de sobredimensionamientos. El ingeniero suele dejar sobredimensionado por no saber dónde se puede recortar.

**Valor.** La herramienta deja de ser pasiva. Aporta valor proactivo que el usuario no había pedido.

**Alcance**

Incluido:
- Panel "Sugerencias" que tras el cálculo lista oportunidades detectadas:
  - Sub-trozos consecutivos al mismo DN con caudal y v muy por debajo del límite → propone DN inferior.
  - Tramos sobredimensionados respecto al ramal padre/hijos.
  - Terminales con presión disponible muy holgada.
- Cada sugerencia con impacto estimado (metros de tubería ahorrados, € si hay precios cargados).
- "Aplicar" la integra como override en F-06.

Excluido:
- Optimización global (problema NP-hard) — solo heurísticas locales en este alcance.

**Enfoque técnico**

Conjunto de detectores como funciones puras `IEnumerable<Suggestion> Detect(IList<ResultRow> rows, SizingCriteria criteria)`. Cada detector implementa una regla. Resultado agregado en un panel.

**Dependencias.** F-06 (overrides) para que las sugerencias sean accionables.

**Esfuerzo.** 6–8 días-persona.

**Riesgos.** Falsos positivos — las heurísticas deben ser conservadoras. Cada sugerencia con un "explica por qué" para que el ingeniero las pueda validar.

**Criterios de aceptación.**

- Tras Calcular, panel "Sugerencias" muestra 0..N propuestas con impacto cuantificado.
- Aplicar una sugerencia integra el cambio como override sin tocar el modelo.
- Cada sugerencia es justificable (cadena de razonamiento visible).

---

## 17. F-15 · Pequeñas mejoras visuales acumuladas

Conjunto de pulidos individualmente pequeños pero que en conjunto se notan.

| ID | Descripción | Esfuerzo |
|---|---|---|
| F-15a | Mini-mapa del esquema en la esquina | 2 d |
| F-15b | Expandir/colapsar sub-árbol clicando en el TEE | 2 d |
| F-15c | Modo "presentación" sin chrome para capturas | 1 d |
| F-15d | Toast con resumen tras Calcular (3 s) | 1 d |
| F-15e | Indicador de carga durante Calcular en redes > 200 tramos | 1 d |
| F-15f | Smooth scroll en el esquema al seleccionar fila lejana | 0.5 d |

---

## 18. Roadmap propuesto

### Ola 1 — Diferenciación rápida (4–6 semanas)

Objetivo: que el calculista perciba la app como "diferente" desde la primera sesión.

| Feature | Esfuerzo | Justificación |
|---|---|---|
| F-08 Atajos + persistencia | 2–3 d | Quick win, mejora general |
| F-04 Búsqueda + filtros | 3–4 d | Frecuencia altísima de uso |
| F-01 Sincronización con Revit | 4–5 d | Cambia la sensación de la app |
| F-02 Camino crítico + heatmap | 3–4 d | Diagnóstico inmediato |

### Ola 2 — Profesionalización del entregable (6–8 semanas)

Objetivo: que la herramienta produzca outputs vendibles al cliente final.

| Feature | Esfuerzo | Justificación |
|---|---|---|
| F-07 Diagnóstico explicativo | 4–6 d | Reduce coste de soporte |
| F-12 Informe PDF con plantilla | 8–10 d | Cierra el flujo de entrega |
| F-03 Generador de esquema en Revit | 12–18 d | Feature flagship |

### Ola 3 — Inteligencia y profundidad (8–10 semanas)

Objetivo: dotar a la herramienta de "criterio propio".

| Feature | Esfuerzo | Justificación |
|---|---|---|
| F-06 Modo what-if | 5–7 d | Iteración rápida |
| F-05 Snapshots y comparación | 6–8 d | Trazabilidad de iteraciones |
| F-14 Sugerencias automáticas | 6–8 d | Valor proactivo diferencial |
| F-10 Mediciones | 4–5 d | Bonus práctico |

### Backlog (sin ola asignada)

- F-09 Multi-sistema simultáneo
- F-11 Inspector lateral
- F-13 Animación de flujo
- F-15 Pulidos visuales

---

## 19. Métricas de éxito

| Métrica | Baseline (estimado) | Objetivo post-Ola 1 |
|---|---|---|
| Tiempo medio de identificación de un tramo problemático | 90 s | 10 s |
| Clics necesarios para llegar de Revit a la fila correspondiente | 4 | 1 |
| Tiempo entre "calcular" y "tener un PDF entregable" | 25 min | < 2 min (tras Ola 2) |
| % de avisos resueltos sin consultar documentación | 30 % | 75 % |
| NPS de calculistas de pruebas piloto | n/a | > 40 |

---

## 20. Glosario

- **BFS index.** Número identificador de un tramo asignado en el orden de descubrimiento topológico (Breadth-First Search) desde la fuente.
- **DFS preorder branch-first.** Orden de visita del árbol que recorre primero el ramal de menor caudal antes de continuar por el tronco. Es lo que la versión actual de MEPCalc utiliza para listar y dibujar los tramos.
- **Camino crítico.** Recorrido desde la fuente hasta un terminal cuya suma de pérdidas de carga es la mayor de la red. Determina la presión mínima de cabecera.
- **Heatmap.** Modo de pintado del esquema donde el color de cada tramo se calcula con un gradiente que mapea una métrica numérica (v, ΔP/m, etc.).
- **Override.** Cambio temporal en memoria de un DN forzado por el usuario, que no se persiste al modelo hasta confirmar.
- **Preset (criterio).** Conjunto de límites de v, ΔP/m, DN mínimo, perfil de simultaneidad y temperaturas de referencia, vinculado a una norma (CTE-HS4, RITE, etc.).
- **Snapshot.** Imagen congelada de un resultado de cálculo, asociada a un timestamp del modelo Revit, persistida en disco junto al `.rvt`.

---

## 21. Anexo · Decisiones pendientes

1. **Política de versionado de snapshots (F-05).** ¿Carpeta junto al `.rvt`? ¿Extensible storage embebido? ¿Servicio cloud?
2. **Plantillas de informe (F-12).** ¿Una por norma o una genérica configurable?
3. **Familias del esquema en Revit (F-03).** ¿Mantenidas por nosotros o aportadas por el cliente? ¿Versión Revit objetivo?
4. **Persistencia de preferencias (F-08).** ¿`%AppData%` por usuario, por máquina o ambas?
5. **Modo no-modal de la ventana.** Toda la sincronización con Revit (F-01) cambia si la ventana deja de ser modal. ¿Cambiamos el modo en esta iteración?
