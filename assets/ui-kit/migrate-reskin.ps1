<#
    migrate-reskin.ps1
    Reemplaza hex hardcodeado -> {DynamicResource E3g.Brush.*} en los XAML.
    Consciente del atributo: el mismo hex puede mapear a tokens distintos
    segun sea Background / BorderBrush / Foreground / grid, etc.

    Reglas = traduccion de assets/ui-kit/RESKIN_MAPPING.md
    Solo toca los hex bien entendidos. Los one-offs no mapeados se DEJAN
    y se listan en el reporte final para revision manual.

    Uso:
      powershell -ExecutionPolicy Bypass -File .\migrate-reskin.ps1            # aplica
      powershell -ExecutionPolicy Bypass -File .\migrate-reskin.ps1 -DryRun    # solo reporta
#>
[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# Atributos cuyo valor es un color de PINCEL (mapean a E3g.Brush.*)
# Incluye Value (Setters de Background/Foreground/etc.) que casi siempre es brush.
$brushAttrs = 'Background|Foreground|BorderBrush|Fill|Stroke|HorizontalGridLinesBrush|VerticalGridLinesBrush|SelectionBrush|Value'
# Atributos cuyo valor es un Color TIPADO (mapean a E3g.Color.*):
# SolidColorBrush Color=, GradientStop Color=, DropShadowEffect Color=
$colorAttrs = 'Color'

# Mapa por DEFECTO: hex normalizado (6 dig, mayus) -> token brush
$map = @{
    '0F6CBD'='Primary'; '4A7A96'='Primary'
    '107C41'='Secondary'; '1D9E75'='Secondary'
    '2E75B6'='Accent'; '2B88D8'='Accent'
    'FFFFFF'='Background'
    'F8FAFC'='SurfaceSubtle'; 'F5F5F5'='SurfaceSubtle'; 'F1F5F9'='SurfaceSubtle'; 'FAFBFC'='SurfaceSubtle'; 'F0F0F0'='SurfaceSubtle'
    'F3F2F1'='Surface'
    'E8ECF0'='SurfaceAlt'
    'EDEBE9'='SurfaceAlt'
    'DDE1E5'='BorderSubtle'; 'DDE3E9'='BorderSubtle'; 'CBD2DC'='BorderSubtle'; 'E0E0E0'='BorderSubtle'; 'CCCCCC'='BorderSubtle'
    'C8CDD2'='Border'; 'E1DFDD'='Border'
    'C8C6C4'='BorderStrong'
    '201F1E'='Text'; '1E293B'='Text'
    '475569'='TextMuted'; '555555'='TextMuted'; '5C6A7E'='TextMuted'; '605E5C'='TextMuted'
    '94A3B8'='TextSoft'; 'B0B8C4'='TextSoft'; 'CBD5E1'='TextSoft'; '8A95A5'='TextSoft'; 'A19F9D'='TextSoft'
    'B91C1C'='ErrorText'; 'C0392B'='ErrorText'; 'D13438'='Error'
    '92400E'='WarningText'; 'FFB900'='Warning'
    '065F46'='SuccessText'; '3B6D11'='SuccessText'
    'FFEBEE'='ErrorBg'
    'FEF3C7'='WarningBg'; 'FFF8E1'='WarningBg'; 'FFF3CD'='WarningBg'
    'DCFCE7'='SuccessBg'; 'D1FAE5'='SuccessBg'
    # --- 2a ronda: cola larga mapeable (revisada por rol/luminancia) ---
    '1F2A37'='Text'; '222222'='Text'; '333333'='Text'; '2B3242'='Text'; '3B4252'='Text'
    '6B7280'='Tertiary'; '595959'='TextMuted'; '64748B'='TextMuted'; '334155'='TextMuted'; '9CA3AF'='TextSoft'
    '888888'='TextSoft'; '999999'='TextSoft'; 'AAAAAA'='TextSoft'; 'A8A8A8'='TextSoft'; 'BDBDBD'='TextSoft'; 'BFBFBF'='TextSoft'; 'B0BEC5'='TextSoft'
    'F8FAFB'='SurfaceSubtle'; 'FAFAFA'='SurfaceSubtle'; 'F7F8FA'='SurfaceSubtle'; 'F7F7F7'='SurfaceSubtle'; 'F9F9F9'='SurfaceSubtle'; 'F0F4F8'='SurfaceSubtle'; 'F5F8FB'='SurfaceSubtle'; 'F1F8FB'='SurfaceSubtle'; 'F2F7FF'='SurfaceSubtle'; 'FAFBFD'='SurfaceSubtle'; 'ECF0F3'='SurfaceSubtle'; 'EFF2F6'='SurfaceSubtle'
    'EEEEEE'='BorderSubtle'; 'EBEBEB'='BorderSubtle'; 'E8E8E8'='BorderSubtle'; 'D8D8D8'='BorderSubtle'; 'D0D5DA'='BorderSubtle'; 'D6E8F0'='BorderSubtle'
    '1A5490'='Primary'; '2E6DA4'='Primary'; '255F94'='Primary'; '2D5A74'='Primary'; '2D5F7A'='Primary'; '3A6A86'='Primary'
    '7AAABB'='Accent'; '5A8A9B'='Accent'; '6A9AAB'='Accent'; '378ADD'='Accent'; '1E40AF'='Accent'
    'B5D2EE'='OnBrandMuted'; 'CCE0F5'='OnBrandMuted'; 'CCE8FF'='OnBrandMuted'; 'B66E0F'='WarningText'
    '166534'='SuccessText'; '147A5A'='SuccessText'; '3D8A5E'='SuccessText'; '116E50'='SuccessText'; '178A64'='SuccessText'; '3A957C'='SuccessText'; '0E5E31'='SuccessText'
    'E8F5E9'='SuccessBg'; 'E6F7EF'='SuccessBg'; 'F0FDF4'='SuccessBg'; 'DFF6E5'='SuccessBg'; 'C8E6B8'='SuccessBg'; '86EFAC'='SuccessBg'
    '856404'='WarningText'; 'B45309'='WarningText'; 'E65100'='WarningText'; '7A5C00'='WarningText'; '8A5A00'='WarningText'
    'FFFDE7'='WarningBg'; 'FDF6E3'='WarningBg'; 'FEF9C3'='WarningBg'; 'FFF7E0'='WarningBg'; 'FFF4E0'='WarningBg'; 'FFF4CE'='WarningBg'
    '991B1B'='ErrorText'; 'A4262C'='ErrorText'; 'E24B4A'='Error'; 'D9534F'='Error'
    'F5DEDE'='ErrorBg'; 'FAE6E3'='ErrorBg'; 'FEE2E2'='ErrorBg'; 'FDE7E9'='ErrorBg'
    '7E52A0'='Module.Sheet'; '7F77DD'='Module.Sheet'; '5B21B6'='Module.Sheet'; '4C1D95'='Module.Sheet'; '5B2A86'='Module.Sheet'
    'DBEAFE'='Module.FilterBg'; 'E7F0FA'='Module.FilterBg'
    # --- 3a ronda: ultimos 20 one-offs (33FFFFFF se deja: scrim translucido) ---
    'E2E6EC'='BorderSubtle'; 'C8D8E0'='BorderSubtle'
    'FACACA'='ErrorBg'; 'CC2222'='Error'; 'FF4500'='Error'
    'FFC107'='Warning'; 'EF9F27'='Warning'; 'E8D800'='Warning'; 'E0A800'='Warning'; 'E6A800'='Warning'
    '90A4AE'='TextSoft'; 'A8C7D8'='TextSoft'
    'E6F0FA'='Module.FilterBg'; 'EDE9FE'='Module.SheetBg'
    'AFD9C5'='SuccessBg'; '5BBFA0'='Secondary'; '4AAA8E'='Secondary'
    '1F4E79'='Primary'; '1AB3C4'='Accent'
    'A0522D'='WarningText'
}
# Excepciones por atributo: clave "ATTR|HEX" -> token (gana sobre $map)
$attrMap = @{
    'BorderBrush|E8ECF0'='Border'
    'HorizontalGridLinesBrush|E8ECF0'='GridLine'
    'VerticalGridLinesBrush|E8ECF0'='GridLine'
}

function Normalize($hex) {
    $h = $hex.TrimStart('#').ToUpper()
    if ($h.Length -eq 8) { if ($h.Substring(0,2) -eq 'FF') { $h = $h.Substring(2) } }
    return $h
}

$files = Get-ChildItem -Path $repo -Recurse -Filter *.xaml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'Colors.xaml' }

$rxBrush = [regex]('(?i)(?<attr>' + $brushAttrs + ')="(?<hex>#[0-9A-Fa-f]{6,8})"')
$rxColor = [regex]('(?i)(?<attr>' + $colorAttrs + ')="(?<hex>#[0-9A-Fa-f]{6,8})"')
$leftover = @{}
$totalRepl = 0; $touched = 0

foreach ($f in $files) {
    $text = Get-Content -Raw -LiteralPath $f.FullName

    # Pase 1: atributos de pincel -> E3g.Brush.*
    $new = $rxBrush.Replace($text, {
        param($m)
        $attr = $m.Groups['attr'].Value
        $hx   = Normalize $m.Groups['hex'].Value
        $tok  = $null
        if ($attrMap.ContainsKey("$attr|$hx")) { $tok = $attrMap["$attr|$hx"] }
        elseif ($map.ContainsKey($hx))         { $tok = $map[$hx] }
        if ($tok) { return "$attr=`"{DynamicResource E3g.Brush.$tok}`"" }
        return $m.Value
    })
    # Pase 2: Color= tipado -> E3g.Color.*
    $new = $rxColor.Replace($new, {
        param($m)
        $hx = Normalize $m.Groups['hex'].Value
        if ($map.ContainsKey($hx)) { return "Color=`"{DynamicResource E3g.Color.$($map[$hx])}`"" }
        return $m.Value
    })

    $n = ([regex]::Matches($new,'\{DynamicResource E3g\.').Count) - ([regex]::Matches($text,'\{DynamicResource E3g\.').Count)

    if ($new -ne $text) {
        $totalRepl += $n; $touched++
        if (-not $DryRun) { Set-Content -LiteralPath $f.FullName -Value $new -Encoding UTF8 -NoNewline }
        Write-Host ("  [{0,3}] {1}" -f $n, $f.FullName.Substring($repo.Length+1)) -ForegroundColor Green
    }

    # leftover hex (no mapeados) para revision
    foreach ($mm in [regex]::Matches($new,'(?i)#[0-9A-Fa-f]{6,8}')) {
        $hx = Normalize $mm.Value
        if (-not $leftover.ContainsKey($hx)) { $leftover[$hx] = 0 }
        $leftover[$hx]++
    }
}

Write-Host ("`nFicheros tocados: {0}   Reemplazos: {1}" -f $touched,$totalRepl) -ForegroundColor Cyan
Write-Host "`n== HEX SOBRANTES (revisar manual) ==" -ForegroundColor Yellow
$leftover.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host ("  #{0}  x{1}" -f $_.Key,$_.Value)
}
if ($DryRun) { Write-Host "`n(DryRun: nada escrito)" -ForegroundColor Magenta }
