// ================================================================
// DimensioningService.cs  —  E3G BIM Tools  FASE 3
// Servei de cotes, etiquetes i llegenda per a l'esquema de principi
//
// Funcionalitats:
//   1. Cotes lineals entre baixants (Dimension)
//   2. SpotElevation per alçades de planta
//   3. Etiquetes de canonada (DN, caudal, pendent)
//   4. Rètols de planta (P01, P02 ... S01)
//   5. Bloc de títol (sistema, data, normativa, escala)
//   6. Llegenda de colors/DN
//   7. Notes de càlcul (UD totals, Q l/s, DN sortida)
//
// Totes les funcions han d'executar-se DINS d'una Transaction activa.
//
// Autors: E3G — Enginyeria i Energia
// Data:   Maig 2026
// ================================================================

using Autodesk.Revit.DB;
using E3G.Saneamiento;       // XarxaInfo
using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Schematic
{
    public static class DimensioningService
    {
        // --------------------------------------------------------
        // 1. COTES LINEALS ENTRE BAIXANTS
        // --------------------------------------------------------

        /// <summary>
        /// Crea una línia de cota horitzontal entre dos baixants.
        /// La cota mesurarà la distància en mm (model → paper).
        /// Retorna l'ElementId de la Dimension creada o Invalid si falla.
        /// </summary>
        public static ElementId AddHorizontalDimension(
            Document     doc,
            View         view,
            double       x0Px, double yPx,
            double       x1Px,
            SchematicParameters prm)
        {
            double f   = prm.Layout.PixelToFeet;
            double x0  = x0Px * f;
            double x1  = x1Px * f;
            double y   = -yPx * f;

            try
            {
                // Necessitem dues ReferenceArray amb punts de referència.
                // Per a Drafting Views usem DetailLines com a punts de referència.
                var refArr = new ReferenceArray();

                // Creiem línies dummies verticals molt curtes per tenir References
                var dl0 = doc.Create.NewDetailCurve(view,
                    Autodesk.Revit.DB.Line.CreateBound(
                        new XYZ(x0, y - 0.001, 0), new XYZ(x0, y + 0.001, 0)));
                var dl1 = doc.Create.NewDetailCurve(view,
                    Autodesk.Revit.DB.Line.CreateBound(
                        new XYZ(x1, y - 0.001, 0), new XYZ(x1, y + 0.001, 0)));

                refArr.Append(dl0.GeometryCurve.GetEndPointReference(0));
                refArr.Append(dl1.GeometryCurve.GetEndPointReference(0));

                var dimLine = Autodesk.Revit.DB.Line.CreateBound(
                    new XYZ(x0, y, 0), new XYZ(x1, y, 0));

                // DimensionType per defecte
                var dimType = GetDefaultDimensionType(doc);
                var dim = doc.Create.NewDimension(view, dimLine, refArr, dimType);
                return dim?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        // --------------------------------------------------------
        // 2. ETIQUETES DE CANONADA
        // --------------------------------------------------------

        /// <summary>
        /// Afegeix una etiqueta de canonada: "ø{dn} · {ud}UD · Q={q} l/s"
        /// al costat d'un tram, amb format CTE/RITE.
        /// </summary>
        public static ElementId AddPipeLabel(
            Document    doc,
            View        view,
            double      xPx, double yPx,
            int         dn,
            double      ud,
            double      qLs,
            double      pendentPct,
            SchematicParameters prm,
            TextNoteType tnt = null)
        {
            string text = BuildPipeLabel(dn, ud, qLs, pendentPct, prm.Normativa);
            return CreateTextNote(doc, view, xPx, yPx, text, prm, tnt);
        }

        private static string BuildPipeLabel(
            int dn, double ud, double qLs, double pendent,
            NormativaFormat normativa)
        {
            switch (normativa)
            {
                case NormativaFormat.CTE_HS5:
                    if (ud > 0 && qLs > 0)
                        return $"ø{dn} · {ud:0}UD · {qLs:0.00} l/s";
                    if (ud > 0)
                        return $"ø{dn} · {ud:0}UD";
                    return $"ø{dn}";

                case NormativaFormat.CTE_HS4:
                    if (qLs > 0 && pendent > 0)
                        return $"ø{dn} · Q={qLs:0.00} l/s · i={pendent:0.0}%";
                    if (qLs > 0)
                        return $"ø{dn} · Q={qLs:0.00} l/s";
                    return $"ø{dn}";

                case NormativaFormat.RITE:
                    if (qLs > 0)
                        return $"DN{dn} — {qLs:0.00} l/s";
                    return $"DN{dn}";

                default:
                    return $"ø{dn}";
            }
        }

        // --------------------------------------------------------
        // 3. RÈTOLS DE PLANTA
        // --------------------------------------------------------

        /// <summary>
        /// Dibuixa els rètols de cada franja de planta (P01, P02 ...)
        /// a l'esquerra del canvas, usant els colors de FloorZoneConfig.
        /// Retorna els IDs de tots els TextNotes creats.
        /// </summary>
        public static List<ElementId> AddFloorLabels(
            Document    doc,
            View        view,
            SchematicParameters prm,
            TextNoteType tnt = null)
        {
            var ids = new List<ElementId>();
            double f   = prm.Layout.PixelToFeet;
            double xPx = prm.Layout.MarginLeft - 10;

            foreach (var zone in prm.FloorZones)
            {
                double yPx = (zone.YBot_px + zone.YTop_px) / 2.0;
                var id = CreateTextNote(doc, view, xPx, yPx, zone.Name, prm, tnt,
                    HorizontalTextAlignment.Right);
                if (id != ElementId.InvalidElementId) ids.Add(id);

                // Línia separadora de zona (de marge fins al final del canvas)
                double yLine = zone.YTop_px;
                double xL = prm.Layout.MarginLeft - 2;
                double xR = prm.Layout.MarginLeft + prm.Layout.Col0Offset
                            + 12 * prm.Layout.ColWidth + 100; // estimació amplada
                AddDetailLineZone(doc, view, xL, yLine, xR, yLine, prm);
            }
            return ids;
        }

        // --------------------------------------------------------
        // 4. LLEGENDA
        // --------------------------------------------------------

        /// <summary>
        /// Genera la llegenda de colors/DN al peu del esquema.
        /// </summary>
        public static List<ElementId> AddLegend(
            Document    doc,
            View        view,
            SchematicParameters prm,
            double      svgW,
            double      svgH,
            TextNoteType tnt = null)
        {
            if (!prm.Legend.ShowLegend) return new List<ElementId>();

            var ids = new List<ElementId>();
            double f    = prm.Layout.PixelToFeet;
            double xCur = prm.Legend.XPosition;
            double y    = svgH + prm.Legend.RowHeight * 2;

            // Títol de la llegenda
            ids.Add(CreateTextNote(doc, view, xCur, y - prm.Legend.RowHeight,
                prm.Legend.TitleText, prm, tnt));

            // Entrades automàtiques des dels LineStyles
            var entries = BuildLegendEntries(prm);
            double col_w = 180;
            int col = 0;

            foreach (var entry in entries)
            {
                double xE = xCur + col * col_w;
                double yE = y + (col > 0 ? 0 : 0);

                // Mostra de línia
                double xL  = xE;
                double xR  = xE + prm.Legend.LineLength;
                double yLine = yE;
                if (prm.LineStyles.TryGetValue(entry.StyleKey, out var ls))
                    AddDetailLineStyled(doc, view, xL, yLine, xR, yLine, entry.StyleKey, prm);

                // Text
                ids.Add(CreateTextNote(doc, view, xR + 6, yE + 2, entry.LabelText, prm, tnt,
                    HorizontalTextAlignment.Left));

                col++;
                if (col * col_w > svgW - xCur - 100)
                {
                    col = 0; y += prm.Legend.RowHeight;
                }
            }

            // Normativa i data
            if (prm.Legend.ShowNormativa)
                ids.Add(CreateTextNote(doc, view, svgW - 10, svgH + prm.Legend.RowHeight,
                    $"Normativa: {NormativaText(prm.Normativa)}", prm, tnt,
                    HorizontalTextAlignment.Right));

            if (prm.Legend.ShowDate)
                ids.Add(CreateTextNote(doc, view, svgW - 10, svgH + prm.Legend.RowHeight * 2,
                    DateTime.Now.ToString("dd/MM/yyyy"), prm, tnt,
                    HorizontalTextAlignment.Right));

            if (prm.Legend.ShowScale)
                ids.Add(CreateTextNote(doc, view, svgW / 2, svgH + prm.Legend.RowHeight,
                    $"Escala 1:{prm.ViewScale}", prm, tnt));

            return ids;
        }

        private static List<LegendEntry> BuildLegendEntries(SchematicParameters prm)
        {
            if (prm.Legend.CustomEntries.Count > 0) return prm.Legend.CustomEntries;

            // Entrades automàtiques per als DN del sanejament
            return new List<LegendEntry>
            {
                new LegendEntry { StyleKey = "DN160", LabelText = "ø160 Sortida / Collector general" },
                new LegendEntry { StyleKey = "DN125", LabelText = "ø125 Baixants principals / Col·lectors" },
                new LegendEntry { StyleKey = "DN110", LabelText = "ø110 Connexió vivenda" },
                new LegendEntry { StyleKey = "DN90",  LabelText = "ø90  Baixants secundaris" },
                new LegendEntry { StyleKey = "DN50",  LabelText = "ø50  Aparells (inodor, rentadora)" },
                new LegendEntry { StyleKey = "DN40",  LabelText = "ø40  Aparells (lavabo, aigüera)" },
            };
        }

        private static string NormativaText(NormativaFormat n)
        {
            switch (n)
            {
                case NormativaFormat.CTE_HS5: return "CTE DB-HS5 · EN 12056 System I";
                case NormativaFormat.CTE_HS4: return "CTE DB-HS4 · EN 806";
                case NormativaFormat.RITE:    return "RITE IT 1.3 · UNE-EN ISO 6708";
                default:                      return "E3G Custom";
            }
        }

        // --------------------------------------------------------
        // 5. BLOC DE TÍTOL (caixetí)
        // --------------------------------------------------------

        /// <summary>
        /// Afegeix un caixetí simple al cantó inferior dret del canvas.
        /// </summary>
        public static List<ElementId> AddTitleBlock(
            Document    doc,
            View        view,
            XarxaInfo   xarxa,
            SchematicParameters prm,
            double      svgW, double svgH,
            TextNoteType tnt = null)
        {
            var ids = new List<ElementId>();
            double f   = prm.Layout.PixelToFeet;
            double bw  = 320; // amplada caixetí
            double bh  = 60;  // alçada caixetí
            double xL  = svgW - bw - 10;
            double xR  = svgW - 10;
            double yT  = svgH - bh - 10;
            double yB  = svgH - 10;

            // Marc
            AddDetailLineZone(doc, view, xL, yT, xR, yT, prm);
            AddDetailLineZone(doc, view, xR, yT, xR, yB, prm);
            AddDetailLineZone(doc, view, xR, yB, xL, yB, prm);
            AddDetailLineZone(doc, view, xL, yB, xL, yT, prm);

            // Contingut
            ids.Add(CreateTextNote(doc, view, xL + 6, yT + 14,
                $"E3G — Enginyeria i Energia", prm, tnt, HorizontalTextAlignment.Left));
            ids.Add(CreateTextNote(doc, view, xL + 6, yT + 26,
                $"Sistema: {xarxa?.Sistema ?? "—"}", prm, tnt, HorizontalTextAlignment.Left));
            ids.Add(CreateTextNote(doc, view, xL + 6, yT + 38,
                $"UDtot={xarxa?.UdTotals:0} · Q={xarxa?.QTotalLs:0.00} l/s · ø{xarxa?.DnSortidaMm}",
                prm, tnt, HorizontalTextAlignment.Left));
            ids.Add(CreateTextNote(doc, view, xR - 6, yT + 14,
                NormativaText(prm.Normativa), prm, tnt, HorizontalTextAlignment.Right));
            ids.Add(CreateTextNote(doc, view, xR - 6, yT + 26,
                $"Escala 1:{prm.ViewScale}", prm, tnt, HorizontalTextAlignment.Right));
            ids.Add(CreateTextNote(doc, view, xR - 6, yT + 38,
                DateTime.Now.ToString("dd/MM/yyyy"), prm, tnt, HorizontalTextAlignment.Right));

            return ids;
        }

        // --------------------------------------------------------
        // 6. NOTES DE BAIXANT (Nom + DN + UD màx)
        // --------------------------------------------------------

        public static ElementId AddBaixantNote(
            Document doc, View view,
            string nom, int dn, double udMax,
            double xPx, double yPx,
            SchematicParameters prm,
            TextNoteType tnt = null)
        {
            string text = $"{nom}\nø{dn}\n{udMax:0}UD";
            return CreateTextNote(doc, view, xPx, yPx, text, prm, tnt);
        }

        // --------------------------------------------------------
        // HELPERS PRIVATS
        // --------------------------------------------------------

        private static DimensionType GetDefaultDimensionType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(dt => dt.StyleType == DimensionStyleType.Linear);
        }

        public static ElementId CreateTextNote(
            Document   doc,
            View       view,
            double     xPx, double yPx,
            string     text,
            SchematicParameters prm,
            TextNoteType tnt = null,
            HorizontalTextAlignment hAlign = HorizontalTextAlignment.Center)
        {
            if (string.IsNullOrWhiteSpace(text)) return ElementId.InvalidElementId;
            try
            {
                double f  = prm.Layout.PixelToFeet;
                var pos   = new XYZ(xPx * f, -yPx * f, 0);
                var tntId = (tnt ?? GetDefaultTextNoteType(doc))?.Id;
                if (tntId == null) return ElementId.InvalidElementId;

                var opts = new TextNoteOptions(tntId)
                {
                    HorizontalAlignment = hAlign,
                    KeepRotatedTextReadable = true
                };
                var tn = TextNote.Create(doc, view.Id, pos, text, opts);
                return tn?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static TextNoteType GetDefaultTextNoteType(Document doc)
        {
            string prefName = null; // es pot parametritzar
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

        private static void AddDetailLineZone(
            Document doc, View view,
            double x0Px, double y0Px,
            double x1Px, double y1Px,
            SchematicParameters prm)
            => AddDetailLineStyled(doc, view, x0Px, y0Px, x1Px, y1Px, "ZONE", prm);

        private static void AddDetailLineStyled(
            Document doc, View view,
            double x0Px, double y0Px,
            double x1Px, double y1Px,
            string styleKey,
            SchematicParameters prm)
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
    }
}
