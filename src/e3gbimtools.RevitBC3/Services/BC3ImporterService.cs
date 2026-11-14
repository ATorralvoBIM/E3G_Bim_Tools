using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion del importador FIEBDC-3.
///
/// El formato FIEBDC-3 organiza la informacion en registros que empiezan
/// por <c>~</c> seguido de una letra:
///
/// <list type="bullet">
///   <item><c>~V</c>: cabecera (formato, version, emisor, codificacion)</item>
///   <item><c>~C</c>: definicion de un concepto (codigo, unidad, resumen, precio, tipo)</item>
///   <item><c>~D</c>: descomposicion (relacion padre-&gt;hijos)</item>
///   <item><c>~T</c>: texto descriptivo de un concepto</item>
///   <item><c>~M</c>: mediciones</item>
/// </list>
///
/// Sprint A1: parser robusto para los 5 registros anteriores.  Otros
/// registros del estandar (~K coeficientes, ~L pliego, ~G grafico, ~E
/// entidades, ~F ficha tecnica, etc.) se ignoran silenciosamente; entraran
/// en sprints posteriores si las necesitamos.
///
/// La codificacion por defecto es ISO-8859-1 (Latin-1), la habitual en
/// FIEBDC-3.  Si el ~V indica otra (ej. ANSI 1252, UTF-8) se reinterpreta.
/// </summary>
public sealed class BC3ImporterService : IBC3ImporterService
{
    public BC3Document Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Ruta vacia.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("No se encuentra el archivo BC3.", filePath);

        // Paso 1 — Leer texto crudo en ISO-8859-1 (default FIEBDC-3).
        var defaultEncoding = GetLatin1Encoding();
        var raw = File.ReadAllText(filePath, defaultEncoding);

        // Paso 2 — Localizar el registro ~V para confirmar codificacion.
        //          Si la cabecera dice otra cosa, releemos con la codificacion correcta.
        var declaredCharset = TryExtractCharsetFromV(raw);
        if (!string.IsNullOrEmpty(declaredCharset) && !IsLatin1Family(declaredCharset))
        {
            try
            {
                var declared = ResolveEncoding(declaredCharset!);
                raw = File.ReadAllText(filePath, declared);
            }
            catch
            {
                // Si no podemos resolver la codificacion declarada, seguimos con Latin-1.
            }
        }

        // Paso 3 — Trocear en registros (cada uno empieza por '~' al inicio de linea).
        var records = SplitRecords(raw);

        // Paso 4 — Crear el documento y procesar registros.
        var doc = new BC3Document { SourcePath = filePath };
        var childCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (record.Length < 2) continue;
            var kind = record[1];

            try
            {
                switch (kind)
                {
                    case 'V': ParseVersion(record, doc); break;
                    case 'C': ParseConcept(record, doc); break;
                    case 'D': ParseDecomposition(record, doc, childCodes); break;
                    case 'T': ParseText(record, doc); break;
                    case 'M': ParseMeasurement(record, doc); break;
                    // Otros registros se ignoran en Sprint A1.
                }
            }
            catch
            {
                // No abortar todo el parse por un registro malformado.
                // En sprints posteriores anadimos logging diagnostico aqui.
            }
        }

        // Paso 5 — Clasificar conceptos (Capitulo vs Partida) e identificar raices.
        foreach (var concept in doc.Concepts.Values)
        {
            if (concept.Type != ConceptType.Unknown) continue;

            // Si tiene hijos en ~D, lo consideramos Capitulo.
            // Si no tiene hijos pero alguien lo referencia, es Partida.
            // Si no hay ni unos ni otros, lo dejamos Unknown.
            if (concept.Children.Count > 0)
                concept.Type = ConceptType.Capitulo;
            else if (childCodes.Contains(concept.Code))
                concept.Type = ConceptType.Partida;
        }

        // Conceptos raiz: estan en doc.Concepts pero no aparecen como hijos en ningun ~D.
        doc.Roots.AddRange(
            doc.Concepts.Values
                .Where(c => !childCodes.Contains(c.Code))
                .OrderBy(c => c.Code, StringComparer.Ordinal));

        return doc;
    }

    // =================================================================
    // Helpers privados
    // =================================================================

    private static List<string> SplitRecords(string raw)
    {
        // Algunos emisores (Presto, RIB) exportan TODOS los registros concatenados
        // sin saltos de linea entre ellos: "~V|...|~C|...|~C|...|".  No podemos
        // confiar en troceado linea a linea.
        //
        // Estrategia: escaneo char a char.  Un nuevo registro empieza cuando
        // encontramos el caracter '~' seguido de una letra mayuscula (A-Z).
        // Los saltos de linea dentro de un registro se sustituyen por espacios
        // para que el registro quede en una sola "linea logica".
        var records = new List<string>();
        var sb = new StringBuilder();
        bool inRecord = false;

        // Salta BOM (UTF-16/UTF-8) si esta presente al inicio del archivo.
        // ﻿ = Byte Order Mark (zero-width no-break space)
        int start = 0;
        if (raw.Length > 0 && raw[0] == '﻿') start = 1;

        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];

            bool isRecordStart =
                c == '~'
                && i + 1 < raw.Length
                && raw[i + 1] >= 'A' && raw[i + 1] <= 'Z';

            if (isRecordStart)
            {
                // Cierra el registro anterior antes de abrir el nuevo.
                if (sb.Length > 0)
                {
                    var prev = sb.ToString().Trim();
                    if (prev.Length > 0) records.Add(prev);
                    sb.Clear();
                }
                inRecord = true;
            }

            if (!inRecord) continue;

            // Saltos de linea dentro del registro -> espacio simple.
            if (c == '\r' || c == '\n')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            var last = sb.ToString().Trim();
            if (last.Length > 0) records.Add(last);
        }

        return records;
    }

    private static string[] SplitFields(string record)
    {
        // Cada registro tiene la forma "~X|campo0|campo1|...|"
        // Tras quitar el "~X" queda "|campo0|campo1|...|".
        // Limpiamos los pipes iniciales y finales para que el primer campo
        // sea efectivamente fields[0].
        if (record.Length < 2) return Array.Empty<string>();
        var body = record.Substring(2);
        if (body.StartsWith("|")) body = body.Substring(1);
        if (body.EndsWith("|")) body = body.Substring(0, body.Length - 1);
        if (body.Length == 0) return Array.Empty<string>();
        return body.Split('|');
    }

    private static void ParseVersion(string record, BC3Document doc)
    {
        var fields = SplitFields(record);
        // ~V|PROPIEDAD_ARCHIVO\VERSION_FORMATO\|EMISOR|FECHA|EMISOR_PAIS|JUEGO_CARACTERES|...|
        if (fields.Length > 0)
        {
            // Field 0 puede traer formato\version separados por \
            var first = fields[0];
            var parts = first.Split('\\');
            if (parts.Length > 1) doc.FormatVersion = parts[1].Trim();
        }
        if (fields.Length > 1) doc.Emitter = fields[1].Trim();
        if (fields.Length > 2) doc.EmissionDate = fields[2].Trim();
        if (fields.Length > 4) doc.CharacterSet = fields[4].Trim();
    }

    private static void ParseConcept(string record, BC3Document doc)
    {
        // ~C|CODIGO|UNIDAD|RESUMEN|PRECIO|FECHA|TIPO|
        var fields = SplitFields(record);
        if (fields.Length < 2) return;

        var code = CleanCode(fields[0]);
        if (string.IsNullOrEmpty(code)) return;

        var concept = doc.Concepts.TryGetValue(code, out var existing)
            ? existing
            : new BC3Concept { Code = code };

        if (fields.Length > 1) concept.Unit    = fields[1].Trim();
        if (fields.Length > 2) concept.Summary = fields[2].Trim();
        if (fields.Length > 3) concept.Price   = ParseDouble(fields[3]);
        if (fields.Length > 4) concept.PriceDate = fields[4].Trim();
        if (fields.Length > 5) concept.Type    = ClassifyType(fields[5].Trim());

        doc.Concepts[code] = concept;
    }

    private static void ParseDecomposition(
        string record, BC3Document doc, HashSet<string> childCodes)
    {
        // ~D|CODIGO_PADRE|HIJO1\FACTOR\RENDIMIENTO\HIJO2\FACTOR\RENDIMIENTO\...|
        var fields = SplitFields(record);
        if (fields.Length < 2) return;

        var parentCode = CleanCode(fields[0]);
        if (string.IsNullOrEmpty(parentCode)) return;

        // Garantiza que el padre existe (aunque su ~C llegue mas tarde).
        if (!doc.Concepts.TryGetValue(parentCode, out var parent))
        {
            parent = new BC3Concept { Code = parentCode };
            doc.Concepts[parentCode] = parent;
        }

        // El campo 1 es una lista de tuplas (codigo\factor\rendimiento) separadas por \
        if (fields.Length > 1)
        {
            var tokens = fields[1].Split('\\');
            for (int i = 0; i + 2 < tokens.Length; i += 3)
            {
                var childCode = CleanCode(tokens[i]);
                if (string.IsNullOrEmpty(childCode)) continue;

                var factor = ParseDouble(tokens[i + 1]);
                var perf   = ParseDouble(tokens[i + 2]);

                parent.Children.Add(new BC3Decomposition
                {
                    ChildCode  = childCode,
                    Factor     = factor == 0 ? 1.0 : factor,
                    Performance = perf == 0 ? 1.0 : perf,
                });

                childCodes.Add(childCode);

                // Pre-crea el concepto hijo si su ~C no ha llegado todavia.
                if (!doc.Concepts.ContainsKey(childCode))
                    doc.Concepts[childCode] = new BC3Concept { Code = childCode };
            }
        }
    }

    private static void ParseText(string record, BC3Document doc)
    {
        // ~T|CODIGO|TEXTO|
        var fields = SplitFields(record);
        if (fields.Length < 2) return;

        var code = CleanCode(fields[0]);
        if (string.IsNullOrEmpty(code)) return;

        if (!doc.Concepts.TryGetValue(code, out var concept))
        {
            concept = new BC3Concept { Code = code };
            doc.Concepts[code] = concept;
        }

        concept.Description = fields[1].Trim();
    }

    private static void ParseMeasurement(string record, BC3Document doc)
    {
        // ~M|CODIGO_PADRE\CODIGO_PARTIDA|UNIDAD|RESUMEN|CANTIDAD|...|
        // Simplificacion Sprint A1: extraemos la cantidad total (campo 3).
        var fields = SplitFields(record);
        if (fields.Length < 2) return;

        // Field 0 puede ser "padre\hijo" — nos interesa el hijo (la partida).
        var codes = fields[0].Split('\\');
        var partidaCode = CleanCode(codes.Length >= 2 ? codes[1] : codes[0]);
        if (string.IsNullOrEmpty(partidaCode)) return;

        if (!doc.Concepts.TryGetValue(partidaCode, out var concept))
        {
            concept = new BC3Concept { Code = partidaCode };
            doc.Concepts[partidaCode] = concept;
        }

        var measurement = new BC3Measurement
        {
            Code = partidaCode,
            Comment = fields.Length > 2 ? fields[2].Trim() : string.Empty,
            Quantity = fields.Length > 3 ? ParseDouble(fields[3]) : 0,
        };
        concept.Measurements.Add(measurement);
    }

    private static string CleanCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().Trim('\\').Trim();
    }

    private static double ParseDouble(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var s = raw.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    private static ConceptType ClassifyType(string raw)
    {
        // FIEBDC-3 estandar: campo TIPO del ~C
        //   (vacio) o 0 = no clasificado (la jerarquia ~D decide despues)
        //   1 = mano de obra
        //   2 = maquinaria
        //   3 = material fisico
        //   C = capitulo explicito
        //   P = partida explicita
        // Otros codigos pueden venir de emisores concretos.
        if (string.IsNullOrWhiteSpace(raw)) return ConceptType.Unknown;
        var s = raw.ToUpperInvariant().Trim();
        return s switch
        {
            "0" => ConceptType.Unknown,
            "1" => ConceptType.ManoDeObra,
            "MO" => ConceptType.ManoDeObra,
            "2" => ConceptType.Maquinaria,
            "MQ" => ConceptType.Maquinaria,
            "3" => ConceptType.Material,
            "MA" => ConceptType.Material,
            "4" => ConceptType.Auxiliar,
            "5" => ConceptType.Partida,   // FIEBDC-3/2020 §3.3: tipo 5 = partida de obra
            "6" => ConceptType.Partida,   // tipo 6 = unidad de obra (se trata igual)
            "C" => ConceptType.Capitulo,
            "CAP" => ConceptType.Capitulo,
            "P" => ConceptType.Partida,
            "PAR" => ConceptType.Partida,
            _ => ConceptType.Unknown,
        };
    }

    private static string? TryExtractCharsetFromV(string raw)
    {
        // Buscamos la primera linea que empiece por ~V| y miramos el campo 5 (charset).
        var lines = raw.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("~V", StringComparison.Ordinal)) continue;
            var fields = SplitFields(trimmed);
            if (fields.Length > 4) return fields[4].Trim();
            return null;
        }
        return null;
    }

    private static Encoding GetLatin1Encoding()
    {
        // ISO-8859-1 (Latin-1) es nativo en .NET 5+ via Encoding.Latin1.
        // Cubre la gran mayoria de BC3 reales.  No requiere paquetes NuGet.
        return Encoding.Latin1;
    }

    private static Encoding ResolveEncoding(string declared)
    {
        // Sin CodePagesEncodingProvider (requiere NuGet) solo podemos resolver
        // las codificaciones nativas de .NET.  Para Windows-1252 (ANSI), que
        // es muy parecido a Latin-1, caemos a Latin-1 que cubre el 99% de los
        // caracteres usados en presupuestos en castellano.
        var name = declared.Trim().ToUpperInvariant();
        return name switch
        {
            "UTF-8" or "UTF8" => Encoding.UTF8,
            "UTF-16" or "UTF16" => Encoding.Unicode,
            _ => Encoding.Latin1,
        };
    }

    private static bool IsLatin1Family(string declared)
    {
        var d = declared.Trim().ToUpperInvariant();
        return d.Contains("8859") || d.Contains("LATIN");
    }
}
