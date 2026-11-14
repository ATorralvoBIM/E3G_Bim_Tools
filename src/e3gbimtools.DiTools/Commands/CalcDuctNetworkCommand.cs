// ============================================================================
//  CalcDuctNetworkCommand.cs
//  Comando Revit que lanza el motor aeraulico contra un MechanicalSystem.
// ============================================================================

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Aeraulics;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.RevitBridge;

namespace E3G.BimTools.DiTools.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CalcDuctNetworkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uidoc = commandData.Application.ActiveUIDocument;
        var doc   = uidoc?.Document;
        if (doc == null) { message = "No hay documento activo."; return Result.Failed; }

        try
        {
            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystem))
                .Cast<MechanicalSystem>()
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name)
                .ToList();
            if (systems.Count == 0)
            {
                TaskDialog.Show("Cálculo Aeráulico", "El modelo no contiene MechanicalSystem.");
                return Result.Cancelled;
            }

            // Picker: TaskDialog con primeros 4 sistemas
            MechanicalSystem system = PickSystem(systems);
            if (system == null) return Result.Cancelled;

            var reader  = new DuctNetworkReader();
            var network = reader.ReadFromSystem(doc, system);
            if (network.Segments.Count == 0)
            {
                TaskDialog.Show("Cálculo Aeráulico",
                    "El sistema '" + system.Name + "' no contiene tramos válidos.");
                return Result.Cancelled;
            }

            ApplySourceFallback(network);
            network.OrientFromSource();

            string presetKey = ChoosePreset(system);
            var criteria     = SizingCriteriaConfig.FromPreset(presetKey);
            var fluid        = AirProperties.AsFluid(20.0, 0.0);

            var calcEngine = ServiceContainer.Resolve<ICalcEngineService>();
            var req        = new DuctNetworkRequest { Network = network, Fluid = fluid, Criteria = criteria };
            var report     = calcEngine.CalculateDuctAsync(doc, req, default).GetAwaiter().GetResult();

            var writer = new ResultWriter();
            writer.WriteToRevit(doc, report, "MEP Calc Aeráulica — " + system.Name);

            var sb = new StringBuilder();
            sb.AppendLine("Sistema:             " + system.Name);
            sb.AppendLine("Preset:              " + presetKey + " (" + criteria.Description + ")");
            sb.AppendLine("Tramos calculados:   " + report.Segments.Count);
            sb.AppendLine("Worst-case ΔP:       " + report.WorstCasePressureDropPa.ToString("0") + " Pa");
            sb.AppendLine("Presión mín terminal:" + report.MinTerminalPressurePa.ToString("0") + " Pa");
            sb.AppendLine("Errores / Avisos:    " + report.CountErrors + " / " + report.CountWarnings);
            sb.AppendLine();
            sb.AppendLine("Escritura al modelo:");
            sb.AppendLine("  Actualizados: " + writer.Updated);
            sb.AppendLine("  Sin paráms MEP_CALC_*: " + writer.Skipped);
            TaskDialog.Show("Cálculo Aeráulico — " + system.Name, sb.ToString());

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Cálculo Aeráulico", "Excepción:\n\n" + ex);
            return Result.Failed;
        }
    }

    private static MechanicalSystem PickSystem(System.Collections.Generic.List<MechanicalSystem> systems)
    {
        if (systems.Count == 1) return systems[0];

        var dlg = new TaskDialog("Cálculo Aeráulico — Elegir sistema")
        {
            MainInstruction = "Selecciona el MechanicalSystem a calcular:",
            MainContent     = string.Join("\n", systems.Take(4).Select(s => "• " + s.Name)),
        };
        for (int i = 0; i < Math.Min(4, systems.Count); i++)
            dlg.AddCommandLink((TaskDialogCommandLinkId)(TaskDialogCommandLinkId.CommandLink1 + i), systems[i].Name);
        dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
        var res = dlg.Show();
        int idx = res - TaskDialogResult.CommandLink1;
        return (idx >= 0 && idx < systems.Count) ? systems[idx] : null;
    }

    private static void ApplySourceFallback(DuctNetwork network)
    {
        if (network.SourceNode != null) return;
        if (network.TerminalNodes.Count == 0) return;
        var src = network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
        src.IsSource = true; src.IsTerminal = false;
        network.TerminalNodes.Remove(src);
        network.SourceNode = src;
        network.SourcePressurePa = 250.0;
    }

    private static string ChoosePreset(MechanicalSystem system)
    {
        try
        {
            return system.SystemType switch
            {
                DuctSystemType.SupplyAir  => "RITE_SUPPLY",
                DuctSystemType.ReturnAir  => "RITE_RETURN",
                DuctSystemType.ExhaustAir => "RITE_EXHAUST",
                _                         => "RITE_SUPPLY",
            };
        }
        catch { return "RITE_SUPPLY"; }
    }
}
