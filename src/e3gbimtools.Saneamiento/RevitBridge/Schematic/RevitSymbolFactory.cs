// ============================================================================
//  RevitSymbolFactory.cs
//  Genera los símbolos esquemáticos (TEE, ELBOW, TERMINAL, CROSS, REDUCER,
//  FUENTE) y los segmentos de línea como DetailCurve + FilledRegion sobre
//  una ViewDrafting.  No depende de ninguna familia .rfa externa — todo se
//  construye con primitivas.  En el futuro las familias se podrán sustituir
//  cambiando solo esta clase.
//
//  Convenciones:
//    - Coordenadas de entrada en MILÍMETROS (las que produce el layout).
//    - Aquí se convierten a feet (unidad interna de Revit) con UnitUtils.
//    - Y interna del SchematicLayout crece hacia abajo; en ViewDrafting
//      crece hacia arriba.  Al construir los XYZ invertimos el signo.
//
//  Transacción:
//    El llamador debe abrir la Transaction; este servicio NO la maneja.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.Saneamiento.Core.Schematic;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public sealed class RevitSymbolFactory
    {
        private readonly Document _doc;
        private readonly View _view;
        private readonly ElementId _systemLineStyleId;
        private readonly ElementId _critPathLineStyleId;
        private readonly ElementId _dashedLineStyleId;
        private readonly ElementId _solidFillId;
        private readonly FilledRegionType _filledRegionType;

        public RevitSymbolFactory(Document doc, View view,
            (byte R, byte G, byte B) systemColor,
            (byte R, byte G, byte B) critColor)
        {
            _doc = doc;
            _view = view;
            _systemLineStyleId = GetOrCreateLineStyle(doc, "MEPCalc_Sistema",
                new Color(systemColor.R, systemColor.G, systemColor.B), 5);
            _critPathLineStyleId = GetOrCreateLineStyle(doc, "MEPCalc_CaminoCritico",
                new Color(critColor.R, critColor.G, critColor.B), 8);
            _dashedLineStyleId = GetOrCreateLineStyle(doc, "MEPCalc_Bifurcacion",
                new Color(systemColor.R, systemColor.G, systemColor.B), 1);

            _solidFillId = GetSolidFillId(doc);
            _filledRegionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();
        }

        // ── COLOCACIÓN DE FAMILIAS .rfa ────────────────────────────────────

        /// <summary>
        /// Coloca una instancia de la FamilySymbol indicada en la posición
        /// dada (coords del layout en mm).  Para familias de tipo Detail Item
        /// usa NewFamilyInstance con vista; para Generic Annotation también.
        /// Devuelve el ElementId creado o InvalidElementId si falla.
        /// </summary>
        public ElementId PlaceFamilyInstance(SchematicPoint pos, FamilySymbol sym, bool critical)
        {
            if (sym == null) return ElementId.InvalidElementId;
            try
            {
                if (!sym.IsActive) sym.Activate();
                var xyz = ToXyz(pos);
                var inst = _doc.Create.NewFamilyInstance(xyz, sym, _view);
                return inst?.Id ?? ElementId.InvalidElementId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        // ── PRIMITIVAS ─────────────────────────────────────────────────────

        /// <summary>Línea simple entre dos puntos del layout.</summary>
        public DetailLine DrawLine(SchematicPoint a, SchematicPoint b, bool critical)
        {
            var p1 = ToXyz(a);
            var p2 = ToXyz(b);
            if (p1.IsAlmostEqualTo(p2)) return null;
            var line = Line.CreateBound(p1, p2);
            var dc = _doc.Create.NewDetailCurve(_view, line) as DetailLine;
            if (dc != null)
            {
                var styleId = critical ? _critPathLineStyleId : _systemLineStyleId;
                if (styleId != ElementId.InvalidElementId)
                {
                    var gs = _doc.GetElement(styleId) as GraphicsStyle;
                    if (gs != null) dc.LineStyle = gs;
                }
            }
            return dc;
        }

        /// <summary>
        /// "Línea discontinua" — simulada con segmentos cortos porque Revit
        /// no expone un estilo punteado vía DetailCurve sin family de estilo
        /// de línea propia.  Cada segmento es de 3 mm con hueco de 3 mm.
        /// </summary>
        public void DrawDashed(SchematicPoint a, SchematicPoint b, bool critical)
        {
            var p1 = ToXyz(a);
            var p2 = ToXyz(b);
            if (p1.IsAlmostEqualTo(p2)) return;
            double total = p1.DistanceTo(p2);
            double dashFt = MmToFt(3.0);
            double gapFt = MmToFt(3.0);
            double step = dashFt + gapFt;
            var dir = (p2 - p1).Normalize();
            for (double t = 0; t < total; t += step)
            {
                var s = p1 + dir * t;
                var e = p1 + dir * Math.Min(t + dashFt, total);
                if (s.IsAlmostEqualTo(e)) continue;
                var seg = Line.CreateBound(s, e);
                var dc = _doc.Create.NewDetailCurve(_view, seg) as DetailLine;
                if (dc != null)
                {
                    var styleId = critical ? _critPathLineStyleId : _dashedLineStyleId;
                    if (styleId != ElementId.InvalidElementId)
                    {
                        var gs = _doc.GetElement(styleId) as GraphicsStyle;
                        if (gs != null) dc.LineStyle = gs;
                    }
                }
            }
        }

        // ── GLIFOS POR TIPO ────────────────────────────────────────────────

        /// <summary>Flecha de fuente (triángulo apuntando hacia abajo).</summary>
        public void DrawSource(SchematicPoint pos)
        {
            double s = 4.0;     // mm de lado
            var top1 = new SchematicPoint(pos.XMm - s, pos.YMm - s);
            var top2 = new SchematicPoint(pos.XMm + s, pos.YMm - s);
            var bot  = new SchematicPoint(pos.XMm, pos.YMm + s);
            DrawLine(top1, top2, false);
            DrawLine(top2, bot, false);
            DrawLine(bot, top1, false);
        }

        /// <summary>Glifo TEE — T tumbada.</summary>
        public void DrawTee(SchematicPoint pos, bool critical)
        {
            double s = 3.0;
            DrawLine(new SchematicPoint(pos.XMm - s, pos.YMm),
                     new SchematicPoint(pos.XMm + s, pos.YMm), critical);
            DrawLine(new SchematicPoint(pos.XMm, pos.YMm),
                     new SchematicPoint(pos.XMm, pos.YMm + s), critical);
        }

        public void DrawElbow(SchematicPoint pos, bool critical)
        {
            double s = 3.0;
            DrawLine(new SchematicPoint(pos.XMm, pos.YMm - s),
                     new SchematicPoint(pos.XMm, pos.YMm), critical);
            DrawLine(new SchematicPoint(pos.XMm, pos.YMm),
                     new SchematicPoint(pos.XMm + s, pos.YMm), critical);
        }

        public void DrawCross(SchematicPoint pos, bool critical)
        {
            double s = 3.0;
            DrawLine(new SchematicPoint(pos.XMm - s, pos.YMm),
                     new SchematicPoint(pos.XMm + s, pos.YMm), critical);
            DrawLine(new SchematicPoint(pos.XMm, pos.YMm - s),
                     new SchematicPoint(pos.XMm, pos.YMm + s), critical);
        }

        public void DrawReducer(SchematicPoint pos, bool critical)
        {
            double w = 4.0, h = 2.0;
            var pts = new[] {
                new SchematicPoint(pos.XMm - w, pos.YMm - h),
                new SchematicPoint(pos.XMm + w, pos.YMm - h/2),
                new SchematicPoint(pos.XMm + w, pos.YMm + h/2),
                new SchematicPoint(pos.XMm - w, pos.YMm + h),
            };
            DrawLine(pts[0], pts[1], critical);
            DrawLine(pts[1], pts[2], critical);
            DrawLine(pts[2], pts[3], critical);
            DrawLine(pts[3], pts[0], critical);
        }

        /// <summary>Terminal: cuadrado relleno (FilledRegion si disponible, si no líneas).</summary>
        public void DrawTerminal(SchematicPoint pos, bool critical)
        {
            double s = 3.0;
            var corners = new[] {
                new SchematicPoint(pos.XMm - s, pos.YMm - s),
                new SchematicPoint(pos.XMm + s, pos.YMm - s),
                new SchematicPoint(pos.XMm + s, pos.YMm + s),
                new SchematicPoint(pos.XMm - s, pos.YMm + s),
            };
            // FilledRegion para que tenga aspecto sólido como en el preview.
            if (_filledRegionType != null && _solidFillId != ElementId.InvalidElementId)
            {
                try
                {
                    var loop = new CurveLoop();
                    var xs = corners.Select(ToXyz).ToArray();
                    loop.Append(Line.CreateBound(xs[0], xs[1]));
                    loop.Append(Line.CreateBound(xs[1], xs[2]));
                    loop.Append(Line.CreateBound(xs[2], xs[3]));
                    loop.Append(Line.CreateBound(xs[3], xs[0]));
                    FilledRegion.Create(_doc, _filledRegionType.Id, _view.Id,
                        new List<CurveLoop> { loop });
                    return;
                }
                catch { /* fallback a líneas */ }
            }
            DrawLine(corners[0], corners[1], critical);
            DrawLine(corners[1], corners[2], critical);
            DrawLine(corners[2], corners[3], critical);
            DrawLine(corners[3], corners[0], critical);
        }

        /// <summary>Círculo de bifurcación (cuando no hay fitting concreto que dibujar).</summary>
        public void DrawJunctionDot(SchematicPoint pos, bool critical)
        {
            double r = 2.0;
            var center = ToXyz(pos);
            // El "círculo" se aproxima con un Arc completo (Revit lo dibuja como elipse en vista).
            try
            {
                var normal = XYZ.BasisZ;
                var refDir = XYZ.BasisX;
                var arc = Arc.Create(center, MmToFt(r), 0, 2 * Math.PI, refDir, XYZ.BasisY);
                _doc.Create.NewDetailCurve(_view, arc);
            }
            catch { /* fallback omitido */ }
        }

        // ── TEXTO ──────────────────────────────────────────────────────────

        public TextNote DrawText(SchematicPoint pos, string text, double heightMm, bool italic)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var tnt = GetTextNoteType(heightMm);
            if (tnt == null) return null;
            var p = ToXyz(pos);
            try
            {
                var opts = new TextNoteOptions(tnt.Id)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                };
                return TextNote.Create(_doc, _view.Id, p, text, opts);
            }
            catch { return null; }
        }

        // ── UTILIDADES ─────────────────────────────────────────────────────

        private XYZ ToXyz(SchematicPoint pt)
            => new XYZ(MmToFt(pt.XMm), -MmToFt(pt.YMm), 0);   // -Y: arriba en Revit

        private static double MmToFt(double mm)
            => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        private TextNoteType GetTextNoteType(double heightMm)
        {
            var all = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>().ToList();
            return all.FirstOrDefault();   // tipo por defecto; mejora futura: crear uno
        }

        private static ElementId GetSolidFillId(Document doc)
        {
            try
            {
                var pattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);
                return pattern?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        /// <summary>
        /// Devuelve un GraphicsStyle existente con el nombre dado o intenta
        /// crear una subcategoría de Detail Items con ese nombre y color.
        /// </summary>
        private static ElementId GetOrCreateLineStyle(Document doc, string name,
            Color color, int weight)
        {
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(GraphicsStyle))
                    .Cast<GraphicsStyle>()
                    .FirstOrDefault(g => g.Name == name
                        && g.GraphicsStyleCategory?.Parent?.Id ==
                           doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).Id);
                if (existing != null) return existing.Id;

                // Crear subcategoría
                var parent = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                var sub = doc.Settings.Categories.NewSubcategory(parent, name);
                sub.LineColor = color;
                sub.SetLineWeight(weight, GraphicsStyleType.Projection);
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id
                    ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }
    }
}
