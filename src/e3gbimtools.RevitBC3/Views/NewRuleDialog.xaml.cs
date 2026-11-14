using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;
using E3G.BimTools.RevitBC3.Services;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Dialogo modal para crear o editar una regla de mapeo.
/// Soporta:
///  - Filtrado de categorias a las medibles.
///  - Sugerencias de valores para cada parametro (escaneando el modelo).
///  - Autocompletado del codigo BC3 desde el BC3 cargado.
/// </summary>
public partial class NewRuleDialog : Window
{
    private readonly Document _doc;
    private readonly IRevitParameterInspector _inspector;
    private readonly ObservableCollection<MappingFilterRow> _filterRows = new();
    private BuiltInCategory _selectedCategory = BuiltInCategory.INVALID;
    private CategoryParameters? _currentCategoryParams;

    /// <summary>
    /// Mapa codigo -> (codigoCapitulo, nombreCapitulo) construido al cargar
    /// el BC3 fuente.  Permite autocompletar el capitulo cuando el usuario
    /// selecciona una partida del dropdown.
    /// </summary>
    private readonly Dictionary<string, (string Code, string Name)> _chapterByCode =
        new(StringComparer.Ordinal);

    /// <summary>
    /// True mientras el usuario edita manualmente capitulo (codigo o nombre);
    /// usamos esta bandera para no sobrescribir su input cuando la seleccion
    /// del BC3 cambia.  Se resetea cuando el dialogo se carga por primera vez.
    /// </summary>
    private bool _chapterEditedByUser;

    /// <summary>Lista de nombres de parametros disponibles para los ComboBox del DataGrid.</summary>
    public ObservableCollection<string> ParameterOptions { get; } = new();

    /// <summary>Operadores soportados (para el ComboBox del DataGrid).</summary>
    public IReadOnlyList<MappingOperator> OperatorOptions { get; } =
        (MappingOperator[])Enum.GetValues(typeof(MappingOperator));

    /// <summary>La regla resultante (null si el usuario cancela).</summary>
    public MappingRule? Result { get; private set; }

    public NewRuleDialog(
        Document doc,
        IReadOnlyList<RevitCategoryInfo> categories,
        IRevitParameterInspector inspector,
        BC3Document? bc3Document,
        IEnumerable<string>? existingChapterNames,
        MappingRule? edit = null)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        E3gWindowBase.SetTitle(this, edit is null ? "Nueva regla" : "Editar regla");

        CategoryCombo.ItemsSource = categories;
        FiltersGrid.ItemsSource = _filterRows;

        // BC3: sugerencias de codigos + mapa codigo->capitulo.
        PopulateBC3Codes(bc3Document);
        BuildChapterMap(bc3Document);

        // Cuando el usuario teclea en los campos de capitulo, marcar que
        // ha editado manualmente (no autocompletar despues).
        ChapterCodeBox.TextChanged += (s, e) => { if (ChapterCodeBox.IsKeyboardFocusWithin) _chapterEditedByUser = true; };
        ChapterNameCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((s, e) =>
            {
                if (ChapterNameCombo.IsKeyboardFocusWithin) _chapterEditedByUser = true;
            }));

        // Auto-rellenar capitulo cuando el usuario selecciona un codigo BC3
        // del dropdown (solo si todavia no ha tecleado a mano nada de capitulo).
        BC3CodeCombo.SelectionChanged += OnBC3CodeSelectionChanged;

        // Capitulos: sugerencias a partir de los que ya hay en otras reglas.
        if (existingChapterNames is not null)
        {
            var distinct = existingChapterNames
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            ChapterNameCombo.ItemsSource = distinct;
        }

        _filterRows.CollectionChanged += (s, e) =>
        {
            if (e.NewItems is null) return;
            foreach (MappingFilterRow row in e.NewItems)
                row.PropertyChanged += OnFilterRowPropertyChanged;
        };

        if (edit is not null)
        {
            var match = categories.FirstOrDefault(c => c.BuiltInCategory == edit.Category);
            if (match is not null) CategoryCombo.SelectedItem = match;

            NameBox.Text = edit.Name;
            ChapterCodeBox.Text = edit.ChapterCode;
            ChapterNameCombo.Text = edit.ChapterName;
            // Si la regla ya tiene capitulo, marcamos como "editado" para no
            // pisarlo al cambiar el codigo BC3.
            _chapterEditedByUser =
                !string.IsNullOrWhiteSpace(edit.ChapterCode) ||
                !string.IsNullOrWhiteSpace(edit.ChapterName);
            PriorityBox.Text = edit.Priority.ToString(CultureInfo.InvariantCulture);
            FactorBox.Text = edit.UnitFactor.ToString("R", CultureInfo.InvariantCulture);
            NotesBox.Text = edit.Notes;

            if (BC3CodeCombo.ItemsSource is IEnumerable<BC3CodeOption> opts)
            {
                var sel = opts.FirstOrDefault(o => string.Equals(o.Code, edit.BC3Code, StringComparison.Ordinal));
                if (sel is not null) BC3CodeCombo.SelectedItem = sel;
                else BC3CodeCombo.Text = edit.BC3Code;
            }
            else
            {
                BC3CodeCombo.Text = edit.BC3Code;
            }

            foreach (var f in edit.Filters) _filterRows.Add(MappingFilterRow.From(f));
        }
    }

    // ============================================================
    // Categoria
    // ============================================================

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is not RevitCategoryInfo cat) return;
        _selectedCategory = cat.BuiltInCategory;
        ReloadParametersFor(cat.BuiltInCategory);

        // Refresca valores sugeridos de filtros existentes (cambia la categoria).
        foreach (var row in _filterRows)
            RefreshAvailableValues(row);
    }

    private void ReloadParametersFor(BuiltInCategory category)
    {
        _currentCategoryParams = _inspector.GetParameters(_doc, category);

        ParameterOptions.Clear();
        var combined = _currentCategoryParams.Instance
            .Select(p => p.Name)
            .Concat(_currentCategoryParams.Type.Select(p => p.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in combined) ParameterOptions.Add(name);

        // Lista de mediciones: built-in + parametros numericos.
        var measurementOptions = new List<string>
        {
            "Longitud (built-in)",
            "Area (built-in)",
            "Volumen (built-in)",
            "Cantidad (built-in)",
        };
        foreach (var p in _currentCategoryParams.Instance.Where(p => p.IsNumeric))
            measurementOptions.Add($"[Inst] {p.Name}");
        foreach (var p in _currentCategoryParams.Type.Where(p => p.IsNumeric))
            measurementOptions.Add($"[Tipo] {p.Name}");

        MeasurementCombo.ItemsSource = measurementOptions;
        if (MeasurementCombo.SelectedIndex < 0 && measurementOptions.Count > 0)
            MeasurementCombo.SelectedIndex = 0;
    }

    // ============================================================
    // BC3 codes
    // ============================================================

    private void PopulateBC3Codes(BC3Document? bc3Document)
    {
        if (bc3Document is null || bc3Document.Concepts.Count == 0)
        {
            BC3CodeCombo.ItemsSource = null;
            return;
        }

        var options = bc3Document.Concepts.Values
            .Where(c => !string.IsNullOrEmpty(c.Code))
            .Select(c => new BC3CodeOption
            {
                Code = c.Code,
                Summary = c.Summary,
                Unit = c.Unit,
            })
            .OrderBy(o => o.Code, StringComparer.Ordinal)
            .ToList();

        BC3CodeCombo.ItemsSource = options;
    }

    /// <summary>
    /// Devuelve el codigo BC3 a usar en la regla, mirando primero la seleccion
    /// del dropdown (SelectedValue, via SelectedValuePath="Code"), y si no hay,
    /// extrayendolo del texto que el usuario haya tecleado.
    /// </summary>
    private string GetBC3CodeFromCombo()
    {
        // Caso 1: el usuario eligio del dropdown -> SelectedValue es el Code.
        if (BC3CodeCombo.SelectedValue is string sel && !string.IsNullOrWhiteSpace(sel))
            return sel.Trim();

        // Caso 2: el usuario escribio (o el ComboBox muestra el Display largo).
        // Si parece un Display ("CODIGO · UD — resumen"), extrae solo el codigo.
        var text = BC3CodeCombo.Text?.Trim() ?? string.Empty;
        return ExtractCodeFromDisplay(text);
    }

    private static string ExtractCodeFromDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        // El primer separador (' ·' o ' —' o ' - ') marca el fin del codigo.
        var seps = new[] { " ·", " —", " - " };
        var minIdx = -1;
        foreach (var sep in seps)
        {
            var idx = t.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && (minIdx < 0 || idx < minIdx)) minIdx = idx;
        }
        return minIdx < 0 ? t : t.Substring(0, minIdx).Trim();
    }

    // ============================================================
    // Filtros (filas)
    // ============================================================

    private void OnAddFilter(object sender, RoutedEventArgs e)
    {
        _filterRows.Add(new MappingFilterRow { Operator = MappingOperator.Equals });
    }

    private void OnRemoveFilter(object sender, RoutedEventArgs e)
    {
        if (FiltersGrid.SelectedItem is MappingFilterRow row)
            _filterRows.Remove(row);
    }

    private void OnFilterRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MappingFilterRow row) return;
        if (e.PropertyName != nameof(MappingFilterRow.ParameterName)) return;
        RefreshAvailableValues(row);
    }

    private void RefreshAvailableValues(MappingFilterRow row)
    {
        row.AvailableValues.Clear();
        if (_selectedCategory == BuiltInCategory.INVALID) return;
        if (string.IsNullOrWhiteSpace(row.ParameterName)) return;

        try
        {
            var values = _inspector.GetParameterValues(_doc, _selectedCategory, row.ParameterName);
            foreach (var v in values) row.AvailableValues.Add(v);
        }
        catch
        {
            // sin sugerencias si algo falla (el usuario puede teclear el valor)
        }
    }

    // ============================================================
    // Save / Cancel
    // ============================================================

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Fuerza commit de la celda en edicion si la hay.
        FiltersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FiltersGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (CategoryCombo.SelectedItem is not RevitCategoryInfo cat)
        {
            MessageBox.Show(this, "Selecciona una categoria.", "Datos incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bc3Code = GetBC3CodeFromCombo();

        if (string.IsNullOrWhiteSpace(bc3Code))
        {
            MessageBox.Show(this, "Introduce o selecciona un codigo BC3.", "Datos incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PriorityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
            priority = 100;
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
            factor = 1.0;

        Result = new MappingRule
        {
            Category = cat.BuiltInCategory,
            CategoryDisplayName = cat.DisplayName,
            Name = NameBox.Text?.Trim() ?? string.Empty,
            ChapterCode = ChapterCodeBox.Text?.Trim() ?? string.Empty,
            ChapterName = ChapterNameCombo.Text?.Trim() ?? string.Empty,
            BC3Code = bc3Code,
            Priority = priority,
            UnitFactor = factor,
            Notes = NotesBox.Text?.Trim() ?? string.Empty,
            Measurement = ParseMeasurement(MeasurementCombo.SelectedItem as string),
            Filters = _filterRows
                .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName))
                .Select(r => r.ToFilter())
                .ToList(),
        };

        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Construye el mapa codigo->capitulo padre recorriendo la jerarquia
    /// del BC3.  Util para auto-rellenar el capitulo cuando el usuario
    /// selecciona una partida.
    /// </summary>
    private void BuildChapterMap(BC3Document? bc3Document)
    {
        _chapterByCode.Clear();
        if (bc3Document is null) return;

        foreach (var root in bc3Document.Roots)
            WalkChapterMap(root, currentChapter: null, bc3Document);
    }

    private void WalkChapterMap(BC3Concept node, BC3Concept? currentChapter, BC3Document doc)
    {
        if (node is null) return;

        var chapterForChildren = node.Type == ConceptType.Capitulo ? node : currentChapter;

        if (currentChapter is not null && !_chapterByCode.ContainsKey(node.Code))
            _chapterByCode[node.Code] = (currentChapter.Code, currentChapter.Summary ?? string.Empty);

        if (node.Type == ConceptType.Capitulo && !_chapterByCode.ContainsKey(node.Code))
            _chapterByCode[node.Code] = (node.Code, node.Summary ?? string.Empty);

        foreach (var dec in node.Children)
        {
            if (doc.Concepts.TryGetValue(dec.ChildCode, out var child))
                WalkChapterMap(child, chapterForChildren, doc);
        }
    }

    private void OnBC3CodeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // No autocompletar si el usuario ya escribio algo en capitulo.
        if (_chapterEditedByUser) return;
        if (BC3CodeCombo.SelectedItem is not BC3CodeOption opt) return;
        if (!_chapterByCode.TryGetValue(opt.Code, out var info)) return;

        // Rellenamos sin disparar la marca de "editado" (no tiene foco el usuario).
        var wasEdited = _chapterEditedByUser;
        ChapterCodeBox.Text = info.Code ?? string.Empty;
        ChapterNameCombo.Text = info.Name ?? string.Empty;
        _chapterEditedByUser = wasEdited;
    }

    private static MeasurementParameter ParseMeasurement(string? selection)
    {
        if (string.IsNullOrEmpty(selection)) return new MeasurementParameter();

        if (selection.StartsWith("Longitud")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Length };
        if (selection.StartsWith("Area"))     return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Area };
        if (selection.StartsWith("Volumen"))  return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Volume };
        if (selection.StartsWith("Cantidad")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Count };

        var pos = selection.IndexOf(']');
        var name = pos > 0 ? selection.Substring(pos + 1).Trim() : selection.Trim();
        return new MeasurementParameter
        {
            BuiltIn = BuiltInMeasurement.None,
            CustomParameterName = name,
        };
    }
}
