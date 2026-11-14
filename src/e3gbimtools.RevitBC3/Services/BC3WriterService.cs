using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Implementacion del exportador BC3 (FIEBDC-3 estricto).
///
/// MODELO CONCEPTUAL (rediseño mayo-2026):
/// El BC3 importado es un CATALOGO de partidas (descripciones, precios,
/// unidades).  El BC3 exportado es una OBRA NUEVA e independiente, con sus
/// propios capitulos y, opcionalmente, subcapitulos.  Los codigos del catalogo
/// se pueden reutilizar en varias reglas, y cada regla genera SU PROPIA
/// partida en el BC3 final.
///
/// Cuando dos o mas reglas apuntan al mismo codigo de catalogo (ej. FECALS y
/// GRISES -> ED15B873), el writer genera codigos finales unicos sufijados:
/// "ED15B873.001", "ED15B873.002", etc.  Comparten descripcion/unidad/precio
/// del catalogo, pero son partidas separadas con su propia medicion.  Esto
/// cumple FIEBDC-3 estricto (codigos ~C unicos) y refleja "una regla = una
/// partida de obra" en Presto/Arquimedes.
///
/// Jerarquia generada (issue #4):
///   ## (proyecto raiz)
///     CAP            (capitulo)
///       CAP.SUB      (subcapitulo, opcional)
///         PARTIDA
///
/// Encoding: Windows-1252 (CP1252).  Es el "ANSI" del ~V FIEBDC-3.  Requiere
/// que CodePagesEncodingProvider este registrado (paquete NuGet
/// System.Text.Encoding.CodePages, declarado en el csproj de este proyecto).
///
/// Formato de registros emitidos:
///   ~V|FICHA|VERFORMA|PROG|CABECERA|JCHAR|NDEC|TIPO|AGENT|
///   ~K|DN\DD\DS\DR\DI\DP\DC\DM\DCH|MONEDA|
///   ~C|<codigo>|<unidad>|<resumen>|<precio>|<fecha>|<tipo>|
///   ~T|<codigo>|<descripcion-extensa>|
///   ~D|<codigoPadre>|<codigoHijo1>\<factor>\<rendimiento>\<codigoHijo2>\...\|
///   ~M|<codPadre>\<codHijo>|<cantidadTotal>|<lineas>|
///
/// Diferencias con el writer del monolito legacy (revit_mcp_plugin/Bc3Writer.cs)
/// que funcionaba en TCQ y se rompieron al portar al nuevo modulo:
///   1. Falta ~K con MONEDA -> TCQ no carga el archivo y se ve obra vacia.
///   2. Fechas DDMMAAAA (8 digitos), no YYMMDD.
///   3. Encoding CP1252 real, no ISO-8859-1 ("ANSI" oficial de FIEBDC-3).
///   4. ~M en formato "corto" historico (RCC|Total|Lineas|) sin trailing \
///      en la RCC ni campos Posicion/Etiqueta vacios.
///   5. Lineas de ~M con 5 campos (Comentario\Unidades\Longitud\Latitud\Altura\),
///      no 6 (la variante "extendida" con Tipo no la lee este TCQ).
///   6. CRLF explicito (no Environment.NewLine).
/// El IfcGUID sigue viajando en el campo Comentario con prefijo "#"
/// (estandar Mediciones.GuidElem), por lo que BimLinkingValidationTests sigue
/// validando con la regex existente.
/// </summary>
public sealed class BC3WriterService : IBC3WriterService
{
    public void Export(
        string outputPath,
        BC3Document? sourceBc3,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<SubChapter> subChapters,
        IReadOnlyList<MappingRule> rules,
        IReadOnlyList<MeasurementResult> measurements,
        BC3ExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Ruta de salida vacia.", nameof(outputPath));
        if (measurements is null) throw new ArgumentNullException(nameof(measurements));

        chapters ??= Array.Empty<Chapter>();
        subChapters ??= Array.Empty<SubChapter>();
        options ??= new BC3ExportOptions();

        // CP1252 (Windows-1252) es el "ANSI" real que TCQ/Presto/Arquimedes
        // esperan cuando ~V declara JCHAR=ANSI.  En .NET Core/5+ las codepages
        // legacy no estan registradas por defecto, hay que registrar el
        // provider para poder pedir GetEncoding(1252) sin que tire.  ISO-8859-1
        // (Encoding.Latin1) NO sirve: difiere de CP1252 en el rango 0x80-0x9F
        // (donde caen €, ‘, ’, “, ”, etc.) y TCQ leeria caracteres basura.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
        catch { /* ya registrado en otra invocacion */ }

        var sb = new StringBuilder();
        // FECHA en FIEBDC-3 va en formato DDMMAAAA (8 digitos).  El writer
        // anterior emitia YYMMDD (6 digitos) y TCQ no parseaba la fecha, lo
        // que dejaba el archivo sin registros valides y mostraba la obra vacia.
        var today = DateTime.Now.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

        // ====== ~V cabecera ======
        // Formato FIEBDC-3 estandar (8 campos):
        //   ~V|FICHA|VERFORMA|PROG|CABECERA|JCHAR|NDEC|TIPO|AGENT|
        //
        // Notas importantes para que TCQ acepte el archivo:
        //   - TIPO se deja VACIO (= 1 por defecto = presupuesto).  El writer
        //     anterior emitia TIPO=2 ("presupuesto con mediciones") porque la
        //     spec lo recomienda, pero el TCQ del usuario ignora la obra al
        //     ver TIPO=2 y la trata como catalogo plano.  El old writer
        //     legacy dejaba TIPO vacio y TCQ cargaba el presupuesto y las
        //     mediciones sin problema.
        //   - JCHAR=ANSI implica codificacion CP1252 (Windows-1252).
        //   - El registro debe terminar con "|" tras el ultimo campo (AGENT),
        //     sin pipes extra.  Pipes sobrantes confunden parsers estrictos.
        sb.Append("~V|")
          .Append(EscapeField(options.Emitter)).Append(".bc3|") // F1 FICHA
          .Append(EscapeField(options.FormatVersion)).Append('|') // F2 VERFORMA
          .Append("e3gbimtools").Append('|')      // F3 PROG
          .Append("e3gbimtools.RevitBC3 ").Append(today).Append('|') // F4 CABECERA (texto libre)
          .Append("ANSI").Append('|')             // F5 JCHAR
          .Append('|')                            // F6 NDEC vacio (decimales del ~K)
          .Append('|')                            // F7 TIPO vacio (= 1 = presupuesto)
          .Append('|')                            // F8 AGENT vacio
          .Append("\r\n");

        // ====== ~K coeficientes y moneda ======
        // FIEBDC-3 ~K declara el numero de decimales para los distintos
        // conceptos y, sobre todo, la MONEDA del presupuesto.  TCQ exige este
        // registro: si falta, la importacion se interrumpe y no se ve nada
        // (el visor permisivo si tolera la ausencia).  Formato:
        //   ~K|DN\DD\DS\DR\DI\DP\DC\DM\DCH|MONEDA|
        //     DN: nº decimales precio descompuesto (cant. naturaleza)
        //     DD: nº decimales precio descompuesto
        //     DS: nº decimales precio simple
        //     DR: nº decimales rendimiento
        //     DI: nº decimales importes
        //     DP: nº decimales porcentajes
        //     DC: nº decimales coeficiente
        //     DM: nº decimales medicion
        //     DCH: nº decimales calidades de hormigon
        // Valores estandar Presto/TCQ: 2 en todos los campos, MONEDA = EUR.
        sb.Append("~K|2\\2\\2\\2\\2\\2\\2\\2\\2|EUR|").Append("\r\n");

        var sourceConcepts = sourceBc3?.Concepts ?? new Dictionary<string, BC3Concept>();
        var rulesById = rules
            .Where(r => !string.IsNullOrEmpty(r.Id))
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Sprint B: indices Chapter/SubChapter por Id para resolver FKs.
        var chaptersById = chapters
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var subChaptersById = subChapters
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // ====== 1. Construir partidas finales (una por medicion no-vacia) ======
        //
        // Cada MeasurementResult que tenga FinalQuantity > 0 se convierte en una
        // partida del BC3 final.
        //
        // Codigo de partida = codigo del catalogo (banco de precios), p.ej.
        // "ED15N711.001" (ITeC), "10.AAR00100" (CYPE), etc.  Es el codigo que
        // los aparejadores quieren ver en la columna "Codi" de TCQ porque les
        // permite cruzar la partida con el banco de precios original.
        //
        // La pertenencia jerarquica (a que capitulo cuelga cada partida) se
        // declara EXPLICITAMENTE en los registros ~D (capitulo -> partidas) y
        // ~M (path capitulo\partida), no por convencion de prefijo en el
        // codigo.  Los lectores BC3 que respetan el spec (TCQ, Presto,
        // Arquimedes en sus versiones recientes) usan el ~D para construir
        // el arbol y NO exigen que el codigo de la partida empiece por el
        // codigo del capitulo padre.
        //
        // Si dos reglas distintas apuntan al mismo codigo de catalogo (p.ej.
        // FECALS y GRISES -> ED15B873), sufijamos con ".001", ".002"... para
        // que los codigos finales sean unicos como obliga FIEBDC-3.
        var nonZero = measurements
            .Where(m => !string.IsNullOrWhiteSpace(m.BC3Code) && m.FinalQuantity > 0)
            .ToList();

        var codeUsageCount = nonZero
            .GroupBy(m => m.BC3Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var exportPartidas = new List<ExportPartida>();
        var codeCounters = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var m in nonZero)
        {
            // Resolver la regla origen: por Id (preferido) o por BC3Code (fallback).
            MappingRule? rule = null;
            if (m.ContributingRuleIds is { Count: > 0 })
            {
                foreach (var id in m.ContributingRuleIds)
                {
                    if (rulesById.TryGetValue(id, out var r)) { rule = r; break; }
                }
            }
            rule ??= rules.FirstOrDefault(r => r.Enabled && r.BC3Code == m.BC3Code);

            var originalCode = m.BC3Code;
            bool needsSuffix = codeUsageCount.TryGetValue(originalCode, out var count) && count > 1;

            string exportCode;
            if (needsSuffix)
            {
                codeCounters.TryGetValue(originalCode, out var seq);
                seq++;
                codeCounters[originalCode] = seq;
                exportCode = $"{originalCode}.{seq:D3}";
            }
            else
            {
                exportCode = originalCode;
            }

            // Datos del catalogo: prioridad al BC3 importado, fallback al cache
            // de la regla (issue #7) y luego al MeasurementResult.
            sourceConcepts.TryGetValue(originalCode, out var srcConcept);

            var unit = FirstNonEmpty(
                srcConcept?.Unit,
                rule?.CatalogUnit,
                m.Unit);

            var baseSummary = FirstNonEmpty(
                srcConcept?.Summary,
                rule?.CatalogSummary,
                m.Description);

            var price = srcConcept?.Price
                ?? 0.0;
            if (price == 0.0 && rule is not null && rule.CatalogPrice > 0) price = rule.CatalogPrice;
            if (price == 0.0) price = m.PriceUnit;

            var extendedDescription = FirstNonEmpty(
                srcConcept?.Description,
                rule?.CatalogDescription,
                string.Empty);

            // Si el mismo codigo de catalogo lo comparten varias reglas y
            // hubo que sufijarlo (.001, .002...), enriquecemos el resumen
            // con el nombre de la regla para diferenciar visualmente las
            // partidas en TCQ/Presto/Arquimedes.
            var summary = baseSummary;
            if (needsSuffix && rule is not null && !string.IsNullOrEmpty(rule.Name))
                summary = $"{baseSummary} ({rule.Name})";

            exportPartidas.Add(new ExportPartida
            {
                Code = exportCode,
                OriginalCatalogCode = originalCode,
                Unit = unit,
                Description = summary,
                ExtendedDescription = extendedDescription,
                PriceUnit = price,
                Quantity = m.FinalQuantity,
                Rule = rule,
                Contributions = m.ElementContributions ?? new List<ElementMeasurementContribution>(),
                HasManualOverride = m.HasOverride,
            });
        }

        // ====== 2. Agrupar partidas en arbol Capitulo > Subcapitulo > Partida ======
        // Sprint B: el arbol viene de las entidades Chapter/SubChapter persistidas
        // y de las FKs de cada regla; no se infiere por strings ni por agrupacion
        // posthoc.  Reglas sin ChapterId -> partida huerfana, no se exporta.
        var chaptersInOrder = BuildChaptersFromFks(
            exportPartidas, chapters, subChapters, chaptersById, subChaptersById);

        // ────────────────────────────────────────────────────────────────────
        // Convencion TCQ/Presto sobre sufijos de codigo en FIEBDC-3:
        //   - Codigos que terminan en "#" se interpretan como CAPITULO o
        //     SUBCAPITULO (concepto agrupador).
        //   - Codigos sin "#" se interpretan como PARTIDA (item final).
        // Esto es ortogonal al campo TIPO del ~C: TCQ no se fia del TIPO si
        // el codigo no encaja en la convencion (otros generadores los dejan
        // a 0 o vacio indistintamente), y siempre comprueba el sufijo.
        //
        // Por eso TCQ veia el archivo "vacio" antes de este parche: los
        // capitulos se emitian sin "##" y TCQ los trataba como partidas
        // huerfanas que no encajaban en la jerarquia del ~D root, asi que
        // descartaba el arbol entero.
        //
        // Aplicamos ahora el sufijo en TODAS las emisiones (~C, ~D, ~M) del
        // codigo de capitulo y subcapitulo, manteniendo el codigo "limpio"
        // que el usuario escribio en sus reglas para el storage interno.
        // ────────────────────────────────────────────────────────────────────
        string Cap(string code)  => code.EndsWith("#", StringComparison.Ordinal) ? code : code + "##";
        string Sub(string code)  => code.EndsWith("#", StringComparison.Ordinal) ? code : code + "#";

        // ====== ~C proyecto raiz ======
        const string rootCode = "##";
        sb.Append("~C|").Append(rootCode).Append('|').Append('|')
          .Append(EscapeField(options.ProjectName)).Append('|')
          .Append('0').Append('|')
          .Append(today).Append('|')
          .Append('0').Append('|')
          .Append("\r\n");

        // ====== ~C de cada capitulo ======
        foreach (var ch in chaptersInOrder)
        {
            sb.Append("~C|").Append(EscapeField(Cap(ch.Code))).Append('|').Append('|')
              .Append(EscapeField(ch.Name)).Append('|')
              .Append('0').Append('|')
              .Append(today).Append('|')
              .Append('0').Append('|')
              .Append("\r\n");

            // ~C de cada subcapitulo REAL del capitulo (los sinteticos no
            // se emiten como concepto separado; sus partidas cuelgan directas
            // del capitulo).
            foreach (var sub in ch.SubChapters.Where(s => !s.IsSynthetic))
            {
                sb.Append("~C|").Append(EscapeField(Sub(sub.Code))).Append('|').Append('|')
                  .Append(EscapeField(sub.Name)).Append('|')
                  .Append('0').Append('|')
                  .Append(today).Append('|')
                  .Append('0').Append('|')
                  .Append("\r\n");
            }
        }

        // ====== ~C de cada partida ======
        // Tipo vacio (= 1 por defecto = partida).  Usar "0" aqui haria que TCQ
        // tratase la partida como capitulo y la ocultase del arbol de obra.
        var emittedPartidaCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in chaptersInOrder)
        {
            foreach (var sub in ch.SubChapters)
            {
                foreach (var partida in sub.Partidas)
                {
                    if (!emittedPartidaCodes.Add(partida.Code)) continue;
                    sb.Append("~C|").Append(EscapeField(partida.Code)).Append('|')
                      .Append(EscapeField(partida.Unit)).Append('|')
                      .Append(EscapeField(partida.Description)).Append('|')
                      .Append(FormatPrice(partida.PriceUnit)).Append('|')
                      .Append(today).Append('|')
                      .Append('|')   // tipo vacio = partida (1 por defecto)
                      .Append("\r\n");
                }
            }
        }

        // ====== ~T descripcion extensa por partida ======
        var emittedDescriptionsByCode = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in chaptersInOrder)
        {
            foreach (var sub in ch.SubChapters)
            {
                foreach (var partida in sub.Partidas)
                {
                    if (!emittedDescriptionsByCode.Add(partida.Code)) continue;
                    if (string.IsNullOrWhiteSpace(partida.ExtendedDescription)) continue;
                    sb.Append("~T|").Append(EscapeField(partida.Code)).Append('|')
                      .Append(EscapeField(partida.ExtendedDescription)).Append('|')
                      .Append("\r\n");
                }
            }
        }

        // ====== ~D proyecto -> capitulos ======
        if (chaptersInOrder.Count > 0)
        {
            sb.Append("~D|").Append(rootCode).Append('|');
            foreach (var ch in chaptersInOrder)
            {
                sb.Append(EscapeField(Cap(ch.Code))).Append('\\').Append("1").Append('\\').Append("1").Append('\\');
            }
            sb.Append('|').Append("\r\n");
        }

        // ====== ~D capitulos -> subcapitulos (solo los NO sinteticos) ======
        // Si un capitulo solo contiene un subcapitulo sintetico (no hay
        // subcapitulos reales), no emitimos ~D cap->sub (seria un loop) y
        // las partidas cuelgan directamente del capitulo (~D cap->partidas).
        foreach (var ch in chaptersInOrder)
        {
            var realSubs = ch.SubChapters.Where(s => !s.IsSynthetic).ToList();
            if (realSubs.Count == 0) continue;
            sb.Append("~D|").Append(EscapeField(Cap(ch.Code))).Append('|');
            foreach (var sub in realSubs)
            {
                sb.Append(EscapeField(Sub(sub.Code))).Append('\\').Append("1").Append('\\').Append("1").Append('\\');
            }
            sb.Append('|').Append("\r\n");
        }

        // ====== ~D capitulos -> partidas directas (subcap sintetico) ======
        foreach (var ch in chaptersInOrder)
        {
            var synthetic = ch.SubChapters.FirstOrDefault(s => s.IsSynthetic);
            if (synthetic is null || synthetic.Partidas.Count == 0) continue;
            sb.Append("~D|").Append(EscapeField(Cap(ch.Code))).Append('|');
            foreach (var partida in synthetic.Partidas)
            {
                // Rendimiento = cantidad real de la partida (los lectores BC3 usan
                // este campo como medicion de la partida dentro del capitulo).
                sb.Append(EscapeField(partida.Code)).Append('\\').Append("1").Append('\\').Append(FormatQuantity(partida.Quantity)).Append('\\');
            }
            sb.Append('|').Append("\r\n");
        }

        // ====== ~D subcapitulos -> partidas (solo subcaps reales) ======
        foreach (var ch in chaptersInOrder)
        {
            foreach (var sub in ch.SubChapters.Where(s => !s.IsSynthetic))
            {
                if (sub.Partidas.Count == 0) continue;
                sb.Append("~D|").Append(EscapeField(Sub(sub.Code))).Append('|');
                foreach (var partida in sub.Partidas)
                {
                    // Rendimiento = cantidad real de la partida.
                    sb.Append(EscapeField(partida.Code)).Append('\\').Append("1").Append('\\').Append(FormatQuantity(partida.Quantity)).Append('\\');
                }
                sb.Append('|').Append("\r\n");
            }
        }

        // ====== ~M mediciones (formato compatible TCQ/Presto) ======
        //
        // FIEBDC-3 admite dos variantes de ~M y TCQ es estricto con ellas:
        //
        //  (a) Formato "corto" historico (TCQ/Presto desde siempre):
        //      ~M|<Padre>\<Hijo>|<MedicionTotal>|<L1><L2>...|
        //      Linea = Comentario\Unidades\Longitud\Latitud\Altura\  (5 campos)
        //
        //  (b) Formato "extendido" FIEBDC-3/2012+ (con Posicion, Etiqueta y
        //      Tipo por linea):
        //      ~M|<RCC>\|<Pos>|<Total>|<Etiqueta>|<L1><L2>...|
        //      Linea = Tipo\Comentario\Unidades\Longitud\Latitud\Altura\  (6 campos)
        //
        // TCQ del usuario es de los lectores que SOLO soporta el formato (a):
        // si recibe el formato (b) interpreta el "1" del Tipo como comentario,
        // los campos quedan desplazados y la obra se ve vacia.  El export
        // anterior (revit_mcp_plugin/Bc3Writer.cs) usaba (a) y funcionaba; al
        // migrar al nuevo writer se paso a (b) y TCQ dejo de poder abrirlo.
        //
        // Para mantener la trazabilidad BIM <-> partida usamos el campo
        // Comentario: prefijo "[Sistema · Nivel]" + "#" + IfcGUID.  El "#"
        // es el separador estandar TCQ/Presto ("Mediciones.GuidElem") y
        // BimLinkingValidationTests sigue extrayendo el GUID con la regex
        // #([0-9A-Za-z_$]{22}).
        //
        // Si la partida tiene override manual no escribimos lineas detalladas:
        // el total prevalece como cantidad unica sin desglose para no falsear
        // la trazabilidad.
        foreach (var ch in chaptersInOrder)
        {
            foreach (var sub in ch.SubChapters)
            {
                foreach (var partida in sub.Partidas)
                {
                    sb.Append("~M|")
                      .Append(EscapeField(Cap(ch.Code))).Append('\\');

                    // Solo emitimos el subcapitulo si NO es el sintetico.
                    if (!sub.IsSynthetic)
                        sb.Append(EscapeField(Sub(sub.Code))).Append('\\');

                    // Path SIN backslash final: TCQ rechaza el trailing \ y
                    // pierde la asociacion padre-hijo.
                    sb.Append(EscapeField(partida.Code)).Append('|')
                      .Append(FormatQuantity(partida.Quantity)).Append('|');

                    // Lineas de medicion detalladas por elemento (formato corto).
                    if (!partida.HasManualOverride
                        && partida.Contributions is { Count: > 0 })
                    {
                        foreach (var c in partida.Contributions)
                        {
                            if (c.Quantity <= 0) continue;

                            // Prefijo de agrupacion "[Sistema · Nivel]" para el Visor BC3.
                            // Si alguno de los dos esta vacio, se omite el prefijo para no
                            // generar etiquetas tipo "[ · P00]" o "[AFS · ]".
                            var hasSystem = !string.IsNullOrWhiteSpace(c.SystemName);
                            var hasLevel  = !string.IsNullOrWhiteSpace(c.LevelName);
                            var prefix = (hasSystem && hasLevel)
                                ? $"[{c.SystemName} \xb7 {c.LevelName}] "   // \xb7 = ·
                                : hasSystem ? $"[{c.SystemName}] "
                                : hasLevel  ? $"[{c.LevelName}] "
                                : string.Empty;

                            // IfcGuid siempre deberia estar poblado (calculado por RevitIfcGuidCalculator
                            // si el parametro IFC_GUID estaba vacio); el fallback es solo por robustez.
                            var guid = string.IsNullOrWhiteSpace(c.IfcGuid)
                                ? "E" + c.ElementId.ToString(CultureInfo.InvariantCulture)
                                : c.IfcGuid;

                            // Comentario: "[Sistema · Nivel]#IfcGUID"  o  "#IfcGUID" si sin prefijo.
                            var comment = string.IsNullOrEmpty(prefix)
                                ? "#" + guid
                                : prefix.TrimEnd() + "#" + guid;
                            sb.Append(EscapeField(comment)).Append('\\')                 // comentario
                              .Append('1').Append('\\')                                  // unidades = 1
                              .Append(FormatQuantity(c.Quantity)).Append('\\')           // longitud = cantidad
                              .Append('\\')                                              // latitud vacia
                              .Append('\\');                                             // altura vacia
                        }
                    }
                    sb.Append('|').Append("\r\n");
                }
            }
        }

        // ====== Escribir archivo en CP1252 (Windows-1252) ======
        // "ANSI" en el ~V implica CP1252 para los lectores hispano-parlantes
        // (TCQ, Presto, Arquimedes, Menfis).  ISO-8859-1 difiere en €, ‘, ’,
        // “, ”, etc. y produce caracteres basura al abrir en TCQ.
        var encoding = Encoding.GetEncoding(1252);
        File.WriteAllText(outputPath, sb.ToString(), encoding);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c!;
        return string.Empty;
    }

    private sealed class ExportPartida
    {
        /// <summary>Codigo final en el BC3 exportado (puede estar sufijado).</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>Codigo original del catalogo BC3 importado (para ~T y trazabilidad).</summary>
        public string OriginalCatalogCode { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ExtendedDescription { get; set; } = string.Empty;
        public double PriceUnit { get; set; }
        public double Quantity { get; set; }
        public MappingRule? Rule { get; set; }
        public List<ElementMeasurementContribution> Contributions { get; set; } = new();
        public bool HasManualOverride { get; set; }
    }

    private sealed class ExportSubChapter
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// True si este subcapitulo es un "contenedor" creado solo porque las
        /// partidas no tenian subcapitulo asignado.  En la jerarquia exportada
        /// se trata como ausente: las partidas cuelgan directamente del capitulo.
        /// </summary>
        public bool IsSynthetic { get; set; }
        /// <summary>Orden tal como vino del modelo (Chapter.Order/SubChapter.Order).</summary>
        public int OrderInModel { get; set; }
        public List<ExportPartida> Partidas { get; } = new();
    }

    private sealed class ExportChapter
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Orden tal como vino del modelo (Chapter.Order).</summary>
        public int OrderInModel { get; set; }
        public List<ExportSubChapter> SubChapters { get; } = new();
    }

    /// <summary>
    /// Sprint B: agrupa las partidas usando las entidades Chapter/SubChapter
    /// del mapeo y las FKs en las reglas, no por strings sueltos.
    ///
    /// Reglas:
    /// - Si una partida no tiene Rule, o la Rule no tiene ChapterId, o el
    ///   ChapterId no se encuentra en el indice -> partida HUERFANA, NO se
    ///   exporta (AC3.3 del Sprint B).
    /// - Si SubChapterId es null o no resuelve, la partida cuelga del
    ///   subcapitulo sintetico del capitulo (igual que antes para reglas
    ///   sin subcap).
    /// - El orden de capitulos respeta <see cref="Chapter.Order"/>; el de
    ///   subcapitulos, <see cref="SubChapter.Order"/>.
    /// - Capitulos sin partidas con medicion se omiten del BC3 final
    ///   (AC1.3): un capitulo vacio se conserva en la configuracion pero
    ///   no contamina el BC3 exportado.
    /// </summary>
    private static List<ExportChapter> BuildChaptersFromFks(
        List<ExportPartida> partidas,
        IReadOnlyList<Chapter> chapters,
        IReadOnlyList<SubChapter> subChapters,
        Dictionary<string, Chapter> chaptersById,
        Dictionary<string, SubChapter> subChaptersById)
    {
        // ExportChapter por Id de chapter del modelo.
        var exportByChapterId = new Dictionary<string, ExportChapter>(StringComparer.Ordinal);
        // ExportSubChapter por Id de subchapter del modelo, dentro de su capitulo.
        var exportSubByChapterId = new Dictionary<string, Dictionary<string, ExportSubChapter>>(StringComparer.Ordinal);

        ExportChapter EnsureChapter(Chapter ch)
        {
            if (exportByChapterId.TryGetValue(ch.Id, out var existing)) return existing;
            var e = new ExportChapter
            {
                Code = !string.IsNullOrEmpty(ch.Code) ? ch.Code : ch.Id.Substring(0, Math.Min(8, ch.Id.Length)),
                Name = !string.IsNullOrEmpty(ch.Name) ? ch.Name : "(sin nombre)",
                OrderInModel = ch.Order,
            };
            exportByChapterId[ch.Id] = e;
            exportSubByChapterId[ch.Id] = new Dictionary<string, ExportSubChapter>(StringComparer.Ordinal);
            return e;
        }

        ExportSubChapter EnsureSyntheticSub(ExportChapter chapter, Chapter modelChapter)
        {
            var syn = chapter.SubChapters.FirstOrDefault(s => s.IsSynthetic);
            if (syn is null)
            {
                syn = new ExportSubChapter
                {
                    Code = chapter.Code,
                    Name = chapter.Name,
                    IsSynthetic = true,
                    OrderInModel = -1,  // sintetico va siempre primero
                };
                chapter.SubChapters.Insert(0, syn);
            }
            return syn;
        }

        ExportSubChapter EnsureSubChapter(ExportChapter chapter, Chapter modelChapter, SubChapter sub)
        {
            var byId = exportSubByChapterId[modelChapter.Id];
            if (byId.TryGetValue(sub.Id, out var existing)) return existing;

            // Codigo "full": si el codigo del subcap no incluye el del capitulo,
            // lo prefijamos para mantener unicidad en el BC3 (Code.Sub).
            var subCode = (sub.Code ?? string.Empty).Trim();
            var fullCode = subCode.Length == 0
                ? $"{chapter.Code}.SUB{(chapter.SubChapters.Count(s => !s.IsSynthetic) + 1):D2}"
                : (subCode.Contains('.') ? subCode : $"{chapter.Code}.{subCode}");

            var e = new ExportSubChapter
            {
                Code = fullCode,
                Name = !string.IsNullOrEmpty(sub.Name) ? sub.Name : fullCode,
                IsSynthetic = false,
                OrderInModel = sub.Order,
            };
            byId[sub.Id] = e;
            chapter.SubChapters.Add(e);
            return e;
        }

        foreach (var partida in partidas)
        {
            var rule = partida.Rule;

            // Sin regla o sin ChapterId -> huerfana.
            if (rule is null
                || string.IsNullOrEmpty(rule.ChapterId)
                || !chaptersById.TryGetValue(rule.ChapterId, out var modelChapter))
            {
                continue;
            }

            var expCh = EnsureChapter(modelChapter);

            ExportSubChapter expSub;
            if (!string.IsNullOrEmpty(rule.SubChapterId)
                && subChaptersById.TryGetValue(rule.SubChapterId, out var modelSub)
                && string.Equals(modelSub.ChapterId, modelChapter.Id, StringComparison.Ordinal))
            {
                expSub = EnsureSubChapter(expCh, modelChapter, modelSub);
            }
            else
            {
                expSub = EnsureSyntheticSub(expCh, modelChapter);
            }
            expSub.Partidas.Add(partida);
        }

        // Ordenamos por Order (lo que decidio el usuario), no por Code.
        return exportByChapterId.Values
            .OrderBy(c => c.OrderInModel)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c =>
            {
                // Subcapitulos: sintetico primero (OrderInModel == -1), luego reales por Order.
                var sorted = c.SubChapters
                    .OrderBy(s => s.OrderInModel)
                    .ThenBy(s => s.Code, StringComparer.Ordinal)
                    .ToList();
                c.SubChapters.Clear();
                foreach (var s in sorted) c.SubChapters.Add(s);
                return c;
            })
            .ToList();
    }

    private static string FormatPrice(double price) =>
        price.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatQuantity(double q) =>
        q.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapa caracteres problematicos en un campo FIEBDC-3.  El '|' es el
    /// separador y '~' inicia un registro; ambos los reemplazamos por espacio
    /// como medida defensiva (raros en descripciones reales).
    /// </summary>
    private static string EscapeField(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace('|', ' ').Replace('~', ' ').Replace("\r", " ").Replace("\n", " ");
    }
}
