using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.TableGen;

/// <summary>
/// Code-behind de la ventana TableGen (Sprint 8 P1 + Sprint 9 P2).
/// Toda la logica vive en <see cref="TableGenViewModel"/>.
/// </summary>
public partial class TableGenWindow : System.Windows.Window
{
    public TableGenWindow(UIDocument uidoc, ITableGenService service)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        DataContext = new TableGenViewModel(uidoc, service);
    }
}
