using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.UI.RevitBridge;
using Microsoft.Win32;
using MenuItem = System.Windows.Controls.MenuItem;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Contenido WPF del dockable pane "Grafismo de filtros".
/// La lógica vive en <see cref="FilterGraphicsDockViewModel"/>.
/// </summary>
public partial class FilterGraphicsDockControl : UserControl
{
    private const string ThemeUri =
        "pack://application:,,,/e3gbimtools.DiTools.UI;component/Themes/E3gTheme.xaml";

    private readonly FilterGraphicsDockViewModel _vm = new();

    public FilterGraphicsDockControl()
    {
        InitializeComponent();
        ApplyTheme();
        DataContext = _vm;
        _vm.Notify += (title, text) =>
            MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Inyecta dependencias y carga el estado inicial. Idempotente.</summary>
    public void Initialize(UIApplication uiApp, WorksetDockExternalEventBus bus)
    {
        _vm.Initialize(uiApp, bus);
        _vm.LoadFromRevit();
    }

    /// <summary>Llamado desde el evento ViewActivated (contexto API válido).</summary>
    public void OnViewActivated() => _vm.LoadFromRevit();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.RequestRefresh();
    private void OnCheckAllClick(object sender, RoutedEventArgs e) => _vm.SetAllChecked(true);
    private void OnCheckNoneClick(object sender, RoutedEventArgs e) => _vm.SetAllChecked(false);
    private void OnAuditClick(object sender, RoutedEventArgs e) => _vm.OpenAudit();

    // ------------------------------------------------------------------ //
    //  Exportar / importar / biblioteca
    // ------------------------------------------------------------------ //

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasFilters)
        {
            MessageBox.Show("La vista activa no tiene filtros.", "Grafismo de filtros");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title            = "Exportar grafismo de filtros",
            Filter           = "Grafismo de filtros (*.json)|*.json",
            InitialDirectory = _vm.DefaultSaveFolder(),
            FileName         = _vm.SuggestedFileName(),
        };
        if (dlg.ShowDialog() == true) _vm.ExportTo(dlg.FileName);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title            = "Importar grafismo de filtros",
            Filter           = "Grafismo de filtros (*.json)|*.json",
            InitialDirectory = _vm.DefaultSaveFolder(),
        };
        if (dlg.ShowDialog() == true) _vm.ImportFrom(dlg.FileName);
    }

    private void OnApplyPresetClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPreset is not { } p)
        {
            MessageBox.Show("No hay presets. Exporta uno o elige una carpeta de biblioteca (⚙).", "Grafismo de filtros");
            return;
        }
        _vm.ImportFrom(p.Path);
    }

    private void OnLibraryClick(object sender, RoutedEventArgs e)
    {
        var current = FilterGraphicsLibrary.LibraryFolder;
        if (current is not null)
        {
            var r = MessageBox.Show(
                $"Biblioteca actual:\n{current}\n\nSí = elegir otra carpeta · No = quitar la biblioteca · Cancelar = dejarla",
                "Biblioteca de presets", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.No) { _vm.SetLibraryFolder(null); return; }
        }

        var dlg = new OpenFolderDialog
        {
            Title = "Carpeta de biblioteca compartida de presets de filtros",
            InitialDirectory = current ?? FilterGraphicsLibrary.EnsureLocalFolder(),
        };
        if (dlg.ShowDialog() == true) _vm.SetLibraryFolder(dlg.FolderName);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", FilterGraphicsLibrary.EnsureLocalFolder())
                { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ------------------------------------------------------------------ //
    //  Edición desde la tabla
    // ------------------------------------------------------------------ //

    private void OnLineCellClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilterGraphicsRowItem row)
            _vm.EditColor(row, FilterGraphicsEdit.LineColor);
    }

    private void OnPatternCellClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FilterGraphicsRowItem row)
            _vm.EditColor(row, FilterGraphicsEdit.PatternColor);
    }

    /// <summary>Construye el menú contextual de la fila bajo el ratón.</summary>
    private void OnGridContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = FiltersGrid.ContextMenu!;
        menu.Items.Clear();

        var row = RowUnderMouse(e.OriginalSource as DependencyObject) ?? FiltersGrid.SelectedItem as FilterGraphicsRowItem;
        if (row is null) { e.Handled = true; return; }
        FiltersGrid.SelectedItem = row;

        menu.Items.Add(new MenuItem { Header = row.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Color de línea…",  () => _vm.EditColor(row, FilterGraphicsEdit.LineColor)));
        menu.Items.Add(Item("Color de patrón…", () => _vm.EditColor(row, FilterGraphicsEdit.PatternColor)));

        var weight = new MenuItem { Header = "Grosor de línea" };
        weight.Items.Add(Item("Por defecto", () => _vm.SetLineWeight(row, null), row.Entry.ProjectionLine?.Weight is null));
        for (int w = 1; w <= 16; w++)
        {
            int v = w;
            weight.Items.Add(Item(v.ToString(), () => _vm.SetLineWeight(row, v), row.Entry.ProjectionLine?.Weight == v));
        }
        menu.Items.Add(weight);

        var transp = new MenuItem { Header = "Transparencia" };
        for (int t = 0; t <= 90; t += 10)
        {
            int v = t;
            transp.Items.Add(Item($"{v}%", () => _vm.SetTransparency(row, v), row.Entry.Transparency == v));
        }
        menu.Items.Add(transp);

        menu.Items.Add(Item("Medio tono", () => _vm.ToggleHalftone(row), row.Entry.Halftone));
        menu.Items.Add(new Separator());

        int checkedCount = _vm.Filters.Count(f => f.IsChecked && f != row);
        var copy = Item($"Copiar este grafismo a los marcados ({checkedCount})", () => _vm.CopyGraphicsToChecked(row));
        copy.IsEnabled = checkedCount > 0;
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Quitar color de línea",       () => _vm.ClearLineColor(row)));
        menu.Items.Add(Item("Quitar patrón de superficie", () => _vm.ClearPattern(row)));
        menu.Items.Add(Item("Quitar todas las modificaciones", () => _vm.ClearAll(row)));
    }

    private static MenuItem Item(string header, Action action, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, IsChecked = isChecked };
        mi.Click += (_, __) => action();
        return mi;
    }

    private static FilterGraphicsRowItem? RowUnderMouse(DependencyObject? d)
    {
        while (d is not null and not DataGridRow)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return (d as DataGridRow)?.Item as FilterGraphicsRowItem;
    }

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
