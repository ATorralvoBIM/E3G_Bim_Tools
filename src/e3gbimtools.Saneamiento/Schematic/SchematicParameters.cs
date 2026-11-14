// ================================================================
// SchematicParameters.cs  —  E3G BIM Tools  FASE 3
// Plantilla de paràmetres configurables per al generador
// d'esquema de principi en Revit (format CTE/RITE)
//
// Conté:
//   - SchematicParameters     : configuració global
//   - FloorZoneConfig          : definició de franges de planta
//   - LineStyleConfig          : color/gruix per DN o tipus
//   - TextStyleConfig          : fonts i mides de text
//   - LegendConfig             : configuració de la llegenda
//   - ExportConfig             : opcions d'exportació DWG/PDF
//
// Ús:
//   var p = SchematicParameters.Default();
//   p.ViewScale = 50;
//   p.TitleBlock = "E3G_A3_CTE";
//   SchematicGeneratorService.Generate(doc, xarxa, p);
//
// Autors: E3G — Enginyeria i Energia
// Data:   Maig 2026
// ================================================================

using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace E3G.Saneamiento.Schematic
{
    // ============================================================
    // CONFIGURACIÓ GLOBAL DEL GENERADOR
    // ============================================================

    public class SchematicParameters
    {
        // ---- Identificació de la vista ----
        /// <summary>Prefix del nom de la Drafting View generada.</summary>
        public string ViewNamePrefix { get; set; } = "E3G — Esquema Principi";

        /// <summary>Escala de la vista (1:N). Default: 50 → 1:50.</summary>
        public int ViewScale { get; set; } = 50;

        /// <summary>Nom del ViewFamilyType de Drafting View a usar.
        /// Si és null, usa el primer disponible.</summary>
        public string DraftingViewTypeName { get; set; } = null;

        // ---- Normativa ----
        /// <summary>Format de les etiquetes de cota: CTE o RITE.</summary>
        public NormativaFormat Normativa { get; set; } = NormativaFormat.CTE_HS5;

        /// <summary>Sistema de numeració de baixants (B1, B2... o S1, S2...).</summary>
        public string PrefixBaixant { get; set; } = "B";

        /// <summary>Sistema de numeració de col·lectors.</summary>
        public string PrefixColector { get; set; } = "C";

        // ---- Layout ----
        public LayoutConfig Layout { get; set; } = new LayoutConfig();

        // ---- Estils de línia ----
        public Dictionary<string, LineStyleConfig> LineStyles { get; set; }
            = LineStyleConfig.DefaultsByDn();

        // ---- Text ----
        public TextStyleConfig TextStyle { get; set; } = new TextStyleConfig();

        // ---- Franges de planta ----
        public List<FloorZoneConfig> FloorZones { get; set; }
            = FloorZoneConfig.DefaultZones();

        // ---- Llegenda ----
        public LegendConfig Legend { get; set; } = new LegendConfig();

        // ---- Exportació DWG ----
        public ExportConfig Export { get; set; } = new ExportConfig();

        // ---- Símbols ----
        public SymbolConfig Symbols { get; set; } = new SymbolConfig();

        // --------------------------------------------------------
        // Factory: configuració per defecte llesta per usar
        // --------------------------------------------------------
        public static SchematicParameters Default() => new SchematicParameters();

        /// <summary>Configuració per a projectes de fontaneria (RITE/CTE HS4).</summary>
        public static SchematicParameters ForFontaneria()
        {
            var p = new SchematicParameters();
            p.Normativa = NormativaFormat.CTE_HS4;
            p.ViewNamePrefix = "E3G — Esquema Fontaneria";
            p.LineStyles["AF"]  = new LineStyleConfig { ColorHex = "#1A6FA8", LineWeight = 5 };
            p.LineStyles["ACS"] = new LineStyleConfig { ColorHex = "#B5271A", LineWeight = 5 };
            p.LineStyles["RCS"] = new LineStyleConfig { ColorHex = "#C96420", LineWeight = 3 };
            return p;
        }

        /// <summary>Configuració per a projectes de sanejament (CTE HS5).</summary>
        public static SchematicParameters ForSanejament() => Default();
    }

    // ============================================================
    // NORMATIVA
    // ============================================================

    public enum NormativaFormat
    {
        CTE_HS5,   // Sanejament (UD, caudals, bajants)
        CTE_HS4,   // Fontaneria (caudals punta, simultaneïtat)
        RITE,      // Instal·lació tèrmica
        Custom
    }

    // ============================================================
    // LAYOUT
    // ============================================================

    public class LayoutConfig
    {
        /// <summary>Marge esquerre del canvas (px/mm virtuals).</summary>
        public double MarginLeft { get; set; } = 50;

        /// <summary>Columna 0: offset des del marge fins al primer baixant.</summary>
        public double Col0Offset { get; set; } = 250;

        /// <summary>Pas horitzontal entre baixants (px). Mínim recomanat: 120.</summary>
        public double ColWidth { get; set; } = 130;

        /// <summary>Longitud del braç de connexió lateral (px).</summary>
        public double ConnectionArmLength { get; set; } = 20;

        /// <summary>Amplada dels panells de col·lectors (px).</summary>
        public double PanelWidth { get; set; } = 150;

        /// <summary>Amplada dels panells d'aparells (px).</summary>
        public double PanelWidthApparatus { get; set; } = 205;

        /// <summary>Gap entre panells apilats (px).</summary>
        public double PanelGap { get; set; } = 8;

        /// <summary>Factor d'escala px → peus Revit (0.5 mm/px × 1/304.8 ft/mm).</summary>
        public double PixelToFeet { get; set; } = 0.5 / 304.8;

        // Cotes Y de les línies fixes de col·lectors
        public double Y_Collector125 { get; set; } = 1018;
        public double Y_Collector160 { get; set; } = 1052;
        public double Y_P00_Top     { get; set; } = 650;
    }

    // ============================================================
    // FRANGES DE PLANTA (zona Z)
    // ============================================================

    public class FloorZoneConfig
    {
        public string Name    { get; set; }
        public double ZBot_mm { get; set; }
        public double ZTop_mm { get; set; }
        public double YBot_px { get; set; }
        public double YTop_px { get; set; }
        public string ColorHex { get; set; } = "#5A5A52";
        public string FillHex  { get; set; } = "none";

        /// <summary>Converteix una cota Z (mm) a Y (px) dins d'aquesta zona.</summary>
        public double ZToY(double z) =>
            YBot_px + (z - ZBot_mm) / (ZTop_mm - ZBot_mm) * (YTop_px - YBot_px);

        public static List<FloorZoneConfig> DefaultZones() => new List<FloorZoneConfig>
        {
            new FloorZoneConfig { Name="S01", ZBot_mm=38500, ZTop_mm=43000, YBot_px=1040, YTop_px=1000, ColorHex="#9A2218" },
            new FloorZoneConfig { Name="P00", ZBot_mm=43000, ZTop_mm=43860, YBot_px=1000, YTop_px= 650, ColorHex="#8C7000", FillHex="#FFF8E0" },
            new FloorZoneConfig { Name="P01", ZBot_mm=43860, ZTop_mm=46370, YBot_px= 650, YTop_px= 555, ColorHex="#5A5A52", FillHex="#FFFEF5" },
            new FloorZoneConfig { Name="P02", ZBot_mm=46370, ZTop_mm=49520, YBot_px= 555, YTop_px= 460, ColorHex="#5A5A52" },
            new FloorZoneConfig { Name="P03", ZBot_mm=49520, ZTop_mm=52670, YBot_px= 460, YTop_px= 365, ColorHex="#5A5A52" },
            new FloorZoneConfig { Name="P04", ZBot_mm=52670, ZTop_mm=55820, YBot_px= 365, YTop_px= 270, ColorHex="#5A5A52" },
            new FloorZoneConfig { Name="P05", ZBot_mm=55820, ZTop_mm=58970, YBot_px= 270, YTop_px= 175, ColorHex="#5A5A52" },
            new FloorZoneConfig { Name="P06", ZBot_mm=58970, ZTop_mm=64000, YBot_px= 175, YTop_px=  60, ColorHex="#5A5A52" },
        };

        /// <summary>Interpolació global (totes les zones).</summary>
        public static double ZToYGlobal(double z, List<FloorZoneConfig> zones)
        {
            foreach (var zo in zones)
                if (z >= zo.ZBot_mm && z <= zo.ZTop_mm)
                    return zo.ZToY(z);
            return z < zones[0].ZBot_mm ? zones[0].YBot_px : zones[zones.Count - 1].YTop_px;
        }
    }

    // ============================================================
    // ESTIL DE LÍNIA
    // ============================================================

    public class LineStyleConfig
    {
        public string ColorHex  { get; set; } = "#000000";
        public int    LineWeight { get; set; } = 2;        // Revit pen weight (1–16)
        public bool   Dashed    { get; set; } = false;

        /// <summary>Estils per DN de canonada (sanejament).</summary>
        public static Dictionary<string, LineStyleConfig> DefaultsByDn()
            => new Dictionary<string, LineStyleConfig>
            {
                ["DN160"] = new LineStyleConfig { ColorHex = "#B5271A", LineWeight = 7 },
                ["DN125"] = new LineStyleConfig { ColorHex = "#1A6FA8", LineWeight = 5 },
                ["DN110"] = new LineStyleConfig { ColorHex = "#C96420", LineWeight = 4 },
                ["DN90"]  = new LineStyleConfig { ColorHex = "#1E8C4E", LineWeight = 4 },
                ["DN50"]  = new LineStyleConfig { ColorHex = "#7B6BAE", LineWeight = 2 },
                ["DN40"]  = new LineStyleConfig { ColorHex = "#7B6BAE", LineWeight = 2 },
                ["ZONE"]  = new LineStyleConfig { ColorHex = "#DDDBD3", LineWeight = 1, Dashed = true },
                ["AXIS"]  = new LineStyleConfig { ColorHex = "#C8C6BE", LineWeight = 1, Dashed = true },
            };

        /// <summary>Retorna la clau d'estil per a un diàmetre nominal (mm).</summary>
        public static string KeyForDn(int dn)
        {
            if (dn >= 160) return "DN160";
            if (dn >= 125) return "DN125";
            if (dn >= 110) return "DN110";
            if (dn >= 90)  return "DN90";
            if (dn >= 50)  return "DN50";
            return "DN40";
        }

        /// <summary>Color Revit (RGB) a partir d'un hex (#RRGGBB).</summary>
        public Color ToRevitColor()
        {
            var h = ColorHex.TrimStart('#');
            byte r = System.Convert.ToByte(h.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(h.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(h.Substring(4, 2), 16);
            return new Color(r, g, b);
        }
    }

    // ============================================================
    // ESTIL DE TEXT
    // ============================================================

    public class TextStyleConfig
    {
        /// <summary>Nom del TextNoteType de Revit a usar (null = primer disponible).</summary>
        public string TextNoteTypeName { get; set; } = null;

        /// <summary>Mida del text de títol de baixant (mm en paper).</summary>
        public double TitleSizeMm { get; set; } = 2.5;

        /// <summary>Mida del text estàndard d'etiquetes (mm en paper).</summary>
        public double LabelSizeMm { get; set; } = 1.8;

        /// <summary>Mida del text de peu/llegenda (mm en paper).</summary>
        public double FooterSizeMm { get; set; } = 1.5;

        /// <summary>Mida del text de cotes (mm en paper).</summary>
        public double DimSizeMm { get; set; } = 2.0;
    }

    // ============================================================
    // CONFIGURACIÓ DE SÍMBOLS
    // ============================================================

    public class SymbolConfig
    {
        /// <summary>Nom de la família Revit per a vàlvules de bola.
        /// Si és null o no es troba, usa geometria DetailCurve.</summary>
        public string ValveFamilyName  { get; set; } = null;

        /// <summary>Nom de la família Revit per a bombes.</summary>
        public string PumpFamilyName   { get; set; } = null;

        /// <summary>Nom de la família Revit per a calderes/escalfadors.</summary>
        public string BoilerFamilyName { get; set; } = null;

        /// <summary>Mida dels símbols desenhats com a DetailCurve (mm virtuals).</summary>
        public double SymbolSize { get; set; } = 15;

        /// <summary>Si true, dibuixa símbols geomètrics quan la família no existeix.</summary>
        public bool UseFallbackGeometry { get; set; } = true;
    }

    // ============================================================
    // CONFIGURACIÓ DE LA LLEGENDA
    // ============================================================

    public class LegendConfig
    {
        public bool   ShowLegend  { get; set; } = true;
        public double XPosition   { get; set; } = 20;
        public double YPosition   { get; set; } = 20;
        public double LineLength  { get; set; } = 40;   // longitud mostra (px)
        public double RowHeight   { get; set; } = 18;   // separació entre files (px)
        public string TitleText   { get; set; } = "LLEGENDA / LEYENDA";

        public bool ShowNormativa { get; set; } = true;
        public bool ShowDate      { get; set; } = true;
        public bool ShowScale     { get; set; } = true;

        // Entrades personalitzades (es generen automàticament si buides)
        public List<LegendEntry> CustomEntries { get; set; } = new List<LegendEntry>();
    }

    public class LegendEntry
    {
        public string StyleKey   { get; set; }  // clau de LineStyles
        public string LabelText  { get; set; }  // text descriptiu
    }

    // ============================================================
    // CONFIGURACIÓ D'EXPORTACIÓ
    // ============================================================

    public class ExportConfig
    {
        /// <summary>Carpeta de destí per a l'exportació DWG/PDF.</summary>
        public string OutputFolder { get; set; } =
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "E3G_Exports");

        /// <summary>Prefix del nom del fitxer exportat.</summary>
        public string FilePrefix { get; set; } = "E3G_EsquemaPrincipi";

        /// <summary>Versió de DWG (AutoCAD 2013, 2010, etc.).</summary>
        public ACADVersion DwgVersion { get; set; } = ACADVersion.R2013;

        /// <summary>Exportar text com a geometria (evita problemes de font).</summary>
        public bool ExportTextAsGeometry { get; set; } = false;

        /// <summary>Mapeig de capes DWG per estil de línia.</summary>
        public Dictionary<string, DwgLayerConfig> LayerMap { get; set; }
            = DwgLayerConfig.DefaultLayerMap();
    }

    public class DwgLayerConfig
    {
        public string LayerName  { get; set; }
        public int    AcadColor  { get; set; }  // color AutoCAD (1–255)

        public static Dictionary<string, DwgLayerConfig> DefaultLayerMap()
            => new Dictionary<string, DwgLayerConfig>
            {
                ["DN160"] = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN160", AcadColor = 1  },  // vermell
                ["DN125"] = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN125", AcadColor = 5  },  // blau
                ["DN110"] = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN110", AcadColor = 30 },  // taronja
                ["DN90"]  = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN90",  AcadColor = 3  },  // verd
                ["DN50"]  = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN50",  AcadColor = 6  },  // magenta
                ["DN40"]  = new DwgLayerConfig { LayerName = "E3G-SANEJ-DN40",  AcadColor = 6  },
                ["ZONE"]  = new DwgLayerConfig { LayerName = "E3G-ZONES",       AcadColor = 8  },  // gris
                ["TEXT"]  = new DwgLayerConfig { LayerName = "E3G-TEXT",        AcadColor = 7  },  // blanc/negre
                ["DIM"]   = new DwgLayerConfig { LayerName = "E3G-COTES",       AcadColor = 7  },
            };
    }
}
