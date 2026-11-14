using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Views;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Views.OneFilter;

namespace E3G.BimTools.DiTools.UI.Views.ViewManager;

/// <summary>
/// Code-behind del ViewManager (Sprint 4).  Mantenemos por consistencia con
/// el resto del modulo el patron "code-behind delgado" en vez de MVVM
/// completo: la ventana llama a los servicios de Core y refresca la UI.
///
/// Sprint 4a (esta iteracion) cablea:
///  - Listado de plantillas (lectura).
///  - Aplicacion masiva (escritura - Transaction unica).
///  - Lectura de View Range (sin escritura masiva todavia).
///  - Diff entre 2 plantillas (General; VG Overrides pendientes).
/// Sprint 4b cierra: duplicar, eliminar, exportar Excel, VG Overrides en diff,
/// editor de View Range completo con validacion y aplicacion masiva.
/// </summary>
public partial class ViewManagerWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly IViewTemplateService _templates;
    private readonly IViewRangeService _ranges;
    private readonly IViewTemplateComparer _comparer;

    private List<ViewTemplateInfo> _allTemplates = new();
    private List<ViewItemVm> _viewsForApply = new();
    private List<ViewItemVm> _viewsForRange = new();
    private bool _showOnlyOrphans;

    public ViewManagerWindow(
        UIDocument uidoc,
        IViewTemplateService templates,
        IViewRangeService ranges,
        IViewTemplateComparer comparer)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));

        Loaded += (_, __) => ReloadAll();
    }

    // =========================================================================
    // Carga inicial
    // =========================================================================

    private void ReloadAll()
    {
        try
        {
            var doc = _uidoc.Document;
            _allTemplates = _templates.ListAll(doc).ToList();

            RenderTemplatesGrid();
            RenderTemplateCombos();

            // Vistas para la pestaña Aplicar: tipo Floor Plan por defecto.
            _viewsForApply = _templates.ListApplicableViews(doc, ViewType.FloorPlan)
                .Select(v => new ViewItemVm(v.Id, v.Name))
                .ToList();
            ApplyViewsList.ItemsSource = _viewsForApply;

            // Vistas para la pestaña View Range: solo ViewPlans.
            _viewsForRange = _templates.ListApplicableViews(doc, ViewType.FloorPlan)
                .Select(v => new ViewItemVm(v.Id, v.Name))
                .ToList();
            RangeReferenceCombo.ItemsSource = _viewsForRange;
            RangeReferenceCombo.DisplayMemberPath = nameof(ViewItemVm.Name);
            RangeViewsList.ItemsSource = _viewsForRange;

            ListInfoText.Text = $"{_allTemplates.Count} plantillas en el documento.";
        }
        catch (Exception ex)
        {
            TaskDialog.Show("ViewManager", "Error al cargar el documento:\n" + ex.Message);
        }
    }

    private void RenderTemplatesGrid()
    {
        var filtered = _showOnlyOrphans
            ? _allTemplates.Where(t => t.IsOrphan).ToList()
            : _allTemplates;
        TemplatesGrid.ItemsSource = filtered;
    }

    private void RenderTemplateCombos()
    {
        ApplyTemplateCombo.ItemsSource = _allTemplates;
        ApplyTemplateCombo.DisplayMemberPath = nameof(ViewTemplateInfo.Name);

        DiffTemplateACombo.ItemsSource = _allTemplates;
        DiffTemplateACombo.DisplayMemberPath = nameof(ViewTemplateInfo.Name);

        DiffTemplateBCombo.ItemsSource = _allTemplates;
        DiffTemplateBCombo.DisplayMemberPath = nameof(ViewTemplateInfo.Name);
    }

    // =========================================================================
    // Pestaña Listado
    // =========================================================================

    private void OnReloadListClick(object sender, RoutedEventArgs e) => ReloadAll();

    private void OnFilterOrphansClick(object sender, RoutedEventArgs e)
    {
        _showOnlyOrphans = !_showOnlyOrphans;
        RenderTemplatesGrid();
        ListInfoText.Text = _showOnlyOrphans
            ? $"Mostrando {_allTemplates.Count(t => t.IsOrphan)} huérfanas."
            : $"{_allTemplates.Count} plantillas en el documento.";
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not ViewTemplateInfo tpl)
        {
            TaskDialog.Show("ViewManager", "Selecciona una plantilla.");
            return;
        }
        var dlg = new TextInputDialog("Renombrar plantilla", "Nuevo nombre:", tpl.Name) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var res = _templates.Rename(_uidoc.Document, tpl.Id, dlg.Result.Trim());
        TaskDialog.Show("ViewManager", res.ToSummary());
        ReloadAll();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not ViewTemplateInfo tpl)
        {
            TaskDialog.Show("ViewManager", "Selecciona una plantilla.");
            return;
        }
        var dlg = new TextInputDialog("Duplicar plantilla", "Nombre de la copia:", tpl.Name + " - copia") { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var res = _templates.Duplicate(_uidoc.Document, tpl.Id, dlg.Result.Trim());
        TaskDialog.Show("ViewManager", res.ToSummary());
        ReloadAll();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (TemplatesGrid.SelectedItem is not ViewTemplateInfo tpl)
        {
            TaskDialog.Show("ViewManager", "Selecciona una plantilla.");
            return;
        }

        // Primer intento sin forzar: si está en uso, el servicio devuelve aviso.
        var res = _templates.Delete(_uidoc.Document, tpl.Id, forceUnassign: false);
        if (!res.Success && tpl.UsageCount > 0)
        {
            var confirm = new TaskDialog("ViewManager")
            {
                MainInstruction = $"La plantilla '{tpl.Name}' está en uso por {tpl.UsageCount} vista(s).",
                MainContent = "¿Desasignarla de esas vistas y eliminarla?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return;
            res = _templates.Delete(_uidoc.Document, tpl.Id, forceUnassign: true);
        }

        TaskDialog.Show("ViewManager", res.ToSummary());
        ReloadAll();
    }

    private void OnExportMatrixClick(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Exportar matriz Vista × Plantilla",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"MatrizVistas_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
        };
        if (sfd.ShowDialog(this) != true) return;

        try
        {
            var path = _templates.ExportMatrixToExcel(_uidoc.Document, sfd.FileName);
            TaskDialog.Show("ViewManager", "Matriz exportada a:\n" + path);
        }
        catch (Exception ex)
        {
            TaskDialog.Show("ViewManager", "Error al exportar la matriz:\n" + ex.Message);
        }
    }

    // =========================================================================
    // Pestaña Aplicar
    // =========================================================================

    private void OnApplyTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApplyTemplateCombo.SelectedItem is not ViewTemplateInfo tpl)
        {
            ApplyTemplateTypeLabel.Text = "—";
            return;
        }
        ApplyTemplateTypeLabel.Text = tpl.ViewType.ToString();

        // Refrescamos la lista de vistas destino al tipo de la plantilla.
        _viewsForApply = _templates.ListApplicableViews(_uidoc.Document, tpl.ViewType)
            .Select(v => new ViewItemVm(v.Id, v.Name))
            .ToList();
        ApplyViewsList.ItemsSource = _viewsForApply;
    }

    private void OnApplyViewsSearchChanged(object sender, TextChangedEventArgs e)
    {
        var term = ApplyViewsSearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(term))
        {
            ApplyViewsList.ItemsSource = _viewsForApply;
            return;
        }
        ApplyViewsList.ItemsSource = _viewsForApply
            .Where(v => v.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    private void OnSelectAllViewsClick(object sender, RoutedEventArgs e)
    {
        foreach (var v in _viewsForApply) v.IsSelected = true;
        ApplyViewsList.Items.Refresh();
    }

    private void OnSelectNoneViewsClick(object sender, RoutedEventArgs e)
    {
        foreach (var v in _viewsForApply) v.IsSelected = false;
        ApplyViewsList.Items.Refresh();
    }

    private void OnApplyTemplateClick(object sender, RoutedEventArgs e)
    {
        if (ApplyTemplateCombo.SelectedItem is not ViewTemplateInfo tpl)
        {
            ApplyStatus.Text = "Selecciona una plantilla.";
            return;
        }
        var targetIds = _viewsForApply.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (targetIds.Count == 0)
        {
            ApplyStatus.Text = "Marca al menos una vista destino.";
            return;
        }

        var result = _templates.ApplyTemplate(_uidoc.Document, tpl.Id, targetIds);
        ApplyStatus.Text = result.ToSummary();
        ReloadAll(); // refresca contadores
    }

    // =========================================================================
    // Pestaña View Range
    // =========================================================================

    private void OnRangeReferenceChanged(object sender, SelectionChangedEventArgs e) { /* nop */ }

    private void OnReadRangeClick(object sender, RoutedEventArgs e)
    {
        if (RangeReferenceCombo.SelectedItem is not ViewItemVm vm)
        {
            RangeStatus.Text = "Elige una vista de referencia.";
            return;
        }
        var info = _ranges.GetViewRange(_uidoc.Document, vm.Id);
        if (info is null)
        {
            RangeStatus.Text = "La vista seleccionada no es ViewPlan.";
            return;
        }
        TopOffsetBox.Text       = info.TopOffset.ToString(CultureInfo.InvariantCulture);
        CutPlaneOffsetBox.Text  = info.CutPlaneOffset.ToString(CultureInfo.InvariantCulture);
        BottomOffsetBox.Text    = info.BottomOffset.ToString(CultureInfo.InvariantCulture);
        ViewDepthOffsetBox.Text = info.ViewDepthOffset.ToString(CultureInfo.InvariantCulture);
        RangeStatus.Text = $"View Range leido de '{vm.Name}' (valores en pies).";
    }

    private void OnApplyRangeClick(object sender, RoutedEventArgs e)
    {
        if (RangeReferenceCombo.SelectedItem is not ViewItemVm vm)
        {
            RangeStatus.Text = "Elige una vista de referencia (de ahí se toman los niveles).";
            return;
        }

        var baseInfo = _ranges.GetViewRange(_uidoc.Document, vm.Id);
        if (baseInfo is null)
        {
            RangeStatus.Text = "La vista de referencia no es ViewPlan.";
            return;
        }

        // Offsets editados en las cajas (en pies); si no se pueden parsear, se usa el de la vista.
        double top = ParseOffset(TopOffsetBox.Text, baseInfo.TopOffset);
        double cut = ParseOffset(CutPlaneOffsetBox.Text, baseInfo.CutPlaneOffset);
        double bottom = ParseOffset(BottomOffsetBox.Text, baseInfo.BottomOffset);
        double depth = ParseOffset(ViewDepthOffsetBox.Text, baseInfo.ViewDepthOffset);

        var info = new ViewRangeInfo(
            baseInfo.TopLevelId, top, cut,
            baseInfo.BottomLevelId, bottom,
            baseInfo.ViewDepthLevelId, depth);

        var targetIds = _viewsForRange.Where(v => v.IsSelected).Select(v => v.Id).ToList();
        if (targetIds.Count == 0)
        {
            RangeStatus.Text = "Marca al menos una vista destino.";
            return;
        }

        var result = _ranges.ApplyViewRange(_uidoc.Document, info, targetIds);
        RangeStatus.Text = result.ToSummary();
    }

    private static double ParseOffset(string? text, double fallback) =>
        double.TryParse((text ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float,
            CultureInfo.InvariantCulture, out double v) ? v : fallback;

    // =========================================================================
    // Pestaña Diff
    // =========================================================================

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        if (DiffTemplateACombo.SelectedItem is not ViewTemplateInfo a ||
            DiffTemplateBCombo.SelectedItem is not ViewTemplateInfo b)
        {
            DiffStatus.Text = "Selecciona ambas plantillas.";
            return;
        }
        if (a.Id == b.Id)
        {
            DiffStatus.Text = "Las plantillas son la misma.";
            DiffGrid.ItemsSource = null;
            return;
        }
        var diff = _comparer.Compare(_uidoc.Document, a.Id, b.Id);
        DiffGrid.ItemsSource = diff.FieldDiffs;
        DiffStatus.Text = diff.AreIdentical
            ? "Las plantillas son funcionalmente idénticas (General, V/G overrides y filtros)."
            : $"{diff.FieldDiffs.Count} diferencia(s): General, V/G overrides y filtros.";
    }

    // =========================================================================
    // Cerrar
    // =========================================================================

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // =========================================================================
    // ViewModel simple para listas con checkbox
    // =========================================================================

    private sealed class ViewItemVm
    {
        public ViewItemVm(ElementId id, string name) { Id = id; Name = name; }
        public ElementId Id { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
        public override string ToString() => Name;
    }
}
