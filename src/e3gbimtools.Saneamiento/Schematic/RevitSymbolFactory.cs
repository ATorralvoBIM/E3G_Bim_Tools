// ================================================================
// RevitSymbolFactory.cs  —  E3G BIM Tools  FASE 3
// Fàbrica de símbols MEP per a l'esquema de principi
//
// Estratègia dual:
//   1. PRIMARY  → cercar la família Revit per nom i instanciar-la
//   2. FALLBACK → dibuixar geometria 2D (DetailCurve) si la
//                 família no existeix al projecte
//
// Símbols suportats:
//   - Vàlvula de bola (esfera)
//   - Vàlvula de retenció (check)
//   - Bomba (cercle + fletxa)
//   - Caldera / escalfador (rectangle + flames)
//   - Dipòsit (rectangle)
//   - Comptador (doble cercle)
//   - Filtre en Y (Y)
//   - Purgador d'aire (triangle)
//
// Autors: E3G — Enginyeria i Energia
// Data:   Maig 2026
// ================================================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Schematic
{
    // ============================================================
    // ENUM DE TIPUS DE SÍMBOL
    // ============================================================

    public enum SymbolType
    {
        BallValve,       // Vàlvula de bola
        CheckValve,      // Vàlvula de retenció
        Pump,            // Bomba
        Boiler,          // Caldera / escalfador
        Tank,            // Dipòsit d'acumulació
        WaterMeter,      // Comptador d'aigua
        FilterY,         // Filtre en Y
        AirVent,         // Purgador d'aire automàtic
        Reducer,         // Reducció de diàmetre
        Tee,             // Derivació en T
        GenericCircle,   // Cercle genèric
    }

    // ============================================================
    // RESULTAT D'UNA INSERCIÓ DE SÍMBOL
    // ============================================================

    public class PlacedSymbol
    {
        public SymbolType   Type       { get; set; }
        public XYZ          Position   { get; set; }
        public ElementId    ElementId  { get; set; }   // FamilyInstance o null
        public List<ElementId> GeomIds { get; set; } = new List<ElementId>(); // DetailCurves
        public bool         IsFallback { get; set; }
        public string       Label      { get; set; }
    }

    // ============================================================
    // FÀBRICA PRINCIPAL
    // ============================================================

    public static class RevitSymbolFactory
    {
        // ---- Cache de FamilySymbol per nom ----
        private static readonly Dictionary<string, FamilySymbol> _symbolCache
            = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

        // ---- Escala px → peus (de LayoutConfig) ----
        private static double _f = 0.5 / 304.8;

        // --------------------------------------------------------
        // API PÚBLICA
        // --------------------------------------------------------

        /// <summary>
        /// Col·loca un símbol a la posició (xPx, yPx) en coordenades
        /// de canvas (pixels virtuals, Y cap avall).
        /// Ha d'executar-se DINS d'una Transaction activa.
        /// </summary>
        public static PlacedSymbol Place(
            Document     doc,
            View         view,
            SymbolType   type,
            double       xPx,
            double       yPx,
            SchematicParameters prm,
            string       label = null,
            double       rotationDeg = 0)
        {
            _f = prm.Layout.PixelToFeet;
            var pos = PxToXYZ(xPx, yPx);

            // 1. Intentar família Revit
            string famName = GetFamilyName(type, prm.Symbols);
            if (!string.IsNullOrEmpty(famName))
            {
                var sym = FindOrLoadSymbol(doc, famName);
                if (sym != null)
                {
                    return PlaceFamilyInstance(doc, view, sym, pos, type, label, rotationDeg);
                }
            }

            // 2. Fallback: geometria DetailCurve
            if (prm.Symbols.UseFallbackGeometry)
            {
                return PlaceFallback(doc, view, type, pos, prm.Symbols.SymbolSize,
                                     prm, label, rotationDeg);
            }

            return null;
        }

        /// <summary>
        /// Col·loca una sèrie de símbols sobre una canonada horitzontal
        /// (entre x0 i x1 a l'alçada yPx), amb distribució equidistant.
        /// </summary>
        public static List<PlacedSymbol> PlaceOnPipe(
            Document doc, View view,
            double x0Px, double x1Px, double yPx,
            List<(SymbolType type, string label)> symbols,
            SchematicParameters prm)
        {
            var result = new List<PlacedSymbol>();
            if (symbols.Count == 0) return result;

            double step = (x1Px - x0Px) / (symbols.Count + 1);
            for (int i = 0; i < symbols.Count; i++)
            {
                double x = x0Px + step * (i + 1);
                result.Add(Place(doc, view, symbols[i].type, x, yPx, prm, symbols[i].label));
            }
            return result;
        }

        // --------------------------------------------------------
        // INTERNALS — FAMÍLIA REVIT
        // --------------------------------------------------------

        private static string GetFamilyName(SymbolType type, SymbolConfig cfg)
        {
            switch (type)
            {
                case SymbolType.BallValve:  return cfg.ValveFamilyName;
                case SymbolType.CheckValve: return cfg.ValveFamilyName;
                case SymbolType.Pump:       return cfg.PumpFamilyName;
                case SymbolType.Boiler:     return cfg.BoilerFamilyName;
                default:                    return null;
            }
        }

        private static FamilySymbol FindOrLoadSymbol(Document doc, string familyName)
        {
            if (_symbolCache.TryGetValue(familyName, out var cached))
                return cached.IsValidObject ? cached : null;

            var sym = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => string.Equals(
                    fs.Family.Name, familyName, StringComparison.OrdinalIgnoreCase));

            if (sym != null) _symbolCache[familyName] = sym;
            return sym;
        }

        private static PlacedSymbol PlaceFamilyInstance(
            Document doc, View view,
            FamilySymbol sym, XYZ pos,
            SymbolType type, string label, double rotDeg)
        {
            if (!sym.IsActive) sym.Activate();
            var inst = doc.Create.NewFamilyInstance(pos, sym, view);

            if (Math.Abs(rotDeg) > 0.01)
            {
                var axis = Autodesk.Revit.DB.Line.CreateBound(pos, pos + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, inst.Id, axis, rotDeg * Math.PI / 180.0);
            }

            return new PlacedSymbol
            {
                Type      = type,
                Position  = pos,
                ElementId = inst.Id,
                IsFallback = false,
                Label     = label
            };
        }

        // --------------------------------------------------------
        // INTERNALS — FALLBACK GEOMETRIA 2D
        // --------------------------------------------------------

        private static PlacedSymbol PlaceFallback(
            Document doc, View view,
            SymbolType type, XYZ pos,
            double sizePx, SchematicParameters prm,
            string label, double rotDeg)
        {
            var ids = new List<ElementId>();
            double r = sizePx * _f / 2.0;  // radi en peus

            switch (type)
            {
                case SymbolType.BallValve:
                    ids.AddRange(DrawBallValve(doc, view, pos, r, prm));
                    break;
                case SymbolType.CheckValve:
                    ids.AddRange(DrawCheckValve(doc, view, pos, r, prm));
                    break;
                case SymbolType.Pump:
                    ids.AddRange(DrawPump(doc, view, pos, r, prm));
                    break;
                case SymbolType.Boiler:
                    ids.AddRange(DrawBoiler(doc, view, pos, r * 1.5, prm));
                    break;
                case SymbolType.Tank:
                    ids.AddRange(DrawTank(doc, view, pos, r * 1.2, prm));
                    break;
                case SymbolType.WaterMeter:
                    ids.AddRange(DrawMeter(doc, view, pos, r, prm));
                    break;
                case SymbolType.FilterY:
                    ids.AddRange(DrawFilterY(doc, view, pos, r, prm));
                    break;
                case SymbolType.AirVent:
                    ids.AddRange(DrawAirVent(doc, view, pos, r, prm));
                    break;
                case SymbolType.Reducer:
                    ids.AddRange(DrawReducer(doc, view, pos, r, prm));
                    break;
                default:
                    ids.AddRange(DrawCircle(doc, view, pos, r, prm));
                    break;
            }

            return new PlacedSymbol
            {
                Type       = type,
                Position   = pos,
                GeomIds    = ids,
                IsFallback = true,
                Label      = label
            };
        }

        // --------------------------------------------------------
        // GEOMETRIES FALLBACK (DetailCurve)
        // --------------------------------------------------------

        // Vàlvula de bola: rombe (quadrat girat 45°)
        private static List<ElementId> DrawBallValve(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            var pts = new[]
            {
                c + new XYZ( r,  0, 0),
                c + new XYZ( 0, -r, 0),
                c + new XYZ(-r,  0, 0),
                c + new XYZ( 0,  r, 0),
                c + new XYZ( r,  0, 0),
            };
            for (int i = 0; i < pts.Length - 1; i++)
                ids.Add(CreateDetailLine(doc, view, pts[i], pts[i + 1], "DN90", prm));
            // Línia vertical de maniobra
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(0, -r * 1.5, 0),
                c + new XYZ(0,  r * 1.5, 0), "DN90", prm));
            return ids;
        }

        // Vàlvula de retenció: triangle + línia
        private static List<ElementId> DrawCheckValve(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            // Triangle omplert (3 costats)
            var A = c + new XYZ(-r, -r, 0);
            var B = c + new XYZ( r,  0, 0);
            var C = c + new XYZ(-r,  r, 0);
            ids.Add(CreateDetailLine(doc, view, A, B, "DN90", prm));
            ids.Add(CreateDetailLine(doc, view, B, C, "DN90", prm));
            ids.Add(CreateDetailLine(doc, view, C, A, "DN90", prm));
            // Línia de tancament
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(r, -r, 0),
                c + new XYZ(r,  r, 0), "DN90", prm));
            return ids;
        }

        // Bomba: cercle + fletxa
        private static List<ElementId> DrawPump(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            ids.AddRange(DrawCirclePoints(doc, view, c, r, 12, "DN125", prm));
            // Fletxa interior
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r * 0.5, 0, 0),
                c + new XYZ( r * 0.5, 0, 0), "DN125", prm));
            // Cap de fletxa
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(r * 0.5, 0, 0),
                c + new XYZ(r * 0.2, -r * 0.3, 0), "DN125", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(r * 0.5, 0, 0),
                c + new XYZ(r * 0.2,  r * 0.3, 0), "DN125", prm));
            return ids;
        }

        // Caldera: rectangle arrodonit amb símbol de flama
        private static List<ElementId> DrawBoiler(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            double w = r, h = r * 1.5;
            // Rectangle
            var tl = c + new XYZ(-w, -h, 0);
            var tr = c + new XYZ( w, -h, 0);
            var br = c + new XYZ( w,  h, 0);
            var bl = c + new XYZ(-w,  h, 0);
            ids.Add(CreateDetailLine(doc, view, tl, tr, "DN160", prm));
            ids.Add(CreateDetailLine(doc, view, tr, br, "DN160", prm));
            ids.Add(CreateDetailLine(doc, view, br, bl, "DN160", prm));
            ids.Add(CreateDetailLine(doc, view, bl, tl, "DN160", prm));
            // Flama (dos arcs simplificats com a línies)
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-w * 0.3, 0, 0),
                c + new XYZ(       0, -h * 0.5, 0), "DN160", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(       0, -h * 0.5, 0),
                c + new XYZ( w * 0.3, 0, 0), "DN160", prm));
            return ids;
        }

        // Dipòsit: rectangle amb línia de nivell
        private static List<ElementId> DrawTank(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            double w = r * 0.8, h = r * 1.4;
            var tl = c + new XYZ(-w, -h, 0); var tr = c + new XYZ(w, -h, 0);
            var br = c + new XYZ( w,  h, 0); var bl = c + new XYZ(-w, h, 0);
            ids.Add(CreateDetailLine(doc, view, tl, tr, "DN125", prm));
            ids.Add(CreateDetailLine(doc, view, tr, br, "DN125", prm));
            ids.Add(CreateDetailLine(doc, view, br, bl, "DN125", prm));
            ids.Add(CreateDetailLine(doc, view, bl, tl, "DN125", prm));
            // Línia de nivell a 70%
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-w * 0.9, -h * 0.3, 0),
                c + new XYZ( w * 0.9, -h * 0.3, 0), "ZONE", prm));
            return ids;
        }

        // Comptador: doble cercle
        private static List<ElementId> DrawMeter(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            ids.AddRange(DrawCirclePoints(doc, view, c, r, 12, "DN90", prm));
            ids.AddRange(DrawCirclePoints(doc, view, c, r * 0.55, 8, "DN90", prm));
            return ids;
        }

        // Filtre en Y: línia + Y
        private static List<ElementId> DrawFilterY(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r, 0, 0), c + new XYZ(r, 0, 0), "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c, c + new XYZ(0,  r * 1.2, 0), "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r * 0.5, r * 0.5, 0), c, "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ( r * 0.5, r * 0.5, 0), c, "DN90", prm));
            return ids;
        }

        // Purgador: triangle apuntant amunt
        private static List<ElementId> DrawAirVent(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            var A = c + new XYZ(-r,  r * 0.5, 0);
            var B = c + new XYZ( r,  r * 0.5, 0);
            var C = c + new XYZ( 0, -r, 0);
            ids.Add(CreateDetailLine(doc, view, A, B, "DN50", prm));
            ids.Add(CreateDetailLine(doc, view, B, C, "DN50", prm));
            ids.Add(CreateDetailLine(doc, view, C, A, "DN50", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(0, -r, 0),
                c + new XYZ(0, -r * 2, 0), "DN50", prm));
            return ids;
        }

        // Reducció: trapezi
        private static List<ElementId> DrawReducer(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            double hw = r * 0.5;
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r, -hw, 0), c + new XYZ(r, -hw * 0.4, 0), "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r,  hw, 0), c + new XYZ(r,  hw * 0.4, 0), "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ(-r, -hw, 0), c + new XYZ(-r, hw, 0), "DN90", prm));
            ids.Add(CreateDetailLine(doc, view,
                c + new XYZ( r, -hw * 0.4, 0), c + new XYZ(r, hw * 0.4, 0), "DN90", prm));
            return ids;
        }

        // Cercle genèric (N segments)
        private static List<ElementId> DrawCircle(
            Document doc, View view, XYZ c, double r, SchematicParameters prm)
            => DrawCirclePoints(doc, view, c, r, 16, "DN90", prm);

        private static List<ElementId> DrawCirclePoints(
            Document doc, View view, XYZ c, double r,
            int segments, string styleKey, SchematicParameters prm)
        {
            var ids = new List<ElementId>();
            double step = 2 * Math.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                double a0 = i * step, a1 = (i + 1) * step;
                var p0 = c + new XYZ(r * Math.Cos(a0), r * Math.Sin(a0), 0);
                var p1 = c + new XYZ(r * Math.Cos(a1), r * Math.Sin(a1), 0);
                ids.Add(CreateDetailLine(doc, view, p0, p1, styleKey, prm));
            }
            return ids;
        }

        // --------------------------------------------------------
        // HELPER: crear DetailCurve amb estil E3G
        // --------------------------------------------------------

        private static ElementId CreateDetailLine(
            Document doc, View view,
            XYZ p0, XYZ p1,
            string styleKey, SchematicParameters prm)
        {
            if (p0.DistanceTo(p1) < 1e-6) return ElementId.InvalidElementId;
            try
            {
                var dl = doc.Create.NewDetailCurve(view,
                    Autodesk.Revit.DB.Line.CreateBound(p0, p1));

                if (prm.LineStyles.TryGetValue(styleKey, out var styleCfg))
                {
                    var gs = FindOrCreateLineStyle(doc, styleKey, styleCfg);
                    if (gs != null) dl.LineStyle = gs;
                }
                return dl.Id;
            }
            catch { return ElementId.InvalidElementId; }
        }

        // --------------------------------------------------------
        // HELPER: cache de GraphicsStyle (LineStyle)
        // --------------------------------------------------------

        private static readonly Dictionary<string, GraphicsStyle> _gsCache
            = new Dictionary<string, GraphicsStyle>(StringComparer.OrdinalIgnoreCase);

        public static GraphicsStyle FindOrCreateLineStyle(
            Document doc, string key, LineStyleConfig cfg)
        {
            if (_gsCache.TryGetValue(key, out var cached) && cached.IsValidObject) return cached;

            string nomEstil = $"E3G_{key}";
            var catLines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            Category cat = null;
            foreach (Category c in catLines.SubCategories)
                if (c.Name == nomEstil) { cat = c; break; }

            if (cat == null)
            {
                cat = doc.Settings.Categories.NewSubcategory(catLines, nomEstil);
                cat.LineColor = cfg.ToRevitColor();
                cat.SetLineWeight(cfg.LineWeight, GraphicsStyleType.Projection);
            }

            var gs = cat.GetGraphicsStyle(GraphicsStyleType.Projection);
            _gsCache[key] = gs;
            return gs;
        }

        // --------------------------------------------------------
        // UTILITAT: convertir px a XYZ Revit (Y invertida)
        // --------------------------------------------------------

        public static XYZ PxToXYZ(double xPx, double yPx, double zFt = 0)
            => new XYZ(xPx * _f, -yPx * _f, zFt);

        /// <summary>Buida la cache (cal cridar a l'inici de cada transacció).</summary>
        public static void ClearCache()
        {
            _symbolCache.Clear();
            _gsCache.Clear();
        }
    }
}
