# Reskin e3gbimtools — Tabla maestra de mapeo (fuente de verdad)

Objetivo: cero hex hardcodeado fuera de `Colors.xaml`. Cada hex viejo → token
semántico `E3g.*`. El valor destino (Fluent Light) vive SOLO en `Colors.xaml`.

Regla de oro: la migración cambia hex → `{DynamicResource E3g.Brush.X}`.
NO se hardcodea ningún color nuevo en los 31 ficheros.

## Tokens existentes (se conservan las claves)

| Token clave              | Valor ACTUAL | Valor DESTINO (Fluent Light) |
|--------------------------|--------------|------------------------------|
| E3g.Color.Primary        | #4A7A96      | #0F6CBD  (Fluent brand)      |
| E3g.Color.Secondary      | #1D9E75      | #107C41  (verde acción)      |
| E3g.Color.Tertiary       | #6B7280      | #616161                      |
| E3g.Color.Accent         | #2E75B6      | #2B88D8                      |
| E3g.Color.BrandStart     | #4A7A96      | #0F6CBD                      |
| E3g.Color.BrandEnd       | #1D9E75      | #2B88D8                      |
| E3g.Color.OnBrand        | #FFFFFF      | #FFFFFF                      |
| E3g.Color.OnBrandMuted   | #CFE0E8      | #D6E8FA                      |
| E3g.Color.Background     | #FFFFFF      | #FFFFFF                      |
| E3g.Color.Surface        | #F4F6F8      | #F3F2F1  (Fluent neutralLight)|
| E3g.Color.SurfaceAlt     | #E7ECF0      | #EDEBE9                      |
| E3g.Color.Border         | #D5DCE2      | #E1DFDD                      |
| E3g.Color.BorderStrong   | #BCC6CF      | #C8C6C4                      |
| E3g.Color.Text           | #1F2A37      | #201F1E  (neutralPrimary)    |
| E3g.Color.TextMuted      | #5C6A7E      | #605E5C  (neutralSecondary)  |
| E3g.Color.TextSoft       | #8A95A5      | #A19F9D  (neutralTertiary)   |
| E3g.Color.TextOnPrimary  | #FFFFFF      | #FFFFFF                      |
| E3g.Color.Success        | #1D9E75      | #107C41                      |
| E3g.Color.Warning        | #E0A800      | #FFB900                      |
| E3g.Color.Error          | #D9534F      | #D13438                      |

## Tokens NUEVOS a añadir (gaps detectados en hex hardcodeado)

| Token clave              | Valor DESTINO | Cubre hex viejos                         |
|--------------------------|---------------|------------------------------------------|
| E3g.Color.SurfaceSubtle  | #FAF9F8       | #F8FAFC #F5F5F5 #F1F5F9 #FAFBFC #F0F0F0  |
| E3g.Color.BorderSubtle   | #EDEBE9       | #DDE1E5 #DDE3E9 #CBD2DC #E0E0E0 #CCCCCC  |
| E3g.Color.GridLine       | #E1DFDD       | #E8ECF0 (HorizontalGridLinesBrush)       |
| E3g.Color.SuccessText    | #0E5E31       | #065F46 #3B6D11                          |
| E3g.Color.SuccessBg      | #DFF6E5       | #DCFCE7 #D1FAE5 #AFD9C5                   |
| E3g.Color.WarningText    | #8A5A00       | #92400E                                  |
| E3g.Color.WarningBg      | #FFF4CE       | #FEF3C7 #FFF8E1 #FFF3CD #FFC107          |
| E3g.Color.ErrorText      | #A4262C       | #B91C1C #C0392B                          |
| E3g.Color.ErrorBg        | #FDE7E9       | #FFEBEE                                   |

## Mapeo hex viejo → token (por rol/atributo)

Greys de superficie / borde (los más frecuentes):
- #C8CDD2  (Color/Fill/Border) → E3g.Brush.Border / E3g.Color.Border
- #E8ECF0  (Background/Fill/Value) → E3g.Brush.SurfaceAlt
- #E8ECF0  (HorizontalGridLinesBrush) → E3g.Brush.GridLine
- #E8ECF0  (BorderBrush) → E3g.Brush.Border
- #DDE1E5 #DDE3E9 #CBD2DC #E0E0E0 #CCCCCC → E3g.Brush.BorderSubtle
- #F8FAFC #F5F5F5 #F1F5F9 #FAFBFC #F0F0F0 → E3g.Brush.SurfaceSubtle
- #FFFFFF #FFFFFFFF → E3g.Brush.Background  (o OnBrand si va sobre banda)

Texto (de oscuro a claro):
- #1E293B → E3g.Brush.Text
- #475569 #555555 #5C6A7E → E3g.Brush.TextMuted
- #94A3B8 #B0B8C4 #CBD5E1 #8A95A5 → E3g.Brush.TextSoft

Marca / acentos:
- #4A7A96 → E3g.Brush.Primary
- #1D9E75 → E3g.Brush.Secondary  (o Success en contexto estado)
- #2E75B6 → E3g.Brush.Accent

Semánticos:
- #B91C1C #C0392B (Foreground) → E3g.Brush.ErrorText ; #FFEBEE (bg) → E3g.Brush.ErrorBg
- #92400E (Foreground) → E3g.Brush.WarningText ; #FEF3C7 #FFF8E1 #FFF3CD (bg) → E3g.Brush.WarningBg
- #065F46 #3B6D11 (Foreground) → E3g.Brush.SuccessText ; #DCFCE7 #D1FAE5 (bg) → E3g.Brush.SuccessBg

Long-tail one-offs (83): revisar 1×1 al migrar cada fichero; asignar al token
del mismo rol y rango de luminancia. Si no encaja en ninguno, anotar aquí.

## Fuente
- E3g.Font.Family: `Lato, Segoe UI, Arial` → `Inter, Segoe UI, Arial`
  (Inter embebida: ver Typography.xaml + csproj Resource).
- FontFamily hardcodeadas (3) → reemplazar por `{DynamicResource E3g.Font.Family}`.
