using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.RevitBridge;

namespace E3G.BimTools.DiTools.UI.Views.Worksets;

/// <summary>
/// Contenido WPF del dockable pane de visibilidad de worksets (Bloque E).
/// La lógica vive en <see cref="WorksetVisibilityDockViewModel"/>.
/// </summary>
public partial class WorksetVisibilityDockControl : UserControl
{
    private const string ThemeUri =
        "pack://application:,,,/e3gbimtools.DiTools.UI;component/Themes/E3gTheme.xaml";

    private readonly WorksetVisibilityDockViewModel _vm = new();

    public WorksetVisibilityDockControl()
    {
        InitializeComponent();
        ApplyTheme();
        DataContext = _vm;
    }

    /// <summary>Inyecta dependencias y carga el estado inicial. Idempotente.</summary>
    public void Initialize(UIApplication uiApp, IWorksetService service, WorksetDockExternalEventBus bus)
    {
        _vm.Initialize(uiApp, service, bus);
        _vm.LoadFromRevit();
    }

    /// <summary>Llamado desde el evento ViewActivated (contexto API válido).</summary>
    public void OnViewActivated() => _vm.LoadFromRevit();

    private void OnShowAllClick(object sender, RoutedEventArgs e) => _vm.SetAll(true);
    private void OnHideAllClick(object sender, RoutedEventArgs e) => _vm.SetAll(false);
    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.RequestRefresh();

    private void ApplyTheme()
    {
        try
        {
            var uri = new Uri(ThemeUri, UriKind.Absolute);
            var already = Resources.MergedDictionaries
                .Any(d => d.Source is not null && d.Source.Equals(uri));
            if (!already)
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });

            if (Resources["E3g.Font.Family"] is FontFamily family)
                FontFamily = family;
        }
        catch { /* sin tema antes que crashear */ }
    }
}
