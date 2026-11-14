// ============================================================================
//  CmdCalcPipingNetwork.cs
//  Comando Revit que lanza el motor hidraulico contra un PipingSystem.
//
//  Flujo:
//    1) Recolectar PipingSystem del documento activo y ofrecer escoger uno.
//    2) PipeNetworkReader construye el PipeNetwork.
//    3) HydraulicCalculator.RunFullCalculation con preset CTE_AF + agua 20 C.
//    4) ResultWriter escribe parametros MEP_CALC_* (una sola Transaction).
//    5) TaskDialog con resumen.
// ============================================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Hydraulics;
using E3G.Saneamiento.RevitBridge;

namespace E3G.Saneamiento
{
    [Transaction(TransactionMode.Manual)]
    public class CmdCalcPipingNetwork : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    message = "No hay documento activo.";
                    return Result.Failed;
                }

                // 1) Listar PipingSystem
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystem))
                    .Cast<PipingSystem>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.Name)
                    .ToList();
                if (systems.Count == 0)
                {
                    TaskDialog.Show("MEP Calc Tuberias", "El modelo no contiene PipingSystem.");
                    return Result.Cancelled;
                }

                // 2) Selector escalable WPF (sustituye al TaskDialog limitado a 4)
                var items = systems.Select(s =>
                {
                    string typeStr = "";
                    try
                    {
                        if (doc.GetElement(s.GetTypeId()) is PipingSystemType pst)
                            typeStr = pst.SystemClassification.ToString();
                    }
                    catch { typeStr = s.SystemType.ToString(); }
                    return new SystemPickerDialog.Item
                    {
                        Label    = s.Name,
                        Sublabel = "Tipo: " + typeStr + "  -  Id " + s.Id.Value,
                        Payload  = s,
                    };
                }).ToList();
                var picker = new SystemPickerDialog(
                    "MEP Calc Tuberias - Seleccionar sistema",
                    systems.Count + " PipingSystem en el modelo activo. Filtra por nombre o clasificacion.",
                    items);
                IntPtr revitHandle = commandData.Application.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                    new WindowInteropHelper(picker) { Owner = revitHandle };
                bool? ok = picker.ShowDialog();
                if (ok != true || picker.Selected == null) return Result.Cancelled;
                var system = (PipingSystem)picker.Selected.Payload;

                // 3) Leer red
                var reader = new PipeNetworkReader();
                var network = reader.ReadFromSystem(doc, system);
                if (network.Segments.Count == 0)
                {
                    TaskDialog.Show("MEP Calc Tuberias",
                        "El sistema '" + system.Name + "' no contiene tramos validos para calcular.");
                    return Result.Cancelled;
                }

                // 4) Fuente: el reader la marca si encuentra un equipo (acometida,
                //    bomba, contador) con connector Out.  Si no, fallback por cota.
                bool sourceAuto = network.SourceNode != null;
                if (network.SourceNode == null && network.TerminalNodes.Count > 0)
                {
                    var src = network.TerminalNodes
                        .OrderByDescending(n => n.ElevationM)
                        .First();
                    src.IsSource = true;
                    src.IsTerminal = false;
                    network.TerminalNodes.Remove(src);
                    network.SourceNode = src;
                }
                network.SourcePressurePa = 300000.0; // 3 bar tipico de acometida

                // Orienta segmentos NodeStart->NodeEnd partiendo de la fuente.
                network.OrientFromSource();

                // 5) Resolver - preset dinamico por SystemClassification
                string presetKey = ChoosePresetForPipingSystem(doc, system);

                var calc     = new HydraulicCalculator();
                var fluid    = ChooseFluidForPreset(presetKey);
                var criteria = SizingCriteriaConfig.FromPreset(presetKey);
                var report   = calc.RunFullCalculation(network, fluid, criteria);

                // 6) Escribir resultados al modelo
                var writer = new ResultWriter();
                writer.WriteToRevit(doc, report,
                    "MEP Calc - " + system.Name);

                // 7) Resumen
                var sb = new StringBuilder();
                sb.AppendLine(network.Summary());
                sb.AppendLine("");
                sb.AppendLine("Preset aplicado:      " + presetKey + " (" + criteria.Description + ")");
                sb.AppendLine("Fuente:               "
                    + (sourceAuto ? "auto (equipo Out)" : "fallback (mayor cota)")
                    + " -> " + (network.SourceNode != null ? ("N" + network.SourceNode.Id) : "?"));
                sb.AppendLine("");
                sb.AppendLine("Tramos calculados:    " + report.Segments.Count);
                sb.AppendLine("Worst case dP:        " + report.WorstCasePressureDropPa.ToString("0") + " Pa");
                sb.AppendLine("Min pres terminal:    " + report.MinTerminalPressurePa.ToString("0") + " Pa");
                sb.AppendLine("Avisos:               " + report.CountWarnings);
                sb.AppendLine("Errores:              " + report.CountErrors);
                sb.AppendLine("");
                sb.AppendLine("Escritura al modelo:");
                sb.AppendLine("  Elementos actualizados: " + writer.Updated);
                sb.AppendLine("  Sin parametros MEP_CALC_*: " + writer.Skipped);
                if (writer.Skipped > 0)
                {
                    sb.AppendLine("");
                    sb.AppendLine("Para que los resultados queden grabados crea un Shared");
                    sb.AppendLine("Parameter File con: MEP_CALC_VELOCITY, _FRICTION_PA_M,");
                    sb.AppendLine("_TOTAL_LOSS_PA, _REYNOLDS, _FRICTION_FACTOR, _OK,");
                    sb.AppendLine("_RECOMMENDED_DN  (todos Number/Text, instancia, Pipes).");
                }

                TaskDialog.Show("MEP Calc Tuberias - " + system.Name, sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP Calc Tuberias",
                    "Excepcion durante el calculo:\n\n" + ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Mapea la SystemClassification de un PipingSystem al preset mas
        /// apropiado de SizingCriteriaConfig.  Por defecto CTE_AF.
        /// </summary>
        private static string ChoosePresetForPipingSystem(Document doc, PipingSystem system)
        {
            try
            {
                if (doc.GetElement(system.GetTypeId()) is PipingSystemType pst)
                {
                    switch (pst.SystemClassification)
                    {
                        case MEPSystemClassification.DomesticHotWater:    return "CTE_ACS";
                        case MEPSystemClassification.DomesticColdWater:   return "CTE_AF";
                        // En Revit 2024+ los identificadores son SupplyHydronic /
                        // ReturnHydronic (no Hydronic{Supply,Return}).
                        case MEPSystemClassification.SupplyHydronic:      return "RITE_HEATING";
                        case MEPSystemClassification.ReturnHydronic:      return "RITE_HEATING";
                        default:                                          return "CTE_AF";
                    }
                }
            }
            catch { }
            return "CTE_AF";
        }

        /// <summary>
        /// Devuelve las propiedades del fluido coherentes con el preset
        /// (temperatura distinta para AF / ACS / calefaccion).
        /// </summary>
        private static FluidProperties ChooseFluidForPreset(string presetKey)
        {
            switch (presetKey)
            {
                case "CTE_ACS":      return FluidProperties.Water60C;
                case "RITE_HEATING": return FluidProperties.Water80C;
                case "CTE_RECIRC":   return FluidProperties.Water60C;
                default:             return FluidProperties.Water20C;
            }
        }
    }
}
