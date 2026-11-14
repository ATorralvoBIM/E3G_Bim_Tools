using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Dialogs;

namespace E3G.BimTools.DiTools.UI.Views.OneFilter;

/// <summary>
/// Ventana principal del OneFilter (Sprint 2).  Permite al usuario:
///  - Seleccionar una o varias categorias de Revit.
///  - Construir un arbol de condiciones AND/OR (anidacion ilimitada).
///  - Ejecutar el filtro y ver los resultados en una grilla.
///  - Aplicar acciones a los resultados: seleccionar, aislar/ocultar en vista,
///    override de color, editar parametro en lote, exportar a Excel (CSV).
///  - Guardar y cargar plantillas JSON.
///
/// La ventana se abre MODAL para garantizar que los cambios (overrides,
/// edicion en lote) son atomicos respecto a la interaccion del usuario.
/// </summary>
public partial class OneFilterWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly Document _doc;
    private readonly IFilterService _filter;
    private readonly IFilterTemplateService _templates;
    private readonly IFilterBatchEditService _batch;
    private readonly IExcelService _excel;

    private readonly ObservableCollection<CategoryDescriptor> _allCategories = new();
    private readonly ObservableCollection<CategoryDescriptor> _shownCategories = new();
    private readonly ObservableCollection<FilterRow> _resultRows = new();

    private FilterResult? _lastResult;

    public OneFilterWindow(
        UIDocument uidoc,
        IFilterService filter,
        IFilterTemplateService templates,
        IFilterBatchEditService batch,
        IExcelService excel)
    {
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _doc = uidoc.Document;
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
        _excel = excel ?? throw new ArgumentNullException(nameof(excel));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        ResultsGrid.ItemsSource = _resultRows;
        CategoriesList.ItemsSource = _shownCategories;

        LoadCategories();
        RefreshTemplatesCombo();

        // Inicializa el editor de grupos con un grupo raiz vacio.
        RootGroupEditor.Initialize(
            new FilterGroup(),
            Array.Empty<ParameterDescriptor>(),
            allowRemove: false);
    }

    // =====================================================================
    // Carga inicial de categorias y plantillas
    // =====================================================================

    private void LoadCategories()
    {
        _allCategories.Clear();
        _shownCategories.Clear();
        try
        {
            foreach (var c in _filter.GetCategories(_doc))
                _allCategories.Add(c);
            foreach (var c in _allCategories)
                _shownCategories.Add(c);
        }
        catch (Exception ex)
        {
            ShowError("No se pudieron cargar las categorías del documento.", ex);
        }
    }

    private void RefreshTemplatesCombo()
    {
        try
        {
            var names = _templates.ListTemplates();
            TemplatesCombo.ItemsSource = names;
            if (names.Count > 0) TemplatesCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowError("No se pudieron listar las plantillas guardadas.", ex);
        }
    }

    // =====================================================================
    // Categorias (busqueda + seleccion)
    // =====================================================================

    private void OnCategorySearchChanged(object sender, TextChangedEventArgs e) => RefreshShownCategories();

    private void RefreshShownCategories()
    {
        var query = (CategorySearchBox.Text ?? string.Empty).Trim();
        _shownCategories.Clear();
        foreach (var c in _allCategories)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                c.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                _shownCategories.Add(c);
            }
        }
    }

    private void OnCategoryToggled(object sender, RoutedEventArgs e)
    {
        // Cuando cambia la seleccion de categorias, refrescamos los parametros
        // disponibles en los combos del editor.
        var selectedBics = _allCategories
            .Where(c => c.IsSelected)
            .Select(c => c.BuiltInCategory)
            .ToList();

        IReadOnlyList<ParameterDescriptor> params_ = Array.Empty<ParameterDescriptor>();
        if (selectedBics.Count > 0)
        {
            try
            {
                params_ = _filter.GetParametersForCategories(_doc, selectedBics);
            }
            catch (Exception ex)
            {
                ShowError("No se pudieron cargar parámetros de las categorías seleccionadas.", ex);
            }
        }

        RootGroupEditor.UpdateParameterChoices(params_);
        StatusText.Text = selectedBics.Count == 0
            ? "Selecciona una o más categorías y añade condiciones, luego pulsa Ejecutar."
            : $"{selectedBics.Count} categoría(s) seleccionada(s) · {params_.Count} parámetro(s) disponibles.";
    }

    // =====================================================================
    // Ejecutar filtro
    // =====================================================================

    private void OnRunFilterClick(object sender, RoutedEventArgs e)
    {
        var def = BuildCurrentDefinition();
        if (def.CategoryIds.Count == 0)
        {
            MessageBox.Show(this, "Marca al menos una categoría antes de ejecutar el filtro.",
                "OneFilter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            _lastResult = _filter.Evaluate(_doc, def);
            var rows = _filter.BuildRows(_doc, _lastResult.MatchedIds);

            _resultRows.Clear();
            foreach (var r in rows) _resultRows.Add(r);

            ResultsCountText.Text = _resultRows.Count.ToString("N0", CultureInfo.GetCultureInfo("es-ES")) + " resultado(s)";
            StatusText.Text = _lastResult.ToSummary();

            if (_lastResult.Warnings.Count > 0)
                MessageBox.Show(this,
                    _lastResult.ToSummary() + "\n\nAvisos:\n  · " +
                    string.Join("\n  · ", _lastResult.Warnings.Take(20)),
                    "OneFilter · con avisos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowError("La evaluación del filtro falló.", ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private FilterDefinition BuildCurrentDefinition()
    {
        var def = new FilterDefinition
        {
            Root = RootGroupEditor.Group,
            CategoryIds = _allCategories
                .Where(c => c.IsSelected)
                .Select(c => (int)c.BuiltInCategory)
                .ToList(),
        };
        return def;
    }

    // =====================================================================
    // Acciones sobre resultados
    // =====================================================================

    private List<ElementId> GetResultIds()
    {
        if (_lastResult is null) return new List<ElementId>();
        return _lastResult.MatchedIds.ToList();
    }

    private void OnSelectInModelClick(object sender, RoutedEventArgs e)
    {
        var ids = GetResultIds();
        if (ids.Count == 0) { Info("No hay resultados para seleccionar."); return; }
        try
        {
            _uidoc.Selection.SetElementIds(ids);
            StatusText.Text = $"{ids.Count} elemento(s) seleccionado(s) en el modelo.";
        }
        catch (Exception ex) { ShowError("No se pudo cambiar la selección.", ex); }
    }

    private void OnIsolateClick(object sender, RoutedEventArgs e) => ApplyViewIsolation(isolate: true);
    private void OnHideClick(object sender, RoutedEventArgs e) => ApplyViewIsolation(isolate: false);

    private void ApplyViewIsolation(bool isolate)
    {
        var ids = GetResultIds();
        if (ids.Count == 0) { Info("No hay resultados sobre los que actuar."); return; }

        var view = _doc.ActiveView;
        if (view is null || !view.CanUseTemporaryVisibilityModes())
        {
            Info("La vista activa no admite aislamiento/ocultacion temporal.");
            return;
        }

        try
        {
            using var tx = new Transaction(_doc, isolate ? "OneFilter: Aislar en vista" : "OneFilter: Ocultar en vista");
            tx.Start();
            if (isolate)
                view.IsolateElementsTemporary(ids);
            else
                view.HideElementsTemporary(ids);
            tx.Commit();
            StatusText.Text = isolate
                ? $"{ids.Count} elemento(s) aislado(s) temporalmente en la vista."
                : $"{ids.Count} elemento(s) ocultado(s) temporalmente en la vista.";
        }
        catch (Exception ex)
        {
            ShowError(isolate ? "No se pudo aislar en la vista." : "No se pudo ocultar en la vista.", ex);
        }
    }

    private void OnOverrideColorClick(object sender, RoutedEventArgs e)
    {
        var ids = GetResultIds();
        if (ids.Count == 0) { Info("No hay resultados sobre los que actuar."); return; }

        var dlg = new SimpleColorDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var revitColor = new Autodesk.Revit.DB.Color(dlg.R, dlg.G, dlg.B);
        var view = _doc.ActiveView;
        if (view is null) { Info("No hay vista activa."); return; }

        try
        {
            using var tx = new Transaction(_doc, "OneFilter: Override color");
            tx.Start();
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(revitColor);
            ogs.SetSurfaceForegroundPatternColor(revitColor);
            try
            {
                var solidId = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id;
                if (solidId is not null && solidId != ElementId.InvalidElementId)
                    ogs.SetSurfaceForegroundPatternId(solidId);
            }
            catch { /* sin solid fill: solo cambia color de linea */ }

            foreach (var id in ids)
                view.SetElementOverrides(id, ogs);
            tx.Commit();
            StatusText.Text = $"Override de color aplicado a {ids.Count} elemento(s) en la vista activa.";
        }
        catch (Exception ex) { ShowError("No se pudo aplicar el override de color.", ex); }
    }

    private void OnClearOverridesClick(object sender, RoutedEventArgs e)
    {
        var ids = GetResultIds();
        if (ids.Count == 0) { Info("No hay resultados sobre los que actuar."); return; }
        var view = _doc.ActiveView;
        if (view is null) { Info("No hay vista activa."); return; }
        try
        {
            using var tx = new Transaction(_doc, "OneFilter: Quitar overrides");
            tx.Start();
            var empty = new OverrideGraphicSettings();
            foreach (var id in ids) view.SetElementOverrides(id, empty);
            tx.Commit();
            StatusText.Text = $"Overrides retirados de {ids.Count} elemento(s).";
        }
        catch (Exception ex) { ShowError("No se pudieron retirar los overrides.", ex); }
    }

    private void OnBatchEditClick(object sender, RoutedEventArgs e)
    {
        var ids = GetResultIds();
        if (ids.Count == 0) { Info("No hay resultados sobre los que actuar."); return; }

        var dlg = new BatchEditDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var op = new BatchEditOperation
        {
            ParameterName = dlg.ParameterName,
            NewValue = dlg.NewValue,
            ApplyToType = dlg.ApplyToType,
        };

        try
        {
            var res = _batch.ApplyBatch(_doc, ids, op);
            StatusText.Text = "BatchEdit: " + res.ToSummary();
            if (res.Warnings.Count > 0)
                MessageBox.Show(this,
                    res.ToSummary() + "\n\nAvisos:\n  · " + string.Join("\n  · ", res.Warnings.Take(20)),
                    "OneFilter · BatchEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError("La edición en lote falló.", ex); }
    }

    private void OnExportExcelClick(object sender, RoutedEventArgs e)
    {
        if (_resultRows.Count == 0) { Info("No hay resultados que exportar."); return; }

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar resultados OneFilter",
            Filter = "CSV separado por punto y coma (*.csv)|*.csv|Excel (xlsx)|*.xlsx",
            FileName = $"OneFilter_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (sfd.ShowDialog(this) != true) return;

        var ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();

        try
        {
            if (ext == ".xlsx")
            {
                var filas = _resultRows.Select(r => new ExportRow
                {
                    ID = r.ElementId,
                    Categoria = r.Category,
                    Familia = r.Family,
                    Tipo = r.Type,
                    Nombre = r.Name,
                    Nivel = r.LevelName,
                }).ToList();
                _excel.WriteSheet(sfd.FileName, "OneFilter", filas);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("ID;Categoria;Familia;Tipo;Nombre;Nivel");
                foreach (var r in _resultRows)
                    sb.AppendLine($"{r.ElementId};{Csv(r.Category)};{Csv(r.Family)};{Csv(r.Type)};{Csv(r.Name)};{Csv(r.LevelName)}");
                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            StatusText.Text = $"Exportado a: {sfd.FileName}";
        }
        catch (Exception ex) { ShowError("La exportación falló.", ex); }
    }

    /// <summary>Fila para el export XLSX (los nombres de propiedad son las cabeceras).</summary>
    private sealed class ExportRow
    {
        public long ID { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string Familia { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Nivel { get; set; } = string.Empty;
    }

    private static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

    // =====================================================================
    // Plantillas
    // =====================================================================

    private void OnLoadTemplateClick(object sender, RoutedEventArgs e)
    {
        if (TemplatesCombo.SelectedItem is not string name) { Info("Selecciona una plantilla de la lista."); return; }
        var def = _templates.Load(name);
        if (def is null)
        {
            MessageBox.Show(this, $"La plantilla '{name}' no existe o está corrupta.",
                "OneFilter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ApplyDefinition(def);
        StatusText.Text = $"Plantilla '{name}' cargada.";
    }

    private void OnSaveTemplateClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TextInputDialog("Guardar plantilla OneFilter", "Nombre de la plantilla:", TemplatesCombo.Text ?? string.Empty)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;
        var name = (dlg.Result ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var def = BuildCurrentDefinition();
            _templates.Save(name, def);
            RefreshTemplatesCombo();
            TemplatesCombo.SelectedItem = name;
            StatusText.Text = $"Plantilla '{name}' guardada.";
        }
        catch (Exception ex) { ShowError("No se pudo guardar la plantilla.", ex); }
    }

    private void OnDeleteTemplateClick(object sender, RoutedEventArgs e)
    {
        if (TemplatesCombo.SelectedItem is not string name) { Info("Selecciona una plantilla."); return; }
        if (MessageBox.Show(this, $"¿Eliminar la plantilla '{name}'?", "OneFilter",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_templates.Delete(name))
        {
            RefreshTemplatesCombo();
            StatusText.Text = $"Plantilla '{name}' eliminada.";
        }
    }

    private void ApplyDefinition(FilterDefinition def)
    {
        // Marca/desmarca categorias.
        var wanted = new HashSet<int>(def.CategoryIds);
        foreach (var c in _allCategories) c.IsSelected = wanted.Contains((int)c.BuiltInCategory);
        // Recarga la lista visible para reflejar cambios del checkbox.
        RefreshShownCategories();
        // Re-aplica filtros aguas abajo (parametros, editor).
        OnCategoryToggled(this, new RoutedEventArgs());
        RootGroupEditor.Initialize(def.Root ?? new FilterGroup(),
            _filter.GetParametersForCategories(_doc,
                _allCategories.Where(c => c.IsSelected).Select(c => c.BuiltInCategory).ToList()),
            allowRemove: false);
    }

    // =====================================================================
    // Cerrar / utilidades
    // =====================================================================

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Info(string text) =>
        MessageBox.Show(this, text, "OneFilter", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowError(string title, Exception ex)
    {
        try
        {
            var dlg = FriendlyErrorDialog.ForException(ex);
            dlg.Owner = this;
            dlg.ShowDialog();
        }
        catch
        {
            MessageBox.Show(this, $"{title}\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
