// ============================================================================
//  PipeSizingApplier.cs
//  RevitBridge — Aplica al modelo el dimensionado sugerido por el cálculo.
//
//  Flujo de trabajo (upgrade "Aplicar dimensionado", mayo 2026):
//    1. El cálculo hidráulico marca tramos con VelocityExceeded / FrictionExceeded
//       y propone una talla (CalcResult.RecommendedSize, p.ej. "DN50").
//    2. Este servicio traduce esa talla a un diámetro interior objetivo y elige
//       la SIGUIENTE talla disponible EN EL CATÁLOGO DEL TIPO Revit de la propia
//       tubería (RoutingPreferenceManager → PipeSegment.GetSizes()) que cumpla.
//    3. Preview() devuelve el plan sin tocar el modelo; Apply() lo ejecuta dentro
//       de una Transaction con SubTransaction por tubería (aislamiento de fallos).
//
//  Catálogo del tipo Revit, lote completo, solo tuberías (fase 1).
//  Las tallas/diámetros de Revit están en PIES; convertimos a mm/m para comparar.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using RvtPipeSegment = Autodesk.Revit.DB.Plumbing.PipeSegment;

namespace e3gbimtools.Calc.RevitBridge
{
    /// <summary>
    /// Calcula y aplica el redimensionado de tuberías que incumplen normativa,
    /// usando el catálogo de tallas del propio tipo de tubería en Revit.
    /// </summary>
    public sealed class PipeSizingApplier
    {
        private const double FeetToMm = 304.8;
        private const double FeetToM  = 0.3048;
        private const double TolFeet  = 1e-6;

        /// <summary>Resumen de un cambio propuesto / aplicado sobre una tubería.</summary>
        public sealed class Change
        {
            public long   ElementId         { get; set; }
            public string TramoLabel        { get; set; }   // texto para el usuario
            public double FromMm            { get; set; }   // diámetro nominal actual
            public double ToMm              { get; set; }   // diámetro nominal propuesto
            public double TargetNominalFeet { get; set; }   // valor a escribir en el parámetro
            public string RecommendedLabel  { get; set; }   // talla del cálculo ("DN50")
            public bool   WillChange        { get; set; }   // hay redimensionado real
            public bool   IsDownsize        { get; set; }   // true = reducir (sobredimensionada)
            public bool   Applied           { get; set; }   // se escribió al modelo
            public string Note              { get; set; }   // motivo si no cambia / error
        }

        /// <summary>Nº de tuberías que efectivamente cambiaron al aplicar.</summary>
        public int Applied { get; private set; }

        /// <summary>Nº de tramos saltados (sin tubería, sin talla, sin catálogo…).</summary>
        public int Skipped { get; private set; }

        /// <summary>Nº de fittings (codos, tés, cruces…) cuyo diámetro se alineó
        /// al de la mayor tubería conectada tras redimensionar.</summary>
        public int FittingsAligned { get; private set; }

        // ------------------------------------------------------------------ //
        //  Preview — construye el plan sin modificar el modelo                 //
        // ------------------------------------------------------------------ //
        public List<Change> Preview(Document doc, CalcReport report)
            => BuildPlan(doc, report);

        // ------------------------------------------------------------------ //
        //  Apply — ejecuta el plan dentro de una Transaction                   //
        // ------------------------------------------------------------------ //
        public List<Change> Apply(Document doc, CalcReport report, string transactionName)
        {
            Applied = 0;
            Skipped = 0;
            FittingsAligned = 0;

            var plan = BuildPlan(doc, report);
            var toChange = plan.Where(c => c.WillChange).ToList();

            // Conjunto de tuberías del sistema calculado: acota el alineado de
            // fittings a la red en cuestión (no a todo el modelo).
            var systemPipeIds = new HashSet<long>();
            if (report != null)
                foreach (var kv in report.Segments)
                    if (kv.Value != null) systemPipeIds.Add(kv.Value.RevitElementId);

            // Aunque ningún tramo cambie de talla, igualmente alineamos los
            // fittings: un codo/té puede haberse quedado rezagado respecto a su
            // tubería más grande aun sin redimensionar nada en esta pasada.
            if (toChange.Count == 0 && systemPipeIds.Count == 0) return plan;

            using (var t = new Transaction(doc, transactionName ?? "MEP Calc - Aplicar dimensionado"))
            {
                t.Start();
                try
                {
                    foreach (var ch in toChange)
                    {
                        using (var st = new SubTransaction(doc))
                        {
                            st.Start();
                            try
                            {
                                var pipe = doc.GetElement(new ElementId(ch.ElementId)) as Pipe;
                                var p = pipe?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                if (p == null || p.IsReadOnly)
                                {
                                    ch.Note = "Parámetro de diámetro no editable.";
                                    st.RollBack();
                                    Skipped++;
                                    continue;
                                }

                                p.Set(ch.TargetNominalFeet);
                                doc.Regenerate();
                                st.Commit();
                                ch.Applied = true;
                                Applied++;
                            }
                            catch (Exception ex)
                            {
                                ch.Note = "Error: " + ex.Message;
                                st.RollBack();
                                Skipped++;
                            }
                        }
                    }

                    // Tras fijar las tallas de las tuberías, alineamos los
                    // fittings conectados a la mayor tubería de cada nodo.
                    doc.Regenerate();
                    AlignConnectedFittings(doc, systemPipeIds);

                    t.Commit();
                }
                catch
                {
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    throw;
                }
            }
            return plan;
        }

        // ------------------------------------------------------------------ //
        //  AlignConnectedFittings — iguala cada fitting (codo, té, cruz…) al   //
        //  diámetro de la MAYOR tubería del sistema conectada a él.            //
        //  Los reductores (PartType.Transition) se omiten: sus bocas son por   //
        //  diseño de distinto tamaño y forzarlas rompería la familia.          //
        //  Cada fitting va en su propia SubTransaction (aislamiento de fallos).//
        // ------------------------------------------------------------------ //
        private void AlignConnectedFittings(Document doc, HashSet<long> systemPipeIds)
        {
            if (systemPipeIds == null || systemPipeIds.Count == 0) return;

            var fittings = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            foreach (var fi in fittings)
            {
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        if (AlignOneFitting(fi, systemPipeIds))
                        {
                            doc.Regenerate();
                            st.Commit();
                            FittingsAligned++;
                        }
                        else
                        {
                            st.RollBack();
                        }
                    }
                    catch
                    {
                        st.RollBack();
                    }
                }
            }
        }

        // Devuelve true si el fitting se modificó. Calcula el radio de la mayor
        // tubería del sistema conectada y lo escribe en todos sus conectores
        // redondos. No toca reductores ni fittings que no tocan el sistema.
        private static bool AlignOneFitting(FamilyInstance fi, HashSet<long> systemPipeIds)
        {
            var cm = fi.MEPModel?.ConnectorManager;
            if (cm == null) return false;

            // Reductores/transiciones: bocas de distinto tamaño por diseño.
            if ((fi.MEPModel as MechanicalFitting)?.PartType == PartType.Transition)
                return false;

            bool touchesSystem = false;
            double maxRadiusFeet = 0.0;
            var roundEnds = new List<Connector>();

            foreach (Connector c in cm.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                if (c.Domain != Domain.DomainPiping) continue;
                if (c.Shape == ConnectorProfileType.Round) roundEnds.Add(c);

                if (!c.IsConnected) continue;
                foreach (Connector other in c.AllRefs)
                {
                    if (other?.Owner == null) continue;
                    if (!(other.Owner is Pipe)) continue;
                    bool physical = false;
                    try { physical = c.IsConnectedTo(other); }
                    catch { physical = false; }
                    if (!physical) continue;

                    if (systemPipeIds.Contains(other.Owner.Id.Value)) touchesSystem = true;
                    try { if (other.Radius > maxRadiusFeet) maxRadiusFeet = other.Radius; }
                    catch { }
                }
            }

            if (!touchesSystem || maxRadiusFeet <= TolFeet || roundEnds.Count == 0)
                return false;

            bool changed = false;
            foreach (var c in roundEnds)
            {
                try
                {
                    if (Math.Abs(c.Radius - maxRadiusFeet) > TolFeet)
                    {
                        c.Radius = maxRadiusFeet;
                        changed = true;
                    }
                }
                catch
                {
                    // Conector de radio fijo o no editable: se ignora.
                }
            }
            return changed;
        }

        // ------------------------------------------------------------------ //
        //  ApplyExplicitSizes — aplica diámetros nominales elegidos a mano     //
        //  (editor por fila). targetNominalM: Id de elemento → nominal [m].    //
        //  Resuelve la talla exacta del catálogo del tipo de cada tubería.     //
        // ------------------------------------------------------------------ //
        public int ApplyExplicitSizes(Document doc, IDictionary<long, double> targetNominalM, string transactionName)
        {
            Applied = 0;
            Skipped = 0;
            if (doc == null || targetNominalM == null || targetNominalM.Count == 0) return 0;

            using (var t = new Transaction(doc, transactionName ?? "MEP Calc - Aplicar diámetro"))
            {
                t.Start();
                try
                {
                    foreach (var kv in targetNominalM)
                    {
                        if (kv.Value <= 0.0) { Skipped++; continue; }
                        using (var st = new SubTransaction(doc))
                        {
                            st.Start();
                            try
                            {
                                var pipe = doc.GetElement(new ElementId(kv.Key)) as Pipe;
                                var p = pipe?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                if (pipe == null || p == null || p.IsReadOnly)
                                {
                                    st.RollBack();
                                    Skipped++;
                                    continue;
                                }

                                double targetFeet = ResolveCatalogNominalFeet(doc, pipe, kv.Value);
                                p.Set(targetFeet);
                                doc.Regenerate();
                                st.Commit();
                                Applied++;
                            }
                            catch
                            {
                                st.RollBack();
                                Skipped++;
                            }
                        }
                    }
                    t.Commit();
                }
                catch
                {
                    if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                    throw;
                }
            }
            return Applied;
        }

        // Encuentra en el catálogo del tipo la talla cuyo nominal más se acerca
        // al objetivo (m) y devuelve su NominalDiameter en pies. Si no hay
        // catálogo, convierte directamente el objetivo a pies.
        private static double ResolveCatalogNominalFeet(Document doc, Pipe pipe, double targetNominalM)
        {
            double targetFeet = targetNominalM / FeetToM;
            var sizes = GetCatalogSizes(doc, pipe);
            if (sizes.Count == 0) return targetFeet;

            MEPSize best = sizes[0];
            double bestDiff = Math.Abs(best.NominalDiameter - targetFeet);
            foreach (var s in sizes)
            {
                double d = Math.Abs(s.NominalDiameter - targetFeet);
                if (d < bestDiff) { bestDiff = d; best = s; }
            }
            return best.NominalDiameter;
        }

        // ------------------------------------------------------------------ //
        //  BuildPlan — lógica común a Preview y Apply                          //
        // ------------------------------------------------------------------ //
        private List<Change> BuildPlan(Document doc, CalcReport report)
        {
            var changes = new List<Change>();
            if (doc == null || report == null) return changes;

            foreach (var kv in report.Segments)
            {
                var res = kv.Value;
                if (res == null) continue;
                if (!res.VelocityExceeded && !res.FrictionExceeded && !res.VelocityBelowMin) continue;

                bool oversize = res.VelocityBelowMin && !res.VelocityExceeded && !res.FrictionExceeded;

                var ch = new Change
                {
                    ElementId        = res.RevitElementId,
                    RecommendedLabel = res.RecommendedSize ?? "",
                    TramoLabel       = "Tramo BFS " + res.BfsIndex,
                    IsDownsize       = oversize,
                };

                var pipe = doc.GetElement(new ElementId(res.RevitElementId)) as Pipe;
                if (pipe == null)
                {
                    ch.Note = "El elemento ya no es una tubería.";
                    changes.Add(ch);
                    continue;
                }

                // Diámetro nominal actual (pies → mm).
                var pDia = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                double currentFeet = pDia != null ? pDia.AsDouble() : 0.0;
                ch.FromMm = currentFeet * FeetToMm;
                ch.ToMm   = ch.FromMm;
                ch.TargetNominalFeet = currentFeet;

                // Objetivo: el diámetro NOMINAL recomendado (mm). El cálculo ya
                // elige la talla del catálogo real del tipo de tubería cuando
                // está disponible (RecommendedNominalM); usamos ese valor para
                // mapear la talla del catálogo sin reparsear etiquetas. Como
                // respaldo, intentamos resolver la etiqueta en las series
                // normativas internas.
                double targetNominalMm;
                double targetInnerM;
                var recN = NominalDiameters.Find(res.RecommendedSize);
                if (res.RecommendedNominalM > 0.0)
                {
                    targetNominalMm = res.RecommendedNominalM * 1000.0;
                    targetInnerM    = recN != null ? recN.InnerDiameterM : 0.0;
                }
                else if (recN != null)
                {
                    targetNominalMm = recN.Dn;
                    targetInnerM    = recN.InnerDiameterM;
                }
                else
                {
                    ch.Note = "Talla recomendada no reconocida.";
                    changes.Add(ch);
                    continue;
                }

                // Catálogo de tallas del tipo de la propia tubería.
                var sizes = GetCatalogSizes(doc, pipe);
                if (sizes.Count == 0)
                {
                    ch.Note = "El tipo de tubería no expone catálogo de tallas.";
                    changes.Add(ch);
                    continue;
                }

                // Elegimos la talla cuyo NOMINAL coincida con el recomendado
                // (la menor del catálogo con nominal >= recomendado, tol 0.5 mm).
                const double NomTolMm = 0.5;
                MEPSize chosen = null;
                foreach (var s in sizes)
                {
                    if (s.NominalDiameter * FeetToMm >= targetNominalMm - NomTolMm)
                    {
                        chosen = s;
                        break;
                    }
                }
                // Si ninguna llega por nominal, garantizamos al menos el interior
                // hidráulico mínimo (si lo conocemos); en último caso la mayor.
                if (chosen == null && targetInnerM > 0.0)
                {
                    foreach (var s in sizes)
                        if (s.InnerDiameter * FeetToM >= targetInnerM - 1e-9) { chosen = s; break; }
                }
                if (chosen == null) chosen = sizes[sizes.Count - 1];

                ch.TargetNominalFeet = chosen.NominalDiameter;
                ch.ToMm = chosen.NominalDiameter * FeetToMm;

                if (oversize)
                {
                    // Tubería sobredimensionada (velocidad baja) => REDUCIR.
                    // Solo aplicamos si la talla elegida es realmente menor.
                    ch.WillChange = chosen.NominalDiameter < currentFeet - TolFeet;
                    if (!ch.WillChange)
                        ch.Note = "El diámetro actual ya es el mínimo que cumple la sugerencia.";
                }
                else
                {
                    // Velocidad o rozamiento excesivos => AUMENTAR; nunca reducimos.
                    ch.WillChange = chosen.NominalDiameter > currentFeet + TolFeet;
                    if (!ch.WillChange)
                        ch.Note = "El diámetro actual ya cumple o supera la sugerencia.";
                }

                changes.Add(ch);
            }

            return changes;
        }

        // ------------------------------------------------------------------ //
        //  GetCatalogSizes — tallas del tipo de tubería, ordenadas asc.        //
        // ------------------------------------------------------------------ //
        private static List<MEPSize> GetCatalogSizes(Document doc, Pipe pipe)
        {
            var result = new List<MEPSize>();
            try
            {
                var pipeType = pipe.PipeType;
                var rpm = pipeType != null ? pipeType.RoutingPreferenceManager : null;
                if (rpm == null) return result;

                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                var segIds = new HashSet<ElementId>();
                for (int i = 0; i < n; i++)
                {
                    var rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                    if (rule != null && rule.MEPPartId != ElementId.InvalidElementId)
                        segIds.Add(rule.MEPPartId);
                }

                foreach (var segId in segIds)
                {
                    var seg = doc.GetElement(segId) as RvtPipeSegment;
                    if (seg == null) continue;
                    foreach (var sz in seg.GetSizes())
                        result.Add(sz);
                }
            }
            catch
            {
                // Catálogo inaccesible → lista vacía, el llamante lo gestiona.
            }

            // Orden ascendente por diámetro nominal y deduplicado.
            return result
                .GroupBy(s => Math.Round(s.NominalDiameter, 6))
                .Select(g => g.First())
                .OrderBy(s => s.NominalDiameter)
                .ToList();
        }
    }
}
