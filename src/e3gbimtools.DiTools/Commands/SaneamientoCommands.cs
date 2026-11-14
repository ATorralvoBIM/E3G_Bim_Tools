using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Bootstrap;

namespace E3G.BimTools.DiTools.Commands;

// Comandos proxy del panel «Saneamiento» (primera app de la pestania
// «e3gbimtools»).  Delegan en los comandos del modulo e3gbimtools.Saneamiento,
// cargado por SaneamientoBridge.

/// <summary>Abre el panel acoplable E3G (Saneamiento HS5 / Fontaneria / Ajustes).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SaneamientoPanelCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);
        return SaneamientoBridge.Execute("E3G.Saneamiento.CmdAbrirPanelMain", commandData, ref message, elements);
    }
}

/// <summary>Abre la ventana de ajustes (idioma, unidades, protocolo, partidas BC3).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SaneamientoAjustesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ServiceContainer.AttachUiApplication(commandData.Application);
        return SaneamientoBridge.Execute("E3G.Saneamiento.CmdAbrirAjustes", commandData, ref message, elements);
    }
}
