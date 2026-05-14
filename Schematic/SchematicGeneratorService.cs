// ================================================================
// SchematicGeneratorService.cs  —  E3G BIM Tools  FASE 3
// Orquestrador principal del generador d'esquema de principi
//
// PIPELINE DE GENERACIÓ:
//   1. Crear/obtenir vista Drafting View amb nom únic
//   2. Crear LineStyles (E3G_DN*) via subtransacció
//   3. Dibuixar franges de planta (línies i rètols)
//   4. Dibuixar baixants (trams verticals + connexions laterals)
//   5. Dibuixar col·lectors horitzontals
//   6. Col·locar símbols MEP (vàlvules, bombes...) si n'hi ha
//   7. Afegir cotes entre baixants
//   8. Afegir etiquetes de canonada (DN, UD, Q)
//   9. Afegir llegenda i caixetí
//  10. Exportar a DWG (opcional)
//
// GESTIÓ DE TRANSACCIONS:
//   - Una sola Transaction("E3G — Generar Esquema") per tot el procés
//   - SubTransaction per a la creació de LineStyles (permesa dins Tx)
//   - RollBack automàtic en cas d'error
//   - ElementId de la vista retornat per obrir-la
//
// RIESGOS I MITIGACIONS:
//   R1 – Vista ja existent → nom únic (timestamp + counter estàtic)
//   R2 – LineStyle no creada dins Tx → SubTransaction dedicada
//   R3 – Família no trobada → fallback DetailCurve automàtic
//   R4 – Cota falla en DraftingView → captura d'excepció per element
//   R5 – Exportació DWG falla → log d'error sense avortar la Tx principal
//
// Autors: E3G — Enginyeria i Energia
// Data:   Maig 2026
// ================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento;       // XarxaInfo, BaixantInfo, TramInfo
using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Schematic
{
    // ============================================================
    // RESULTAT DE LA GENERACIÓ
    // ============================================================

    public class SchematicResult
    {
        public bool    Success     { get; set; }
        public int     ViewId      { get; set; } = -1;
        public string  ViewName    { get; set; }
        public string  DwgPath     { get; set; }
        public string  ErrorMessage { get; set; }
        public int     LinesCreated  { get; set; }
        public int     TexsCreated   { get; set; }
        public int     SymbolsPlaced { get; set; }
        public TimeSpan ElapsedTime  { get; set; }
    }

    // ============================================================
    // SERVEI PRINCIPAL
    // ============================================================

    public static class SchematicGeneratorService
    {
        // Counter estàtic per garantir noms de vista únics
        private static int _viewCounter = 0;

        // --------------------------------------------------------
        // ENTRY POINT PRINCIPAL
        // --------------------------------------------------------

        /// <summary>
        /// Genera l'esquema de principi complet dins d'una Transaction.
        /// Retorna un SchematicResult amb el ViewId i estadístiques.
        /// </summary>
        public static SchematicResult Generate(
            Document            doc,
            XarxaInfo           xarxa,
            SchematicParameters prm  = null,
            bool                exportDwg = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            prm = prm ?? SchematicParameters.Default();

            if (doc == null || xarxa == null || xarxa.Baixants.Count == 0)
                return Fail("Dades insuficients per generar l'esquema.");

            // Nom únic de la vista [R1]
            _viewCounter++;
            string nomVista = $"{prm.ViewNamePrefix} — {xarxa.Sistema}" +
                              $" — {DateTime.Now:yyyyMMdd_HHmm}_{_viewCounter:D2}";

            var result = new SchematicResult { ViewName = nomVista };

            using (var tx = new Transaction(doc, "E3G — Generar Esquema de Principi"))
            {
                tx.Start();
                try
                {
                    // ---- 0. Preparació ----
                    RevitSymbolFactory.ClearCache();

                    // ---- 1. Crear LineStyles [R2] ----
                    CreateLineStylesInSubTx(doc, prm);

                    // ---- 2. Crear vista ----
                    var vista = CreateDraftingView(doc, nomVista, prm);
                    if (vista == null) { tx.RollBack(); return Fail("No s'ha pogut crear la Drafting View."); }

                    var tnt = GetTextNoteType(doc, prm.TextStyle.TextNoteTypeName);

                    // ---- 3. Layout: franges de planta ----
                    DrawFloorZones(doc, vista, xarxa, prm, tnt);

                    // ---- 4. Baixants ----
                    int lines = 0;
                    lines += DrawBaixants(doc, vista, xarxa, prm, tnt);

                    // ---- 5. Col·lectors horitzontals ----
                    lines += DrawCollectors(doc, vista, xarxa, prm, tnt);

                    // ---- 6. Col·lector ø125 + sortida ø160 ----
                    lines += DrawMainCollectors(doc, vista, xarxa, prm, tnt);

                    // ---- 7. Símbols MEP ----
                    int syms = DrawMepSymbols(doc, vista, xarxa, prm);

                    // ---- 8. Cotes entre baixants ----
                    DrawDimensions(doc, vista, xarxa, prm);

                    // ---- 9. Llegenda + caixetí ----
                    int txts = DrawLegendAndTitleBlock(doc, vista, xarxa, prm, tnt);

                    // ---- 10. Confirmar transacció ----
                    tx.Commit();

                    // Obrir la vista
                    try { new UIDocument(doc).ActiveView = vista; } catch { }

                    result.Success      = true;
                    result.ViewId       = vista.Id.IntegerValue;
                    result.LinesCreated = lines;
                    result.TexsCreated  = txts;
                    result.SymbolsPlaced = syms;
                    result.ElapsedTime  = sw.Elapsed;

                    // ---- 11. Exportació DWG (fora de la Tx) [R5] ----
                    if (exportDwg)
                    {
                        try
                        {
                            result.DwgPath = DwgExportService.Export(
                                doc, vista, xarxa, prm);
                        }
                        catch (Exception ex)
                        {
                            result.ErrorMessage = $"DWG export warning: {ex.Message}";
                        }
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { }
                    return Fail($"Error generant l'esquema:\n{ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        // --------------------------------------------------------
        // STEP 1: LINE STYLES (SubTransaction) [R2]
        // --------------------------------------------------------

        private static void CreateLineStylesInSubTx(Document doc, SchematicParameters prm)
        {
            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    foreach (var kvp in prm.LineStyles)
                        RevitSymbolFactory.FindOrCreateLineStyle(doc, kvp.Key, kvp.Value);
                    sub.Commit();
                }
                catch { sub.RollBack(); }
            }
        }

        // --------------------------------------------------------
        // STEP 2: CREAR DRAFTING VIEW
        // --------------------------------------------------------

        private static ViewDrafting CreateDraftingView(
            Document doc, string name, SchematicParameters prm)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v =>
                    v.ViewFamily == ViewFamily.Drafting &&
                    (prm.DraftingViewTypeName == null ||
                     v.Name.Equals(prm.DraftingViewTypeName, StringComparison.OrdinalIgnoreCase)));

            if (vft == null) return null;

            var vista = ViewDrafting.Create(doc, vft.Id);
            vista.Name  = name;
            vista.Scale = prm.ViewScale;
            return vista;
        }

        // --------------------------------------------------------
        // STEP 3: FRANGES DE PLANTA
        // --------------------------------------------------------

        private static void DrawFloorZones(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm,
            TextNoteType tnt)
        {
            double N    = xarxa.Baixants.Count;
            double xR   = prm.Layout.MarginLeft + prm.Layout.Col0Offset + N * prm.Layout.ColWidth + 80;

            foreach (var zone in prm.FloorZones)
            {
                DrawDetailLine(doc, view,
                    prm.Layout.MarginLeft - 2, zone.YTop_px,
                    xR, zone.YTop_px,
                    "ZONE", prm);

                // Rètol nom planta
                DimensioningService.CreateTextNote(doc, view,
                    prm.Layout.MarginLeft - 8, zone.YTop_px + 10,
                    zone.Name, prm, tnt, HorizontalTextAlignment.Right);
            }

            // Línia eix vertical de referència
            DrawDetailLine(doc, view,
                prm.Layout.MarginLeft - 2, prm.FloorZones[0].YTop_px,
                prm.Layout.MarginLeft - 2, prm.Layout.Y_Collector160 + 20,
                "AXIS", prm);
        }

        // --------------------------------------------------------
        // STEP 4: BAIXANTS
        // --------------------------------------------------------

        private static int DrawBaixants(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm,
            TextNoteType tnt)
        {
            int count = 0;
            int N = xarxa.Baixants.Count;

            for (int i = 0; i < N; i++)
            {
                var b   = xarxa.Baixants[i];
                double x = XB(i, prm);
                bool   isL = (b.ConnSideIdx == 0);
                string dnKey = LineStyleConfig.KeyForDn(b.DnMm);

                // --- Trams verticals ---
                foreach (var t in b.Trams.OrderBy(t => t.ZLo))
                {
                    double yT = ZToY(t.ZLo, prm);
                    double yB = ZToY(t.ZHi, prm);
                    double y0 = Math.Min(yT, yB);
                    double y1 = Math.Max(yT, yB);

                    if (t.Ud > 0)
                        DrawDetailLine(doc, view, x, y0, x, y1, dnKey, prm);
                    else
                        DrawDetailLineDashed(doc, view, x, y0, x, y1, dnKey, prm);

                    count++;
                }

                // --- Prolongació al col·lector ø125 ---
                double yBotActiu = b.Trams
                    .Where(t => t.Ud > 0)
                    .Select(t => Math.Max(ZToY(t.ZLo, prm), ZToY(t.ZHi, prm)))
                    .DefaultIfEmpty(ZToY(b.ZMinMm, prm))
                    .Max();
                DrawDetailLine(doc, view, x, yBotActiu, x, prm.Layout.Y_Collector125, dnKey, prm);
                count++;

                // --- Connexions laterals (al mig del gap entre trams) ---
                string cKey = LineStyleConfig.KeyForDn(b.DnMm >= 110 ? 110 : 50);
                foreach (double z in b.ConnZ)
                {
                    double yc   = ZToY(z, prm);
                    double xBrac = isL ? x - prm.Layout.ConnectionArmLength
                                       : x + prm.Layout.ConnectionArmLength;
                    DrawDetailLine(doc, view, x, yc, xBrac, yc, cKey, prm);
                    count++;

                    // Etiqueta de connexió
                    string cLabel = b.DnMm >= 110 ? "ø110·12UD" : "ø50·2UD";
                    DimensioningService.CreateTextNote(doc, view,
                        isL ? xBrac - 3 : xBrac + 3, yc + 3,
                        cLabel, prm, tnt,
                        isL ? HorizontalTextAlignment.Right : HorizontalTextAlignment.Left);
                }

                // --- Etiquetes del baixant ---
                DimensioningService.CreateTextNote(doc, view,
                    x, 30, b.Nom, prm, tnt);
                DimensioningService.CreateTextNote(doc, view,
                    x, 41, $"ø{b.DnMm}", prm, tnt);

                double udX = isL ? x + 5 : x - 5;
                DimensioningService.CreateTextNote(doc, view,
                    udX, ZToY(43200, prm), $"{b.UdMax:0}UD", prm, tnt,
                    isL ? HorizontalTextAlignment.Left : HorizontalTextAlignment.Right);
            }

            return count;
        }

        // --------------------------------------------------------
        // STEP 5: COL·LECTORS HORITZONTALS
        // --------------------------------------------------------

        private static int DrawCollectors(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm,
            TextNoteType tnt)
        {
            int count = 0;
            int N = xarxa.Baixants.Count;
            if (N == 0) return 0;

            foreach (var ht in xarxa.ColectorsHoritzontals)
            {
                // Trobar baixant més proper
                int bIdx = -1; double dMin = double.MaxValue;
                for (int i = 0; i < N; i++)
                {
                    var b = xarxa.Baixants[i];
                    double d = Math.Min(
                        Math.Sqrt(Math.Pow(ht.X0 - b.XMm, 2) + Math.Pow(ht.Y0 - b.YMm, 2)),
                        Math.Sqrt(Math.Pow(ht.X1 - b.XMm, 2) + Math.Pow(ht.Y1 - b.YMm, 2)));
                    if (d < dMin) { dMin = d; bIdx = i; }
                }
                if (bIdx < 0) continue;

                double bx  = XB(bIdx, prm);
                double yH  = ZToY(43200, prm) + 35;
                string dnKey = LineStyleConfig.KeyForDn(ht.DnMm);

                DrawDetailLine(doc, view, bx - 12, yH, bx + 12, yH, dnKey, prm);
                DimensioningService.CreateTextNote(doc, view,
                    bx, yH + 10, $"ø{ht.DnMm}·{ht.Ud:0}UD", prm, tnt);
                count++;
            }
            return count;
        }

        // --------------------------------------------------------
        // STEP 6: COL·LECTOR PRINCIPAL + SORTIDA
        // --------------------------------------------------------

        private static int DrawMainCollectors(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm,
            TextNoteType tnt)
        {
            int N = xarxa.Baixants.Count;
            if (N == 0) return 0;

            double xCL = prm.Layout.MarginLeft + 20;
            double xCR = XB(N - 1, prm) + 24;
            double yC125 = prm.Layout.Y_Collector125;
            double yC160 = prm.Layout.Y_Collector160;

            // Col·lector ø125
            DrawDetailLine(doc, view, xCL, yC125, xCR, yC125, "DN125", prm);
            DrawDetailLine(doc, view, xCL, yC125 - 7, xCL, yC125 + 7, "DN125", prm);
            DrawDetailLine(doc, view, xCR, yC125 - 7, xCR, yC125 + 7, "DN125", prm);
            DimensioningService.CreateTextNote(doc, view,
                (xCL + xCR) / 2, yC125 + 13,
                $"ø125 · {xarxa.UdTotals:0} UD", prm, tnt);

            // Baixada ø160
            DrawDetailLine(doc, view, xCL, yC125, xCL, yC160, "DN160", prm);

            // Sortida ø160 (línia horitzontal amb fletxa)
            double xSFin = xCR + 60;
            DrawDetailLine(doc, view, xCL, yC160, xSFin, yC160, "DN160", prm);

            // Etiquetes sortida
            DimensioningService.CreateTextNote(doc, view,
                prm.Layout.MarginLeft + 10, yC160 + 5, "S01.A", prm, tnt);
            DimensioningService.CreateTextNote(doc, view,
                (xCL + XB(N - 1, prm)) / 2, yC160 - 8,
                $"ø{xarxa.DnSortidaMm}", prm, tnt);
            DimensioningService.CreateTextNote(doc, view,
                (xCL + XB(N - 1, prm)) / 2, yC160 + 16,
                $"{xarxa.UdTotals:0}UD · Q={xarxa.QTotalLs:0.00} l/s → Xarxa municipal",
                prm, tnt);

            return 4;
        }

        // --------------------------------------------------------
        // STEP 7: SÍMBOLS MEP
        // --------------------------------------------------------

        private static int DrawMepSymbols(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm)
        {
            // En un esquema de sanejament bàsic no hi ha símbols MEP.
            // Aquest mètode es pot ampliar per a fontaneria / RITE.
            // Exemple: col·locar vàlvules de bola a cada baixant al niveau P00.

            int count = 0;

            // Exemple comentat — activar quan s'ampliï per a fontaneria:
            /*
            for (int i = 0; i < xarxa.Baixants.Count; i++)
            {
                double x = XB(i, prm);
                double y = ZToY(43200, prm); // a nivell P00
                var sym = RevitSymbolFactory.Place(
                    doc, view, SymbolType.BallValve, x, y, prm, $"V{i+1}");
                if (sym != null) count++;
            }
            */

            return count;
        }

        // --------------------------------------------------------
        // STEP 8: COTES ENTRE BAIXANTS
        // --------------------------------------------------------

        private static void DrawDimensions(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm)
        {
            // Cotes entre baixants consecutius (a nivell de la línia del col·lector)
            int N = xarxa.Baixants.Count;
            double yDim = prm.Layout.Y_Collector125 + 30;

            for (int i = 0; i < N - 1; i++)
            {
                DimensioningService.AddHorizontalDimension(
                    doc, view,
                    XB(i, prm), yDim,
                    XB(i + 1, prm),
                    prm);
            }
        }

        // --------------------------------------------------------
        // STEP 9: LLEGENDA + CAIXETÍ
        // --------------------------------------------------------

        private static int DrawLegendAndTitleBlock(
            Document doc, View view,
            XarxaInfo xarxa, SchematicParameters prm,
            TextNoteType tnt)
        {
            int N = xarxa.Baixants.Count;
            double svgW = prm.Layout.MarginLeft + prm.Layout.Col0Offset
                          + N * prm.Layout.ColWidth + 100;
            double svgH = prm.Layout.Y_Collector160 + 60;

            var legendIds = DimensioningService.AddLegend(
                doc, view, prm, svgW, svgH, tnt);

            var titleIds = DimensioningService.AddTitleBlock(
                doc, view, xarxa, prm, svgW, svgH, tnt);

            return legendIds.Count + titleIds.Count;
        }

        // --------------------------------------------------------
        // HELPERS INTERNS
        // --------------------------------------------------------

        private static double XB(int i, SchematicParameters prm)
            => prm.Layout.MarginLeft + prm.Layout.Col0Offset + i * prm.Layout.ColWidth;

        private static double ZToY(double z, SchematicParameters prm)
            => FloorZoneConfig.ZToYGlobal(z, prm.FloorZones);

        private static void DrawDetailLine(
            Document doc, View view,
            double x0Px, double y0Px,
            double x1Px, double y1Px,
            string styleKey, SchematicParameters prm)
        {
            double f = prm.Layout.PixelToFeet;
            var p0 = new XYZ( x0Px * f, -y0Px * f, 0);
            var p1 = new XYZ( x1Px * f, -y1Px * f, 0);
            if (p0.DistanceTo(p1) < 1e-6) return;
            try
            {
                var dl = doc.Create.NewDetailCurve(view,
                    Autodesk.Revit.DB.Line.CreateBound(p0, p1));
                if (prm.LineStyles.TryGetValue(styleKey, out var cfg))
                {
                    var gs = RevitSymbolFactory.FindOrCreateLineStyle(doc, styleKey, cfg);
                    if (gs != null) dl.LineStyle = gs;
                }
            }
            catch { }
        }

        private static void DrawDetailLineDashed(
            Document doc, View view,
            double x0Px, double y0Px,
            double x1Px, double y1Px,
            string styleKey, SchematicParameters prm)
        {
            // Per a trams UD=0 usem el mateix estil però amb opacitat reduïda
            // (Revit no suporta dash natiu per color override, usem el LineStyle AXIS)
            DrawDetailLine(doc, view, x0Px, y0Px, x1Px, y1Px, "AXIS", prm);
        }

        private static TextNoteType GetTextNoteType(Document doc, string prefName)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>();
            if (!string.IsNullOrEmpty(prefName))
            {
                var named = all.FirstOrDefault(t =>
                    t.Name.IndexOf(prefName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (named != null) return named;
            }
            return all.FirstOrDefault();
        }

        private static SchematicResult Fail(string msg)
            => new SchematicResult { Success = false, ErrorMessage = msg };
    }
}
