using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace E3G.BimTools.RevitBC3.Tests;

// ============================================================
//  BimLinkingValidationTests
// ============================================================
//  Validador de archivos BC3 + .bimlink.json generados por
//  e3gbimtools.RevitBC3.
//
//  NO depende de Revit API: solo lee archivos del disco.
//  Se puede invocar desde la ventana RevitBC3MainWindow (boton
//  "Validar exportacion") o ejecutarse desde un script externo.
//
//  Checks implementados:
//    V1  Todos los IfcGUIDs del .bimlink.json son validos
//        (22 caracteres del alfabeto IFC Base64: 0-9 A-Z a-z _ $).
//    V2  Todos los codigos de partida del .bimlink.json existen
//        como conceptos en el .bc3 asociado.
//    V3  Para partidas sin override manual: la suma de cantidades
//        de los elementos del bimlink = cantidad total declarada
//        (tolerancia relativa 0.1 %).
//    V4  El .bc3 no contiene GUIDs con prefijo "RVT:" (legado).
//    V5  Todos los ~M del .bc3 con lineas detalladas tienen al
//        menos un GUID valido de 22 chars.
//    V6  La cobertura de GUID (DesdeParametro + DesdeCalculo)
//        declarada en EstadisticasGuid coincide con el recuento
//        real de elementos del bimlink.
// ============================================================

/// <summary>Resultado individual de un check de validacion.</summary>
public sealed class ValidationCheck
{
    public string Id      { get; init; } = string.Empty;
    public string Nombre  { get; init; } = string.Empty;
    public bool   Passed  { get; init; }
    public string Detalle { get; init; } = string.Empty;
}

/// <summary>Informe completo de una validacion.</summary>
public sealed class ValidationReport
{
    public string   BimlinkPath { get; init; } = string.Empty;
    public string   Bc3Path     { get; init; } = string.Empty;
    public DateTime Timestamp   { get; init; } = DateTime.Now;
    public List<ValidationCheck> Checks { get; init; } = new();

    public int Total    => Checks.Count;
    public int Passed   => Checks.Count(c => c.Passed);
    public int Failed   => Checks.Count(c => !c.Passed);
    public bool AllPassed => Failed == 0;

    /// <summary>Genera un resumen legible para mostrar en UI o log.</summary>
    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════╗");
        sb.AppendLine("║  VALIDACION BIM LINKING BC3-IFC — e3gbimtools.RevitBC3  ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════╝");
        sb.AppendLine($"  Fecha    : {Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  .bimlink : {Path.GetFileName(BimlinkPath)}");
        sb.AppendLine($"  .bc3     : {Path.GetFileName(Bc3Path)}");
        sb.AppendLine();

        foreach (var c in Checks)
        {
            var icon = c.Passed ? "✓" : "✗";
            sb.AppendLine($"  [{icon}] {c.Id} {c.Nombre}");
            if (!c.Passed || !string.IsNullOrEmpty(c.Detalle))
                sb.AppendLine($"       {c.Detalle}");
        }

        sb.AppendLine();
        sb.AppendLine($"  Resultado: {Passed}/{Total} checks pasados"
                      + (AllPassed ? " — OK ✓" : $" — {Failed} FALLO(S) ✗"));
        return sb.ToString();
    }
}

/// <summary>
/// Ejecuta todos los checks de validacion sobre un par .bimlink.json / .bc3.
/// </summary>
public static class BimLinkingValidationTests
{
    // Alfabeto IFC Base64 (22 chars de 64 caracteres posibles)
    private static readonly Regex IfcGuidRegex =
        new(@"^[0-9A-Za-z_$]{22}$", RegexOptions.Compiled);

    // Regex para extraer IfcGUID de una linea ~M (formato: [Pref]#GUID o #GUID)
    private static readonly Regex MLineGuidRegex =
        new(@"#([0-9A-Za-z_$]{22})(?:\\|$)", RegexOptions.Compiled);

    // Tolerancia para comparar cantidades (0.1 %)
    private const double QtyTolerance = 0.001;

    /// <summary>
    /// Ejecuta la validacion completa dado un archivo .bimlink.json.
    /// El .bc3 se busca automaticamente en la misma carpeta con el mismo
    /// nombre base.
    /// </summary>
    public static ValidationReport Run(string bimlinkPath)
    {
        if (!File.Exists(bimlinkPath))
            throw new FileNotFoundException("Archivo .bimlink.json no encontrado.", bimlinkPath);

        // Derivar ruta del .bc3 (mismo nombre, extension .bc3)
        var bc3Path = Path.ChangeExtension(
            bimlinkPath.Replace(".bimlink.json", string.Empty, StringComparison.OrdinalIgnoreCase),
            ".bc3");

        return Run(bimlinkPath, bc3Path);
    }

    /// <summary>
    /// Ejecuta la validacion completa dado rutas explicitas.
    /// </summary>
    public static ValidationReport Run(string bimlinkPath, string bc3Path)
    {
        var checks = new List<ValidationCheck>();

        // ── Cargar .bimlink.json ──────────────────────────────────────────
        BimLinkDocumentJson? bimlink = null;
        try
        {
            var json = File.ReadAllText(bimlinkPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            bimlink = JsonSerializer.Deserialize<BimLinkDocumentJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            checks.Add(Fail("LOAD1", "Cargar .bimlink.json",
                $"No se pudo leer el archivo: {ex.Message}"));
            return new ValidationReport
            {
                BimlinkPath = bimlinkPath,
                Bc3Path     = bc3Path,
                Checks      = checks,
            };
        }

        if (bimlink is null)
        {
            checks.Add(Fail("LOAD1", "Cargar .bimlink.json", "El JSON esta vacio o mal formado."));
            return new ValidationReport { BimlinkPath = bimlinkPath, Bc3Path = bc3Path, Checks = checks };
        }

        // ── Cargar .bc3 ───────────────────────────────────────────────────
        HashSet<string>? bc3Codigos = null;
        List<string>?    bc3MLines  = null;
        bool             bc3Loaded  = false;

        if (File.Exists(bc3Path))
        {
            try
            {
                var bc3Text = ReadBc3(bc3Path);
                (bc3Codigos, bc3MLines) = ParseBc3Lightweight(bc3Text);
                bc3Loaded = true;
            }
            catch (Exception ex)
            {
                checks.Add(Fail("LOAD2", "Cargar .bc3", $"No se pudo leer el BC3: {ex.Message}"));
            }
        }
        else
        {
            checks.Add(Fail("LOAD2", "Cargar .bc3",
                $"El archivo .bc3 no existe en: {bc3Path}"));
        }

        // ── V1: IfcGUIDs validos en bimlink ───────────────────────────────
        {
            var invalidos = new List<string>();
            foreach (var partida in bimlink.Partidas ?? Enumerable.Empty<BimLinkPartidaJson>())
            {
                foreach (var elem in partida.Elementos ?? Enumerable.Empty<BimLinkElementJson>())
                {
                    if (!IsValidIfcGuid(elem.IfcGuid))
                        invalidos.Add($"  Partida {partida.Codigo} / ElementId {elem.ElementId}: \"{elem.IfcGuid}\"");
                }
            }
            if (invalidos.Count == 0)
                checks.Add(Pass("V1", "IfcGUIDs validos (22 chars, alfabeto IFC Base64)",
                    $"Todos los GUIDs del bimlink son validos."));
            else
                checks.Add(Fail("V1", "IfcGUIDs validos (22 chars, alfabeto IFC Base64)",
                    $"{invalidos.Count} GUID(s) invalido(s):\n" + string.Join("\n", invalidos.Take(10))));
        }

        // ── V2: Codigos de partida existen en .bc3 ────────────────────────
        if (bc3Loaded && bc3Codigos is not null)
        {
            var faltantes = (bimlink.Partidas ?? Enumerable.Empty<BimLinkPartidaJson>())
                .Where(p => !string.IsNullOrEmpty(p.Codigo)
                         && !bc3Codigos.Contains(p.Codigo))
                .Select(p => p.Codigo)
                .ToList();

            if (faltantes.Count == 0)
                checks.Add(Pass("V2", "Codigos de partida existen en el BC3",
                    "Todos los codigos del bimlink se encuentran en el BC3."));
            else
                checks.Add(Fail("V2", "Codigos de partida existen en el BC3",
                    $"{faltantes.Count} codigo(s) del bimlink NO estan en el BC3: "
                    + string.Join(", ", faltantes.Take(10))));
        }
        else if (!bc3Loaded)
        {
            checks.Add(Skip("V2", "Codigos de partida existen en el BC3",
                "Saltado: no se pudo cargar el BC3."));
        }

        // ── V3: Suma de cantidades == total declarado ─────────────────────
        {
            var errores = new List<string>();
            foreach (var p in bimlink.Partidas ?? Enumerable.Empty<BimLinkPartidaJson>())
            {
                if (p.EsOverrideManual) continue; // override → no validar suma
                if (p.Elementos is null || p.Elementos.Count == 0) continue;

                var suma = p.Elementos.Sum(e => e.Cantidad);
                var expected = p.Cantidad;
                if (expected == 0) continue;

                var diff = Math.Abs(suma - expected) / expected;
                if (diff > QtyTolerance)
                    errores.Add($"  {p.Codigo}: suma elementos={suma:0.###} ≠ total={expected:0.###} (diff={diff:P2})");
            }
            if (errores.Count == 0)
                checks.Add(Pass("V3", "Suma de cantidades elemento == total partida",
                    "Todas las partidas cuadran (tolerancia 0.1%)."));
            else
                checks.Add(Fail("V3", "Suma de cantidades elemento == total partida",
                    $"{errores.Count} partida(s) con discrepancia:\n" + string.Join("\n", errores.Take(10))));
        }

        // ── V4: Sin GUIDs con prefijo "RVT:" en el BC3 ───────────────────
        if (bc3Loaded && bc3MLines is not null)
        {
            var rvtCount = bc3MLines.Count(l => l.Contains("RVT:", StringComparison.Ordinal));
            if (rvtCount == 0)
                checks.Add(Pass("V4", "Sin GUIDs legacy 'RVT:' en BC3",
                    "No se encontraron GUIDs con prefijo RVT: (correcto)."));
            else
                checks.Add(Fail("V4", "Sin GUIDs legacy 'RVT:' en BC3",
                    $"{rvtCount} linea(s) de medicion contienen GUIDs 'RVT:' invalidos. "
                    + "Recalcular mediciones para regenerar los GUIDs correctamente."));
        }
        else if (!bc3Loaded)
        {
            checks.Add(Skip("V4", "Sin GUIDs legacy 'RVT:' en BC3",
                "Saltado: no se pudo cargar el BC3."));
        }

        // ── V5: Lineas ~M con GUID valido ─────────────────────────────────
        if (bc3Loaded && bc3MLines is not null)
        {
            var sinGuid = 0;
            var conGuid = 0;
            foreach (var line in bc3MLines)
            {
                // Si la linea tiene campos de desglose (contiene '#'), comprobar
                var hashIdx = line.IndexOf('#');
                if (hashIdx < 0) continue; // linea sin desglose, no aplica
                var m = MLineGuidRegex.Match(line, hashIdx);
                if (m.Success) conGuid++;
                else sinGuid++;
            }
            var total = conGuid + sinGuid;
            if (total == 0)
            {
                checks.Add(Pass("V5", "Lineas de medicion con GUID IFC valido",
                    "No hay lineas de desglose en este BC3 (sin contribuciones detalladas)."));
            }
            else
            {
                var pct = conGuid * 100.0 / total;
                var msg = $"{conGuid}/{total} lineas ({pct:0.#}%) con GUID IFC valido de 22 chars.";
                if (pct >= 95.0)
                    checks.Add(Pass("V5", "Lineas de medicion con GUID IFC valido", msg));
                else if (pct >= 80.0)
                    checks.Add(new ValidationCheck
                    {
                        Id = "V5", Nombre = "Lineas de medicion con GUID IFC valido",
                        Passed = true, // advertencia pero no fallo
                        Detalle = $"⚠ ADVERTENCIA: {msg} (recomendado ≥95%)"
                    });
                else
                    checks.Add(Fail("V5", "Lineas de medicion con GUID IFC valido",
                        $"{msg} La cobertura es baja (<80%). "
                        + "Puede que algunos elementos no tengan UniqueId valido o el calculo fallo."));
            }
        }
        else if (!bc3Loaded)
        {
            checks.Add(Skip("V5", "Lineas de medicion con GUID IFC valido",
                "Saltado: no se pudo cargar el BC3."));
        }

        // ── V6: Cobertura declarada en bimlink coincide con recuento real ─
        {
            var stats = bimlink.EstadisticasGuid;
            if (stats is not null && stats.TotalElementos > 0)
            {
                // Reconteo real desde los elementos del bimlink
                int param = 0, calc = 0, fallback = 0;
                foreach (var p in bimlink.Partidas ?? Enumerable.Empty<BimLinkPartidaJson>())
                {
                    foreach (var e in p.Elementos ?? Enumerable.Empty<BimLinkElementJson>())
                    {
                        switch (e.OrigenGuid)
                        {
                            case "Parameter":  param++;    break;
                            case "Calculated": calc++;     break;
                            default:           fallback++; break;
                        }
                    }
                }
                int totalReal = param + calc + fallback;
                double coberturaReal = totalReal == 0 ? 0.0 : (param + calc) * 100.0 / totalReal;
                double coberturaDecl = stats.CoberturaPorc;
                var diff = Math.Abs(coberturaReal - coberturaDecl);

                if (diff < 1.0)
                    checks.Add(Pass("V6", "Cobertura GUID declarada coincide con recuento real",
                        $"Cobertura: {coberturaReal:0.#}% (Parameter={param}, Calculated={calc}, Fallback={fallback})."));
                else
                    checks.Add(Fail("V6", "Cobertura GUID declarada coincide con recuento real",
                        $"Cobertura declarada={coberturaDecl:0.#}% ≠ recuento real={coberturaReal:0.#}% "
                        + $"(diferencia={diff:0.#}pp). El bimlink puede haberse generado con datos de extraccion parciales."));
            }
            else
            {
                checks.Add(Pass("V6", "Cobertura GUID declarada coincide con recuento real",
                    "Sin estadisticas de extraccion en el bimlink (normal si no hubo extraccion previa)."));
            }
        }

        return new ValidationReport
        {
            BimlinkPath = bimlinkPath,
            Bc3Path     = bc3Path,
            Checks      = checks,
        };
    }

    // ============================================================
    // Helpers privados
    // ============================================================

    private static bool IsValidIfcGuid(string? guid)
    {
        if (string.IsNullOrEmpty(guid)) return false;
        return IfcGuidRegex.IsMatch(guid);
    }

    private static ValidationCheck Pass(string id, string nombre, string detalle) =>
        new() { Id = id, Nombre = nombre, Passed = true,  Detalle = detalle };

    private static ValidationCheck Fail(string id, string nombre, string detalle) =>
        new() { Id = id, Nombre = nombre, Passed = false, Detalle = detalle };

    private static ValidationCheck Skip(string id, string nombre, string detalle) =>
        new() { Id = id, Nombre = nombre, Passed = true,  Detalle = $"[SALTADO] {detalle}" };

    /// <summary>Lee el BC3 con deteccion automatica de encoding (Latin1/UTF-8).</summary>
    private static string ReadBc3(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // BOM UTF-8?
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // Intentar UTF-8 valido
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>
    /// Parseo ligero del BC3: extrae los codigos de conceptos (~C) y
    /// las lineas de medicion extendidas (~M).
    /// </summary>
    private static (HashSet<string> codigos, List<string> mLines) ParseBc3Lightweight(string text)
    {
        var codigos = new HashSet<string>(StringComparer.Ordinal);
        var mLines  = new List<string>();

        foreach (var line in text.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.StartsWith("~C|", StringComparison.Ordinal))
            {
                var parts = l.Split('|');
                if (parts.Length > 1)
                {
                    var cod = parts[1].Split('\\')[0].Trim();
                    if (!string.IsNullOrEmpty(cod)) codigos.Add(cod);
                }
            }
            else if (l.StartsWith("~M|", StringComparison.Ordinal))
            {
                mLines.Add(l);
            }
        }

        return (codigos, mLines);
    }

    // ============================================================
    //  DTOs internos para deserializar el .bimlink.json
    //  (independientes de los modelos del proyecto para poder usar
    //   este validator sin referencias circulares)
    // ============================================================

    private sealed class BimLinkDocumentJson
    {
        public string?                    Version          { get; set; }
        public string?                    Generator        { get; set; }
        public string?                    Generated        { get; set; }
        public string?                    Bc3File          { get; set; }
        public string?                    IfcModel         { get; set; }
        public List<BimLinkPartidaJson>?  Partidas         { get; set; }
        public BimLinkGuidStatsJson?      EstadisticasGuid { get; set; }
    }

    private sealed class BimLinkPartidaJson
    {
        public string?                    Codigo           { get; set; }
        public string?                    CodigoCatalogo   { get; set; }
        public double                     Cantidad         { get; set; }
        public bool                       EsOverrideManual { get; set; }
        public List<BimLinkElementJson>?  Elementos        { get; set; }
    }

    private sealed class BimLinkElementJson
    {
        public string? IfcGuid    { get; set; }
        public string? OrigenGuid { get; set; }
        public long    ElementId  { get; set; }
        public double  Cantidad   { get; set; }
    }

    private sealed class BimLinkGuidStatsJson
    {
        public int    TotalElementos  { get; set; }
        public int    DesdeParametro  { get; set; }
        public int    DesdeCalculo    { get; set; }
        public int    SinGuid         { get; set; }
        public double CoberturaPorc   { get; set; }
    }
}
