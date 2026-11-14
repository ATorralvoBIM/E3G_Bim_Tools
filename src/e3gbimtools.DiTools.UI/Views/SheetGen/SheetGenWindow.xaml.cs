using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sheets;
using E3G.BimTools.DiTools.UI.Controls;
using Microsoft.Win32;

namespace E3G.BimTools.DiTools.UI.Views.SheetGen;

/// <summary>
/// Code-behind de SheetGenWindow (Sprint 6).
/// La lógica de negocio vive en <see cref="SheetGenViewModel"/>;
/// aquí solo se conectan los eventos de UI.
/// </summary>
public partial class SheetGenWindow : Window
{
    private readonly SheetGenViewModel _vm;

    public SheetGenWindow(UIDocument uidoc, ISheetGenService service)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        _vm         = new SheetGenViewModel(uidoc, service);
        DataContext = _vm;
    }

    // ================================================================== //
    //  PESTAÑA 1 — Crear / Importar                                        //
    // ================================================================== //

    private void OnAddRowClick(object sender, RoutedEventArgs e)
        => _vm.AddSheetRow();

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
        => _vm.RemoveSheetRow(NewSheetsGrid.SelectedItem as SheetDescriptor);

    private async void OnCreateSheetsClick(object sender, RoutedEventArgs e)
        => await _vm.CreateSheetsAsync();

    private void OnImportExcelClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Selecciona el archivo Excel con el listado de sheets",
            Filter = "Excel (*.xlsx)|*.xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        _vm.ImportFromExcel(dlg.FileName);
    }

    private void OnExportExcelClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title            = "Exportar listado de sheets",
            Filter           = "Excel (*.xlsx)|*.xlsx",
            FileName         = "sheets_export.xlsx",
            DefaultExt       = ".xlsx",
            AddExtension     = true
        };
        if (dlg.ShowDialog() != true) return;
        _vm.ExportToExcel(dlg.FileName);
    }

    // ================================================================== //
    //  PESTAÑA 2 — Layouts                                                 //
    // ================================================================== //

    private void OnNewLayoutClick(object sender, RoutedEventArgs e)
    {
        _vm.NewLayout();
        UpdateLayoutEditor();
    }

    private void OnDeleteLayoutClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLayout is null) return;
        var res = MessageBox.Show(
            $"¿Eliminar el layout «{_vm.SelectedLayout.Name}»?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        _vm.DeleteSelectedLayout();
        ClearLayoutEditor();
    }

    private void OnSaveLayoutClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLayout is null)
        {
            MessageBox.Show("Selecciona o crea un layout primero.", "SheetGen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var placements = PlacementsGrid.ItemsSource as IEnumerable<ViewPlacementSpec>
                         ?? Enumerable.Empty<ViewPlacementSpec>();

        _vm.SaveSelectedLayout(
            LayoutNameBox.Text,
            LayoutDescBox.Text,
            placements.ToList());

        MessageBox.Show("Layout guardado.", "SheetGen", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnAddPlacementClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLayout is null) return;
        _vm.SelectedLayout.Placements.Add(new ViewPlacementSpec());
        UpdateLayoutEditor();
    }

    private void OnRemovePlacementClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedLayout is null) return;
        if (PlacementsGrid.SelectedItem is ViewPlacementSpec spec)
        {
            _vm.SelectedLayout.Placements.Remove(spec);
            UpdateLayoutEditor();
        }
    }

    private void UpdateLayoutEditor()
    {
        if (_vm.SelectedLayout is null) { ClearLayoutEditor(); return; }
        LayoutNameBox.Text     = _vm.SelectedLayout.Name;
        LayoutDescBox.Text     = _vm.SelectedLayout.Description ?? string.Empty;
        PlacementsGrid.ItemsSource = _vm.SelectedLayout.Placements;
    }

    private void ClearLayoutEditor()
    {
        LayoutNameBox.Text     = string.Empty;
        LayoutDescBox.Text     = string.Empty;
        PlacementsGrid.ItemsSource = null;
    }

    // ================================================================== //
    //  PESTAÑA 3 — Revisiones                                             //
    // ================================================================== //

    private void OnApplyRevisionsClick(object sender, RoutedEventArgs e)
    {
        var selected = RevSheetsGrid.SelectedItems.Cast<SheetInfo>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selecciona al menos una sheet.", "SheetGen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.ApplyRevisions(selected);
        _vm.RefreshAll();
    }

    // ================================================================== //
    //  PESTAÑA 4 — Auditoría / Renumerar                                  //
    // ================================================================== //

    private void OnAuditClick(object sender, RoutedEventArgs e)
    {
        _vm.RunAudit();
        OrphanGrid.ItemsSource    = _vm.AuditResult?.OrphanSheets;
        DuplicatesList.ItemsSource = _vm.AuditResult?.DuplicateNumbers;

        var msg = _vm.AuditResult?.ToSummary() ?? string.Empty;
        MessageBox.Show(msg, "Auditoría", MessageBoxButton.OK,
            _vm.AuditResult?.IsClean == true ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnPreviewRenumClick(object sender, RoutedEventArgs e)
    {
        SyncRenumRequest();
        var selected = RevSheetsGrid.SelectedItems.Cast<SheetInfo>().ToList();
        _vm.PreviewRenumber(selected);
        RenumPreviewGrid.ItemsSource = _vm.RenumPreview
            .Select(t => new { t.OldNumber, t.NewNumber })
            .ToList();
    }

    private void OnApplyRenumClick(object sender, RoutedEventArgs e)
    {
        SyncRenumRequest();
        var selected = RevSheetsGrid.SelectedItems.Cast<SheetInfo>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selecciona las sheets a renumerar en la pestaña Revisiones.", "SheetGen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show(
            $"¿Renumerar {selected.Count} sheet(s)?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        _vm.ApplyRenumber(selected);
        RenumPreviewGrid.ItemsSource = null;
        _vm.RefreshAll();
    }

    private void SyncRenumRequest()
    {
        _vm.RenumPrefix  = PrefixBox.Text;
        _vm.RenumSuffix  = SuffixBox.Text;
        _vm.RenumStart   = int.TryParse(StartBox.Text, out var s)  ? s : 1;
        _vm.RenumPadding = int.TryParse(PadBox.Text,   out var p)  ? p : 2;
    }

    // ================================================================== //
    //  Footer                                                             //
    // ================================================================== //

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _vm.RefreshAll();
    private void OnCloseClick(object sender, RoutedEventArgs e)   => Close();
}
