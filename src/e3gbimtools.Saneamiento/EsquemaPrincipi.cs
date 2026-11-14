// ================================================================
// EsquemaPrincipi.cs  —  E3G BIM Tools  v2.0.0
// Pestaña "Esquema de Principi" del plugin E3G Saneamiento HS5
//
// Funcionalitat (idèntica a v1.0.0):
//   1. L'usuari calcula el sistema amb MagiCAD
//   2. L'usuari selecciona totes les tuberíes del sistema a Revit
//   3. Clica "Generar Esquema" → genera l'esquema de principi
//   4. Botó "Inserir a Revit" → crea una Drafting View
//
// MILLORES v2.0.0 respecte v1.0.0:
//   [LAYOUT v18]
//   - COL_W=130px garanteix zero solapaments d'etiquetes
//   - Connexions al MIG del gap entre trams (no al nivell de planta)
//   - Etiquetes connexió a CADA connexió individual (ø50·2UD, ø110·12UD)
//   - Alternança L/R per baixant — zero solapaments matemàticament
//   - UD màxim al costat OPOSAT a les connexions (zToY(43200))
//   - Panells col·lectors connectats al BAIXANT D'ORIGEN
//   - Anti-col·lisió Union-Find per panells
//   - Franja P00 ampliada (350px) per encabir panells
//   - SVG_W dinàmic — fletxa S01.A sempre dins del canvas
//   [DRAFTING VIEW]
//   - NOM ÚNIC: timestamp + counter estàtic (_counter++)
//     Evita l'error Revit "ja existeix una vista amb aquest nom"
//   - COLORS via OverrideGraphicSettings per cada DetailLine
//     Soluciona el problema de tot en negre
//
// Dependències:
//   - RevitAPI.dll / RevitAPIUI.dll (Revit 2021-2026)
//   - Plugin principal E3G_Saneamiento.cs (TabControl, ExternalEvent)
//
// Autors: E3G — Enginyeria i Energia
// Data:   Abril 2026
// ================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfGrid = System.Windows.Controls.Grid;

namespace E3G.Saneamiento
{
    // ============================================================
    // MODELS DE DADES  (idèntics a v1.0.0 + ConnZ i ConnSideIdx)
    // ============================================================

    public class BaixantInfo
    {
        public string Nom { get; set; }  // "B1", "B2" ...
        public int DnMm { get; set; }  // 90, 110, 125 ...
        public double XMm { get; set; }  // posició X en mm
        public double YMm { get; set; }  // posició Y en mm
        public double ZMinMm { get; set; }  // cota inferior
        public double ZMaxMm { get; set; }  // cota superior
        public string PlantaMin { get; set; }  // "P01"
        public string PlantaMax { get; set; }  // "P06"
        public bool EsConnexioVivenda { get; set; }

        public Dictionary<string, double> UdPerPlanta { get; set; }
            = new Dictionary<string, double>();

        // [NOU v2] Costat de les etiquetes de connexió (alterna: 0=L, 1=R)
        public int ConnSideIdx { get; set; }

        // [NOU v2] Z exactes de les connexions = mig del gap entre trams
        // Z connexió = (ZHi_tram_inferior + ZLo_tram_superior) / 2
        public List<double> ConnZ { get; set; } = new List<double>();

        public double UdMax => Trams.Where(t => t.Ud > 0)
                                    .Select(t => t.Ud)
                                    .DefaultIfEmpty(0).Max();

        public List<TramInfo> Trams { get; set; } = new List<TramInfo>();
    }

    public class TramInfo
    {
        public int ElementId { get; set; }
        public int DnMm { get; set; }
        public double Ud { get; set; }
        public double QLs { get; set; }
        // CORREGIT
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double Z0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Z1 { get; set; }
        public double LongitudMm { get; set; }
public bool EsVertical { get; set; }
public bool EsHoritzontal { get; set; }
public string Sistema { get; set; }
public double ZLo => Math.Min(Z0, Z1);
public double ZHi => Math.Max(Z0, Z1);
    }

    public class XarxaInfo
{
    public string Sistema { get; set; }
    public List<BaixantInfo> Baixants { get; set; } = new List<BaixantInfo>();
    public List<TramInfo> ColectorsHoritzontals { get; set; } = new List<TramInfo>();
    public List<TramInfo> TramsSortida { get; set; } = new List<TramInfo>();
    public List<TramInfo> TramsAparells { get; set; } = new List<TramInfo>();
    public double UdTotals { get; set; }
    public double QTotalLs { get; set; }
    public int DnSortidaMm { get; set; }
    public int NombrePlantes { get; set; }
}

// ============================================================
// MOTOR D'ANÀLISI DE LA XARXA
// ============================================================

public static class AnalisadorXarxa
{
    public static XarxaInfo Analitzar(Document doc, List<int> pipeIds)
    {
        var trams = LlegirTrams(doc, pipeIds);
        if (trams.Count == 0) return null;

        var sistema = trams.Select(t => t.Sistema)
                           .GroupBy(s => s)
                           .OrderByDescending(g => g.Count())
                           .First().Key;

        var xarxa = new XarxaInfo { Sistema = sistema };

        var verticals = trams.Where(t => t.EsVertical).ToList();
        var horitzontals = trams.Where(t => t.EsHoritzontal
                                            && t.LongitudMm > 50
                                            && t.DnMm >= 50
                                            && t.DnMm < 160).ToList();
        var sortida = trams.Where(t => t.DnMm >= 160).ToList();
        var aparells = trams.Where(t => t.DnMm <= 50).ToList();

        xarxa.TramsAparells = aparells;
        xarxa.TramsSortida = sortida;
        xarxa.ColectorsHoritzontals = horitzontals.OrderBy(t => t.ZLo).ToList();
        xarxa.Baixants = AgruparBaixants(verticals);

        if (sortida.Count > 0)
        {
            xarxa.UdTotals = sortida.Max(t => t.Ud);
            xarxa.QTotalLs = sortida.Max(t => t.QLs);
            xarxa.DnSortidaMm = sortida.Max(t => t.DnMm);
        }

        var totsZ = trams.SelectMany(t => new[] { t.Z0, t.Z1 });
        xarxa.NombrePlantes = PlantesServides(totsZ.Min(), totsZ.Max()).Count;

        return xarxa;
    }

    private static List<TramInfo> LlegirTrams(Document doc, List<int> ids)
    {
        var result = new List<TramInfo>();
        foreach (int id in ids)
        {
            var e = doc.GetElement(new ElementId(id));
            if (e?.Category?.Id.Value != (int)BuiltInCategory.OST_PipeCurves) continue;
            var loc = e.Location as LocationCurve;
            if (loc == null) continue;

            var p0 = loc.Curve.GetEndPoint(0);
            var p1 = loc.Curve.GetEndPoint(1);
            double x0 = p0.X * 304.8, y0 = p0.Y * 304.8, z0 = p0.Z * 304.8;
            double x1 = p1.X * 304.8, y1 = p1.Y * 304.8, z1 = p1.Z * 304.8;
            double dz = Math.Abs(z1 - z0);
            double dxy = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2));
            double len = Math.Sqrt(dxy * dxy + dz * dz);

            int dn = ParseDn(e.LookupParameter("Tamaño")?.AsValueString() ?? "");
            if (dn <= 0) continue;

            double ud = e.LookupParameter("MC Discharge Unit Sum")?.AsDouble() ?? 0;
            double qRaw = e.LookupParameter("MC Piping Max Drainage Flow")?.AsDouble() ?? 0;
            string sis = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)
                           ?.AsValueString() ?? "";

            result.Add(new TramInfo
            {
                ElementId = id,
                DnMm = dn,
                Ud = ud,
                QLs = Math.Round(qRaw * 28316.8 / 1000.0, 2),
                X0 = x0,
                Y0 = y0,
                Z0 = z0,
                X1 = x1,
                Y1 = y1,
                Z1 = z1,
                LongitudMm = Math.Round(len),
                EsVertical = dz > 500 && dxy < 200,
                EsHoritzontal = dxy > 200 && dz < 200,
                Sistema = sis
            });
        }
        return result;
    }

    private static List<BaixantInfo> AgruparBaixants(List<TramInfo> verticals)
    {
        var grups = verticals
            .GroupBy(t => $"{Math.Round(t.X0 / 250) * 250}_{Math.Round(t.Y0 / 250) * 250}")
            .OrderBy(g => double.Parse(g.Key.Split('_')[0]))
            .ToList();

        var baixants = new List<BaixantInfo>();
        int idx = 1;

        foreach (var g in grups)
        {
            var trams = g.OrderBy(t => t.ZLo).ToList();
            int dnMax = trams.Max(t => t.DnMm);
            double zMin = trams.Min(t => t.ZLo);
            double zMax = trams.Max(t => t.ZHi);

            var udPlanta = new Dictionary<string, double>();
            foreach (var t in trams)
            {
                string p = ZaPlanta((t.Z0 + t.Z1) / 2.0);
                if (!udPlanta.ContainsKey(p) || t.Ud > udPlanta[p])
                    udPlanta[p] = t.Ud;
            }

            // [NOU v2] ConnZ: Z al mig de cada GAP entre trams consecutius
            var connZ = new List<double>();
            for (int i = 0; i < trams.Count - 1; i++)
            {
                double hiAct = trams[i].ZHi;
                double loSeg = trams[i + 1].ZLo;
                if (loSeg > hiAct + 10)
                    connZ.Add((hiAct + loSeg) / 2.0);
            }
            // Filtrar: connexió ha de tenir trams actius als dos costats
            var actius = trams.Where(t => t.Ud > 0).ToList();
            if (actius.Count >= 2)
                connZ = connZ.Where(z =>
                    actius.Any(t => t.ZHi <= z + 200) &&
                    actius.Any(t => t.ZLo >= z - 200)).ToList();

            baixants.Add(new BaixantInfo
            {
                Nom = $"B{idx}",
                DnMm = dnMax,
                XMm = g.First().X0,
                YMm = g.First().Y0,
                ZMinMm = zMin,
                ZMaxMm = zMax,
                PlantaMin = ZaPlanta(zMin + 100),
                PlantaMax = ZaPlanta(zMax - 100),
                UdPerPlanta = udPlanta,
                EsConnexioVivenda = trams.Any(t => t.Ud >= 10),
                ConnSideIdx = (idx - 1) % 2,  // [NOU v2]
                ConnZ = connZ,         // [NOU v2]
                Trams = trams,
            });
            idx++;
        }

        return baixants.OrderBy(b => b.XMm).ToList();
    }

    public static int ParseDn(string tamano)
    {
        var s = (tamano ?? "").Replace("ø", "").Replace("Ø", "").Trim();
        return int.TryParse(s, out int dn) ? dn : 0;
    }

    public static string ZaPlanta(double zMm)
    {
        if (zMm < 41000) return "S01";
        if (zMm < 43860) return "P00";
        if (zMm < 46370) return "P01";
        if (zMm < 49520) return "P02";
        if (zMm < 52670) return "P03";
        if (zMm < 55820) return "P04";
        if (zMm < 58970) return "P05";
        return "P06";
    }

    public static List<string> PlantesServides(double zMin, double zMax)
    {
        var totes = new[] { "P01", "P02", "P03", "P04", "P05", "P06" };
        return totes.Where(p => { double zP = PlantaAZ(p); return zMin <= zP + 500 && zMax >= zP - 500; }).ToList();
    }

    private static double PlantaAZ(string p)
    {
        switch (p)
        {
            case "P01": return 44000;
            case "P02": return 47000;
            case "P03": return 50000;
            case "P04": return 53000;
            case "P05": return 56000;
            case "P06": return 59500;
            default: return 40000;
        }
    }
}

// ============================================================
// GENERADOR HTML  (substitueix GeneradorSvg de v1.0.0)
// Layout v18 definitiu
// ============================================================

public static class GeneradorHtml
{
    // ---- Colors i gruixos per DN ----
    static string ColorBaixant(int dn) => dn >= 125 ? "#1A6FA8" : "#1E8C4E";
    static string ColorConnexio(int dn) => dn >= 110 ? "#C96420" : "#7B6BAE";
    static string LabelConnexio(int dn) => dn >= 110 ? "ø110·12UD" : "ø50·2UD";
    static double SwConnexio(int dn) => dn >= 110 ? 2.0 : 1.5;
    static double SwBaixant(int dn) => dn >= 125 ? 3.0 : 2.5;

    // ---- Constants de layout v18 ----
    const int LM = 50;
    const int COL0 = 250;
    const int COL_W = 130;  // pas entre baixants — garanteix no solapament
    const int ARM = 20;   // braç connexió
    const int PW = 150;  // amplada panells col·lectors
    const int PW_AP = 205;  // amplada panells aparells
    const int GAP = 8;
    const int TH = 17;   // altura capçalera panell
    const int IH = 12;   // altura per item
    const int PV = 5;    // padding vertical
    const int FH = 14;   // altura peu (TOTAL UD)
    const int GP = 8;    // gap entre panells apilats

    // ---- Escala Z→Y (P00 = 350px) ----
    static readonly (double zBot, double zTop, double yBot, double yTop)[] ZONES = {
            (38500,43000,1040,1000),
            (43000,43860,1000, 650), // P00 — 350px
            (43860,46370, 650, 555),
            (46370,49520, 555, 460),
            (49520,52670, 460, 365),
            (52670,55820, 365, 270),
            (55820,58970, 270, 175),
            (58970,64000, 175,  60),
        };
    static readonly string[] ZONA_NOMS = { "S01", "P00", "P01", "P02", "P03", "P04", "P05", "P06" };
    static readonly string[] ZONA_COLS = { "#9A2218", "#8C7000", "#5A5A52", "#5A5A52", "#5A5A52", "#5A5A52", "#5A5A52", "#5A5A52" };

    const double Y_P00_top = 650;
    const double Y_C125 = 1018;
    const double Y_C160 = 1052;

    static double ZToY(double z)
    {
        foreach (var zo in ZONES)
            if (z >= zo.zBot && z <= zo.zTop)
                return zo.yBot + (z - zo.zBot) / (zo.zTop - zo.zBot) * (zo.yTop - zo.yBot);
        return z < 38500 ? 1060 : 60;
    }

    static int XB(int i) => LM + COL0 + i * COL_W;
    static int PanelH(int n) => TH + PV + n * IH + PV + FH + PV;

    public static string Generar(XarxaInfo xarxa)
    {
        if (xarxa == null || xarxa.Baixants.Count == 0)
            return HtmlBuit();

        int N = xarxa.Baixants.Count;

        // ---- Panells aparells (estàtics) ----
        var apItems_P00 = new List<string> {
                "10 × Inodor   ø110  4UD  →  40UD",
                " 1 × Sumidero  ø50   1UD  →   1UD",
            };
        var apItems_P01 = new List<string> {
                "10 × Lavabo          ø40  1UD  →  10UD",
                " 4 × Aigüera         ø50  1UD  →   4UD",
                " 4 × Rentavaixelles  ø50  1UD  →   4UD",
                " 1 × Rentadora       ø50  3UD  →   3UD",
                " 1 × Termo elèctric  ø40  1UD  →   1UD",
            };

        // ---- Panells col·lectors: un per baixant amb col·lectors ----
        var pcDefs = new List<(int bajIdx, string side, int xL, int xR, int h,
                                string title, string color, List<string> items, int udTotal)>();
        var horitzPerBaixant = new Dictionary<int, List<TramInfo>>();
        foreach (var ht in xarxa.ColectorsHoritzontals)
        {
            int bIdx = -1; double dMin = double.MaxValue;
            for (int i = 0; i < N; i++)
            {
                var b = xarxa.Baixants[i];
                double d = Math.Min(
                    Math.Sqrt(Math.Pow(ht.X0 - b.XMm, 2) + Math.Pow(ht.Y0 - b.YMm, 2)),
                    Math.Sqrt(Math.Pow(ht.X1 - b.XMm, 2) + Math.Pow(ht.Y1 - b.YMm, 2)));
                if (d < dMin) { dMin = d; bIdx = i; }
            }
            // Sempre assignem al més proper, sense llindar de distància
            if (bIdx < 0 || N == 0) continue;

            if (!horitzPerBaixant.ContainsKey(bIdx)) horitzPerBaixant[bIdx] = new List<TramInfo>();
            horitzPerBaixant[bIdx].Add(ht);
        }
        foreach (var kvp in horitzPerBaixant.OrderBy(k => k.Key))
        {
            int bi = kvp.Key;
            var hList = kvp.Value.OrderByDescending(h => h.Ud).ToList();
            var items = hList.Select(h => $"ø{h.DnMm} · {h.Ud:0}UD").ToList();
            int udTot = (int)hList.Sum(h => h.Ud);
            string color = ColorBaixant(xarxa.Baixants[bi].DnMm);
            // Panell al costat oposat a les connexions
            string connSide = xarxa.Baixants[bi].ConnSideIdx == 0 ? "L" : "R";
            string pSide = connSide == "L" ? "R" : "L";
            int xL = pSide == "L" ? XB(bi) - GAP - PW : XB(bi) + GAP;
            int xR = pSide == "L" ? XB(bi) - GAP : XB(bi) + GAP + PW;
            pcDefs.Add((bi, pSide, xL, xR, PanelH(items.Count),
                $"Col·lectors → {xarxa.Baixants[bi].Nom}", color, items, udTot));
        }

        // ---- Anti-col·lisió Union-Find ----
        var allP = new List<(int xL, int xR, int h)>();
        allP.Add((LM + 4, LM + 4 + PW_AP, PanelH(apItems_P00.Count)));  // 0
        allP.Add((LM + 4, LM + 4 + PW_AP, PanelH(apItems_P01.Count)));  // 1
        foreach (var pc in pcDefs) allP.Add((pc.xL, pc.xR, pc.h));

        int M = allP.Count;
        var par = Enumerable.Range(0, M).ToArray();
        int Find(int i) { while (par[i] != i) { par[i] = par[par[i]]; i = par[i]; } return i; }
        void Unite(int a, int b) { par[Find(a)] = Find(b); }
        for (int i = 0; i < M; i++) for (int j = i + 1; j < M; j++)
            if (allP[i].xL < allP[j].xR && allP[i].xR > allP[j].xL) Unite(i, j);
        var grups = new Dictionary<int, List<int>>();
        for (int i = 0; i < M; i++) { int g = Find(i); if (!grups.ContainsKey(g)) grups[g] = new List<int>(); grups[g].Add(i); }
        var panelY = new double[M];
        foreach (var kvp in grups)
        {
            var ids = kvp.Value.OrderBy(i => allP[i].xL).ThenBy(i => i).ToList();
            double yCur = Y_P00_top + 8;
            foreach (var i in ids) { panelY[i] = yCur; yCur += allP[i].h + GP; }
        }

        // ---- Dimensions SVG ----
        double xMaxP = allP.Select(p => (double)p.xR).Concat(new[] { (double)(XB(N - 1) + 30) }).Max();
        double SVG_W = xMaxP + 75, SVG_H = Y_C160 + 52;

        // ---- Generar HTML ----
        var sb = new StringBuilder();
        sb.Append(CapçaleraHtml(xarxa));
        sb.Append($"<div style=\"overflow-x:auto;padding:10px 16px;\">\n");
        sb.Append($"<svg viewBox=\"0 0 {SVG_W:0} {SVG_H:0}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"IBM Plex Mono,monospace\" style=\"display:block;background:#FAFAF8;\">\n");
        sb.Append("<defs><marker id=\"arr\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"5\" markerHeight=\"5\" orient=\"auto-start-reverse\"><path d=\"M2 1L8 5L2 9\" fill=\"none\" stroke=\"context-stroke\" stroke-width=\"1.5\"/></marker></defs>\n");
        sb.Append($"<rect width=\"{SVG_W:0}\" height=\"{SVG_H:0}\" fill=\"#FAFAF8\"/>\n");

        // Franges de planta
        for (int i = 0; i < ZONES.Length; i++)
        {
            var zo = ZONES[i];
            string fill = i == 1 ? "#FFF8E0" : i == 2 ? "#FFFEF5" : "none";
            if (fill != "none") sb.Append($"<rect x=\"{LM}\" y=\"{zo.yTop:0}\" width=\"{SVG_W - LM - 6:0}\" height=\"{zo.yBot - zo.yTop:0}\" fill=\"{fill}\" opacity=\"0.6\"/>\n");
            sb.Append(SL(LM, zo.yTop, SVG_W - 6, zo.yTop, "#DDDBD3", 0.5, "5 4"));
            sb.Append(ST(LM - 5, zo.yTop + 10, ZONA_NOMS[i], 9.5, ZONA_COLS[i], "end", 500));
        }
        sb.Append(SL(LM, ZONES[ZONES.Length - 1].yBot, SVG_W - 6, ZONES[ZONES.Length - 1].yBot, "#DDDBD3", 0.5, "5 4"));
        sb.Append(SL(LM - 2, 55, LM - 2, Y_C160 + 28, "#C8C6BE", 0.8));

        foreach (double sc in new[] { 2.5, 5.5, 7.5 })
            sb.Append(SL(LM + COL0 + sc * COL_W, 52, LM + COL0 + sc * COL_W, Y_C125 - 4, "#E8E6E0", 0.5, "3 4"));
        foreach (var (lab, col) in new[] { ("B1–B3·ø90", 1.0), ("B4–B6·ø125", 4.0), ("B7–B8·ø125", 6.5), ("B9–B10·ø90", 8.5) })
            sb.Append(ST(LM + COL0 + col * COL_W, 48, lab, 7.5, "#BBBBAA", "middle"));

        // Baixants
        for (int i = 0; i < N; i++)
        {
            var b = xarxa.Baixants[i];
            double x = XB(i);
            bool isL = b.ConnSideIdx == 0;
            string bColor = ColorBaixant(b.DnMm), cColor = ColorConnexio(b.DnMm), cLabel = LabelConnexio(b.DnMm);
            double bSW = SwBaixant(b.DnMm), cSW = SwConnexio(b.DnMm);

            var tramsOrd = b.Trams.OrderBy(t => t.ZLo).ToList();
            foreach (var t in tramsOrd)
            {
                double yT = Math.Min(ZToY(t.ZLo), ZToY(t.ZHi)), yB = Math.Max(ZToY(t.ZLo), ZToY(t.ZHi));
                if (t.Ud > 0) sb.Append(SL(x, yT, x, yB, bColor, bSW));
                else sb.Append($"<line x1=\"{x:0}\" y1=\"{yT:0}\" x2=\"{x:0}\" y2=\"{yB:0}\" stroke=\"{bColor}\" stroke-width=\"{bSW * 0.7:0.0}\" stroke-dasharray=\"5 4\" opacity=\"0.3\"/>\n");
            }

            double yBotActiu = tramsOrd.Where(t => t.Ud > 0).Select(t => Math.Max(ZToY(t.ZLo), ZToY(t.ZHi))).DefaultIfEmpty(ZToY(b.ZMinMm)).Max();
            sb.Append(SL(x, yBotActiu, x, Y_C125, bColor, bSW));
            sb.Append(SC(x, Y_C125, 4, "#FAFAF8", bColor, 1.5));

            foreach (double z in b.ConnZ)
            {
                double yc = ZToY(z), xBrac = isL ? x - ARM : x + ARM;
                sb.Append(SL(x, yc, xBrac, yc, cColor, cSW));
                sb.Append(SC(x, yc, 3, "#FAFAF8", bColor, 1.5));
                sb.Append(ST(isL ? xBrac - 3 : xBrac + 3, yc + 3, cLabel, 6.5, cColor, isL ? "end" : "start"));
            }

            double udX = isL ? x + 5 : x - 5;
            sb.Append(ST(udX, ZToY(43200), $"{b.UdMax:0}UD", 7.5, bColor, isL ? "start" : "end", 600));
            sb.Append(ST(x, 30, b.Nom, 9, bColor, "middle", 600));
            sb.Append(ST(x, 41, $"ø{b.DnMm}", 7, "#888", "middle"));
        }

        // Panell Aparells P00
        {
            double y = panelY[0]; int hh = PanelH(apItems_P00.Count); double xC = LM + 4 + PW_AP / 2.0 - 14;
            sb.Append(DrawPanel(LM + 4, (int)y, PW_AP, hh, "Aparells P00", "#B08010", apItems_P00, 41));
            sb.Append(SL(xC, y + hh, xC, Y_C125, "#B08010", 1.5, "3 2", 0.8)); sb.Append(SC(xC, Y_C125, 3, "#FAFAF8", "#B08010", 1.2));
        }

        // Panell Aparells P01
        {
            double y = panelY[1]; int hh = PanelH(apItems_P01.Count); double xC = LM + 4 + PW_AP / 2.0 + 14;
            sb.Append(DrawPanel(LM + 4, (int)y, PW_AP, hh, "Aparells P01", "#6666AA", apItems_P01, 22));
            sb.Append(SL(xC, y + hh, xC, Y_C125, "#6666AA", 1.5, "3 2", 0.8)); sb.Append(SC(xC, Y_C125, 3, "#FAFAF8", "#6666AA", 1.2));
        }

        // Panells col·lectors
        for (int pi = 0; pi < pcDefs.Count; pi++)
        {
            var pc = pcDefs[pi]; double y = panelY[2 + pi];
            double bx = XB(pc.bajIdx), yMid = y + pc.h / 2.0, xEdge = pc.side == "L" ? pc.xR : pc.xL;
            sb.Append(DrawPanel(pc.xL, (int)y, PW, pc.h, pc.title, pc.color, pc.items, pc.udTotal));
            sb.Append(SL(xEdge, yMid, bx, yMid, pc.color, 1.5, "3 2", 0.8));
            sb.Append(SC(bx, yMid, 3, "#FAFAF8", pc.color, 1.2));
        }

        // Col·lector ø125
        double xCL = LM + 20, xCR = XB(N - 1) + 24;
        sb.Append(SL(xCL, Y_C125, xCR, Y_C125, "#1A6FA8", 3));
        sb.Append(SL(xCL, Y_C125 - 7, xCL, Y_C125 + 7, "#1A6FA8", 2.5));
        sb.Append(SL(xCR, Y_C125 - 7, xCR, Y_C125 + 7, "#1A6FA8", 2.5));
        sb.Append(ST((xCL + xCR) / 2, Y_C125 + 13, $"ø125 · {xarxa.UdTotals:0} UD", 8, "#1A6FA8", "middle", 600));
        sb.Append(SL(xCL, Y_C125, xCL, Y_C160, "#B5271A", 4));

        // Col·lector ø160
        double xSFin = SVG_W - 38;
        sb.Append($"<line x1=\"{LM + 37:0}\" y1=\"{Y_C160:0}\" x2=\"{xSFin:0}\" y2=\"{Y_C160:0}\" stroke=\"#B5271A\" stroke-width=\"4\" marker-end=\"url(#arr)\"/>\n");
        sb.Append(SR(LM + 5, Y_C160 - 8, 32, 16, 2, "#B5271A", "none", 0));
        sb.Append(ST(LM + 21, Y_C160 + 4, "S01.A", 7.5, "white", "middle", 600));
        sb.Append(ST((xCL + XB(N - 1)) / 2, Y_C160 - 5, $"ø{xarxa.DnSortidaMm}", 9, "#B5271A", "middle", 600));
        sb.Append(ST((xCL + XB(N - 1)) / 2, Y_C160 + 16, $"{xarxa.UdTotals:0} UD · Q={xarxa.QTotalLs:0.00} l/s → Xarxa municipal", 8, "#8A1A10", "middle"));
        sb.Append(ST(SVG_W - 8, 58, "↑ aguas arriba", 7, "#CCCCAA", "end"));
        sb.Append(ST(SVG_W - 8, Y_C160 + 4, "↓ aguas abajo", 7, "#CCCCAA", "end"));

        sb.Append("</svg>\n</div>\n");
        sb.Append(PeuHtml());
        return sb.ToString();
    }

    // ---- Helpers SVG ----
    static string SL(double x1, double y1, double x2, double y2, string s, double sw, string dash = null, double? op = null)
    {
        var r = new StringBuilder($"<line x1=\"{x1:0}\" y1=\"{y1:0}\" x2=\"{x2:0}\" y2=\"{y2:0}\" stroke=\"{s}\" stroke-width=\"{sw:0.0}\"");
        if (dash != null) r.Append($" stroke-dasharray=\"{dash}\"");
        if (op.HasValue) r.Append($" opacity=\"{op.Value:0.0}\"");
        r.Append("/>\n"); return r.ToString();
    }
    static string SC(double cx, double cy, double r, string f, string s, double sw)
        => $"<circle cx=\"{cx:0}\" cy=\"{cy:0}\" r=\"{r:0.0}\" fill=\"{f}\" stroke=\"{s}\" stroke-width=\"{sw:0.0}\"/>\n";
    static string ST(double x, double y, string t, double sz, string f, string a = "middle", int w = 400)
        => $"<text x=\"{x:0}\" y=\"{y:0}\" font-size=\"{sz:0.0}\" fill=\"{f}\" text-anchor=\"{a}\" font-weight=\"{w}\" font-family=\"IBM Plex Mono,monospace\">{t}</text>\n";
    static string SR(double x, double y, double w, double h, double rx, string f, string s, double sw)
        => $"<rect x=\"{x:0}\" y=\"{y:0}\" width=\"{w:0}\" height=\"{h:0}\" rx=\"{rx:0}\" fill=\"{f}\" stroke=\"{s}\" stroke-width=\"{sw:0.0}\"/>\n";

    static string DrawPanel(int xL, int y, int w, int h, string title, string color, List<string> items, int udTotal)
    {
        var s = new StringBuilder();
        s.Append(SR(xL, y, w, h, 4, "#FAFAF8", color, 1.2));
        s.Append($"<rect x=\"{xL}\" y=\"{y}\" width=\"{w}\" height=\"{TH}\" rx=\"3\" fill=\"{color}\" opacity=\"0.15\"/>\n");
        s.Append(ST(xL + w / 2.0, y + TH - 4, title, 7.5, color, "middle", 600));
        s.Append(SL(xL + 3, y + TH, xL + w - 3, y + TH, color, 0.5, null, 0.4));
        for (int ii = 0; ii < items.Count; ii++) s.Append(ST(xL + 6, y + TH + PV + ii * IH + 8, items[ii], 7, "#444", "start"));
        double yF = y + TH + PV + items.Count * IH + PV;
        s.Append(SL(xL + 3, yF, xL + w - 3, yF, color, 0.5, null, 0.4));
        s.Append(ST(xL + 6, yF + FH - 2, "TOTAL:", 7.5, color, "start", 600));
        s.Append(ST(xL + w - 6, yF + FH - 2, $"{udTotal}UD", 7.5, color, "end", 600));
        return s.ToString();
    }

    static string HtmlBuit() => @"<!DOCTYPE html><html><head><meta charset='utf-8'/></head>
<body style='margin:0;display:flex;align-items:center;justify-content:center;
height:100vh;background:#FFFFFF;font-family:Segoe UI,sans-serif;color:#605E5C;'>
<div style='text-align:left;max-width:360px;'>
<div style='font-size:15px;font-weight:600;color:#201F1E;margin:0 0 10px;'>Esquema de principio</div>
<div style='font-size:12px;line-height:1.9;'>
<span style='display:inline-block;width:18px;height:18px;line-height:18px;text-align:center;border-radius:9px;background:#E6F1FB;color:#0F6CBD;font-weight:600;font-size:11px;margin-right:8px;'>1</span>Calcula el sistema (MagiCAD).<br/>
<span style='display:inline-block;width:18px;height:18px;line-height:18px;text-align:center;border-radius:9px;background:#E6F1FB;color:#0F6CBD;font-weight:600;font-size:11px;margin-right:8px;'>2</span>Selecciona todas las tuberías del sistema en Revit.<br/>
<span style='display:inline-block;width:18px;height:18px;line-height:18px;text-align:center;border-radius:9px;background:#E6F1FB;color:#0F6CBD;font-weight:600;font-size:11px;margin-right:8px;'>3</span>Pulsa <b>Generar esquema</b>.
</div></div></body></html>";

    static string CapçaleraHtml(XarxaInfo x) =>
$@"<!DOCTYPE html>
<html><head><meta http-equiv='X-UA-Compatible' content='IE=11'/><meta charset='utf-8'/>
<style>
*{{box-sizing:border-box;margin:0;padding:0;}}
body{{font-family:'IBM Plex Mono',monospace,sans-serif;background:#F4F2EE;}}
.hdr{{background:#1A1A18;padding:8px 16px;display:flex;align-items:center;justify-content:space-between;border-bottom:3px solid #4A7A96;}}
.logo{{color:#4A7A96;border:1.5px solid #4A7A96;padding:2px 7px;border-radius:3px;font-size:12px;font-weight:600;}}
.htit{{font-size:9px;color:#AAA;margin-left:10px;}}
.badge{{background:#2A2A28;border:1px solid #444;border-radius:3px;padding:2px 5px;font-size:8px;color:#AAA;}}
.leg{{background:#FAFAF8;border-top:1px solid #D8D6CE;padding:7px 16px;display:flex;flex-wrap:wrap;gap:4px 14px;align-items:center;font-size:8.5px;color:#6B6B62;}}
.li{{display:flex;align-items:center;gap:4px;}}
.foot{{padding:5px 16px;font-size:7.5px;color:#AAA;background:#FAFAF8;display:flex;justify-content:space-between;border-top:1px solid #D8D6CE;}}
</style></head><body>
<div class='hdr'>
<div style='display:flex;align-items:center;'>
<span class='logo'>E3G</span>
<span class='htit'>BIM Tools · Esquema de Principi · {x.Sistema} · EN 12056 System I</span>
</div>
<span class='badge'>{x.UdTotals:0} UD · ø{x.DnSortidaMm} · {x.QTotalLs:0.00} l/s</span>
</div>";

    static string PeuHtml() =>
@"<div class='leg'>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#7B6BAE' stroke-width='1.5'/></svg>ø40/50 Aparells</div>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#1E8C4E' stroke-width='2.5'/></svg>ø90 B1-B3, B9-B10</div>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#C96420' stroke-width='2'/></svg>ø110 Connexió vivenda</div>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#1A6FA8' stroke-width='3'/></svg>ø125 B4-B8 / col·lectors</div>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#B5271A' stroke-width='3.5'/></svg>ø160 Sortida</div>
<div class='li'><svg width='7' height='7'><circle cx='3.5' cy='3.5' r='3' fill='white' stroke='#1A6FA8' stroke-width='1.5'/></svg>Connexió al mig del gap</div>
<div class='li'><svg width='18' height='5'><line x1='0' y1='2.5' x2='18' y2='2.5' stroke='#888' stroke-width='1.5' stroke-dasharray='4 3' opacity='0.4'/></svg>Tap UD=0</div>
<div class='li' style='margin-left:auto;color:#AAA;font-size:7.5px;'>Panells al baixant d'origen · Zero solapaments garantits</div>
</div>
<div class='foot'>
<span>E3G — Enginyeria i Energia · BIM Tools v2.0.0</span>
<span>EN 12056 System I · MC Discharge Unit Sum</span>
<span>" + DateTime.Now.ToString("dd/MM/yyyy") + @"</span>
</div></body></html>";
}

// ============================================================
// EXPORTADOR A DRAFTING VIEW DE REVIT
//
// [NOU v2.0.0 — FIX 1] NOM ÚNIC
//   Cada crida genera un nom diferent:
//   "E3G — Esquema Principi — 10_10_Fecals — 20260415_1423_01"
//   Format: Sistema + data + hora + counter estàtic
//   El counter (_counter) mai es reinicia dins de la mateixa
//   sessió de Revit → garanteix unicitat absoluta.
//
// [NOU v2.0.0 — FIX 2] COLORS
//   Revit pinta les DetailLines en negre per defecte perquè
//   usa el color del GraphicsStyle del lineStyle assignat.
//   Solució: après de crear cada DetailLine, cridar
//   vista.SetElementOverrides(dl.Id, ogs) amb el color
//   del DN corresponent via OverrideGraphicSettings.
//   Ha d'estar dins de la mateixa transacció.
// ============================================================

public static class ExportadorDraftingView
{
    // [FIX 1] Counter estàtic: s'incrementa cada crida, mai es reinicia
    private static int _counter = 0;

    // Escala: píxels SVG → peus Revit
    private const double MM_PER_PX = 0.5;
    private const double FT_PER_MM = 1.0 / 304.8;
    private const double F = MM_PER_PX * FT_PER_MM;

    // [FIX 2] Colors Revit per DN (idèntics als del HTML)
    static Autodesk.Revit.DB.Color ColorRevit(int dn)
    {
        if (dn >= 160) return new Autodesk.Revit.DB.Color(181, 39, 26); // #B5271A vermell
        if (dn >= 125) return new Autodesk.Revit.DB.Color(26, 111, 168); // #1A6FA8 blau
        if (dn >= 110) return new Autodesk.Revit.DB.Color(201, 100, 32); // #C96420 taronja
        if (dn >= 90) return new Autodesk.Revit.DB.Color(30, 140, 78); // #1E8C4E verd
        return new Autodesk.Revit.DB.Color(123, 107, 174);                // #7B6BAE violeta
    }

    static int GruixRevit(int dn)
    {
        if (dn >= 160) return 7;
        if (dn >= 125) return 5;
        if (dn >= 110) return 4;
        if (dn >= 90) return 4;
        return 2;
    }

        // [FIX 2] Aplicar color i gruix a un DetailCurve
        // Ha d'executar-se DINS de la mateixa transacció que crea l'element
        // Cache de LineStyles creats durant la transacció
        static Dictionary<int, ElementId> _lineStyleCache = new Dictionary<int, ElementId>();

        static ElementId GetOrCreateLineStyle(Document doc, int dn)
        {
            if (_lineStyleCache.TryGetValue(dn, out var cached)) return cached;

            var color = ColorRevit(dn);
            string nomEstil = $"E3G_DN{dn}";

            // Buscar si ja existeix
            var catLines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category existent = null;
            foreach (Category c in catLines.SubCategories)
                if (c.Name == nomEstil) { existent = c; break; }

            if (existent == null)
            {
                existent = doc.Settings.Categories.NewSubcategory(catLines, nomEstil);
                existent.LineColor = color;
                existent.SetLineWeight(GruixRevit(dn), GraphicsStyleType.Projection);
            }

            var styleId = existent.GetGraphicsStyle(GraphicsStyleType.Projection).Id;
            _lineStyleCache[dn] = styleId;
            return styleId;
        }

        static ElementId CrearLinha(Document doc, View vista,
            double x1, double y1, double x2, double y2, int dn)
        {
            var p0 = new XYZ(x1 * F, -y1 * F, 0);
            var p1 = new XYZ(x2 * F, -y2 * F, 0);
            if (p0.DistanceTo(p1) < 1e-6) return null;
            var dl = doc.Create.NewDetailCurve(vista,
                Autodesk.Revit.DB.Line.CreateBound(p0, p1));
            try
            {
                var styleId = GetOrCreateLineStyle(doc, dn);
                dl.LineStyle = doc.GetElement(styleId) as GraphicsStyle;
            }
            catch { }
            return dl.Id;
        }

        // Helper: crear TextNote
        static void CrearText(Document doc, View vista, TextNoteType tnt,
        double x, double y, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        TextNote.Create(doc, vista.Id, new XYZ(x * F, -y * F, 0), text, tnt.Id);
    }

    // Constants de layout (idèntiques al HTML)
    const int LM_DV = 50, COL0_DV = 250, COL_W_DV = 130, ARM_DV = 20;
    static int XB_DV(int i) => LM_DV + COL0_DV + i * COL_W_DV;

    // Escala Z→Y px (idèntica al HTML)
    static readonly (double zBot, double zTop, double yBot, double yTop)[] ZN = {
            (38500,43000,1040,1000),(43000,43860,1000,650),(43860,46370,650,555),
            (46370,49520,555,460),(49520,52670,460,365),(52670,55820,365,270),
            (55820,58970,270,175),(58970,64000,175,60),
        };
    static double ZToY_DV(double z)
    {
        foreach (var zo in ZN) if (z >= zo.zBot && z <= zo.zTop) return zo.yBot + (z - zo.zBot) / (zo.zTop - zo.zBot) * (zo.yTop - zo.yBot);
        return z < 38500 ? 1060 : 60;
    }
    const double Y_C125_DV = 1018, Y_C160_DV = 1052;

    public static int CrearDraftingView(Document doc, XarxaInfo xarxa)
    {
        if (xarxa == null) return -1;

        // [FIX 1] NOM ÚNIC: incrementar counter i afegir timestamp
        _counter++;
        string nomVista = $"E3G — Esquema Principi — {xarxa.Sistema}" +
                          $" — {DateTime.Now:yyyyMMdd_HHmm}_{_counter:D2}";

        int N = xarxa.Baixants.Count;

        using (var tx = new Transaction(doc, "E3G — Crear Esquema de Principi"))
        {
            _lineStyleCache.Clear();
            tx.Start();
            try
            {
                // Crear la Drafting View
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                if (vft == null) { tx.RollBack(); return -1; }

                var vista = ViewDrafting.Create(doc, vft.Id);
                vista.Name = nomVista;  // [FIX 1] nom únic garantit
                vista.Scale = 50;

                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (tnt == null) { tx.RollBack(); return -1; }

                double svgW = LM_DV + COL0_DV + N * COL_W_DV + 200;

                // Línies de referència de planta (color ø50 = violeta neutre)
                string[] zonaLabels = { "S01", "P00", "P01", "P02", "P03", "P04", "P05", "P06" };
                double[] zonaY = { 1000, 950, 650, 555, 460, 365, 270, 175 };
                for (int i = 0; i < zonaLabels.Length; i++)
                {
                    CrearLinha(doc, vista, LM_DV, zonaY[i], svgW, zonaY[i], 50); // gris via violeta
                    CrearText(doc, vista, tnt, LM_DV - 30, zonaY[i] + 4, zonaLabels[i]);
                }

                // Baixants
                for (int i = 0; i < N; i++)
                {
                    var b = xarxa.Baixants[i];
                    double x = XB_DV(i);
                    bool isL = b.ConnSideIdx == 0;
                    int dn = b.DnMm;

                    // Trams verticals [FIX 2: color per DN]
                    foreach (var t in b.Trams.OrderBy(t => t.ZLo))
                    {
                        double yT = Math.Min(ZToY_DV(t.ZLo), ZToY_DV(t.ZHi));
                        double yB = Math.Max(ZToY_DV(t.ZLo), ZToY_DV(t.ZHi));
                        CrearLinha(doc, vista, x, yT, x, yB, dn);
                    }

                    // Baixada al col·lector [FIX 2]
                    double yBotActiu = b.Trams.Where(t => t.Ud > 0)
                        .Select(t => Math.Max(ZToY_DV(t.ZLo), ZToY_DV(t.ZHi)))
                        .DefaultIfEmpty(ZToY_DV(b.ZMinMm)).Max();
                    CrearLinha(doc, vista, x, yBotActiu, x, Y_C125_DV, dn);

                    // Connexions al mig del gap [FIX 2: color connexió]
                    int dnConn = dn >= 110 ? 110 : 50;
                    foreach (double z in b.ConnZ)
                    {
                        double yc = ZToY_DV(z), xBrac = isL ? x - ARM_DV : x + ARM_DV;
                        CrearLinha(doc, vista, x, yc, xBrac, yc, dnConn);
                    }

                    // Etiquetes text
                    CrearText(doc, vista, tnt, x, 22, b.Nom);
                    CrearText(doc, vista, tnt, x, 34, $"ø{b.DnMm}");
                    double udX = isL ? x + 14 : x - 14;
                    CrearText(doc, vista, tnt, udX, ZToY_DV(43200) - 4, $"{b.UdMax:0}UD");
                }

                // Col·lectors horitzontals
                foreach (var ht in xarxa.ColectorsHoritzontals)
                {
                    int bIdx = -1; double dMin = double.MaxValue;
                    for (int i = 0; i < N; i++)
                    {
                        var b = xarxa.Baixants[i];
                        double d = Math.Min(Math.Sqrt(Math.Pow(ht.X0 - b.XMm, 2) + Math.Pow(ht.Y0 - b.YMm, 2)),
                                            Math.Sqrt(Math.Pow(ht.X1 - b.XMm, 2) + Math.Pow(ht.Y1 - b.YMm, 2)));
                        if (d < dMin) { dMin = d; bIdx = i; }
                    }
                    if (bIdx < 0 || N == 0) continue;
                    double bx = XB_DV(bIdx), yH = ZToY_DV(43200) + 35;
                    CrearLinha(doc, vista, bx - 12, yH, bx + 12, yH, ht.DnMm); // [FIX 2]
                    CrearText(doc, vista, tnt, bx, yH + 10, $"ø{ht.DnMm}·{ht.Ud:0}UD");
                }

                // Col·lector ø125 [FIX 2: blau]
                double xCL = LM_DV + 20, xCR = XB_DV(N - 1) + 24;
                CrearLinha(doc, vista, xCL, Y_C125_DV, xCR, Y_C125_DV, 125);
                CrearLinha(doc, vista, xCL, Y_C125_DV - 7, xCL, Y_C125_DV + 7, 125);
                CrearLinha(doc, vista, xCR, Y_C125_DV - 7, xCR, Y_C125_DV + 7, 125);
                CrearText(doc, vista, tnt, (xCL + xCR) / 2, Y_C125_DV + 16, $"ø125 · {xarxa.UdTotals:0} UD");

                // Baixada i col·lector ø160 [FIX 2: vermell]
                CrearLinha(doc, vista, xCL, Y_C125_DV, xCL, Y_C160_DV, 160);
                CrearLinha(doc, vista, xCL, Y_C160_DV, xCR + 40, Y_C160_DV, 160);
                CrearText(doc, vista, tnt, LM_DV + 10, Y_C160_DV + 5, "S01.A");
                CrearText(doc, vista, tnt, (xCL + XB_DV(N - 1)) / 2, Y_C160_DV - 8, $"ø{xarxa.DnSortidaMm}");
                CrearText(doc, vista, tnt, (xCL + XB_DV(N - 1)) / 2, Y_C160_DV + 16,
                    $"{xarxa.UdTotals:0}UD · Q={xarxa.QTotalLs:0.00} l/s → Xarxa municipal");

                tx.Commit();

                // Obrir la vista
                new UIDocument(doc).ActiveView = vista;
                return (int)vista.Id.Value;
            }
            catch (Exception ex)
            {
                tx.RollBack();
                TaskDialog.Show("E3G — Error", $"Error creant la Drafting View:\n{ex.Message}");
                return -1;
            }
        }
    }
}

// ============================================================
// CONTROL WPF — PESTAÑA "ESQUEMA DE PRINCIPI"
// Idèntic a v1.0.0 excepte: GeneradorSvg → GeneradorHtml
//                           MostrarSvg   → MostrarHtml
// ============================================================

public class EsquemaPrincipiControl : UserControl
{
    private Document _doc;
    private List<int> _pipeIds;
    private XarxaInfo _xarxa;
    private WebBrowser _browser;
    private TextBlock _labelEstat;
    private Button _btnGenerar;
    private Button _btnInserir;
    private Button _btnAmpliar;
    private string _htmlActual;

    public ExternalEvent EventoInserir { get; set; }
    public XarxaInfo XarxaActual => _xarxa;

    public EsquemaPrincipiControl()
    {
        InicialitzarUI();
    }

    // Estilo E3G (como el resto del panel): tarjeta plana con botones de barra de
    // herramientas del tema (Toolbar.Primary/Tint/Ghost) con glifos Lucide, texto
    // de estado debajo y el visor del esquema en un marco con borde suave.
    // Los estilos "Card", "Glyph", "BtnText" y "Hint" vienen de los recursos de
    // SaneamientoWindow (DynamicResource: se resuelven al entrar en el arbol).
    private const string GlyphNetwork = "M17 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M3 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M10 2h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3 M12 12V8";
    private const string GlyphLogIn = "M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4 M10 17l5-5-5-5 M15 12H3";
    private const string GlyphMaximize = "M15 3h6v6 M9 21H3v-6 M21 3l-7 7 M3 21l7-7";

    private static Button CrearBoton(string estilo, string glifo, string texto, string tooltip)
    {
        var btn = new Button { Margin = new Thickness(0, 0, 6, 6), ToolTip = tooltip };
        btn.SetResourceReference(FrameworkElement.StyleProperty, estilo);

        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(glifo) };
        path.SetResourceReference(FrameworkElement.StyleProperty, "Glyph");
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        var icono = new Viewbox { Width = 14, Height = 14, Margin = new Thickness(0, 0, 7, 0), Child = canvas };

        var lbl = new TextBlock { Text = texto };
        lbl.SetResourceReference(FrameworkElement.StyleProperty, "BtnText");

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(icono);
        sp.Children.Add(lbl);
        btn.Content = sp;
        return btn;
    }

    private void InicialitzarUI()
    {
        var root = new StackPanel();

        // --- Tarjeta de acciones ---------------------------------------------
        var card = new Border();
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        var cardBody = new StackPanel();

        var titulo = new TextBlock { Text = "Esquema de principio" };
        titulo.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitle");
        cardBody.Children.Add(titulo);

        var ayuda = new TextBlock
        {
            Text = "Genera el esquema vertical de la red (bajantes, colectores y salida) a partir de las tuberías seleccionadas, y lo inserta en Revit como vista de diseño.",
            Margin = new Thickness(0, 2, 0, 10),
        };
        ayuda.SetResourceReference(FrameworkElement.StyleProperty, "Hint");
        cardBody.Children.Add(ayuda);

        _btnGenerar = CrearBoton("E3g.Style.Toolbar.Primary", GlyphNetwork, "Generar esquema",
            "Analiza las tuberías seleccionadas en Revit y dibuja el esquema");
        _btnGenerar.Click += OnGenerarClick;

        _btnInserir = CrearBoton("E3g.Style.Toolbar.Tint", GlyphLogIn, "Insertar en Revit",
            "Crea una vista de diseño con el esquema y la abre");
        _btnInserir.IsEnabled = false;
        _btnInserir.Click += OnInserirClick;

        _btnAmpliar = CrearBoton("E3g.Style.Toolbar.Ghost", GlyphMaximize, "Ampliar",
            "Abre el esquema en una ventana grande");
        _btnAmpliar.IsEnabled = false;
        _btnAmpliar.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_htmlActual))
                new VisorEsquema(_htmlActual).Show();
        };

        var acciones = new WrapPanel();
        acciones.Children.Add(_btnGenerar);
        acciones.Children.Add(_btnInserir);
        acciones.Children.Add(_btnAmpliar);
        cardBody.Children.Add(acciones);

        _labelEstat = new TextBlock
        {
            Text = "Selecciona las tuberías del sistema en Revit y pulsa «Generar esquema».",
            Margin = new Thickness(0, 4, 0, 0),
        };
        _labelEstat.SetResourceReference(FrameworkElement.StyleProperty, "Hint");
        cardBody.Children.Add(_labelEstat);

        card.Child = cardBody;
        root.Children.Add(card);

        // --- Visor del esquema -------------------------------------------------
        var marco = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Height = 560,
        };
        marco.SetResourceReference(Border.BorderBrushProperty, "E3g.Brush.BorderSubtle");
        marco.SetResourceReference(Border.BackgroundProperty, "E3g.Brush.Card");
        _browser = new WebBrowser { Margin = new Thickness(0) };
        marco.Child = _browser;
        root.Children.Add(marco);
        MostrarPantallaInicial();

        this.Content = root;
    }

    // Idèntic a v1.0.0
    public void IniciarAmbDoc(Document doc, List<int> pipeIds)
    {
        _doc = doc;
        _pipeIds = pipeIds ?? new List<int>();
        Dispatcher.BeginInvoke(new Action(() => {
            int n = _pipeIds.Count;
            _labelEstat.Text = n > 0
                ? $"{n} tuberías seleccionadas. Pulsa «Generar esquema» para analizarlas."
                : "Ninguna tubería seleccionada. Selecciona las tuberías en Revit.";
            _btnGenerar.IsEnabled = n > 0;
            _btnInserir.IsEnabled = false;
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // [CANVI v2] GeneradorSvg.Generar → GeneradorHtml.Generar
    private void OnGenerarClick(object sender, RoutedEventArgs e)
    {
        if (_doc == null || _pipeIds == null || _pipeIds.Count == 0)
        {
            _labelEstat.Text = "No hay tuberías seleccionadas en Revit."; return;
        }
        try
        {
            _labelEstat.Text = "Analizando la red…";
            _xarxa = AnalisadorXarxa.Analitzar(_doc, _pipeIds);
            if (_xarxa == null) { _labelEstat.Text = "No se han encontrado tuberías válidas (ø ≥ 90)."; return; }
            string html = GeneradorHtml.Generar(_xarxa); // [CANVI v2]
            _htmlActual = html;
            MostrarHtml(html);                          // [CANVI v2]
            _btnAmpliar.IsEnabled = true;
            _labelEstat.Text = $"Esquema generado · {_xarxa.Sistema} · " +
                              $"{_xarxa.Baixants.Count} bajantes · " +
                              $"{_xarxa.UdTotals:0} UD · salida ø{_xarxa.DnSortidaMm}";
            _btnInserir.IsEnabled = true;
        }
        catch (Exception ex) { _labelEstat.Text = $"Error: {ex.Message}"; }
    }

    // Idèntic a v1.0.0
    private void OnInserirClick(object sender, RoutedEventArgs e)
    {
        if (_xarxa == null || _doc == null) return;
        if (EventoInserir == null) { _labelEstat.Text = "Error: el evento de inserción no está configurado."; return; }
        _labelEstat.Text = "Insertando en Revit…";
        _btnInserir.IsEnabled = false;
        EventoInserir.Raise();
    }

    // Idèntic a v1.0.0
    public void NotificarInsercioCompletada(int viewId)
    {
        if (viewId > 0) _labelEstat.Text = $"Vista de diseño creada (ID {viewId}) y abierta en Revit.";
        else _labelEstat.Text = "Error al crear la vista de diseño.";
        _btnInserir.IsEnabled = _xarxa != null;
    }

    // [CANVI v2] Era MostrarSvg → ara MostrarHtml
    // Desa l'HTML complet a un fitxer temporal i navega (WebBrowser WPF)
    private void MostrarHtml(string htmlContent)
    {
        string tmpPath = Path.Combine(Path.GetTempPath(), "E3G_EsquemaPrincipi.html");
        File.WriteAllText(tmpPath, htmlContent, Encoding.UTF8);
        _browser.Navigate(new Uri(tmpPath));
    }

    // Idèntic a v1.0.0
    private void MostrarPantallaInicial()
    {
        _browser.NavigateToString(GeneradorHtml.Generar(null));
    }

    // Idèntic a v1.0.0
    public void BuidarBrowser()
    {
        _browser?.Navigate("about:blank");
    }
}
    public class VisorEsquema : Window
    {
        public VisorEsquema(string htmlContent)
        {
            Title = "Esquema de principio";
            Width = 1200; Height = 750;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;

            var browser = new WebBrowser { Margin = new Thickness(0) };
            Content = browser;

            string tmpPath = Path.Combine(Path.GetTempPath(), "E3G_EsquemaVisor.html");
            File.WriteAllText(tmpPath, htmlContent, System.Text.Encoding.UTF8);
            browser.Navigate(new Uri(tmpPath));
        }
    }
}  // fi namespace

