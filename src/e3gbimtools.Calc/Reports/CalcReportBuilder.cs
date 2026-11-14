// ============================================================================
//  CalcReportBuilder.cs
//  Reports — Construye la MEMORIA DE CÁLCULO (ReportData) a partir del informe
//  del motor, los resultados por tramo y los datos de partida.
//
//  Objetivo: que cada número del motor sea VERIFICABLE a mano. Por eso el
//  apartado central es la "justificación paso a paso" del camino crítico:
//  cada tramo se desarrolla con la fórmula, los valores sustituidos y el
//  resultado (A, v, Re, ε/D, f, ΔP/m, singulares, ΔP_tramo).
//
//  Desacoplado de los tipos de Revit y de PipeSegment/DuctSegment: el llamante
//  aporta, por RevitId, los datos geométricos del tramo (SegmentCalcInputs).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using e3gbimtools.Calc.Core.Analytics;
using e3gbimtools.Calc.Core.Common;

namespace e3gbimtools.Calc.Reports
{
    /// <summary>Datos geométricos/de cálculo de un tramo, por RevitId.</summary>
    public sealed class SegmentCalcInputs
    {
        public double DiameterM;        // diámetro interior [m] (0 si no aplica, p.ej. rectangular)
        public double LengthM;          // longitud del tramo [m]
        public double RoughnessM;       // rugosidad absoluta ε [m]
        public double RawFlowM3s;       // caudal bruto (sin simultaneidad) [m³/s]
        public double DesignFlowM3s;    // caudal de diseño (con K) [m³/s]
        public double SimultaneityK = 1.0;
        public int    DownstreamFixtures;
        public string MaterialKey = "";
        /// <summary>Accesorios del tramo: etiqueta + K.</summary>
        public List<KeyValuePair<string, double>> Fittings = new List<KeyValuePair<string, double>>();
    }

    /// <summary>Metadatos de portada de la memoria.</summary>
    public sealed class CalcReportMeta
    {
        public string Discipline  = "Hidráulica";   // o "Aeráulica"
        public string ProjectName = "";
        public string ProjectCode = "";
        public string ClientName  = "";
        public string Author      = "";
        public string AuthorRole  = "";
        public string EngineVersion = "";
        public bool   IsHydraulic = true;
    }

    public static class CalcReportBuilder
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        public static ReportData Build(
            CalcReportMeta meta,
            CalcReport report,
            IList<ResultRow> rows,
            FluidProperties fluid,
            SizingCriteriaConfig criteria,
            Func<long, SegmentCalcInputs> inputsOf)
        {
            if (meta == null)   meta   = new CalcReportMeta();
            if (report == null) report = new CalcReport();
            if (rows == null)   rows   = new List<ResultRow>();

            string flowUnit = "L/s";
            var data = new ReportData();

            // -------------------- Portada --------------------
            data.Cover = new ReportCover
            {
                Title       = "Memoria de cálculo — " + meta.Discipline,
                Subtitle    = string.IsNullOrEmpty(report.SystemName) ? "" : "Sistema: " + report.SystemName,
                ProjectName = meta.ProjectName,
                ProjectCode = meta.ProjectCode,
                ClientName  = meta.ClientName,
                AuthorName  = meta.Author,
                AuthorRole  = meta.AuthorRole,
                IssueDate   = DateTime.Now,
                Norm        = StandardLabel(criteria?.Standard ?? SizingStandard.Custom),
            };

            // -------------------- 1. Objeto --------------------
            data.Sections.Add(new ReportSection
            {
                Numbering = "1",
                Title = "Objeto",
                PageBreakBefore = false,
                Paragraphs =
                {
                    "La presente memoria justifica el dimensionado " + meta.Discipline.ToLowerInvariant()
                        + " del sistema «" + (report.SystemName ?? "(sin nombre)") + "», calculado con el "
                        + "motor MEP de e3gbimtools" + (string.IsNullOrEmpty(meta.EngineVersion) ? "" : " v" + meta.EngineVersion)
                        + ". Se recogen los datos de partida, el método y normativa aplicados, los "
                        + "resultados por tramo y el desarrollo de cálculo del recorrido más desfavorable "
                        + "para permitir su verificación.",
                },
            });

            // -------------------- 2. Normativa y método --------------------
            var metodo = new ReportSection
            {
                Numbering = "2",
                Title = "Normativa y método de cálculo",
                PageBreakBefore = false,
            };
            metodo.Paragraphs.Add("Normativa de referencia: " + StandardLabel(criteria?.Standard ?? SizingStandard.Custom)
                + (criteria?.Description != null ? "  (" + criteria.Description + ")" : "") + ".");
            metodo.Paragraphs.Add("Pérdida de carga lineal (Darcy-Weisbach):  ΔP/L = f · (1/D) · ½ · ρ · v²");
            metodo.Paragraphs.Add("Factor de fricción f (Colebrook-White, régimen turbulento):  "
                + "1/√f = −2·log₁₀( (ε/D)/3,7 + 2,51/(Re·√f) )");
            metodo.Paragraphs.Add("Número de Reynolds:  Re = ρ · v · D / μ");
            metodo.Paragraphs.Add("Pérdidas singulares (accesorios):  ΔP = Σ Kᵢ · ½ · ρ · v²");
            metodo.Paragraphs.Add("Velocidad:  v = Q / A,  con  A = π · D² / 4");
            if ((criteria?.SimultaneityProfile ?? SimultaneityProfile.None) != SimultaneityProfile.None)
                metodo.Paragraphs.Add("Caudal de diseño:  Q_diseño = Q_bruto · K(n),  factor de simultaneidad K "
                    + "según perfil «" + SimultaneityLabel(criteria.SimultaneityProfile) + "» (n = aparatos aguas abajo).");
            metodo.Paragraphs.Add("Presión en cada nudo:  P_aguas-abajo = P_aguas-arriba − ΔP_tramo − ρ·g·Δz.");
            data.Sections.Add(metodo);

            // -------------------- 3. Datos de partida --------------------
            var datos = new ReportSection
            {
                Numbering = "3",
                Title = "Datos de partida y criterios",
                PageBreakBefore = false,
            };
            if (fluid != null)
            {
                datos.Criteria.Add(C("Fluido", fluid.Label));
                datos.Criteria.Add(C("Temperatura", N(fluid.TemperatureC, 1), "°C"));
                datos.Criteria.Add(C("Densidad ρ", N(fluid.Density, 1), "kg/m³"));
                datos.Criteria.Add(C("Viscosidad dinámica μ", fluid.DynamicViscosity.ToString("0.000e-0", Es), "Pa·s"));
            }
            if (criteria != null)
            {
                datos.Criteria.Add(C("Criterio de dimensionado",
                    criteria.Criterion == SizingCriteria.MaxVelocity ? "Velocidad máxima"
                    : criteria.Criterion == SizingCriteria.MaxFriction ? "Pérdida unitaria máxima" : "Económico"));
                if (criteria.MaxVelocityMs > 0) datos.Criteria.Add(C("Velocidad máxima", N(criteria.MaxVelocityMs, 2), "m/s"));
                if (criteria.MinVelocityMs > 0) datos.Criteria.Add(C("Velocidad mínima", N(criteria.MinVelocityMs, 2), "m/s"));
                if (criteria.MaxFrictionPaPm > 0) datos.Criteria.Add(C("Pérdida unitaria máxima", N(criteria.MaxFrictionPaPm, 0), "Pa/m"));
                datos.Criteria.Add(C("Simultaneidad", SimultaneityLabel(criteria.SimultaneityProfile)));
            }
            datos.Criteria.Add(C("Presión en fuente", N(report.SourcePressurePa / 1000.0, 1), "kPa"));
            data.Sections.Add(datos);

            // -------------------- 4. Resultados por tramo --------------------
            var pipeRows = rows.Where(r => r != null && !r.IsFitting).ToList();
            var tabla = new ReportSection
            {
                Numbering = "4",
                Title = "Resultados por tramo",
                PageBreakBefore = true,
            };
            var table = new ReportTable { RepeatHeader = true };
            table.Columns.Add(Col("Tramo", 0.7f, ReportAlign.Center));
            table.Columns.Add(Col("Nudos", 1.2f));
            table.Columns.Add(Col("DN", 0.8f, ReportAlign.Center));
            table.Columns.Add(Col("Q (" + flowUnit + ")", 0.9f, ReportAlign.Right));
            table.Columns.Add(Col("v (m/s)", 0.8f, ReportAlign.Right));
            table.Columns.Add(Col("ΔP/m (Pa/m)", 1.0f, ReportAlign.Right));
            table.Columns.Add(Col("ΔP (Pa)", 0.9f, ReportAlign.Right));
            table.Columns.Add(Col("DN sug.", 0.8f, ReportAlign.Center));
            table.Columns.Add(Col("Estado", 1.4f));
            foreach (var r in pipeRows)
            {
                table.Rows.Add(new ReportTableRow
                {
                    Severity = SeverityOf(r.StatusKind),
                    Cells = new List<string>
                    {
                        r.BfsIndex > 0 ? r.BfsIndex.ToString(Es) : "",
                        r.NodePath ?? "",
                        r.Size ?? "",
                        r.FlowLps.ToString("0.00", Es),
                        r.Velocity.ToString("0.00", Es),
                        r.FrictionPaPm.ToString("0", Es),
                        r.TotalLoss.ToString("0", Es),
                        r.RecommendedSize ?? "",
                        string.IsNullOrEmpty(r.Status) ? "" : r.Status,
                    },
                });
            }
            tabla.Table = table;
            data.Sections.Add(tabla);

            // -------------------- 5. Camino crítico --------------------
            var cp = new CriticalPathService().Analyze(rows);
            var cpSection = new ReportSection
            {
                Numbering = "5",
                Title = "Camino crítico",
                PageBreakBefore = true,
            };
            cpSection.Paragraphs.Add("Recorrido más desfavorable desde la fuente hasta el terminal con mayor "
                + "pérdida de carga acumulada. Es el que condiciona la presión disponible / la altura de bomba.");
            if (cp != null && cp.Path != null && cp.Path.Count > 0)
            {
                var cpBlock = new ReportCriticalPath
                {
                    Summary = "ΔP acumulada peor caso: " + N(cp.TotalDeltaP / 1000.0, 2) + " kPa  ("
                        + N(cp.TotalDeltaP, 0) + " Pa)",
                };
                int order = 1;
                foreach (var r in cp.Path)
                {
                    double acc = cp.AccumulatedAt != null && cp.AccumulatedAt.TryGetValue(r, out var a) ? a : 0.0;
                    cpBlock.Steps.Add(new ReportCriticalPathStep
                    {
                        Order   = order++,
                        Element = (r.BfsIndex > 0 ? "Tramo " + r.BfsIndex + "  " : "") + (r.NodePath ?? ""),
                        Size    = r.Size ?? "",
                        FlowLps = r.FlowLps.ToString("0.00", Es),
                        DpPa    = r.TotalLoss.ToString("0", Es),
                        AccPa   = N(acc, 0),
                    });
                }
                cpSection.CriticalPath = cpBlock;
            }
            else
            {
                cpSection.Paragraphs.Add("No se ha podido determinar el camino crítico (red sin terminales válidos).");
            }
            data.Sections.Add(cpSection);

            // -------------------- 6. Justificación paso a paso --------------------
            if (cp != null && cp.Path != null && cp.Path.Count > 0 && fluid != null)
            {
                var pasos = new ReportSection
                {
                    Numbering = "6",
                    Title = "Justificación paso a paso (camino crítico)",
                    PageBreakBefore = true,
                };
                pasos.Paragraphs.Add("Desarrollo del cálculo de cada tramo del recorrido más desfavorable. "
                    + "Los valores entre paréntesis son los datos sustituidos en cada fórmula.");
                double g = 9.80665;
                foreach (var r in cp.Path)
                {
                    CalcResult cr = null;
                    report.Segments.TryGetValue(r.RevitId, out cr);
                    SegmentCalcInputs inp = inputsOf != null ? inputsOf(r.RevitId) : null;
                    AppendSegmentSteps(pasos.Paragraphs, r, cr, inp, fluid, criteria, g);
                }
                data.Sections.Add(pasos);
            }

            // -------------------- 7. Conclusiones --------------------
            int incumplen = pipeRows.Count(r => r.StatusKind == "Warning" || r.StatusKind == "Error" || r.StatusKind == "Disconnected");
            int cortes = pipeRows.Count(r => r.Disconnected);
            var concl = new ReportSection
            {
                Numbering = "7",
                Title = "Conclusiones",
                PageBreakBefore = false,
            };
            concl.Criteria.Add(C("Tramos calculados", pipeRows.Count.ToString(Es)));
            concl.Criteria.Add(C("ΔP peor caso", N(report.WorstCasePressureDropPa / 1000.0, 2), "kPa"));
            concl.Criteria.Add(C("Presión mínima en terminal", N(report.MinTerminalPressurePa / 1000.0, 2), "kPa"));
            concl.Criteria.Add(C("Tramos con incidencia", incumplen.ToString(Es)));
            if (cortes > 0)
                concl.Criteria.Add(C("Cortes (desconexión)", cortes.ToString(Es), "", "Revisar continuidad de la red"));
            concl.Paragraphs.Add(incumplen == 0
                ? "Todos los tramos cumplen los criterios establecidos."
                : "Existen " + incumplen + " tramo(s) con incidencias (velocidad/rozamiento fuera de límite o desconexión). "
                  + "Véase la columna «Estado» de la tabla de resultados.");
            data.Sections.Add(concl);

            return data;
        }

        // ------------------------------------------------------------------ //
        //  Desarrollo paso a paso de un tramo                                  //
        // ------------------------------------------------------------------ //
        private static void AppendSegmentSteps(
            List<string> outp, ResultRow r, CalcResult cr, SegmentCalcInputs inp,
            FluidProperties fluid, SizingCriteriaConfig criteria, double g)
        {
            string header = "── " + (r.BfsIndex > 0 ? "Tramo " + r.BfsIndex + "  " : "") + (r.NodePath ?? "")
                + (string.IsNullOrEmpty(r.Size) ? "" : "   " + r.Size);
            if (inp != null && inp.LengthM > 0) header += "   ·   L = " + N(inp.LengthM, 2) + " m";
            if (inp != null && !string.IsNullOrEmpty(inp.MaterialKey)) header += "   ·   " + inp.MaterialKey;
            outp.Add(header);

            double rho = fluid.Density;
            double mu  = fluid.DynamicViscosity;
            double v   = cr != null ? cr.VelocityMs : r.Velocity;

            // Caudal de diseño
            if (inp != null && inp.SimultaneityK > 0 && inp.SimultaneityK < 1.0)
                outp.Add("   Q_diseño = Q_bruto · K = " + N(inp.RawFlowM3s * 1000.0, 3) + " · " + N(inp.SimultaneityK, 3)
                    + " = " + N(inp.DesignFlowM3s * 1000.0, 3) + " L/s   (n = " + inp.DownstreamFixtures + " aparatos)");

            // Área y velocidad
            if (inp != null && inp.DiameterM > 0)
            {
                double area = Math.PI * inp.DiameterM * inp.DiameterM / 4.0;
                double qd   = inp.DesignFlowM3s > 0 ? inp.DesignFlowM3s : (cr != null ? cr.FlowM3s : 0.0);
                outp.Add("   A = π·D²/4 = π·(" + N(inp.DiameterM, 4) + ")²/4 = " + area.ToString("0.000000", Es) + " m²");
                outp.Add("   v = Q/A = " + N(qd * 1000.0, 3) + "e-3 / " + area.ToString("0.000000", Es)
                    + " = " + N(v, 3) + " m/s" + LimitV(criteria, v));
            }
            else
            {
                outp.Add("   v = Q/A = " + N(v, 3) + " m/s" + LimitV(criteria, v));
            }

            // Reynolds
            if (inp != null && inp.DiameterM > 0)
                outp.Add("   Re = ρ·v·D/μ = " + N(rho, 1) + "·" + N(v, 3) + "·" + N(inp.DiameterM, 4) + "/" + mu.ToString("0.000e-0", Es)
                    + " = " + N(cr != null ? cr.ReynoldsNumber : r.Reynolds, 0));
            else
                outp.Add("   Re = " + N(cr != null ? cr.ReynoldsNumber : r.Reynolds, 0));

            // Rugosidad relativa y f
            if (inp != null && inp.DiameterM > 0 && inp.RoughnessM > 0)
                outp.Add("   ε/D = " + (inp.RoughnessM * 1000.0).ToString("0.000", Es) + " mm / " + N(inp.DiameterM * 1000.0, 1)
                    + " mm = " + (inp.RoughnessM / inp.DiameterM).ToString("0.00000", Es));
            if (cr != null && cr.FrictionFactor > 0)
                outp.Add("   f = Colebrook-White(Re, ε/D) = " + N(cr.FrictionFactor, 4));

            // ΔP/m
            double dppm = cr != null ? cr.FrictionPaPm : r.FrictionPaPm;
            if (inp != null && inp.DiameterM > 0 && cr != null)
                outp.Add("   ΔP/m = f·(1/D)·½·ρ·v² = " + N(cr.FrictionFactor, 4) + "·(1/" + N(inp.DiameterM, 4) + ")·½·"
                    + N(rho, 1) + "·" + N(v, 3) + "² = " + N(dppm, 1) + " Pa/m" + LimitF(criteria, dppm));
            else
                outp.Add("   ΔP/m = " + N(dppm, 1) + " Pa/m" + LimitF(criteria, dppm));

            // Singulares
            double singular = cr != null ? cr.SingularLossPa : 0.0;
            if (inp != null && inp.Fittings != null && inp.Fittings.Count > 0)
            {
                double sumK = inp.Fittings.Sum(kv => kv.Value);
                string detalle = string.Join(", ", inp.Fittings.Select(kv => kv.Key + " K=" + N(kv.Value, 2)));
                outp.Add("   ΔP_sing = ΣK·½·ρ·v² = " + N(sumK, 2) + "·½·" + N(rho, 1) + "·" + N(v, 3) + "² = "
                    + N(singular, 1) + " Pa   [" + detalle + "]");
            }
            else if (singular > 0)
            {
                outp.Add("   ΔP_sing = " + N(singular, 1) + " Pa");
            }

            // Total del tramo
            double linear = cr != null ? cr.LinearLossPa : (dppm * (inp != null ? inp.LengthM : 0.0));
            double total  = cr != null ? cr.TotalLossPa : (linear + singular);
            if (inp != null && inp.LengthM > 0)
                outp.Add("   ΔP_tramo = ΔP/m·L + ΔP_sing = " + N(dppm, 1) + "·" + N(inp.LengthM, 2) + " + " + N(singular, 1)
                    + " = " + N(total, 0) + " Pa");
            else
                outp.Add("   ΔP_tramo = " + N(total, 0) + " Pa");

            outp.Add(""); // separador
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                             //
        // ------------------------------------------------------------------ //
        private static string LimitV(SizingCriteriaConfig c, double v)
        {
            if (c == null || c.MaxVelocityMs <= 0) return "";
            return "   [límite " + N(c.MaxVelocityMs, 2) + " m/s → " + (v <= c.MaxVelocityMs + 1e-9 ? "CUMPLE" : "NO CUMPLE") + "]";
        }

        private static string LimitF(SizingCriteriaConfig c, double dppm)
        {
            if (c == null || c.Criterion != SizingCriteria.MaxFriction || c.MaxFrictionPaPm <= 0) return "";
            return "   [límite " + N(c.MaxFrictionPaPm, 0) + " Pa/m → " + (dppm <= c.MaxFrictionPaPm + 1e-9 ? "CUMPLE" : "NO CUMPLE") + "]";
        }

        private static ReportRowSeverity SeverityOf(string statusKind)
        {
            switch (statusKind)
            {
                case "OK":           return ReportRowSeverity.Ok;
                case "Warning":      return ReportRowSeverity.Warning;
                case "Error":        return ReportRowSeverity.Error;
                case "Disconnected": return ReportRowSeverity.Error;
                default:             return ReportRowSeverity.None;
            }
        }

        private static string StandardLabel(SizingStandard s)
        {
            switch (s)
            {
                case SizingStandard.CTE_HS4: return "CTE DB-HS4";
                case SizingStandard.RITE:    return "RITE";
                case SizingStandard.ASHRAE:  return "ASHRAE";
                default:                     return "Criterios personalizados";
            }
        }

        private static string SimultaneityLabel(SimultaneityProfile p)
        {
            switch (p)
            {
                case SimultaneityProfile.Residential: return "Vivienda (K = 1/√(n−1))";
                case SimultaneityProfile.Office:      return "Oficinas (K = 1/√(n−1), mín. 0,20)";
                case SimultaneityProfile.Hospital:    return "Hospital (K = 1,7/√n)";
                case SimultaneityProfile.Hotel:       return "Hotel (K = 1/√(n−1), mín. 0,25)";
                case SimultaneityProfile.Commercial:  return "Comercial (K = 1,5/√n)";
                default:                              return "Sin simultaneidad (suma directa)";
            }
        }

        private static ReportCriterion C(string key, string value, string unit = "", string note = "")
            => new ReportCriterion { Key = key, Value = value, Unit = unit, Note = note };

        private static ReportColumn Col(string header, float weight, ReportAlign align = ReportAlign.Left)
            => new ReportColumn { Header = header, Weight = weight, Align = align };

        private static string N(double v, int decimals)
            => v.ToString("F" + decimals, Es);
    }
}
