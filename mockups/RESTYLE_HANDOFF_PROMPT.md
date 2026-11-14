# PROMPT DE HANDOFF — Aplicar restyling Fluent Light a TODAS las ventanas

> Pega esto como mensaje inicial en una sesión nueva (modo caveman opcional).
> Proyecto: **e3gbimtools** (Revit 2025, WPF, net8.0-windows) en `C:\Dev\E3GBIMTools`. Shell: `powershell.exe`.

---

## CONTEXTO

En una sesión previa se diseñó y **aprobó** un prototipo de restyling "Fluent Light" sobre
una barra de título personalizada. La fuente de verdad VISUAL es el mockup autocontenido:

- **`C:\Dev\E3GBIMTools\mockups\TitleBarMockup.xaml`** (Window sin x:Class, brushes inline)
- Render para revisar: `powershell.exe -ExecutionPolicy Bypass -File C:\Dev\E3GBIMTools\mockups\Show-TitleBarMockup.ps1`

**ABRE Y RENDERIZA EL MOCKUP ANTES DE TOCAR NADA** para ver el objetivo exacto.

El objetivo de ESTA sesión: trasladar ese diseño a **producción** y propagarlo a las **23 ventanas**
de la app, empezando por la de referencia `RevitBC3MainWindow`.

---

## DISEÑO APROBADO (resumen de lo que hay que reproducir)

1. **Chrome propio**: `WindowStyle="None"` + `System.Windows.Shell.WindowChrome`
   (CaptionHeight=44, ResizeBorderThickness=8). Botones nativos de Windows fuera.
2. **Barra de título custom** (alto 44): logo badge "MEP" (azul) + "e3gbimtools — Revit → BC3"
   + separador + estado de conexión (dot + "Revit conectado"). A la derecha: separador vertical
   + 3 botones caption propios.
3. **Botones caption** (min / restaurar / cerrar): redondeados (CornerRadius 8), espaciados,
   glifos **Lucide** (Path, stroke 2, caps/joins redondeados, viewBox 24 escalado a ~15px con Viewbox).
   - min = "minus", restaurar = "copy" (2 cuadros), cerrar = "x".
   - Hover min/restaurar: tinte azul de marca + glifo azul. Hover cerrar: rojo `#D13438` + glifo blanco.
   - `WindowChrome.IsHitTestVisibleInChrome="True"` en cada botón.
4. **Línea de acento** de marca: 1px degradado azul que se desvanece en los bordes (sutil).
5. **Esquinas redondeadas** de ventana + sombra (vía `AllowsTransparency="True"`). ⚠️ Ver RIESGO abajo.
6. **Breadcrumb de módulo** en el cuerpo (Inicio › chip azul "Mediciones BC3"), NO en la barra.
7. **Toolbar de cuerpo**: botones PLANOS (sin borde) por tono dentro de la gama azul/gris:
   - 1 CTA primario relleno azul (`Primary`), 1 secundario tinte azul (`Brand.Tint`/`Accent`),
     resto **ghost** gris (transparente, hover gris superficie).
   - Cada botón con icono Lucide + texto. Estados **hover** y **pressed** definidos.
   - Separador vertical entre grupo entrada y grupo salida.
8. **Foco de teclado visible**: anillo azul (`FocusVisualStyle`).
9. **SIN VERDE decorativo**: criterio = **azul (acción/acento) + gris (neutro)**. El verde `Secondary`
   se conserva SOLO como color semántico de "éxito" real, nunca como acento de UI.
10. **Tono "subido"** (menos pálido) en todas las superficies. Valores aprobados (del mockup):
    - Body `#EFEDEA` · TitleBar `#DFDCD7` · Surface/Header `#E6E3DF` · Card `#F6F4F1` · Border `#CDC9C4`
    - Estados botón: Primary.Hover `#0E5FA6` · Primary.Pressed `#0B5394` ·
      Tint.Hover `#D2E4F7` · Tint.Pressed `#BFD8F2` · Ghost.Hover `#E0DCD6` · Ghost.Pressed `#D2CDC6`
    - Acentos: Primary `#0F6CBD` · Accent `#2B88D8` · Brand.Tint `#E6F1FB` · Close.Hover `#D13438`
11. **Tipografía Inter** — YA está embebida en el tema (`E3g.Font.Family`). En producción se usa el
    TOKEN, NO la ruta de archivo del mockup.

---

## ARQUITECTURA DEL TEMA (cómo está montado)

- Tema se carga **en runtime** con `E3gWindowBase.ApplyTheme(this)` llamado en el constructor de cada
  ventana DESPUÉS de `InitializeComponent()`. Fichero: `src/e3gbimtools.DiTools.UI/Controls/E3gWindowBase.cs`.
- `Themes/E3gTheme.xaml` mergea → `Colors.xaml` + `Typography.xaml` + `Controls.xaml`.
- **Colores/brushes** → `src/e3gbimtools.DiTools.UI/Themes/Colors.xaml` (tokens `E3g.Color.*` / `E3g.Brush.*`).
- **Estilos reutilizables** → `src/e3gbimtools.DiTools.UI/Themes/Controls.xaml`.
- Inter embebida → `Typography.xaml` (`E3g.Font.Family` = pack URI a `Fonts/#Inter`).

### ⚠️ REGLA CRÍTICA — DynamicResource
Todas las referencias `E3g.*` en el CUERPO de un XAML deben ser **`{DynamicResource}`** (el tema se
mergea tras `InitializeComponent`). Solo los recursos definidos inline en el propio `Window.Resources`
pueden usar `{StaticResource}`. Romper esto = crash al abrir (ya pasó con TableGen).

---

## PLAN DE TRABAJO SUGERIDO

### Fase 1 — Tokens y estilos compartidos (una vez, propagación automática)
1. **`Colors.xaml`**: subir el tono de los tokens base a los valores aprobados (punto 10). Añadir tokens
   que falten (Accent ya existe; añadir hover/pressed si se decide tokenizarlos). Mantener `Secondary`
   verde solo como semántico de éxito.
2. **`Controls.xaml`**: añadir estilos reutilizables:
   - `E3g.Style.CaptionButton` y `E3g.Style.CaptionButton.Close` (glifos Lucide, hover/pressed).
   - `E3g.Style.TitleBar` (o un UserControl `E3gTitleBar` con DPs: Title, Subtitle, ShowMaximize).
   - `E3g.Style.Toolbar.Primary` / `.Tint` / `.Ghost` (planos, hover+pressed, foco).
   - `E3g.Style.FocusRing`.
   - Copiar los Path data de glifos Lucide del mockup (o referenciar `assets/icons/lucide/*.svg`).
3. **Chrome + lógica de ventana**: crear helper `E3gWindowChrome` (o ampliar `E3gWindowBase`) que:
   - Aplique `WindowChrome` y `WindowStyle=None`.
   - Cablee min / restaurar-maximizar / cerrar / arrastre.
   - Maneje el **padding al maximizar** (con WindowStyle=None la ventana maximizada se sale de pantalla
     y tapa la barra de tareas; corregir con `WindowChrome` + ajuste de margen al estado Maximized).

### Fase 2 — Ventana de referencia
4. Aplicar todo a **`src/e3gbimtools.RevitBC3/Views/RevitBC3MainWindow.xaml`** (+ code-behind).
   Sustituir la actual "banda de marca" azul por la barra de título custom. Verificar con Revit.

### Fase 3 — Propagar al resto (22 ventanas)
Mismo patrón en (algunas son diálogos pequeños — barra simplificada, p. ej. solo cerrar):
```
src/e3gbimtools.Calc/Views/CalcWindow.xaml
src/e3gbimtools.Calc/Views/SchematicPreviewWindow.xaml
src/e3gbimtools.DiTools/Views/AboutWindow.xaml
src/e3gbimtools.DiTools.UI/Dialogs/FriendlyErrorDialog.xaml
src/e3gbimtools.DiTools.UI/Views/CopyLevels/CopyLevelsWindow.xaml
src/e3gbimtools.DiTools.UI/Views/FamilyReviser/FamilyReviserWindow.xaml
src/e3gbimtools.DiTools.UI/Views/OneFilter/BatchEditDialog.xaml
src/e3gbimtools.DiTools.UI/Views/OneFilter/OneFilterWindow.xaml
src/e3gbimtools.DiTools.UI/Views/OneFilter/SimpleColorDialog.xaml
src/e3gbimtools.DiTools.UI/Views/OneFilter/TextInputDialog.xaml
src/e3gbimtools.DiTools.UI/Views/OneParameter/FindReplaceDialog.xaml
src/e3gbimtools.DiTools.UI/Views/OneParameter/OneParameterWindow.xaml
src/e3gbimtools.DiTools.UI/Views/ParaManager/ParaManagerWindow.xaml
src/e3gbimtools.DiTools.UI/Views/SectionBoxer/SectionBoxerWindow.xaml
src/e3gbimtools.DiTools.UI/Views/SheetGen/SheetGenWindow.xaml
src/e3gbimtools.DiTools.UI/Views/SheetLink/SheetLinkWindow.xaml
src/e3gbimtools.DiTools.UI/Views/TableGen/TableGenWindow.xaml
src/e3gbimtools.DiTools.UI/Views/ViewManager/ViewManagerWindow.xaml
src/e3gbimtools.RevitBC3/Views/CatalogPickerDialog.xaml
src/e3gbimtools.RevitBC3/Views/ChapterEditorDialog.xaml
src/e3gbimtools.RevitBC3/Views/NewRuleDialog.xaml
src/e3gbimtools.RevitBC3/Views/RuleDiagnosticDialog.xaml
```

---

## REGLAS TÉCNICAS CRÍTICAS (no romper)

- **DynamicResource** para todo `E3g.*` en el cuerpo (ver arriba).
- **Build SIEMPRE Debug, SIN `/p:Platform`**. El addin
  (`%APPDATA%\Autodesk\Revit\Addins\2025\e3gbimtools.DiTools.dev.addin`) carga desde
  `bin\Debug\net8.0-windows\`. Release o "Any CPU" dejan la DLL donde Revit NO la lee → cambios fantasma.
  MSBuild: `'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'`.
- Tras editar: build Debug, **confirmar timestamp fresco de las 4 DLLs** en `bin\Debug`, y pedir al
  usuario **cerrar y reabrir Revit por completo**.
- **Custom chrome / Revit-owned**: estas ventanas abren con el hWnd de Revit como owner. Verificar
  SIEMPRE en ese contexto: arrastre, min, restaurar/maximizar (incl. padding al maximizar) y cierre.
- **RIESGO `AllowsTransparency="True"`** (esquinas redondeadas + sombra): puede dar problemas de
  rendimiento o de arrastre/airspace en ventanas owned por Revit. Si falla:
  alternativa A = `WindowChrome.CornerRadius` sin transparencia; alternativa B = esquinas rectas.
  Probar pronto; no dejar que bloquee el resto.
- **UTF-8**: cuidado al editar XAML con acentos/símbolos (€, →, ·). Hubo corrupción en ~13 ventanas en
  el pasado. Mantener codificación UTF-8 y verificar glifos tras editar.

---

## DEFINICIÓN DE HECHO

- Todas las ventanas con barra de título custom + botones propios coherentes con el mockup.
- Paleta tono "subido" aplicada globalmente; cero verde decorativo.
- Build Debug OK, 4 DLLs frescas en `bin\Debug`, sin crashes al abrir cada ventana en Revit.
- Diálogos pequeños con barra simplificada (al menos botón cerrar custom).
```
```

---

### NOTA sobre el mockup
El mockup usa brushes inline y `FontFamily="file:///.../Inter/extras/ttf/#Inter"` SOLO porque es
autocontenido (render sin compilar). En producción NO se copian esos valores literales: se usan los
tokens `E3g.*` (DynamicResource) y `E3g.Font.Family`. El mockup es la referencia VISUAL, no el código a copiar tal cual.
