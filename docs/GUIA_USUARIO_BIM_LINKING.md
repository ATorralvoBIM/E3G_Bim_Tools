# Guía de usuario — Vinculación BIM: Revit → BC3 → IFC → TCQ/Presto

> **e3gbimtools · Módulo RevitBC3 · Mayo 2026**

Esta guía describe el flujo completo para generar un presupuesto BC3 con
trazabilidad elemento a elemento, de forma que al abrir el BC3 en TCQ o Presto
y hacer clic sobre una partida, el visor IFC resalte automáticamente los
elementos de Revit que la componen.

---

## 1. Prerrequisitos

| Requisito | Versión / detalle |
|-----------|-------------------|
| Autodesk Revit | 2025 (add-in compilado para net8.0-windows) |
| e3gbimtools instalado | .addin en `%APPDATA%\Autodesk\Revit\Addins\2025\` |
| TCQ (Teambim Cost & Quantities) | Módulo BIM activado para vinculación IFC |
| Presto / Arquímedes | Versión con soporte FIEBDC-3/2016+ y IfcGUID en ~M |
| Archivo BC3 catálogo | El BC3 fuente con los capítulos y partidas del proyecto |

> **Nota:** el add-in funciona sin haber exportado el modelo a IFC previamente.
> Calcula los IfcGUIDs de forma determinista desde el UniqueId de cada elemento.

---

## 2. Flujo paso a paso

### Paso 1 — Abrir la herramienta en Revit

1. Abre el modelo Revit de MEP (tuberías, conductos, etc.).
2. En la cinta de Revit, haz clic en la pestaña **e3gbimtools**.
3. Dentro del panel **Mediciones BC3**, haz clic en el botón **Revit → BC3**.
   Se abre la ventana **RevitBC3**.

---

### Paso 2 — Importar el catálogo BC3

En la pestaña **1 · Catálogo BC3** (panel izquierdo, arriba):

1. Haz clic en **📂 Importar BC3...**.
2. Selecciona el archivo `.bc3` que contiene el catálogo de partidas del proyecto
   (p. ej. `2020 01 NIR BdPrecios ING Descompuestos.bc3`).
3. El árbol de capítulos y partidas se carga en el panel izquierdo superior.

---

### Paso 3 — Crear reglas de mapeo

Las **reglas** definen qué elementos de Revit se miden con qué partida del BC3.

En la sección central (tabla de reglas):

1. Haz clic en **+ Nueva regla** (barra de herramientas sobre la tabla).
2. En el **panel editor** (derecha), configura:
   - **Nombre** de la regla (descripción libre, p. ej. "Tuberías PVC DN75").
   - **Categoría Revit**: elige de la lista desplegable (p. ej. *Tuberías*).
   - **Filtros**: condiciones adicionales para afinar qué elementos se capturan
     (p. ej. `Diámetro nominal` igual a `75`).
   - **Código BC3**: el código de la partida del catálogo (p. ej. `SANCOL0001`).
   - **Parámetro de medición**: el parámetro de Revit que se usa como cantidad
     (`Length` para longitud en m, `Area`, `Volume`, `Count` o uno personalizado).
   - **Capítulo** y **Subcapítulo** (opcionales): agrupación en el BC3 exportado.
3. Haz clic en **Guardar regla**.
4. Repite para todas las familias / diámetros / sistemas del proyecto.

> **Consejo:** marca la casilla **Act.** de cada regla para activarla. Las reglas
> inactivas se ignoran en el cálculo.

---

### Paso 4 — Calcular mediciones

1. Haz clic en **⚡ Calcular** (barra de herramientas superior).
2. El motor recorre todos los elementos del modelo activo, aplica cada regla
   activa y acumula las cantidades.
3. La tabla inferior muestra las **mediciones** por partida:
   columnas Código · Descripción · Elementos · Cantidad · Unidad.
4. Revisa la columna **Elem.**: si es 0 para una regla activa, la categoría o
   los filtros no han capturado ningún elemento — revisa la configuración.
5. Si necesitas ajustar manualmente una cantidad, edita la celda directamente.
   La fila se colorea para indicar que tiene un *override* manual.

---

### Paso 5 (opcional) — Preparar parámetros para el IFC

Este paso es necesario si quieres que el IFC exportado desde Revit incluya los
códigos de partida BC3 como propiedades del elemento.

1. Haz clic en **🔗 Preparar IFC...** (toolbar derecha, habilitado tras calcular).
2. Acepta la confirmación.
3. El add-in crea/actualiza en cada elemento Revit los parámetros:
   - `E3G_BC3_Code` — código de la partida a la que pertenece el elemento.
   - `E3G_BC3_Quantity` — cantidad que aporta ese elemento.
   - `E3G_BC3_Chapter` y `E3G_BC3_Unit` — capítulo y unidad.
4. Ahora puedes **exportar el IFC desde Revit** (pestaña R → Exportar → IFC).
   Los parámetros aparecerán como propiedades IFC del elemento.

---

### Paso 6 — Exportar el BC3 y el archivo de vinculación

1. Haz clic en **📤 Exportar BC3...** (toolbar derecha).
2. Elige la carpeta de destino y el nombre del archivo (p. ej.
   `PRESUPUESTO_20260523.bc3`).
3. El add-in genera **dos archivos** en la misma carpeta:
   - `PRESUPUESTO_20260523.bc3` — presupuesto FIEBDC-3 con mediciones detalladas
     (una línea `~M` por elemento, con el IfcGUID en el campo comentario).
   - `PRESUPUESTO_20260523.bimlink.json` — archivo de vinculación con la
     correspondencia partida ↔ elementos (IfcGUID, sistema MEP, nivel, cantidad).
4. El mensaje de éxito muestra cuántas partidas se han escrito y la
   **cobertura de IfcGUID** (p. ej. "Cobertura IfcGUID: 98.4%").

---

### Paso 7 — Validar la exportación

Para verificar que el BC3 y el bimlink son correctos antes de entregarlos:

1. Haz clic en **🔍 Validar exportación** (toolbar derecha, habilitado tras exportar).
2. El validador ejecuta 6 checks automáticos:

| Check | Qué verifica |
|-------|--------------|
| V1 | Todos los IfcGUIDs del bimlink son de 22 caracteres (alfabeto IFC Base64) |
| V2 | Los códigos de partida del bimlink existen como conceptos en el BC3 |
| V3 | La suma de cantidades por elemento coincide con el total de la partida |
| V4 | El BC3 no contiene GUIDs legacy con prefijo "RVT:" |
| V5 | Las líneas de medición del BC3 tienen GUIDs IFC válidos (≥95%) |
| V6 | La cobertura declarada en el bimlink coincide con el recuento real |

3. El resultado se muestra en pantalla y se guarda como
   `PRESUPUESTO_20260523.validation.txt` junto al BC3.

---

### Paso 8 — Importar en TCQ

1. Abre TCQ y crea un nuevo proyecto (o abre uno existente).
2. **Importar BC3**: menú *Archivo → Importar → Presupuesto BC3*. Selecciona
   `PRESUPUESTO_20260523.bc3`.
3. **Vinculación BIM**: menú *BIM → Cargar modelo IFC* y selecciona el IFC
   exportado desde Revit.
4. TCQ/Presto leen el campo comentario de los `~M` para asociar cada línea de
   medición con el IfcGUID correspondiente. Al hacer clic en una partida, el
   visor IFC resalta los elementos.

> **Nota sobre el archivo .bimlink.json:** este archivo está preparado para
> futuras integraciones directas. Actualmente la vinculación en TCQ se hace
> vía los IfcGUIDs embebidos en el BC3 (campo comentario del `~M`).

---

## 3. Qué hacer si la cobertura de GUIDs es baja

La cobertura indica qué porcentaje de elementos tienen un IfcGUID fiable
(leído del parámetro `IFC_GUID` o calculado desde el UniqueId de Revit).

| Cobertura | Estado | Acción recomendada |
|-----------|--------|--------------------|
| ≥ 95% | 🟢 Excelente | Sin acción. |
| 80–94% | 🟡 Aceptable | Revisar elementos sin GUID (categorías especiales, familias antiguas). |
| < 80% | 🔴 Problemático | Ver sección siguiente. |

### Causas habituales de baja cobertura

- **Elementos sin UniqueId válido**: familias de sistema o elementos de detalle
  sin representación en el modelo 3D. Estos no tienen IfcGUID en el IFC
  exportado; tampoco lo tendrán en el BC3.
- **UniqueId con formato no estándar**: elementos importados de otros formatos
  (DWG, DXF) pueden tener UniqueIds que no siguen el formato Revit estándar.
- **Modelo muy fragmentado**: si el modelo fue creado con múltiples episodios
  (guardados con DocumentGuid distintos), el cálculo puede producir colisiones.
  En ese caso, exporta el IFC una vez desde Revit para poblar el parámetro
  `IFC_GUID` y vuelve a calcular.

### Pasos para mejorar la cobertura

1. Haz clic en **🔗 Preparar IFC...** y luego exporta el IFC desde Revit
   (pestaña R → Exportar → IFC). Esto puebla `IFC_GUID` en todos los elementos.
2. Vuelve a hacer clic en **⚡ Calcular** para regenerar las mediciones.
3. Exporta de nuevo con **📤 Exportar BC3...**.
4. Valida con **🔍 Validar exportación**: la cobertura debería ser 100%.

---

## 4. Limitaciones conocidas

- **Solo Revit 2025**: el add-in usa ForgeTypeId directamente. No es compatible
  con Revit 2022–2024 en esta versión.
- **Windows-1252 → Latin1**: el BC3 se escribe en ISO-8859-1. Algunos
  caracteres Windows-1252 (€, …, –) se transliteran. Poco probable en
  descripciones de partidas de construcción en castellano.
- **Override manual sin trazabilidad**: si ajustas manualmente la cantidad de
  una partida, el BC3 exporta la cantidad corregida pero sin líneas de desglose
  por elemento (para no falsear la trazabilidad). El `.bimlink.json` también
  marca esa partida como `EsOverrideManual: true`.
- **TCQ vinculación automática**: la vinculación IfcGUID en TCQ se hace a través
  del campo comentario del `~M`. Si TCQ actualiza su formato en versiones
  futuras, puede ser necesario ajustar el generador de BC3.
- **Sin Google Sheets** (SheetLink P3): la integración directa con Google Sheets
  está pendiente de Sprint 13.
- **Sin tests automatizados en CI**: la compilación es manual desde Visual
  Studio (Compilación → Recompilar solución). No hay pipeline de CI/CD en esta
  versión de desarrollo.

---

## 5. Visor BC3 local (debug)

El archivo `bc3/Visor_BC3.html` permite inspeccionar visualmente un `.bc3`
exportado sin necesidad de Presto ni TCQ:

1. Abre `Visor_BC3.html` en cualquier navegador moderno.
2. Haz clic en **📂 Seleccionar fitxer BC3...** y elige el `.bc3` exportado.
3. Verás los capítulos, partidas, cantidades y las líneas de medición agrupadas
   por sistema MEP + nivel.
4. El indicador de **cobertura IfcGUID** (verde/amarillo/rojo) muestra el
   porcentaje de líneas con un GUID IFC de 22 caracteres válido.

---

*Documento generado automáticamente por Claude (Cowork mode) — e3gbimtools Sprint A5 · Mayo 2026*
