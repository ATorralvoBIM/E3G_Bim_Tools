// ============================================================================
//  CalcPipingNetworkCommand.cs
//  Comando Revit que lanza el motor hidraulico contra un PipingSystem y
//  escribe los resultados MEP_CALC_* al modelo en una sola transaccion.
//
//  Usa ICalcEngineService del contenedor DI.
//  El usuario elige el sistema desde un selector WPF (SystemPickerDialog).
// ============================================================================

using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Aeraulics;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using e3gbimtools.Calc.RevitBridge;

namespace E3G.BimTools.DiTools.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CalcPipingNetworkCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);

        var uidoc = commandData.Application.ActiveUIDocument;
        var doc   = uidoc?.Document;
        if (doc == null) { message = "No hay documento activo."; return Result.Failed; }

        try
        {
            // 1) Listar PipingSystem
            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystem))
                .Cast<PipingSystem>()
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name)
                .ToList();
            if (systems.Count == 0)
            {
                TaskDialog.Show("Cálculo Hidráulico", "El modelo no contiene PipingSystem.");
                return Result.Cancelled;
            }

            // 2) Picker WPF (reusa SystemPickerDialog del monolito si cargado, o fallback TaskDialog)
            PipingSystem system = PickSystem(commandData, doc, systems);
            if (system == null) return Result.Cancelled;

            // 3) Leer red
            var reader  = new PipeNetworkReader();
            var network = reader.ReadFromSystem(doc, system);
            if (network.Segments.Count == 0)
            {
                TaskDialog.Show("Cálculo Hidráulico",
                    "El sistema '" + system.Name + "' no contiene tramos válidos.");
                return Result.Cancelled;
            }

            // 4) Fuente y orientación
            ApplySourceFallback(network);
            network.OrientFromSource();

            // 5) Preset y fluido
            string presetKey = ChoosePreset(doc, system);
            var criteria = SizingCriteriaConfig.FromPreset(presetKey);
            var fluid    = ChooseFluid(presetKey);

            // 6) Calcular (sincrono desde comando: hilo Revit)
            var calcEngine = ServiceContainer.Resolve<ICalcEngineService>();
            var req        = new PipeNetworkRequest { Network = network, Fluid = fluid, Criteria = criteria };
            var report     = calcEngine.CalculatePipingAsync(doc, req, default).GetAwaiter().GetResult();

            // 7) Escribir resultados
            var writer = new ResultWriter();
            writer.WriteToRevit(doc, report, "MEP Calc Hidráulica — " + system.Name);

            // 8) Resumen
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
            TaskDialog.Show("Cálculo Hidráulico — " + system.Name, sb.ToString());

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Cálculo Hidráulico", "Excepción:\n\n" + ex);
            return Result.Failed;
        }
    }

    private static PipingSystem PickSystem(ExternalCommandData cmd, Document doc,
        System.Collections.Generic.List<PipingSystem> systems)
    {
        // Fallback simple con TaskDialog si solo hay 1 sistema
        if (systems.Count == 1) return systems[0];

        // Intenta SystemPickerDialog (está en el monolito compilado aparte).
        // Si no está disponible, usa un TaskDialog básico con los primeros 4.
        try
        {
            var type = Type.GetType("E3G.Saneamiento.SystemPickerDialog, E3G_Saneamiento");
            if (type != null)
            {
                // monolito disponible en el AppDomain: construir y mostrar
                var itemType = type.GetNestedType("Item");
                var items    = systems.Select(s =>
                {
                    var item = Activator.CreateInstance(itemType);
                    itemType.GetProperty("Label").SetValue(item, s.Name);
                    itemType.GetProperty("Sublabel").SetValue(item,
                        "Tipo: " + TryGetSysType(doc, s) + "  Id " + s.Id.Value);
                    itemType.GetProperty("Payload").SetValue(item, s);
                    return item;
                }).ToList();

                var ctor = type.GetConstructors()[0];
                var itemListType = typeof(System.Collections.Generic.List<>).MakeGenericType(itemType);
                var list = Activator.CreateInstance(itemListType);
                foreach (var i in items)
                    itemListType.GetMethod("Add").Invoke(list, new[] { i });

                var picker = (Window)ctor.Invoke(new object[]
                {
                    "Cálculo Hidráulico — Seleccionar sistema",
                    systems.Count + " PipingSystem en el documento. Filtra por nombre.",
                    list,
                });
                IntPtr h = cmd.Application.MainWindowHandle;
                if (h != IntPtr.Zero) new WindowInteropHelper(picker) { Owner = h };
                bool? ok = (bool?)type.GetMethod("ShowDialog").Invoke(picker, null);
                if (ok != true) return null;
                var sel = type.GetProperty("Selected").GetValue(picker);
                return (PipingSystem)itemType.GetProperty("Payload").GetValue(sel);
            }
        }
        catch { /* fallback to TaskDialog */ }

        // TaskDialog fallback
        var dlg = new TaskDialog("Cálculo Hidráulico — Elegir sistema")
        {
            MainInstruction = "Selecciona el PipingSystem a calcular:",
            MainContent     = string.Join("\n", systems.Take(4).Select(s => "• " + s.Name)),
        };
        for (int i = 0; i < Math.Min(4, systems.Count); i++)
        {
            var s = systems[i];
            dlg.AddCommandLink((TaskDialogCommandLinkId)(TaskDialogCommandLinkId.CommandLink1 + i), s.Name);
        }
        dlg.CommonButtons = TaskDialogCommonButtons.Cancel;
        var res = dlg.Show();
        int idx = res - TaskDialogResult.CommandLink1;
        return (idx >= 0 && idx < systems.Count) ? systems[idx] : null;
    }

    private static string TryGetSysType(Document doc, PipingSystem sys)
    {
        try
        {
            if (doc.GetElement(sys.GetTypeId()) is PipingSystemType pst)
                return pst.SystemClassification.ToString();
        }
        catch { }
        return sys.SystemType.ToString();
    }

    private static void ApplySourceFallback(PipeNetwork network)
    {
        if (network.SourceNode != null) return;
        if (network.TerminalNodes.Count == 0) return;
        var src = network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
        src.IsSource = true; src.IsTerminal = false;
        network.TerminalNodes.Remove(src);
        network.SourceNode = src;
        network.SourcePressurePa = 300000.0;
    }

    private static string ChoosePreset(Document doc, PipingSystem sys)
    {
        try
        {
            if (doc.GetElement(sys.GetTypeId()) is PipingSystemType pst)
            {
                return pst.SystemClassification switch
                {
                    MEPSystemClassification.DomesticHotWater => "CTE_ACS",
                    MEPSystemClassification.DomesticColdWater => "CTE_AF",
                    MEPSystemClassification.SupplyHydronic => "RITE_HEATING",
                    MEPSystemClassification.ReturnHydronic => "RITE_HEATING",
                    _ => "CTE_AF",
                };
            }
        }
        catch { }
        return "CTE_AF";
    }

    private static FluidProperties ChooseFluid(string presetKey) => presetKey switch
    {
        "CTE_ACS"      => FluidProperties.Water60C,
        "RITE_HEATING" => FluidProperties.Water80C,
        "CTE_RECIRC"   => FluidProperties.Water60C,
        _              => FluidProperties.Water20C,
    };
}
