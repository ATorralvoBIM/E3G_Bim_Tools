# Handoff — Reskin "Fluent Light" de e3gbimtools

## Contexto
- Dos productos en un repo:
  - `e3gbimtools.sln` (carpeta `src/*`) — ESTE es el target del reskin. **UNTRACKED en git.**
  - `E3G_Saneamiento.sln` (XAML root-level) — legacy, git-tracked, NO tocar.
- Plugin Revit 2025, WPF, net8.0-windows, x64.
- Tema vive en `src/e3gbimtools.DiTools.UI/Themes/` (`Colors.xaml`, `Typography.xaml`, `E3gTheme.xaml`).
- Cada Window necesita `E3gWindowBase.ApplyTheme(this);` tras `InitializeComponent();`
  (namespace `E3G.BimTools.DiTools.UI.Controls`).
- Entorno: usar `powershell.exe` (NO `pwsh`). Revit DLLs en `E:\APPS\Autodesk\Revit 2025\`.
  Compilar con `dotnet build e3gbimtools.sln` (el .sln YA compila limpio).

## HECHO (actualizado 2026-05-30)
- `Themes/Colors.xaml` → paleta Fluent Light, conserva todas las keys `E3g.Color.*`/
  `E3g.Brush.*` + 9 tokens nuevos.
- Inter embebida (`Typography.xaml` + `Fonts/Inter-*.ttf` + `<Resource>` en csproj).
- 583 hex migrados a tokens. Único hex intencional: `#33FFFFFF` (scrim) en RevitBC3MainWindow.xaml.
- 6 ventanas con `ApplyTheme`.
- **`.sln` arreglado: compila limpio (0 errores).**
- **Bug `SetIcon` (recursión infinita en fallback PNG) — ARREGLADO 2026-05-30.**
- **INTERIOR DE VENTANAS — HECHO 2026-05-30 (0 errores build):**
  - `E3gWindowBase.ApplyTheme` ahora también fija `Window.Background` (#FFFFFF).
  - `Controls.xaml`: estilo implícito `Button` añadido (Fluent sutil: Surface bg, CornerRadius=6, hover).
  - `SectionBoxerWindow` + `SheetGenWindow`: `ApplyTheme` añadido al constructor.
  - `CalcWindow.xaml`: colores `E3g.Color.*` definidos inline (fix: brushes DynamicResource no resolvían).
    Font Inter + Background blancos fijados directamente en el elemento Window.
  - 6 XAML hardcoded migrados a tokens: `#DDD`→`E3g.Brush.Border`, `#666`→`E3g.Brush.TextMuted`,
    `#444`→`E3g.Brush.TextMuted`, `#333`→`E3g.Brush.Text`, `#555`→`E3g.Brush.TextMuted`,
    `#CCC`→`E3g.Brush.Border`. Archivos: CopyLevels, OneFilter, BatchEdit, ParaManager,
    ViewManager, SectionBoxer, SheetGen (sep.), FamilyReviser.
  - `Background="{DynamicResource E3g.Brush.Background}"` añadido a todas las ventanas afectadas.
- **ICONOS (Lucide) — HECHO, compila limpio (0 errores). FALTA solo render en Revit:**
  - Nuevo `src/e3gbimtools.DiTools/Ribbon/LucideIcons.cs`: convierte trazos Lucide
    (viewBox 24×24, ISC license) a `DrawingImage` vectorial. Sin rasterizar, escala
    nítido 16/32px. Color marca #0F6CBD. 16 iconos definidos.
  - `RibbonBuilder.cs`: añadido `SetIcon(btn)` + `IconKeyFor(name)`; reemplazados los
    16 bloques `TryLoadIcon(...)` repetidos por `SetIcon(btn)`. PNG heredado queda como
    fallback dentro de SetIcon.
  - SVG fuente en `assets/icons/lucide/*.svg` (referencia).
  - Mapeo: CopyLevels=layers, OneFilter=funnel, ParaManager=sliders, ViewManager=layout,
    SectionBoxer=box, SheetGen=fileplus, FamilyReviser=boxes, TableGen=table,
    OneParameter=tag, SheetLink=link, RevitBC3=receipt, CalcPiping=droplets,
    CalcDuct=wind, CalcWindow=calculator, CalcSelfTest=flask, About=info.

## PENDIENTE INMEDIATO
1. **Validar render en Revit 2025** — cargar addin y revisar con el checklist de abajo.
2. Ajustar si algo no convence (tokens, espaciado, contraste).

## CHECKLIST VALIDACIÓN REVIT
Carga el addin (Debug build en `src/e3gbimtools.DiTools/bin/Debug/net8.0-windows/`) y verifica:

**Ribbon (tab "e3gbimtools")**
- [ ] Panel "Inicio Proyecto": botón About → icono `info` (círculo + i)
- [ ] Panel "DiTools": 10 botones de herramienta, cada uno con su icono Lucide distinto
      (CopyLevels=layers, OneFilter=funnel, ParaManager=sliders, ViewManager=layout,
       SectionBoxer=box, SheetGen=fileplus, FamilyReviser=boxes, TableGen=table,
       OneParameter=tag, SheetLink=link)
- [ ] Panel "Mediciones BC3": Revit→BC3 → icono `receipt`
- [ ] Panel "Cálculo MEP": 4 botones (droplets, wind, calculator, flask)
- [ ] Iconos nítidos en 32px (LargeImage) y 16px (Image) — color #0F6CBD

**Ventanas (abrir cada herramienta)**
- [ ] Fondo blanco/gris muy claro (Fluent Light, NO el gris oscuro anterior)
- [ ] Fuente Inter en textos (sans-serif redondeada, no Segoe UI)
- [ ] Acento azul #0F6CBD en botones primarios
- [ ] Sin textos ilegibles (contraste texto/fondo correcto)

**Si falla algo:**
- Iconos no aparecen → buscar en `%APPDATA%\Autodesk\Revit\Addins\2025\` si el .addin
  apunta al ensamblado correcto
- Inter no carga → comprobar que las .ttf están en el DLL (build action Resource en
  `e3gbimtools.DiTools.UI.csproj`)
- Tema no se aplica → verificar que las 6 ventanas llaman `E3gWindowBase.ApplyTheme(this)`
  DESPUÉS de `InitializeComponent()`

## RIESGOS ABIERTOS
- `src/` UNTRACKED en git → sin red de seguridad. Recomendado `git add src/` + commit.
- Proyecto Calc inyecta `E3g.Color.*` localmente, no referencia DiTools.UI — modelo opaco.
- "Compila" ≠ "renderiza bien" — solo verificable en Revit 2025.

---

## PROMPT PARA NUEVA SESIÓN

> Continúo un reskin "Fluent Light" de la app e3gbimtools (plugin Revit 2025, WPF,
> net8.0-windows). Target = `e3gbimtools.sln` / carpeta `src/*` en C:\Dev\E3GBIMTools.
> Usa `powershell.exe` (no pwsh). Lee `assets/ui-kit/HANDOFF.md` para el estado completo.
>
> Estado: TODO compila limpio (`dotnet build e3gbimtools.sln` = 0 errores). Reskin completo
> en código: paleta Fluent Light, fuente Inter embebida, 583 hex migrados, e iconos
> vectoriales Lucide en el ribbon (`src/e3gbimtools.DiTools/Ribbon/LucideIcons.cs` +
> `RibbonBuilder.cs` con `SetIcon`/`IconKeyFor`).
>
> Lo único que falta es VALIDAR EL RENDER en Revit 2025 (no verificable fuera): cargar el
> addin y revisar ribbon (iconos por comando), ventanas (paleta Fluent Light) y fuente Inter.
> Si algo no convence, ajustar mapeo/color de iconos o tokens. Modo caveman activo.
