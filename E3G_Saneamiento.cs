using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Schematic;  // FASE 3: SchematicGeneratorService
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;

namespace E3G.Saneamiento
{
    // =========================================================================
    // MODELOS DE DATOS — SANEAMIENTO
    // =========================================================================
    public class ResultadoTuberia
    {
        public ElementId Id { get; set; }
        public string SysType { get; set; }
        public double UD { get; set; }
        public int DNActual { get; set; }
        public int DNCorrect { get; set; }
        public int IdxCte { get; set; }
        public bool EsCorrect { get; set; }
        public string ColorSys { get; set; }
    }

    public class FilaGrupo
    {
        public string GrupoTipo { get; set; }
        public int NumInstancias { get; set; }
        public int TipoId { get; set; }
        public List<int> InstanciaIds { get; set; } = new List<int>();
        public string Aparatos { get; set; }
        public Dictionary<string, double> DNS { get; set; } = new Dictionary<string, double>();
        public double TotalM { get; set; }
    }

    public class FilaServicioComun
    {
        public string Categoria { get; set; }
        public string Nivel { get; set; }
        public string DN { get; set; }
        public string ColorHex { get; set; }
        public double LongitudM { get; set; }
    }

    public class AparatoSinGrupo
    {
        public string Nivel { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResumenMediciones
    {
        public double TotalM { get; set; }
        public double InteriorM { get; set; }
        public double BajantesM { get; set; }
        public double ColectoresM { get; set; }
        public double AparcamientoM { get; set; }
        public List<FilaGrupo> Grupos { get; set; } = new List<FilaGrupo>();
        public List<FilaServicioComun> ServiciosComunes { get; set; } = new List<FilaServicioComun>();
        public List<AparatoSinGrupo> AparatosSinGrupo { get; set; } = new List<AparatoSinGrupo>();
        public Dictionary<string, double> InteriorPorDN { get; set; } = new Dictionary<string, double>();
        public List<int> IdsInterior { get; set; } = new List<int>();
        public List<int> IdsBajantes { get; set; } = new List<int>();
        public List<int> IdsColectores { get; set; } = new List<int>();
        public List<int> IdsAparcamiento { get; set; } = new List<int>();

        public Dictionary<string, double> MetresPorCategoria { get; set; }
            = new Dictionary<string, double>();
        public Dictionary<string, List<int>> IdsPorCategoria { get; set; }
            = new Dictionary<string, List<int>>();
    }
    public class FilaBajantPluvial
    {
        public string PlantaInici { get; set; }
        public string PlantaFi { get; set; }
        public string DN { get; set; }
        public double LongitudM { get; set; }
    }

    public class ResumenPluvials
    {
        public double TotalM { get; set; }
        public double BajantsM { get; set; }
        public double HoritzontalsM { get; set; }
        public int NumBuneres { get; set; }
        public List<FilaBajantPluvial> Bajants { get; set; } = new List<FilaBajantPluvial>();
        public Dictionary<string, double> MedicioPorDN { get; set; } = new Dictionary<string, double>();
    }

    public class ResumenGrises
    {
        public double TotalM { get; set; }
        public List<FilaGrupo> Grupos { get; set; } = new List<FilaGrupo>();
        public Dictionary<string, double> MedicioPorDN { get; set; } = new Dictionary<string, double>();
    }

    // =========================================================================
    // MODELOS DE DATOS — FONTANERÍA
    // =========================================================================
    public class FilaGrupoFon
    {
        public string TipoNombre { get; set; }
        public int TipoId { get; set; }
        public int NumInstancias { get; set; }
        public List<string> Plantas { get; set; } = new List<string>();
        public List<int> InstanciaIds { get; set; } = new List<int>();
        public Dictionary<int, int> InstanciaIdPorPlanta { get; set; } = new Dictionary<int, int>();
        public double FlowTotal { get; set; }
        public double FlowDim { get; set; }
        public double Dpm { get; set; }
        public double Velocidad { get; set; }
        public Dictionary<string, Dictionary<string, double>> MedicionSisDN { get; set; }
            = new Dictionary<string, Dictionary<string, double>>();
        public Dictionary<string, Dictionary<string, int>> Aparatos { get; set; }
            = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, Dictionary<string, double>> Derivaciones { get; set; }
            = new Dictionary<string, Dictionary<string, double>>();
        public Dictionary<string, List<int>> IdsDerivacionPorPlanta { get; set; }
            = new Dictionary<string, List<int>>();
    }

    public class ResumenFontaneria
    {
        public List<FilaGrupoFon> Grupos { get; set; } = new List<FilaGrupoFon>();
        public Dictionary<string, Dictionary<string, double>> TotalesDN { get; set; }
            = new Dictionary<string, Dictionary<string, double>>();
    }

    // =========================================================================
    // SESIÓN GLOBAL
    // =========================================================================
    public static class Sesion
    {
        public static List<ResultadoTuberia> Resultados { get; set; } = new List<ResultadoTuberia>();
        public static ResumenMediciones UltimasMediciones { get; set; }
        public static ResumenPluvials UltimesPluvials { get; set; }
        public static ResumenGrises UltimesGrises { get; set; }
        public static List<int> UltimosIdsSeleccionados { get; set; } = new List<int>();
    }
    public static class SaneamientoSession
    {
        public static EventoSaneamiento Handler { get; set; }
        public static ExternalEvent Evento { get; set; }
        public static EventoSeleccion HandlerSeleccion { get; set; }
        public static ExternalEvent EventoSel { get; set; }
        public static EventoMedicionPluvials HandlerPluvials { get; set; }
        public static ExternalEvent EventoPluvials { get; set; }
        public static EventoMedicionGrises HandlerGrises { get; set; }
        public static ExternalEvent EventoGrises { get; set; }

        // Capturado al primer Idling — antes está null. Usar siempre con null-check.
        public static UIApplication UiApp { get; set; }
    }

    public static class FontaneriaSession
    {
        public static EventoMedicionFontaneria HandlerMedicion { get; set; }
        public static ExternalEvent EventoMedicion { get; set; }
        public static EventoSeleccion HandlerSeleccion { get; set; }
        public static ExternalEvent EventoSel { get; set; }
        public static ResumenFontaneria UltimoResumen { get; set; }
    }

    // =========================================================================
    // MOTOR CTE DB-HS5
    // =========================================================================
    public static class CteMotor
    {
        public static readonly int[] CteDN = { 90, 110, 125, 160, 200, 250, 315, 350 };
        public static readonly double[] CteUD = { 96, 264, 390, 880, 1600, 2900, 5710, 8300 };
        public static readonly int[] CteR = { 226, 239, 232, 160, 55, 127, 26, 245 };
        public static readonly int[] CteG = { 75, 159, 216, 82, 138, 119, 179, 245 };
        public static readonly int[] CteB = { 74, 39, 0, 45, 221, 221, 196, 245 };

        private static readonly string[] Nombres = {
            "Rojo", "Naranja", "Amarillo", "Marrón", "Azul", "Violeta", "Cian", "Blanco"
        };
        private static readonly string[] Hexes = {
            "#E24B4A", "#EF9F27", "#E8D800", "#A0522D",
            "#378ADD", "#7F77DD", "#1AB3C4", "#E0E0E0"
        };

        public static string NombreColor(int idx) =>
            idx >= 0 && idx < Nombres.Length ? Nombres[idx] : "—";
        public static string GetColorHex(int idx) =>
            idx >= 0 && idx < Hexes.Length ? Hexes[idx] : "#E2E8F0";

        public static string GetColorHexByDN(string dn)
        {
            switch (dn)
            {
                case "ø40": return "#94A3B8";
                case "ø50": return "#64748B";
                case "ø90": return "#E24B4A";
                case "ø110": return "#EF9F27";
                case "ø125": return "#E8D800";
                case "ø160": return "#A0522D";
                case "ø200": return "#378ADD";
                case "ø250": return "#7F77DD";
                case "ø315": return "#1AB3C4";
                case "ø350": return "#E0E0E0";
                default: return "#E2E8F0";
            }
        }

        public static int GetIdx(double ud)
        {
            for (int i = 0; i < CteUD.Length; i++)
                if (ud <= CteUD[i]) return i;
            return CteUD.Length - 1;
        }

        public static bool EsSistemaValido(string sysType)
        {
            if (string.IsNullOrEmpty(sysType)) return false;
            return sysType.Contains("10_10") || sysType.Contains("Fecal")
                || sysType.Contains("10_30") || sysType.Contains("Gris")
                || sysType.Contains("Grey");
        }

        public static int ParseDN(string tamano)
        {
            if (string.IsNullOrEmpty(tamano)) return 0;
            string limpio = tamano.Replace("ø", "").Replace("Ø", "")
                                  .Replace("DN", "").Replace("mm", "").Trim();
            return int.TryParse(limpio, out int dn) ? dn : 0;
        }

        public static (int r, int g, int b) ParseColorSistema(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return (127, 127, 127);
            var parts = raw.Split('-');
            if (parts.Length < 3) return (127, 127, 127);
            return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }

        public static void AplicarColor(View vista, ElementId id, int r, int g, int b)
        {
            var ogs = new OverrideGraphicSettings();
            var color = new Color((byte)r, (byte)g, (byte)b);
            ogs.SetProjectionLineColor(color);
            ogs.SetSurfaceForegroundPatternColor(color);
            vista.SetElementOverrides(id, ogs);
        }
    }

    // =========================================================================
    // MOTOR DE MEDICIONES — SANEAMIENTO
    // =========================================================================
    public static class MedicionMotor
    {
        private const double Z_P00A = 187450.0;
        private const double Z_P01A = 191290.0;
        private const double TOL = 100.0;

        private static bool EsEdificioA(string nivel) =>
            !string.IsNullOrEmpty(nivel) && nivel.Contains(".A");

        private static bool EsAparcamiento(string nivel) =>
            !string.IsNullOrEmpty(nivel) && nivel.StartsWith("S") && nivel.Contains(".A");

        private static string NombreAparato(string familia)
        {
            if (familia.Contains("INODOR")) return "Inodoro";
            if (familia.Contains("DUTXA")) return "Ducha";
            if (familia.Contains("LAVABO")) return "Lavabo";
            if (familia.Contains("AIGÜERA")) return "Fregadero";
            if (familia.Contains("RENTAVAIXELLES")) return "Lavavajillas";
            if (familia.Contains("RENTADORA")) return "Lavadora";
            if (familia.Contains("Sumidero")) return "Sumidero";
            if (familia.Contains("TERMO")) return "Termo";
            return familia.Replace("MC_E3G_", "").Replace("_SA", "");
        }

        public static ResumenMediciones Calcular(Document doc)
        {
            var resultado = new ResumenMediciones();

            var nivelesDic = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(l => l.Name.Contains(".A"))
                .ToDictionary(
                    l => l.Name,
                    l => UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters)
                );

            var dnsPorTipo = new Dictionary<string, Dictionary<string, double>>();
            var nivelPorTipo = new Dictionary<string, string>();
            var idPorTipo = new Dictionary<string, int>();
            var instPorTipo = new Dictionary<string, HashSet<int>>();
            var instIdsPorTipo = new Dictionary<string, HashSet<int>>();

            var pipesEnGrupo = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => {
                    var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
                    return sys.Contains("10_10") && e.GroupId != ElementId.InvalidElementId;
                });

            foreach (var e in pipesEnGrupo)
            {
                var nivel = e.LookupParameter("Nivel de referencia")?.AsValueString() ?? "—";
                if (!EsEdificioA(nivel)) continue;

                var grp = doc.GetElement(e.GroupId) as Group;
                var gtId = grp?.GetTypeId();
                var gt = gtId != null ? doc.GetElement(gtId) as GroupType : null;
                string tipoNombre = gt?.Name ?? "—";
                int tipoIdInt = gtId?.IntegerValue ?? 0;

                var dn = e.LookupParameter("Tamaño")?.AsValueString() ?? "—";
                double len = UnitUtils.ConvertFromInternalUnits(
                    e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                    UnitTypeId.Meters);

                string nivelGrupo = grp?.LookupParameter("Nivel de referencia")?.AsValueString() ?? nivel;

                if (!dnsPorTipo.ContainsKey(tipoNombre))
                {
                    dnsPorTipo[tipoNombre] = new Dictionary<string, double>();
                    nivelPorTipo[tipoNombre] = nivelGrupo;
                    idPorTipo[tipoNombre] = tipoIdInt;
                    instPorTipo[tipoNombre] = new HashSet<int>();
                    instIdsPorTipo[tipoNombre] = new HashSet<int>();
                }

                if (!dnsPorTipo[tipoNombre].ContainsKey(dn))
                    dnsPorTipo[tipoNombre][dn] = 0;
                dnsPorTipo[tipoNombre][dn] += len;

                instPorTipo[tipoNombre].Add(e.GroupId.IntegerValue);
                instIdsPorTipo[tipoNombre].Add(e.GroupId.IntegerValue);
                resultado.IdsInterior.Add(e.GroupId.IntegerValue);

                resultado.InteriorM += len;
                resultado.TotalM += len;

                if (!resultado.InteriorPorDN.ContainsKey(dn))
                    resultado.InteriorPorDN[dn] = 0;
                resultado.InteriorPorDN[dn] += len;
            }

            resultado.IdsInterior = resultado.IdsInterior.Distinct().ToList();

            var aparatosPorTipo = new Dictionary<string, Dictionary<string, int>>();

            var grupos = new FilteredElementCollector(doc)
                .OfClass(typeof(Group))
                .Cast<Group>()
                .Where(g => {
                    var gt = doc.GetElement(g.GetTypeId()) as GroupType;
                    return gt != null && dnsPorTipo.ContainsKey(gt.Name);
                });

            foreach (var grp in grupos)
            {
                var gt = doc.GetElement(grp.GetTypeId()) as GroupType;
                if (gt == null) continue;
                string tipoNombre = gt.Name;

                if (!aparatosPorTipo.ContainsKey(tipoNombre))
                    aparatosPorTipo[tipoNombre] = new Dictionary<string, int>();

                var aparatos = grp.GetMemberIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e?.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PlumbingFixtures)
                    .Select(e => {
                        var et = doc.GetElement(e.GetTypeId()) as ElementType;
                        return NombreAparato(et?.FamilyName ?? e.Name);
                    })
                    .GroupBy(n => n)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var kv in aparatos)
                {
                    if (!aparatosPorTipo[tipoNombre].ContainsKey(kv.Key))
                        aparatosPorTipo[tipoNombre][kv.Key] = 0;
                    aparatosPorTipo[tipoNombre][kv.Key] =
                        Math.Max(aparatosPorTipo[tipoNombre][kv.Key], kv.Value);
                }
            }

            foreach (var kv in dnsPorTipo.OrderBy(x => nivelPorTipo[x.Key]).ThenBy(x => x.Key))
            {
                string tipo = kv.Key;
                var dns = kv.Value;
                double total = dns.Values.Sum();
                var apar = aparatosPorTipo.ContainsKey(tipo)
                                 ? aparatosPorTipo[tipo] : new Dictionary<string, int>();
                int numInst = instPorTipo.ContainsKey(tipo) ? instPorTipo[tipo].Count : 0;

                string aparatosStr = string.Join(", ", apar
                    .OrderBy(a => a.Key)
                    .Select(a => a.Value > 1 ? $"{a.Key} ×{a.Value}" : a.Key));

                resultado.Grupos.Add(new FilaGrupo
                {
                    GrupoTipo = tipo,
                    NumInstancias = numInst,
                    TipoId = idPorTipo.ContainsKey(tipo) ? idPorTipo[tipo] : 0,
                    InstanciaIds = instIdsPorTipo.ContainsKey(tipo)
                                    ? instIdsPorTipo[tipo].ToList() : new List<int>(),
                    Aparatos = aparatosStr,
                    DNS = dns.ToDictionary(d => d.Key, d => Math.Round(d.Value, 2)),
                    TotalM = Math.Round(total, 2),
                });
            }

            var aparatosSinGrupo = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => e.GroupId == ElementId.InvalidElementId)
                .Where(e => {
                    var nivel = e.LookupParameter("Nivel")?.AsValueString()
                             ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsValueString() ?? "";
                    return nivel.Contains(".A");
                })
                .Select(e => {
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    var nivel = e.LookupParameter("Nivel")?.AsValueString()
                             ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsValueString() ?? "—";
                    return new { nombre = NombreAparato(et?.FamilyName ?? e.Name), nivel };
                })
                .GroupBy(a => new { a.nombre, a.nivel })
                .Select(g => new AparatoSinGrupo
                {
                    Nombre = g.Key.nombre,
                    Nivel = g.Key.nivel,
                    Cantidad = g.Count()
                })
                .OrderBy(a => a.Nivel).ThenBy(a => a.Nombre)
                .ToList();

            resultado.AparatosSinGrupo = aparatosSinGrupo;

            var scData = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>();

            var pipesServComun = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => {
                    var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
                    return sys.Contains("10_10") && e.GroupId == ElementId.InvalidElementId;
                });

            foreach (var e in pipesServComun)
            {
                var nivel = e.LookupParameter("Nivel de referencia")?.AsValueString() ?? "—";
                if (!EsEdificioA(nivel)) continue;

                var dn = e.LookupParameter("Tamaño")?.AsValueString() ?? "—";
                double len = UnitUtils.ConvertFromInternalUnits(
                    e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                    UnitTypeId.Meters);

                var loc = (e.Location as LocationCurve)?.Curve;
                string orient = "H";
                if (loc != null)
                {
                    var dir = loc.GetEndPoint(1) - loc.GetEndPoint(0);
                    orient = Math.Abs(dir.Z) > Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y) ? "V" : "H";
                }

                double elevNivel = nivelesDic.ContainsKey(nivel) ? nivelesDic[nivel] : 0;

                // Classificació via Protocol (si hi ha protocol actiu) o fallback hardcoded
                string categoria;
                string? grupProtocol = Protocol.AgrupadorMedicions.ObtenerGrupo(e, doc);

                if (!string.IsNullOrEmpty(grupProtocol))
                {
                    categoria = grupProtocol;

                    // Acumular al diccionari dinàmic
                    if (!resultado.MetresPorCategoria.ContainsKey(categoria))
                        resultado.MetresPorCategoria[categoria] = 0;
                    resultado.MetresPorCategoria[categoria] += len;

                    if (!resultado.IdsPorCategoria.ContainsKey(categoria))
                        resultado.IdsPorCategoria[categoria] = new List<int>();
                    resultado.IdsPorCategoria[categoria].Add(e.Id.IntegerValue);

                    // Mantenir retrocompatibilitat amb camps fixos
                    // (coincidència per nom, case-insensitive)
                    var catLower = categoria.ToLowerInvariant();
                    if (catLower.Contains("bajant") || catLower.Contains("bajante"))
                    {
                        resultado.BajantesM += len;
                        resultado.IdsBajantes.Add(e.Id.IntegerValue);
                    }
                    else if (catLower.Contains("col·lector") || catLower.Contains("colector"))
                    {
                        resultado.ColectoresM += len;
                        resultado.IdsColectores.Add(e.Id.IntegerValue);
                    }
                    else if (catLower.Contains("aparcament") || catLower.Contains("aparcamiento"))
                    {
                        resultado.AparcamientoM += len;
                        resultado.IdsAparcamiento.Add(e.Id.IntegerValue);
                    }
                    else
                    {
                        // Categoria custom — va a colectores com a fallback comptable
                        resultado.ColectoresM += len;
                        resultado.IdsColectores.Add(e.Id.IntegerValue);
                    }
                }
                else
                {
                    // Fallback hardcoded (sense Protocol configurat)
                    if (EsAparcamiento(nivel))
                    {
                        categoria = "Aparcament";
                        resultado.AparcamientoM += len;
                        resultado.IdsAparcamiento.Add(e.Id.IntegerValue);
                    }
                    else if (orient == "V" && len > 1.0 && elevNivel >= Z_P00A - TOL)
                    {
                        categoria = "Bajants";
                        resultado.BajantesM += len;
                        resultado.IdsBajantes.Add(e.Id.IntegerValue);
                    }
                    else if (orient == "H" && elevNivel >= Z_P00A - TOL && elevNivel <= Z_P01A + TOL)
                    {
                        categoria = "Col·lectors P00-P01";
                        resultado.ColectoresM += len;
                        resultado.IdsColectores.Add(e.Id.IntegerValue);
                    }
                    else
                    {
                        categoria = "Serveis comuns";
                        resultado.ColectoresM += len;
                        resultado.IdsColectores.Add(e.Id.IntegerValue);
                    }

                    // Fallback també al diccionari dinàmic
                    if (!resultado.MetresPorCategoria.ContainsKey(categoria))
                        resultado.MetresPorCategoria[categoria] = 0;
                    resultado.MetresPorCategoria[categoria] += len;
                    if (!resultado.IdsPorCategoria.ContainsKey(categoria))
                        resultado.IdsPorCategoria[categoria] = new List<int>();
                    resultado.IdsPorCategoria[categoria].Add(e.Id.IntegerValue);
                }

                resultado.TotalM += len;

                if (!scData.ContainsKey(categoria))
                    scData[categoria] = new Dictionary<string, Dictionary<string, double>>();
                if (!scData[categoria].ContainsKey(nivel))
                    scData[categoria][nivel] = new Dictionary<string, double>();
                if (!scData[categoria][nivel].ContainsKey(dn))
                    scData[categoria][nivel][dn] = 0;
                scData[categoria][nivel][dn] += len;
            }

            // Construir ServiciosComunes des del diccionari dinàmic
            // L'ordre de les seccions del Protocol mana
            var seccionsProtocol = Protocol.ProtocolSession.ProtocolActual?.Seccions
                .Select(s => s.Etiqueta).ToList()
                ?? new List<string> { "Bajants", "Col·lectors P00-P01", "Aparcament", "Serveis comuns" };

            // Primer les categories que estan al Protocol (en ordre)
            foreach (var cat in seccionsProtocol)
            {
                if (!scData.ContainsKey(cat)) continue;
                foreach (var nivelKv in scData[cat].OrderBy(x => x.Key))
                    foreach (var dnKv in nivelKv.Value.OrderBy(x => x.Key))
                        resultado.ServiciosComunes.Add(new FilaServicioComun
                        {
                            Categoria = cat,
                            Nivel = nivelKv.Key,
                            DN = dnKv.Key,
                            ColorHex = CteMotor.GetColorHexByDN(dnKv.Key),
                            LongitudM = Math.Round(dnKv.Value, 2),
                        });
            }

            // Després categories que no estan al Protocol (fallback)
            foreach (var cat in scData.Keys.Except(seccionsProtocol).OrderBy(x => x))
            {
                foreach (var nivelKv in scData[cat].OrderBy(x => x.Key))
                    foreach (var dnKv in nivelKv.Value.OrderBy(x => x.Key))
                        resultado.ServiciosComunes.Add(new FilaServicioComun
                        {
                            Categoria = cat,
                            Nivel = nivelKv.Key,
                            DN = dnKv.Key,
                            ColorHex = CteMotor.GetColorHexByDN(dnKv.Key),
                            LongitudM = Math.Round(dnKv.Value, 2),
                        });
            }
            resultado.TotalM = Math.Round(resultado.TotalM, 2);
            resultado.InteriorM = Math.Round(resultado.InteriorM, 2);
            resultado.BajantesM = Math.Round(resultado.BajantesM, 2);
            resultado.ColectoresM = Math.Round(resultado.ColectoresM, 2);
            resultado.AparcamientoM = Math.Round(resultado.AparcamientoM, 2);

            foreach (var k in resultado.InteriorPorDN.Keys.ToList())
                resultado.InteriorPorDN[k] = Math.Round(resultado.InteriorPorDN[k], 2);

            return resultado;
        }
    }

    // =========================================================================
    // MOTOR DE FONTANERÍA
    // =========================================================================
    public static class FontaneriaMotor
    {
        private static string NombreSistema(string sysType)
        {
            if (sysType.Contains("20_10")) return "AFS";
            if (sysType.Contains("20_20")) return "ACS";
            if (sysType.Contains("20_30")) return "AR";
            return "—";
        }

        private static string NombreAparato(string familia)
        {
            if (familia.Contains("INODOR")) return "Inodoro";
            if (familia.Contains("LAVABO")) return "Lavabo";
            if (familia.Contains("DUTXA")) return "Ducha";
            if (familia.Contains("BANYERA")) return "Bañera";
            if (familia.Contains("AIGÜERA")) return "Fregadero";
            if (familia.Contains("RENTAVAIXELLES")) return "Lavavajillas";
            if (familia.Contains("RENTADORA")) return "Lavadora";
            if (familia.Contains("TERMO")) return "Termo";
            return familia.Replace("MC_E3G_", "").Replace("_AF", "").Replace("_ACS", "").Replace("_SA", "");
        }

        public static ResumenFontaneria Calcular(Document doc)
        {
            var resultado = new ResumenFontaneria();
            var dnsPorTipo = new Dictionary<string, FilaGrupoFon>();

            // ── GRUPOS CON FONTANERÍA ──
            var gruposConFon = new FilteredElementCollector(doc)
                .OfClass(typeof(Group))
                .Cast<Group>()
                .Where(g => {
                    var gt = doc.GetElement(g.GetTypeId()) as GroupType;
                    if (gt == null) return false;
                    return g.GetMemberIds().Any(id => {
                        var e = doc.GetElement(id);
                        if (e?.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) return false;
                        var sys = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
                        return sys.Contains("20_10");
                    });
                })
                .ToList();

            foreach (var grp in gruposConFon)
            {
                var gt = doc.GetElement(grp.GetTypeId()) as GroupType;
                if (gt == null) continue;
                string tipo = gt.Name;
                string nivel = grp.LookupParameter("Nivel de referencia")?.AsValueString()
                            ?? grp.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsValueString() ?? "—";

                if (!dnsPorTipo.ContainsKey(tipo))
                    dnsPorTipo[tipo] = new FilaGrupoFon { TipoNombre = tipo, TipoId = grp.GetTypeId().IntegerValue };

                var fila = dnsPorTipo[tipo];
                fila.NumInstancias++;
                fila.InstanciaIds.Add(grp.Id.IntegerValue);
                if (!fila.Plantas.Contains(nivel)) fila.Plantas.Add(nivel);
                fila.InstanciaIdPorPlanta[grp.Id.IntegerValue] = grp.Id.IntegerValue;

                // Caudal tubería AFS principal
                Element mainPipe = null; double maxFlow = 0;
                foreach (var id in grp.GetMemberIds())
                {
                    var e = doc.GetElement(id);
                    if (e?.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) continue;
                    var sys = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
                    if (!sys.Contains("20_10")) continue;
                    double fl = e.LookupParameter("MC Water Flow Total")?.AsDouble() ?? 0;
                    if (fl > maxFlow) { maxFlow = fl; mainPipe = e; }
                }
                if (mainPipe != null && fila.FlowTotal == 0)
                {
                    fila.FlowTotal = Math.Round((mainPipe.LookupParameter("MC Water Flow Total")?.AsDouble() ?? 0) * 28.3168, 4);
                    fila.FlowDim = Math.Round((mainPipe.LookupParameter("MC Water Dimensioning Flow")?.AsDouble() ?? 0) * 28.3168, 4);
                    double rawDpm = mainPipe.LookupParameter("MC Water dp/m")?.AsDouble() ?? 0;
                    fila.Dpm = Math.Round(UnitUtils.ConvertFromInternalUnits(rawDpm, UnitTypeId.Kilopascals), 3);
                }

                // Medición por sistema y DN
                foreach (var id in grp.GetMemberIds())
                {
                    var e = doc.GetElement(id);
                    if (e?.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) continue;
                    string sysType = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
                    string sisNom = NombreSistema(sysType);
                    if (sisNom == "—") continue;
                    string dn = e.LookupParameter("Tamaño")?.AsValueString() ?? "—";
                    double len = UnitUtils.ConvertFromInternalUnits(
                        e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                        UnitTypeId.Meters);
                    if (!fila.MedicionSisDN.ContainsKey(sisNom))
                        fila.MedicionSisDN[sisNom] = new Dictionary<string, double>();
                    if (!fila.MedicionSisDN[sisNom].ContainsKey(dn))
                        fila.MedicionSisDN[sisNom][dn] = 0;
                    fila.MedicionSisDN[sisNom][dn] += len;
                }

                // Aparatos — solo primera instancia
                if (fila.Aparatos.Count == 0)
                {
                    foreach (var id in grp.GetMemberIds())
                    {
                        var e = doc.GetElement(id);
                        if (e?.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PlumbingFixtures) continue;
                        var et = doc.GetElement(e.GetTypeId()) as ElementType;
                        string fn = et?.FamilyName ?? e.Name;
                        string sisNom = fn.Contains("_ACS") ? "ACS" : fn.Contains("INODOR") ? "AR" : "AFS";
                        string nombre = NombreAparato(fn);
                        if (!fila.Aparatos.ContainsKey(sisNom))
                            fila.Aparatos[sisNom] = new Dictionary<string, int>();
                        if (!fila.Aparatos[sisNom].ContainsKey(nombre))
                            fila.Aparatos[sisNom][nombre] = 0;
                        fila.Aparatos[sisNom][nombre]++;
                    }
                }
            }

            // Normalizar medición por instancia
            foreach (var fila in dnsPorTipo.Values)
            {
                int n = fila.NumInstancias;
                if (n <= 1) continue;
                foreach (var sis in fila.MedicionSisDN.Keys.ToList())
                    foreach (var dn in fila.MedicionSisDN[sis].Keys.ToList())
                        fila.MedicionSisDN[sis][dn] = Math.Round(fila.MedicionSisDN[sis][dn] / n, 3);
            }

            // ── DERIVACIONES — recorrer red desde nodo AF por grupo y nivel ──
            var nodosFon = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(e => {
                    var fn = (doc.GetElement(e.GetTypeId()) as ElementType)?.FamilyName ?? "";
                    return fn.Contains("ConnectionNode") || fn.Contains("Node");
                })
                .Select(e => new {
                    elemento = e,
                    grupo = e.LookupParameter("Comentarios")?.AsString() ?? "",
                    nivel = e.LookupParameter("Nivel")?.AsValueString()
                            ?? e.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsValueString() ?? "—",
                })
                .Where(x => !string.IsNullOrEmpty(x.grupo) && x.nivel.Contains(".A"))
                .ToList();

            foreach (var nodoInfo in nodosFon)
            {
                string grupoNombre = nodoInfo.grupo;
                string nivel = nodoInfo.nivel;
                var nodo = nodoInfo.elemento;

                if (!dnsPorTipo.ContainsKey(grupoNombre)) continue;

                var visited = new HashSet<int>();
                var queue = new Queue<ElementId>();
                var pipeIds = new List<int>();
                var dnData = new Dictionary<string, double>();

                var connMgr = nodo.MEPModel?.ConnectorManager;
                if (connMgr == null) continue;

                foreach (Connector c in connMgr.Connectors)
                {
                    if (!c.IsConnected) continue;
                    foreach (Connector refC in c.AllRefs)
                        if (refC.Owner.Id != nodo.Id)
                            queue.Enqueue(refC.Owner.Id);
                }

                int iter = 0;
                while (queue.Count > 0 && iter < 500)
                {
                    iter++;
                    var currentId = queue.Dequeue();
                    if (visited.Contains(currentId.IntegerValue)) continue;
                    visited.Add(currentId.IntegerValue);

                    var current = doc.GetElement(currentId);
                    if (current == null) continue;

                    // Parar si es el contador de agua
                    var famNameCurrent = (doc.GetElement(current.GetTypeId()) as ElementType)?.FamilyName ?? "";
                    if (famNameCurrent.ToLower().Contains("water-meter") || famNameCurrent.ToLower().Contains("meter"))
                        break;

                    bool esTuberia = current.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves;
                    bool esAccesorio = current.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeFitting;
                    bool sinGrupo = current.GroupId == ElementId.InvalidElementId;

                    if ((esTuberia || esAccesorio) && sinGrupo)
                    {
                        var sys = current.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
                        if (sys.Contains("20_10"))
                        {
                            if (esTuberia)
                            {
                                pipeIds.Add(currentId.IntegerValue);
                                string dn = current.LookupParameter("Tamaño")?.AsValueString() ?? "—";
                                double len = UnitUtils.ConvertFromInternalUnits(
                                    current.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                                    UnitTypeId.Meters);
                                if (!dnData.ContainsKey(dn)) dnData[dn] = 0;
                                dnData[dn] += len;
                            }

                            // Continuar BFS por MEPCurve o FamilyInstance
                            if (current is MEPCurve mc)
                                foreach (Connector c2 in mc.ConnectorManager.Connectors)
                                    if (c2.IsConnected)
                                        foreach (Connector r2 in c2.AllRefs)
                                            if (!visited.Contains(r2.Owner.Id.IntegerValue))
                                                queue.Enqueue(r2.Owner.Id);

                            if (current is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                                foreach (Connector c2 in fi.MEPModel.ConnectorManager.Connectors)
                                    if (c2.IsConnected)
                                        foreach (Connector r2 in c2.AllRefs)
                                            if (!visited.Contains(r2.Owner.Id.IntegerValue))
                                                queue.Enqueue(r2.Owner.Id);
                        }
                    }
                }

                var fila = dnsPorTipo[grupoNombre];
                fila.Derivaciones[nivel] = dnData.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 3));
                if (!fila.IdsDerivacionPorPlanta.ContainsKey(nivel))
                    fila.IdsDerivacionPorPlanta[nivel] = new List<int>();
                fila.IdsDerivacionPorPlanta[nivel].AddRange(pipeIds);
            }

            // ── RESULTADO FINAL ──
            resultado.Grupos = dnsPorTipo.Values.OrderBy(f => f.TipoNombre).ToList();

            foreach (var fila in resultado.Grupos)
            {
                int n = fila.NumInstancias;
                foreach (var sis in fila.MedicionSisDN)
                {
                    if (!resultado.TotalesDN.ContainsKey(sis.Key))
                        resultado.TotalesDN[sis.Key] = new Dictionary<string, double>();
                    foreach (var dn in sis.Value)
                    {
                        if (!resultado.TotalesDN[sis.Key].ContainsKey(dn.Key))
                            resultado.TotalesDN[sis.Key][dn.Key] = 0;
                        resultado.TotalesDN[sis.Key][dn.Key] += Math.Round(dn.Value * n, 2);
                    }
                }
            }

            return resultado;
        }
    }

    // =========================================================================
    // MOTOR DE MEDICIONS — PLUVIALS (10_20)
    // =========================================================================
    public static class PluvialsMotor
    {
        public static ResumenPluvials Calcular(Document doc)
        {
            var resultado = new ResumenPluvials();

            var nivells = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            string GetNivell(double elevMm)
            {
                Level anterior = null;
                foreach (var n in nivells)
                {
                    double elev = UnitUtils.ConvertFromInternalUnits(n.Elevation, UnitTypeId.Millimeters);
                    if (elevMm < elev) break;
                    anterior = n;
                }
                return anterior?.Name ?? "—";
            }

            var canonades = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => {
                    var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
                    return sys.Contains("10_20");
                })
                .ToList();

            foreach (var e in canonades)
            {
                var loc = (e.Location as LocationCurve)?.Curve;
                if (loc == null) continue;

                double len = UnitUtils.ConvertFromInternalUnits(
                    e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                    UnitTypeId.Meters);
                string dn = e.LookupParameter("Tamaño")?.AsValueString() ?? "—";

                var dir = loc.GetEndPoint(1) - loc.GetEndPoint(0);
                bool esVertical = Math.Abs(dir.Z) > Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);

                if (!resultado.MedicioPorDN.ContainsKey(dn))
                    resultado.MedicioPorDN[dn] = 0;
                resultado.MedicioPorDN[dn] += len;
                resultado.TotalM += len;

                if (esVertical && len > 0.5)
                {
                    resultado.BajantsM += len;
                    double elevInici = UnitUtils.ConvertFromInternalUnits(loc.GetEndPoint(0).Z, UnitTypeId.Millimeters);
                    double elevFi = UnitUtils.ConvertFromInternalUnits(loc.GetEndPoint(1).Z, UnitTypeId.Millimeters);
                    double elevMin = Math.Min(elevInici, elevFi);
                    double elevMax = Math.Max(elevInici, elevFi);
                    resultado.Bajants.Add(new FilaBajantPluvial
                    {
                        PlantaInici = GetNivell(elevMin),
                        PlantaFi = GetNivell(elevMax),
                        DN = dn,
                        LongitudM = Math.Round(len, 2),
                    });
                }
                else
                {
                    resultado.HoritzontalsM += len;
                }
            }

            var buneres = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => {
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    return (et?.FamilyName ?? "").Contains("E3G_Sumidero");
                })
                .ToList();

            resultado.NumBuneres = buneres.Count;
            resultado.TotalM = Math.Round(resultado.TotalM, 2);
            resultado.BajantsM = Math.Round(resultado.BajantsM, 2);
            resultado.HoritzontalsM = Math.Round(resultado.HoritzontalsM, 2);
            foreach (var k in resultado.MedicioPorDN.Keys.ToList())
                resultado.MedicioPorDN[k] = Math.Round(resultado.MedicioPorDN[k], 2);

            return resultado;
        }
    }

    // =========================================================================
    // MOTOR DE MEDICIONS — GRISES (10_30)
    // =========================================================================
    public static class GrisesMotor
    {
        public static ResumenGrises Calcular(Document doc)
        {
            var resultado = new ResumenGrises();
            var dnsPorTipo = new Dictionary<string, Dictionary<string, double>>();
            var nivelPorTipo = new Dictionary<string, string>();
            var idPorTipo = new Dictionary<string, int>();
            var instPorTipo = new Dictionary<string, HashSet<int>>();
            var instIdsPorTipo = new Dictionary<string, HashSet<int>>();

            var canonades = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => {
                    var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
                    return sys.Contains("10_30") && e.GroupId != ElementId.InvalidElementId;
                });

            foreach (var e in canonades)
            {
                var nivel = e.LookupParameter("Nivel de referencia")?.AsValueString() ?? "—";
                var grp = doc.GetElement(e.GroupId) as Group;
                var gtId = grp?.GetTypeId();
                var gt = gtId != null ? doc.GetElement(gtId) as GroupType : null;
                string tipoNombre = gt?.Name ?? "—";
                int tipoIdInt = gtId?.IntegerValue ?? 0;

                string dn = e.LookupParameter("Tamaño")?.AsValueString() ?? "—";
                double len = UnitUtils.ConvertFromInternalUnits(
                    e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0,
                    UnitTypeId.Meters);

                string nivelGrupo = grp?.LookupParameter("Nivel de referencia")?.AsValueString() ?? nivel;

                if (!dnsPorTipo.ContainsKey(tipoNombre))
                {
                    dnsPorTipo[tipoNombre] = new Dictionary<string, double>();
                    nivelPorTipo[tipoNombre] = nivelGrupo;
                    idPorTipo[tipoNombre] = tipoIdInt;
                    instPorTipo[tipoNombre] = new HashSet<int>();
                    instIdsPorTipo[tipoNombre] = new HashSet<int>();
                }

                if (!dnsPorTipo[tipoNombre].ContainsKey(dn))
                    dnsPorTipo[tipoNombre][dn] = 0;
                dnsPorTipo[tipoNombre][dn] += len;

                instPorTipo[tipoNombre].Add(e.GroupId.IntegerValue);
                instIdsPorTipo[tipoNombre].Add(e.GroupId.IntegerValue);
                resultado.TotalM += len;

                if (!resultado.MedicioPorDN.ContainsKey(dn))
                    resultado.MedicioPorDN[dn] = 0;
                resultado.MedicioPorDN[dn] += len;
            }

            foreach (var kv in dnsPorTipo.OrderBy(x => nivelPorTipo[x.Key]).ThenBy(x => x.Key))
            {
                string tipo = kv.Key;
                double total = kv.Value.Values.Sum();
                int numInst = instPorTipo.ContainsKey(tipo) ? instPorTipo[tipo].Count : 0;

                resultado.Grupos.Add(new FilaGrupo
                {
                    GrupoTipo = tipo,
                    NumInstancias = numInst,
                    TipoId = idPorTipo.ContainsKey(tipo) ? idPorTipo[tipo] : 0,
                    InstanciaIds = instIdsPorTipo.ContainsKey(tipo)
                                    ? instIdsPorTipo[tipo].ToList() : new List<int>(),
                    DNS = kv.Value.ToDictionary(d => d.Key, d => Math.Round(d.Value, 2)),
                    TotalM = Math.Round(total, 2),
                });
            }

            resultado.TotalM = Math.Round(resultado.TotalM, 2);
            foreach (var k in resultado.MedicioPorDN.Keys.ToList())
                resultado.MedicioPorDN[k] = Math.Round(resultado.MedicioPorDN[k], 2);

            return resultado;
        }
    }

    // =========================================================================
    // EVENTO DE SELECCIÓN
    // =========================================================================
    public class EventoSeleccion : IExternalEventHandler
    {
        public List<int> IdsASeleccionar { get; set; } = new List<int>();

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null || IdsASeleccionar == null || IdsASeleccionar.Count == 0) return;
            var ids = IdsASeleccionar
                .Select(id => new ElementId(id))
                .Where(id => uidoc.Document.GetElement(id) != null)
                .ToList();
            if (ids.Count == 0) return;

            uidoc.Selection.SetElementIds(ids);

            // Guardar para recuperación futura
            Sesion.UltimosIdsSeleccionados = ids.Select(id => id.IntegerValue).ToList();
        }

        public string GetName() => "E3G Selección";
    }

    // =========================================================================
    // DOCKABLE PANE PROVIDER — SANEAMIENTO
    // =========================================================================
    public class SaneamientoPaneProvider : IDockablePaneProvider
    {
        private ExternalEvent _evento;
        private EventoSaneamiento _handler;
        public SaneamientoWindow Pagina { get; private set; }

        public SaneamientoPaneProvider(ExternalEvent evento, EventoSaneamiento handler)
        {
            _evento = evento;
            _handler = handler;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            Pagina = new SaneamientoWindow(_evento, _handler);
            _handler.Pagina = Pagina;
            data.FrameworkElement = Pagina;
            data.InitialState = new DockablePaneState
            {
                DockPosition = Autodesk.Revit.UI.DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser,
            };
        }
    }

    // =========================================================================
    // DOCKABLE PANE PROVIDER — FONTANERÍA
    // =========================================================================
    public class FontaneriaPaneProvider : IDockablePaneProvider
    {
        public FontaneriaWindow Pagina { get; private set; }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            Pagina = new FontaneriaWindow();
            data.FrameworkElement = Pagina;
            data.InitialState = new DockablePaneState
            {
                DockPosition = Autodesk.Revit.UI.DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser,
            };
        }
    }

    // =========================================================================
    // DOCKABLE PANE PROVIDER — MAIN (contenidor unificat)
    // =========================================================================
    public class MainPaneProvider : IDockablePaneProvider
    {
        private ExternalEvent _evento;
        private EventoSaneamiento _handler;
        public MainWindow Pagina { get; private set; }

        public MainPaneProvider(ExternalEvent evento, EventoSaneamiento handler)
        {
            _evento = evento;
            _handler = handler;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            var saneamiento = new SaneamientoWindow(_evento, _handler);
            _handler.Pagina = saneamiento;
            var fontaneria = new FontaneriaWindow();
            Pagina = new MainWindow(saneamiento, fontaneria, new AjustesWindow());
            data.FrameworkElement = Pagina;
            data.InitialState = new DockablePaneState
            {
                DockPosition = Autodesk.Revit.UI.DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser,
            };
        }
    }

    // =========================================================================
    // HELPERS RIBBON
    // =========================================================================
    internal static class RibbonHelper
    {
        internal static System.Windows.Media.Imaging.BitmapImage CargarIcono(string ruta)
        {
            if (!System.IO.File.Exists(ruta)) return null;
            return new System.Windows.Media.Imaging.BitmapImage(new Uri(ruta));
        }
    }

    // =========================================================================
    // EXTERNAL APPLICATION
    // =========================================================================
    public class App : IExternalApplication
    {
        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567891"));
        public static readonly DockablePaneId PaneIdFontaneria =
            new DockablePaneId(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567892"));
        public static readonly DockablePaneId PaneIdMain =
            new DockablePaneId(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567893"));

        private static EventoSaneamiento _handler;
        private static ExternalEvent _evento;
        private static SaneamientoPaneProvider _provider;
        private static FontaneriaPaneProvider _providerFon;
        private static MainPaneProvider _providerMain;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // Captura de UIApplication en TRES puntos para máxima fiabilidad:
                //  1. Primer Idling (suele dispararse en milisegundos tras OnStartup)
                //  2. Cuando se activa una vista (al abrir proyecto)
                //  3. (En EventoSaneamiento.Execute, ya añadido — cualquier medición)

                EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> capturarIdling = null;
                capturarIdling = (sender, e) =>
                {
                    if (sender is UIApplication ui && SaneamientoSession.UiApp == null)
                    {
                        SaneamientoSession.UiApp = ui;
                        app.Idling -= capturarIdling;

                        try
                        {
                            var doc = ui.ActiveUIDocument?.Document;
                            if (doc != null && Protocol.ProtocolSession.ProtocolActual == null)
                            {
                                var storage = new Protocol.ProtocolStorage();
                                var protocol = storage.Cargar(doc);
                                if (protocol.Seccions.Count == 0 ||
                                    protocol.Seccions.All(s => s.Regles.Count == 0))
                                    protocol = Protocol.ProtocolAutoDetect.Suggerir(doc);
                                Protocol.ProtocolSession.ProtocolActual = protocol;
                            }
                        }
                        catch { }
                    }
                };

                app.Idling += capturarIdling;

                EventHandler<Autodesk.Revit.UI.Events.ViewActivatedEventArgs> capturarVista = null;
                capturarVista = (sender, e) =>
                {
                    if (sender is UIApplication ui)
                    {
                        SaneamientoSession.UiApp = ui;
                        app.ViewActivated -= capturarVista;
                    }
                };
                app.ViewActivated += capturarVista;

                _handler = new EventoSaneamiento();
                _evento = ExternalEvent.Create(_handler);
                _provider = new SaneamientoPaneProvider(_evento, _handler);
                app.RegisterDockablePane(PaneId, "E3G Saneamiento HS5", _provider);

                _providerFon = new FontaneriaPaneProvider();
                app.RegisterDockablePane(PaneIdFontaneria, "E3G Fontanería", _providerFon);

                _providerMain = new MainPaneProvider(_evento, _handler);
                app.RegisterDockablePane(PaneIdMain, "E3G BIM Tools", _providerMain);

                var handlerSel = new EventoSeleccion();
                var eventoSel = ExternalEvent.Create(handlerSel);
                SaneamientoSession.HandlerSeleccion = handlerSel;
                SaneamientoSession.EventoSel = eventoSel;

                string tab = "E3G BIM Tools";
                try { app.CreateRibbonTab(tab); } catch { }

                string dll = Assembly.GetExecutingAssembly().Location;
                string dllDir = System.IO.Path.GetDirectoryName(dll);

                var icoMain32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_san_32.png"));
                var icoMain16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_san_16.png"));

                RibbonPanel rpMain = app.CreateRibbonPanel(tab, "E3G BIM Tools");
                var btnMain = new PushButtonData("BtnMain", "E3G BIM Tools", dll,
                    "E3G.Saneamiento.CmdAbrirPanelMain")
                { ToolTip = "Obre el panell E3G BIM Tools." };
                if (icoMain32 != null) { btnMain.LargeImage = icoMain32; btnMain.Image = icoMain16; }
                rpMain.AddItem(btnMain);

                // -- MEP Calc Self-Test (Sprint 1 - Core.Common) --
                var icoCalc32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_panel_32.png"));
                var icoCalc16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_small_16.png"));
                var btnCalcTest = new PushButtonData("BtnMepCalcSelfTest", "MEP Calc" + System.Environment.NewLine + "Self-Test", dll,
                    "E3G.Saneamiento.CmdMepCalcSelfTest")
                { ToolTip = "Executa els smoke-tests del motor de calcul MEP (Sprint 1 - Core.Common)." };
                if (icoCalc32 != null) { btnCalcTest.LargeImage = icoCalc32; btnCalcTest.Image = icoCalc16; }
                rpMain.AddItem(btnCalcTest);

                // -- MEP Calc Tuberias (Sprint 4) --
                var icoFon32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_fon_32.png"));
                var icoFon16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_fon_16.png"));
                var btnCalcPiping = new PushButtonData("BtnMepCalcPiping", "MEP Calc" + System.Environment.NewLine + "Tuberias", dll,
                    "E3G.Saneamiento.CmdCalcPipingNetwork")
                { ToolTip = "Calcula un PipingSystem del modelo (Darcy-Weisbach + Colebrook-White) y escribe MEP_CALC_* en cada tramo." };
                if (icoFon32 != null) { btnCalcPiping.LargeImage = icoFon32; btnCalcPiping.Image = icoFon16; }
                rpMain.AddItem(btnCalcPiping);

                // -- MEP Calc Conductos (Sprint 4) --
                var icoVen32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_ven_32.png"));
                var icoVen16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_ven_16.png"));
                var btnCalcDucts = new PushButtonData("BtnMepCalcDucts", "MEP Calc" + System.Environment.NewLine + "Conductos", dll,
                    "E3G.Saneamiento.CmdCalcDuctNetwork")
                { ToolTip = "Calcula un MechanicalSystem del modelo (igual friccion / velocidad maxima)." };
                if (icoVen32 != null) { btnCalcDucts.LargeImage = icoVen32; btnCalcDucts.Image = icoVen16; }
                rpMain.AddItem(btnCalcDucts);

                // -- MEP Calc Ventana (Sprint 5 — UI WPF principal) --
                var icoCli32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_cli_32.png"));
                var icoCli16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_cli_16.png"));
                var btnCalcWindow = new PushButtonData("BtnMepCalcWindow", "MEP Calc" + System.Environment.NewLine + "Ventana", dll,
                    "E3G.Saneamiento.CmdShowCalcWindow")
                { ToolTip = "Abre la ventana principal de calculo MEP con DataGrid de resultados, criterios configurables y aplicacion al modelo." };
                if (icoCli32 != null) { btnCalcWindow.LargeImage = icoCli32; btnCalcWindow.Image = icoCli16; }
                rpMain.AddItem(btnCalcWindow);

                // ── Ajustos ──────────────────────────────────────────────────
                var icoAjustes32 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_panel_32.png"));
                var icoAjustes16 = RibbonHelper.CargarIcono(System.IO.Path.Combine(dllDir, "Resources", "icono_small_16.png"));
                var btnAjustes = new PushButtonData(
                    "BtnAjustes",
                    "E3G" + System.Environment.NewLine + "Ajustos",
                    dll,
                    "E3G.Saneamiento.CmdAbrirAjustes")
                {
                    ToolTip = "Obre la finestra d'ajustos: idioma, unitats, protocol de classificació…"
                };
                if (icoAjustes32 != null) { btnAjustes.LargeImage = icoAjustes32; btnAjustes.Image = icoAjustes16; }
                rpMain.AddItem(btnAjustes);

                var handlerPluvials = new EventoMedicionPluvials();
                SaneamientoSession.HandlerPluvials = handlerPluvials;
                SaneamientoSession.EventoPluvials = ExternalEvent.Create(handlerPluvials);

                var handlerGrises = new EventoMedicionGrises();
                SaneamientoSession.HandlerGrises = handlerGrises;
                SaneamientoSession.EventoGrises = ExternalEvent.Create(handlerGrises);

                SaneamientoSession.Handler = _handler;
                SaneamientoSession.Evento = _evento;

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("E3G Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        private static void AplicarColoresPaneles()
        {
            try
            {
                var adWindows = System.AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "AdWindows");
                if (adWindows == null) return;
                var ribbonType = adWindows.GetType("Autodesk.Windows.ComponentManager");
                var ribbon = ribbonType?.GetProperty("Ribbon",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public)?.GetValue(null);
                if (ribbon == null) return;
                var panelColors = new Dictionary<string, string>
                {
                    { "Saneamiento HS5", "#4a7a96" },
                    { "Fontanería",      "#1D9E75" },
                    { "Climatización",   "#5b9ec9" },
                    { "Ventilación",     "#EF9F27" },
                    { "Electricidad",    "#7F77DD" },
                    { "Contraincendios", "#E24B4A" },
                };
                var tabs = ribbon.GetType().GetProperty("Tabs")?.GetValue(ribbon)
                    as System.Collections.IEnumerable;
                if (tabs == null) return;
                foreach (var tab in tabs)
                {
                    var tabTitle = tab.GetType().GetProperty("Title")?.GetValue(tab)?.ToString();
                    if (tabTitle != "E3G BIM Tools") continue;
                    var panels = tab.GetType().GetProperty("Panels")?.GetValue(tab)
                        as System.Collections.IEnumerable;
                    if (panels == null) continue;
                    foreach (var panel in panels)
                    {
                        var src = panel.GetType().GetProperty("Source")?.GetValue(panel);
                        var title = src?.GetType().GetProperty("Title")?.GetValue(src)?.ToString();
                        if (title == null || !panelColors.ContainsKey(title)) continue;
                        var color = ParseHexColor(panelColors[title]);
                        var darkColor = System.Windows.Media.Color.FromRgb(
                            (byte)(color.R * 0.72),
                            (byte)(color.G * 0.72),
                            (byte)(color.B * 0.72));
                        var darkBrush = new System.Windows.Media.SolidColorBrush(darkColor);
                        panel.GetType()
                            .GetProperty("CustomPanelTitleBarBackground")
                            ?.SetValue(panel, darkBrush);
                    }
                    break;
                }
            }
            catch { }
        }

        private static System.Windows.Media.Color ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }
    }

    // =========================================================================
    // COMANDOS RIBBON — SANEAMIENTO
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CmdAbrirPanelMain : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try
            {
                var pane = cData.Application.GetDockablePane(App.PaneIdMain);
                if (!pane.IsShown()) pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdAbrirPanel : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try
            {
                var pane = cData.Application.GetDockablePane(App.PaneIdMain);
                if (!pane.IsShown()) pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }
    [Transaction(TransactionMode.Manual)]
    public class CmdAnalizarRibbon : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try { SaneamientoSession.Handler.Accion = AccionEvento.Analizar; SaneamientoSession.Evento.Raise(); return Result.Succeeded; }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdActualizarRibbon : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try { SaneamientoSession.Handler.Accion = AccionEvento.Actualizar; SaneamientoSession.Evento.Raise(); return Result.Succeeded; }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdRestaurarRibbon : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try { SaneamientoSession.Handler.Accion = AccionEvento.Restaurar; SaneamientoSession.Evento.Raise(); return Result.Succeeded; }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    // =========================================================================
    // COMANDOS RIBBON — FONTANERÍA
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CmdAbrirPanelFontaneria : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try
            {
                var pane = cData.Application.GetDockablePane(App.PaneIdFontaneria);
                if (!pane.IsShown()) pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMedirFontaneria : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        {
            try { FontaneriaSession.EventoMedicion?.Raise(); return Result.Succeeded; }
            catch (Exception ex) { msg = ex.Message; return Result.Failed; }
        }
    }

    // =========================================================================
    // COMANDOS PRÓXIMAMENTE
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CmdProximamente2 : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        { TaskDialog.Show("E3G BIM Tools", "Climatización — próximamente disponible."); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdProximamente3 : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        { TaskDialog.Show("E3G BIM Tools", "Ventilación — próximamente disponible."); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdProximamente4 : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        { TaskDialog.Show("E3G BIM Tools", "Electricidad — próximamente disponible."); return Result.Succeeded; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdProximamente5 : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els)
        { TaskDialog.Show("E3G BIM Tools", "Contraincendios — próximamente disponible."); return Result.Succeeded; }
    }

    // =========================================================================
    // ENUM Y EVENT HANDLERS
    // =========================================================================
    public enum AccionEvento { Analizar, Actualizar, Restaurar }

    public class EventoSaneamiento : IExternalEventHandler
    {
        public AccionEvento Accion { get; set; }
        public SaneamientoWindow Pagina { get; set; }

        public void Execute(UIApplication app)
        {
            // Captura defensiva — cualquier acción de saneamiento garantiza UiApp.
            SaneamientoSession.UiApp = app;

            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var vista = doc.ActiveView;
            switch (Accion)
            {
                case AccionEvento.Analizar: EjecutarAnalisis(uidoc, doc, vista); break;
                case AccionEvento.Actualizar: EjecutarActualizacion(doc, vista); break;
                case AccionEvento.Restaurar: EjecutarRestaurar(doc, vista); break;
            }
            Pagina?.Dispatcher.Invoke(() => Pagina.ActualizarTabla());
        }

        public string GetName() => "E3G Saneamiento HS5";

        private void EjecutarAnalisis(UIDocument uidoc, Document doc, View vista)
        {
            var selIds = uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                Pagina?.Dispatcher.Invoke(() =>
                    System.Windows.MessageBox.Show("Selecciona primero las tuberías en Revit.", "E3G Saneamiento"));
                return;
            }
            var resultados = new List<ResultadoTuberia>();
            foreach (var id in selIds)
            {
                Element e = doc.GetElement(id);
                if (e?.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeCurves) continue;
                string sysType = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
                if (!CteMotor.EsSistemaValido(sysType)) continue;
                double ud = e.LookupParameter("MC Discharge Unit Sum")?.AsDouble() ?? 0;
                string tamano = e.LookupParameter("Tamaño")?.AsValueString() ?? "ø0";
                var sysTypeId = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
                var sysTypeEl = sysTypeId != null ? doc.GetElement(sysTypeId) : null;
                string colorSys = sysTypeEl?.LookupParameter("Color de Sistema")?.AsValueString() ?? "127-127-127";
                int dnActual = CteMotor.ParseDN(tamano);
                int idx = CteMotor.GetIdx(ud);
                int dnCorrecto = CteMotor.CteDN[idx];
                resultados.Add(new ResultadoTuberia
                {
                    Id = id,
                    SysType = sysType,
                    UD = ud,
                    DNActual = dnActual,
                    DNCorrect = dnCorrecto,
                    IdxCte = idx,
                    EsCorrect = dnActual >= dnCorrecto,
                    ColorSys = colorSys,
                });
            }
            if (resultados.Count == 0)
            {
                Pagina?.Dispatcher.Invoke(() =>
                    System.Windows.MessageBox.Show("No se encontraron tuberías de saneamiento.", "E3G Saneamiento"));
                return;
            }
            using (Transaction tr = new Transaction(doc, "E3G HS5 — Analizar"))
            {
                tr.Start();
                foreach (var r in resultados)
                {
                    if (r.EsCorrect)
                    {
                        var (cr, cg, cb) = CteMotor.ParseColorSistema(r.ColorSys);
                        CteMotor.AplicarColor(vista, r.Id, cr, cg, cb);
                    }
                    else
                        CteMotor.AplicarColor(vista, r.Id,
                            CteMotor.CteR[r.IdxCte], CteMotor.CteG[r.IdxCte], CteMotor.CteB[r.IdxCte]);
                }
                tr.Commit();
            }
            Sesion.Resultados = resultados;
        }

        private void EjecutarActualizacion(Document doc, View vista)
        {
            if (Sesion.Resultados == null || Sesion.Resultados.Count == 0) return;
            var actualizados = new List<ResultadoTuberia>();
            foreach (var prev in Sesion.Resultados)
            {
                Element e = doc.GetElement(prev.Id);
                if (e == null) continue;
                double ud = e.LookupParameter("MC Discharge Unit Sum")?.AsDouble() ?? 0;
                string tamano = e.LookupParameter("Tamaño")?.AsValueString() ?? "ø0";
                int dnActual = CteMotor.ParseDN(tamano);
                int idx = CteMotor.GetIdx(ud);
                int dnCorrecto = CteMotor.CteDN[idx];
                actualizados.Add(new ResultadoTuberia
                {
                    Id = prev.Id,
                    SysType = prev.SysType,
                    UD = ud,
                    DNActual = dnActual,
                    DNCorrect = dnCorrecto,
                    IdxCte = idx,
                    EsCorrect = dnActual >= dnCorrecto,
                    ColorSys = prev.ColorSys,
                });
            }
            using (Transaction tr = new Transaction(doc, "E3G HS5 — Actualizar"))
            {
                tr.Start();
                foreach (var r in actualizados)
                {
                    if (r.EsCorrect)
                    {
                        var (cr, cg, cb) = CteMotor.ParseColorSistema(r.ColorSys);
                        CteMotor.AplicarColor(vista, r.Id, cr, cg, cb);
                    }
                    else
                        CteMotor.AplicarColor(vista, r.Id,
                            CteMotor.CteR[r.IdxCte], CteMotor.CteG[r.IdxCte], CteMotor.CteB[r.IdxCte]);
                }
                tr.Commit();
            }
            Sesion.Resultados = actualizados;
        }

        private void EjecutarRestaurar(Document doc, View vista)
        {
            if (Sesion.Resultados == null || Sesion.Resultados.Count == 0) return;
            using (Transaction tr = new Transaction(doc, "E3G HS5 — Restaurar colores"))
            {
                tr.Start();
                foreach (var r in Sesion.Resultados)
                {
                    var (cr, cg, cb) = CteMotor.ParseColorSistema(r.ColorSys);
                    CteMotor.AplicarColor(vista, r.Id, cr, cg, cb);
                }
                tr.Commit();
            }
        }
    }

    public class EventoMedicion : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }
        public ResumenMediciones Resultado { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            Resultado = MedicionMotor.Calcular(doc);
            Pagina?.Dispatcher.Invoke(() => Pagina.ActualizarMediciones(Resultado));
        }

        public string GetName() => "E3G Mediciones HS5";
    }

    public class EventoEsquemaPrincipi : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var pipeIds = uidoc.Selection.GetElementIds()
                .Select(id => id.IntegerValue)
                .ToList();

            Pagina?.Dispatcher.Invoke(() =>
                Pagina.IniciarEsquema(doc, pipeIds));
        }

        public string GetName() => "E3G Esquema Principi";
    }

    public class EventoInserirEsquema : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            // Obtenir la xarxa des del control
            var xarxa = Pagina?.tabEsquema?.XarxaActual;
            if (xarxa == null) return;

            // ---- FASE 3: SchematicGeneratorService ----
            var prm = SchematicParameters.Default();
            // Adaptar la normativa si el sistema és de fontaneria
            if (xarxa.Sistema?.ToLower().Contains("fon") == true ||
                xarxa.Sistema?.ToLower().Contains("af") == true)
                prm = SchematicParameters.ForFontaneria();

            var result = SchematicGeneratorService.Generate(doc, xarxa, prm,
                exportDwg: false);

            int vid = result.ViewId;

            // Obrir la vista
            if (vid > 0)
            {
                var viewEl = doc.GetElement(new ElementId(vid)) as View;
                if (viewEl != null)
                    uidoc.ActiveView = viewEl;
            }

            Pagina?.Dispatcher.Invoke(() =>
                Pagina.tabEsquema?.NotificarInsercioCompletada(vid));
        }

        public string GetName() => "E3G Inserir Esquema";
    }

    public class EventoMedicionPluvials : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }
        public ResumenPluvials Resultado { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            Resultado = PluvialsMotor.Calcular(doc);
            Sesion.UltimesPluvials = Resultado;
            Pagina?.Dispatcher.Invoke(() => Pagina.ActualizarPluvials(Resultado));
        }

        public string GetName() => "E3G Medicions Pluvials";
    }

    public class EventoMedicionGrises : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }
        public ResumenGrises Resultado { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            Resultado = GrisesMotor.Calcular(doc);
            Sesion.UltimesGrises = Resultado;
            Pagina?.Dispatcher.Invoke(() => Pagina.ActualizarGrises(Resultado));
        }

        public string GetName() => "E3G Medicions Grises";
    }

    public class EventoMedicionFontaneria : IExternalEventHandler
    {
        public FontaneriaWindow Pagina { get; set; }
        public ResumenFontaneria Resultado { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            Resultado = FontaneriaMotor.Calcular(doc);
            Pagina?.Dispatcher.Invoke(() => Pagina.ActualizarDatos(Resultado));
        }

        public string GetName() => "E3G Medición Fontanería";
    }

    // =========================================================================
    // COMANDOS LEGACY
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CmdAnalizar : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdActualizar : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els) => Result.Succeeded;
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdRestaurar : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string msg, ElementSet els) => Result.Succeeded;
    }
}
