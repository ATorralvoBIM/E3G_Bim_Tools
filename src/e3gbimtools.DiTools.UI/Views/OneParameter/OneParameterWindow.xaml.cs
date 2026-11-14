using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.OneParameter;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Views.OneFilter;

// Alias para evitar ambiguedad entre System.Windows.Controls.TextBox y Autodesk.Revit.UI.TextBox
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;

namespace E3G.BimTools.DiTools.UI.Views.OneParameter;

/// <summary>
/// Ventana principal de OneParameter (Sprint 10 — completo).
///
/// Tab 1 — Ver/Editar:
///   1. Escribir el parametro + elegir scope + (opcionalmente) categorias.
///   2. Buscar — el DataGrid muestra todos los elementos que lo tienen.
///   3. Editar celdas inline, usar "Editar en lote" o "Find &amp; Replace".
///   4. "Aplicar cambios" — una sola transaccion, Ctrl-Z deshace todo.
///
/// Tab 2 — Transformaciones:
///   1. Elegir operacion (SetValue, Prefix, Suffix, Concat, RegexReplace).
///   2. "Preview" — tabla Before/After sin escribir nada.
///   3. "Aplicar transformacion" — escribe en Revit con una transaccion.
/// </summary>
public partial class OneParameterWindow : Window
{
    // ------------------------------------------------------------------
    // Dependencias
    // ------------------------------------------------------------------
    private readonly UIDocument            _uidoc;
    private readonly Document              _doc;
    private readonly IOneParameterService  _service;
    private readonly IFilterService        _filterService;

    // ------------------------------------------------------------------
    // Estado local
    // ------------------------------------------------------------------
    private readonly ObservableCollection<OneParameterRow>     _rows     = new();
    private readonly ObservableCollection<CategoryItem>        _catItems = new();
    private readonly ObservableCollection<TransformPreviewRow> _preview  = new();
    private          OneParameterSearchResult?                 _lastResult;
    private          string?                                   _currentParamName;

    // ------------------------------------------------------------------
    // Ctor
    // ------------------------------------------------------------------
    public OneParameterWindow(
        UIDocument           uidoc,
        IOneParameterService service,
        IFilterService       filterService)
    {
        _uidoc         = uidoc         ?? throw new ArgumentNullException(nameof(uidoc));
        _doc           = uidoc.Document;
        _service       = service       ?? throw new ArgumentNullException(nameof(service));
        _filterService = filterService ?? throw new ArgumentNullException(nameof(filterService));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        ResultsGrid.ItemsSource = _rows;
        CatListBox.ItemsSource  = _catItems;
        PreviewGrid.ItemsSource = _preview;

        LoadParamNames();
        LoadCategories();
    }

    // ====================================================================
    // Inicializacion
    // ====================================================================

    private void LoadParamNames()
    {
        try
        {
            var names = _service.GetParameterNames(_doc);
            ParamNameCombo.ItemsSource = names;
        }
        catch (Exception ex)
        {
            ShowStatus($"Aviso: no se pudieron cargar nombres de parametro — {ex.Message}");
        }
    }

    private void LoadCategories()
    {
        _catItems.Clear();
        try
        {
            var cats = _filterService.GetCategories(_doc);
            foreach (var c in cats.OrderBy(c => c.Name))
                _catItems.Add(new CategoryItem
                {
                    Name       = c.Name,
                    Bic        = c.BuiltInCategory,
                    IsSelected = false,
                });
        }
        catch { /* silencioso */ }
    }

    // ====================================================================
    // Busqueda
    // ====================================================================

    private void OnSearch(object sender, RoutedEventArgs e) => ExecuteSearch();

    private void OnParamComboKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) ExecuteSearch();
    }

    private void ExecuteSearch()
    {
        var paramName = (ParamNameCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(paramName))
        {
            MessageBox.Show("Escribe el nombre del parametro que quieres buscar.",
                "OneParameter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _currentParamName = paramName;
        ShowStatus("Buscando...");
        _rows.Clear();
        UpdateActionButtons();

        try
        {
            var request = BuildSearchRequest(paramName);
            _lastResult = _service.Search(_doc, request);

            foreach (var row in _lastResult.Rows)
                _rows.Add(row);

            ShowStatus(_lastResult.ToSummary());

            if (_lastResult.Warnings.Count > 0)
                ShowStatus(string.Join("; ", _lastResult.Warnings));

            UpdateActionButtons();
        }
        catch (Exception ex)
        {
            ShowStatus($"Error en la busqueda: {ex.Message}");
        }
    }

    /// <summary>Construye la request leyendo scope y filtros de la UI.</summary>
    private OneParameterSearchRequest BuildSearchRequest(string paramName)
    {
        // Scope
        var scope = OneParameterScope.AllModel;
        ElementId? activeViewId = null;
        IReadOnlyList<ElementId>? selectedIds = null;

        if (ScopeCombo.SelectedItem is ComboBoxItem scopeItem)
        {
            switch (scopeItem.Tag as string)
            {
                case "CurrentView":
                    scope        = OneParameterScope.CurrentView;
                    activeViewId = _uidoc.ActiveView?.Id;
                    break;
                case "CurrentSelection":
                    scope       = OneParameterScope.CurrentSelection;
                    selectedIds = _uidoc.Selection.GetElementIds().ToList();
                    break;
            }
        }

        // Filtro de categorias
        var selectedCats = _catItems.Where(c => c.IsSelected).ToList();
        IReadOnlyList<BuiltInCategory>? catFilter = selectedCats.Count > 0
            ? selectedCats
                  .Where(c => c.Bic.HasValue)
                  .Select(c => c.Bic!.Value)
                  .ToList()
            : null;

        return new OneParameterSearchRequest
        {
            ParameterName       = paramName,
            Scope               = scope,
            ActiveViewId        = activeViewId,
            SelectedElementIds  = selectedIds,
            CategoryFilter      = catFilter,
            IncludeTypeElements = IncludeTypesCheck.IsChecked == true,
        };
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        _preview.Clear();
        _lastResult       = null;
        _currentParamName = null;
        ShowStatus("Escribe el nombre de un parametro y pulsa Buscar.");
        UpdateActionButtons();
    }

    // ====================================================================
    // Filtro de categorias (popup)
    // ====================================================================

    private void OnCatSearch(object sender, TextChangedEventArgs e)
    {
        var q = CatSearchBox.Text.Trim();
        CatListBox.ItemsSource = string.IsNullOrEmpty(q)
            ? (System.Collections.IEnumerable)_catItems
            : _catItems
                .Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void OnToggleAllCategories(object sender, RoutedEventArgs e)
    {
        bool allSelected = _catItems.All(c => c.IsSelected);
        foreach (var c in _catItems) c.IsSelected = !allSelected;
        CatListBox.Items.Refresh();
        UpdateCatFilterLabel();
    }

    private void OnCatCheckedChanged(object sender, RoutedEventArgs e)
        => UpdateCatFilterLabel();

    private void UpdateCatFilterLabel()
    {
        var selected = _catItems.Count(c => c.IsSelected);
        CatFilterToggle.Tag = selected == 0
            ? "Todas las categorias"
            : $"{selected} categoria(s) seleccionada(s)";
    }

    // ====================================================================
    // DataGrid Tab 1 — edicion inline
    // ====================================================================

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not OneParameterRow row) return;

        if (e.EditingElement is WpfTextBox tb)
        {
            var newVal = tb.Text;
            row.IsModified = !string.Equals(newVal, row.CurrentValue, StringComparison.Ordinal);
        }

        UpdateModifiedCount();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

    // ====================================================================
    // Acciones Tab 1
    // ====================================================================

    private void OnSelectInRevit(object sender, RoutedEventArgs e)
    {
        var ids = ResultsGrid.SelectedItems
            .OfType<OneParameterRow>()
            .Select(r => new ElementId(r.ElementId))
            .ToList();
        if (ids.Count == 0) return;
        try
        {
            _uidoc.Selection.SetElementIds(ids);
            ShowStatus($"{ids.Count} elemento(s) seleccionado(s) en Revit.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo seleccionar en Revit: {ex.Message}",
                "OneParameter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnBulkEdit(object sender, RoutedEventArgs e)
    {
        var targets = ResultsGrid.SelectedItems
            .OfType<OneParameterRow>()
            .Where(r => !r.IsReadOnly)
            .ToList();

        if (targets.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un elemento editable.",
                "Editar en lote", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new TextInputDialog(
            "Editar en lote",
            $"Nuevo valor para '{_currentParamName}' en {targets.Count} elemento(s):",
            targets[0].CurrentValue)
        { Owner = this };

        if (dlg.ShowDialog() != true) return;

        var newVal = dlg.Result;
        foreach (var row in targets)
        {
            row.EditedValue = newVal;
            row.IsModified  = !string.Equals(newVal, row.CurrentValue, StringComparison.Ordinal);
        }

        ResultsGrid.Items.Refresh();
        UpdateModifiedCount();
        ShowStatus($"Valor provisional asignado a {targets.Count} elemento(s). " +
                   "Pulsa 'Aplicar cambios' para escribir en Revit.");
    }

    private void OnFindReplace(object sender, RoutedEventArgs e)
    {
        var paramName = _currentParamName ?? (ParamNameCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(paramName))
        {
            MessageBox.Show("Escribe el nombre del parametro antes de usar Find & Replace.",
                "Find & Replace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new FindReplaceDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<BuiltInCategory>? catFilter = null;
            var selectedCats = _catItems.Where(c => c.IsSelected).ToList();
            if (selectedCats.Count > 0)
                catFilter = selectedCats
                    .Where(c => c.Bic.HasValue)
                    .Select(c => c.Bic!.Value)
                    .ToList();

            var result = _service.FindReplace(
                _doc, paramName,
                dlg.FindText, dlg.ReplaceText, dlg.MatchCase, catFilter);

            ShowStatus($"Find & Replace: {result.ToSummary()}");

            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", result.Warnings),
                    "Find & Replace — Avisos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

            if (_rows.Count > 0) ExecuteSearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error en Find & Replace: {ex.Message}",
                "OneParameter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Exportar resultados OneParameter",
            Filter     = "CSV (*.csv)|*.csv",
            FileName   = $"OneParameter_{_currentParamName}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            DefaultExt = ".csv",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID,Categoria,Familia,Tipo,Nombre,Nivel,Valor");
            foreach (var r in _rows)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(r.ElementId.ToString()),
                    CsvEscape(r.Category),
                    CsvEscape(r.Family),
                    CsvEscape(r.TypeName),
                    CsvEscape(r.ElementName),
                    CsvEscape(r.Level),
                    CsvEscape(r.EditedValue)));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowStatus($"CSV exportado: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al exportar CSV: {ex.Message}",
                "OneParameter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnApplyEdits(object sender, RoutedEventArgs e)
    {
        var pending = _rows.Where(r => r.IsModified).ToList();
        if (pending.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Se van a escribir {pending.Count} valor(es) en Revit.\n" +
            "Esta operacion se puede deshacer con Ctrl-Z.",
            "Aplicar cambios", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var result = _service.ApplyEdits(_doc, _rows.ToList());
            ResultsGrid.Items.Refresh();
            UpdateModifiedCount();
            ShowStatus($"Aplicado: {result.ToSummary()}");

            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", result.Warnings),
                    "Aplicar cambios — Avisos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al aplicar cambios: {ex.Message}",
                "OneParameter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDiscardEdits(object sender, RoutedEventArgs e)
    {
        int discarded = 0;
        foreach (var row in _rows.Where(r => r.IsModified).ToList())
        {
            row.EditedValue = row.CurrentValue;
            row.IsModified  = false;
            discarded++;
        }
        ResultsGrid.Items.Refresh();
        UpdateModifiedCount();
        if (discarded > 0)
            ShowStatus($"{discarded} cambio(s) descartado(s).");
    }

    // ====================================================================
    // Tab 2 — Transformaciones
    // ====================================================================

    private void OnOperationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OperationCombo?.SelectedItem is not ComboBoxItem item) return;

        var op = item.Tag as string ?? "SetValue";

        var isRegex  = op == "RegexReplace";
        var isSuffix = op is "Suffix" or "Concat";
        var isPrefix = op is "Prefix" or "Concat" or "SetValue";

        SimplePanel.Visibility = isRegex ? WpfVisibility.Collapsed : WpfVisibility.Visible;
        RegexPanel.Visibility  = isRegex ? WpfVisibility.Visible   : WpfVisibility.Collapsed;

        if (!isRegex)
        {
            PrefixLabel.Text       = op == "SetValue" ? "Valor nuevo:" : "Prefijo:";
            PrefixLabel.Visibility = isPrefix ? WpfVisibility.Visible   : WpfVisibility.Collapsed;
            PrefixBox.Visibility   = isPrefix ? WpfVisibility.Visible   : WpfVisibility.Collapsed;
            SuffixLabel.Visibility = isSuffix ? WpfVisibility.Visible   : WpfVisibility.Collapsed;
            SuffixBox.Visibility   = isSuffix ? WpfVisibility.Visible   : WpfVisibility.Collapsed;
        }

        _preview.Clear();
        ApplyTransformBtn.IsEnabled = false;
        TransformStatusText.Text    = string.Empty;
    }

    private void OnTransformPreview(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("Busca primero un parametro para poder hacer el preview.",
                "Transformaciones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var request = BuildTransformRequest();
            if (request is null) return;

            var targets       = GetTransformTargets();
            var previewResult = _service.PreviewTransform(targets, request);

            _preview.Clear();
            foreach (var row in previewResult.Rows)
                _preview.Add(row);

            var changeCount = previewResult.WillChangeCount;
            TransformStatusText.Text    = $"Preview: {previewResult.ToSummary()}";
            ApplyTransformBtn.IsEnabled = changeCount > 0;

            ShowStatus($"Preview listo: {changeCount} elemento(s) cambiaran.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error en el preview: {ex.Message}",
                "Transformaciones", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnApplyTransform(object sender, RoutedEventArgs e)
    {
        var changeCount = _preview.Count(r => r.WillChange);
        if (changeCount == 0) return;

        var confirm = MessageBox.Show(
            $"Se van a modificar {changeCount} elemento(s) en Revit.\n" +
            "Esta operacion se puede deshacer con Ctrl-Z.",
            "Aplicar transformacion", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var request = BuildTransformRequest();
            if (request is null) return;

            var targets = GetTransformTargets();
            var result  = _service.ApplyTransform(_doc, targets, request);

            ResultsGrid.Items.Refresh();
            _preview.Clear();
            ApplyTransformBtn.IsEnabled = false;
            TransformStatusText.Text    = $"Aplicado: {result.ToSummary()}";
            ShowStatus($"Transformacion aplicada: {result.ToSummary()}");

            if (result.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", result.Warnings),
                    "Transformacion — Avisos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al aplicar transformacion: {ex.Message}",
                "Transformaciones", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TransformRequest? BuildTransformRequest()
    {
        if (string.IsNullOrEmpty(_currentParamName))
        {
            MessageBox.Show("Busca primero un parametro antes de usar las transformaciones.",
                "Transformaciones", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var opTag = (OperationCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "SetValue";
        var op = opTag switch
        {
            "Prefix"       => TransformOperation.Prefix,
            "Suffix"       => TransformOperation.Suffix,
            "Concat"       => TransformOperation.Concat,
            "RegexReplace" => TransformOperation.RegexReplace,
            _              => TransformOperation.SetValue,
        };

        return new TransformRequest
        {
            ParameterName = _currentParamName,
            Operation     = op,
            Prefix        = PrefixBox.Text      ?? string.Empty,
            Suffix        = SuffixBox.Text      ?? string.Empty,
            Pattern       = PatternBox.Text     ?? string.Empty,
            Replacement   = ReplacementBox.Text ?? string.Empty,
            MatchCase     = RegexMatchCaseCheck.IsChecked == true,
        };
    }

    private IReadOnlyList<OneParameterRow> GetTransformTargets()
    {
        if (TransformSelectedRadio.IsChecked == true)
        {
            var selected = ResultsGrid.SelectedItems
                .OfType<OneParameterRow>()
                .ToList();
            if (selected.Count > 0) return selected;
        }
        return _rows.ToList();
    }

    // ====================================================================
    // Helpers de estado
    // ====================================================================

    private void UpdateActionButtons()
    {
        bool hasRows     = _rows.Count > 0;
        bool hasSelected = ResultsGrid.SelectedItems.Count > 0;
        int  modCount    = _rows.Count(r => r.IsModified);

        SelectRevitBtn.IsEnabled = hasSelected;
        BulkEditBtn.IsEnabled    = hasSelected;
        FindReplaceBtn.IsEnabled = !string.IsNullOrEmpty(_currentParamName) || hasRows;
        ExportCsvBtn.IsEnabled   = hasRows;
        ApplyEditsBtn.IsEnabled  = modCount > 0;
        ApplyEditsBtn.Content    = $"Aplicar cambios ({modCount})";
    }

    private void UpdateModifiedCount()
    {
        int mod = _rows.Count(r => r.IsModified);
        ModifiedText.Text       = mod > 0 ? $"{mod} edicion(es) pendiente(s)" : string.Empty;
        ModifiedText.Visibility = mod > 0 ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        UpdateActionButtons();
    }

    private void ShowStatus(string message) => StatusText.Text = message;

    private static string CsvEscape(string? value)
    {
        if (value is null) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ====================================================================
    // Clase auxiliar para el ListBox de categorias
    // ====================================================================

    public sealed class CategoryItem
    {
        public string           Name       { get; set; } = string.Empty;
        public BuiltInCategory? Bic        { get; set; }
        public bool             IsSelected { get; set; }
    }
}
