// ============================================================================
//  CmdCalcDuctNetwork.cs
//  Comando Revit que lanza el motor aeraulico contra un MechanicalSystem.
// ============================================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Aeraulics;
using E3G.Saneamiento.RevitBridge;

namespace E3G.Saneamiento
{
    [Transaction(TransactionMode.Manual)]
    public class CmdCalcDuctNetwork : IExternalCommand
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

                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(MechanicalSystem))
                    .Cast<MechanicalSystem>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.Name)
                    .ToList();
                if (systems.Count == 0)
                {
                    TaskDialog.Show("MEP Calc Conductos", "El modelo no contiene MechanicalSystem.");
                    return Result.Cancelled;
                }

                // Selector escalable WPF (sustituye al TaskDialog limitado a 4)
                var items = systems.Select(s => new SystemPickerDialog.Item
                {
                    Label    = s.Name,
                    Sublabel = "Tipo: " + s.SystemType + "  -  Id " + s.Id.Value,
                    Payload  = s,
                }).ToList();
                var picker = new SystemPickerDialog(
                    "MEP Calc Conductos - Seleccionar sistema",
                    systems.Count + " MechanicalSystem en el modelo activo. Filtra por nombre o tipo.",
                    items);
                IntPtr revitHandle = commandData.Application.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                    new WindowInteropHelper(picker) { Owner = revitHandle };
                bool? ok = picker.ShowDialog();
                if (ok != true || picker.Selected == null) return Result.Cancelled;
                var system = (MechanicalSystem)picker.Selected.Payload;

                var reader = new DuctNetworkReader();
                var network = reader.ReadFromSystem(doc, system);
                if (network.Segments.Count == 0)
                {
                    TaskDialog.Show("MEP Calc Conductos",
                        "El sistema '" + system.Name + "' no contiene tramos validos.");
                    return Result.Cancelled;
                }

                // Fuente: el reader la marca si encuentra un MechanicalEquipment con
                // connector Out.  Si no la encuentra, fallback heuristico por cota.
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
                network.SourcePressurePa = 250.0; // 250 Pa tipico ventilador

                // Orienta segmentos NodeStart->NodeEnd partiendo de la fuente
                // para que el solver pueda hacer el BFS correcto.
                network.OrientFromSource();

                // Preset dinamico segun el tipo de sistema (Supply/Return/Exhaust)
                string presetKey = ChoosePresetForDuctSystem(system);

                var calc     = new AeraulicCalculator();
                var fluid    = AirProperties.AsFluid(20.0, 0.0);
                var criteria = SizingCriteriaConfig.FromPreset(presetKey);
                var report   = calc.RunFullCalculation(network, fluid, criteria);

                var writer = new ResultWriter();
                writer.WriteToRevit(doc, report, "MEP Calc - " + system.Name);

                var sb = new StringBuilder();
                sb.AppendLine(network.Summary());
                sb.AppendLine("");
                sb.AppendLine("Preset aplicado:      " + presetKey + " (" + criteria.Description + ")");
                sb.AppendLine("Fuente:               "
                    + (sourceAuto ? "auto (MechanicalEquipment Out)" : "fallback (mayor cota)")
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

                TaskDialog.Show("MEP Calc Conductos - " + system.Name, sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEP Calc Conductos",
                    "Excepcion durante el calculo:\n\n" + ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Mapea el tipo de sistema mecanico de Revit al preset de
        /// SizingCriteriaConfig mas adecuado.  Por defecto RITE_SUPPLY.
        /// </summary>
        private static string ChoosePresetForDuctSystem(MechanicalSystem system)
        {
            try
            {
                switch (system.SystemType)
                {
                    case DuctSystemType.SupplyAir:  return "RITE_SUPPLY";
                    case DuctSystemType.ReturnAir:  return "RITE_RETURN";
                    case DuctSystemType.ExhaustAir: return "RITE_EXHAUST";
                    default:                        return "RITE_SUPPLY";
                }
            }
            catch
            {
                return "RITE_SUPPLY";
            }
        }
    }
}
