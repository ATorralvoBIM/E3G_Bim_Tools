// revit_mcp_plugin/codigo/Protocol/ProtocolAutoDetect.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Genera un protocol inicial amb regles suggerides llegint el .rvt actual.
    /// L'usuari el revisa i ajusta — no s'aplica mai sense confirmació.
    /// </summary>
    public static class ProtocolAutoDetect
    {
        public static ProtocolInstalacio Suggerir(Document doc)
        {
            var vivendes = SuggerirVivendes(doc);
            var aparcament = SuggerirAparcament(doc);
            var bajants = SuggerirBajants();
            var colectors = SuggerirColectors(doc);

            return new ProtocolInstalacio
            {
                Instalacio = "Sanejament",
                Vivendes = vivendes,
                Aparcament = aparcament,
                Bajants = bajants,
                Colectors = colectors,
                BajantLongitudMinM = 1.0,
                ToleranciaElevacioM = 0.5
            };
        }

        // ── Vivendes: keyword matching sobre nom de GroupType ────────────────
        private static ProtocolSeccio SuggerirVivendes(Document doc)
        {
            // Keywords multi-idioma que solen indicar vivienda
            var keywords = new[] { "VIV", "VIVENDA", "VIVIENDA", "APT", "PIS",
                                   "FLAT", "DWELLING", "T1_", "T2_", "T3_",
                                   "1D_", "2D_", "3D_", "_VIV", "_PIS" };

            // Troba el patró comú que matcheja més GroupTypes del projecte
            var groupTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .Select(gt => gt.Name.ToUpperInvariant())
                .ToList();

            if (!groupTypes.Any())
                return SeccioAmbKeywordGeneric("Interior vivendes", "VIV");

            // Selecciona la keyword que cobreix més noms
            var millor = keywords
                .OrderByDescending(k => groupTypes.Count(n => n.Contains(k)))
                .FirstOrDefault(k => groupTypes.Any(n => n.Contains(k)));

            return millor != null
                ? SeccioAmbKeyword("Interior vivendes", "Type Name", FilterOperator.Conte, millor)
                : SeccioAmbKeywordGeneric("Interior vivendes", "VIV");
        }

        // ── Aparcament: elevació < 0 o nom comença per S ─────────────────────
        private static ProtocolSeccio SuggerirAparcament(Document doc)
        {
            var regles = new List<ProtocolRegla>();

            // Detecta si hi ha nivells amb nom tipus S01, S01.A, Sotano...
            var nivells = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => l.Name)
                .ToList();

            bool haSotano = nivells.Any(n =>
                n.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("B", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("SOTAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("BASEMENT", StringComparison.OrdinalIgnoreCase) >= 0);

            if (haSotano)
            {
                regles.Add(new ProtocolRegla
                {
                    Nom = "Nivells subterranis (S*.A)",
                    Aplica = ElementTipus.Nivell,
                    Logica = LogicaCondicions.Totes,
                    Condicions = new List<FilterCondicio>
                    {
                        new() { ParamNom = "Name", Operador = FilterOperator.ComencaPer, Valor = "S" }
                    }
                });
            }
            else
            {
                // Fallback: elevació negativa
                regles.Add(new ProtocolRegla
                {
                    Nom = "Nivells amb elevació negativa",
                    Aplica = ElementTipus.Nivell,
                    Logica = LogicaCondicions.Totes,
                    Condicions = new List<FilterCondicio>
                    {
                        new() { ParamNom = ParamVirtual.ElevacioNivell_M,
                                Operador = FilterOperator.MenorQue, Valor = "0" }
                    }
                });
            }

            return new ProtocolSeccio { Etiqueta = "Aparcament", Regles = regles };
        }

        // ── Bajants: vertical + longitud >= 1m ──────────────────────────────
        private static ProtocolSeccio SuggerirBajants()
        {
            var regla = new ProtocolRegla
            {
                Nom = "Bajants (verticals ≥ 1m)",
                Aplica = ElementTipus.Canonada,
                Logica = LogicaCondicions.Totes,
                Condicions = new List<FilterCondicio>
                {
                    new() { ParamNom = ParamVirtual.Vertical,  Operador = FilterOperator.EsIgualA, Valor = "true" },
                    new() { ParamNom = ParamVirtual.LongitudM, Operador = FilterOperator.MajorIgualQue, Valor = "1" }
                }
            };
            return new ProtocolSeccio { Etiqueta = "Bajants", Regles = new List<ProtocolRegla> { regla } };
        }

        // ── Col·lectors: horitzontals entre P00 i P01 ───────────────────────
        private static ProtocolSeccio SuggerirColectors(Document doc)
        {
            // Intenta trobar P00.A i P01.A (o equivalents)
            var nivells = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            // Nivell base (elevació >= 0 més baixa)
            var base0 = nivells.FirstOrDefault(l => l.Elevation >= -0.5);
            var base1 = nivells.FirstOrDefault(l => l.Elevation > (base0?.Elevation ?? 0) + 0.5);

            var condicions = new List<FilterCondicio>
            {
                new() { ParamNom = ParamVirtual.Horitzontal, Operador = FilterOperator.EsIgualA, Valor = "true" }
            };

            if (base0 != null && base1 != null)
            {
                var elev0M = Math.Round(base0.Elevation * 0.3048, 2);
                var elev1M = Math.Round(base1.Elevation * 0.3048, 2);
                condicions.Add(new FilterCondicio
                {
                    ParamNom = ParamVirtual.ElevacioNivell_M,
                    Operador = FilterOperator.MajorIgualQue,
                    Valor = elev0M.ToString("G")
                });
                condicions.Add(new FilterCondicio
                {
                    ParamNom = ParamVirtual.ElevacioNivell_M,
                    Operador = FilterOperator.MenorIgualQue,
                    Valor = elev1M.ToString("G")
                });
            }

            var regla = new ProtocolRegla
            {
                Nom = "Col·lectors P00-P01",
                Aplica = ElementTipus.Canonada,
                Logica = LogicaCondicions.Totes,
                Condicions = condicions
            };
            return new ProtocolSeccio
            {
                Etiqueta = "Col·lectors P00-P01",
                Regles = new List<ProtocolRegla> { regla }
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static ProtocolSeccio SeccioAmbKeyword(
            string etiqueta, string param, FilterOperator op, string valor)
        {
            return new ProtocolSeccio
            {
                Etiqueta = etiqueta,
                Regles = new List<ProtocolRegla>
                {
                    new()
                    {
                        Nom    = $"Regla automàtica ({valor})",
                        Aplica = ElementTipus.GrupTipus,
                        Logica = LogicaCondicions.Totes,
                        Condicions = new List<FilterCondicio>
                        {
                            new() { ParamNom = param, Operador = op, Valor = valor }
                        }
                    }
                }
            };
        }

        private static ProtocolSeccio SeccioAmbKeywordGeneric(string etiqueta, string keyword)
            => SeccioAmbKeyword(etiqueta, "Type Name", FilterOperator.Conte, keyword);
    }
}