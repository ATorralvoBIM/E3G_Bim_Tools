using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.Core.Models.Parameters;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Dialogs;
using E3G.BimTools.DiTools.UI.Views.OneFilter;

namespace E3G.BimTools.DiTools.UI.Views.ParaManager;

/// <summary>
/// Ventana principal del ParaManager (Sprint 3).  Cuatro pestañas:
///  1. Lista de parámetros del documento (project + shared).
///  2. Crear parámetro nuevo y vincularlo a categorías.
///  3. Gestor del archivo de parámetros compartidos (.txt).
///  4. Editar valores de un parámetro en lote sobre toda una categoría.
/// </summary>
public partial class ParaManagerWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly Document _doc;
    private readonly IParaManagerService _paraManager;
    private readonly IFilterService _filter;
    private readonly IParameterValueBulkEditService _bulkEdit;

    private readonly ObservableCollection<ParameterRow> _rows = new();
    private readonly ObservableCollection<CategoryDescriptor> _allCategories = new();
    private readonly ObservableCollection<CategoryDescriptor> _shownCategories = new();
    private readonly ObservableCollection<BulkPreviewRow> _bulkPreview = new();

    private SharedParameterFileInfo? _sharedInfo;
    private List<ParameterDescriptor> _bulkParameterCache = new();

    public ParaManagerWindow(
        UIDocument uidoc,
        IParaManagerService paraManager,
        IFilterService filter,
        IParameterValueBulkEditService bulkEdit)
    {
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _doc = uidoc.Document;
        _paraManager = paraManager ?? throw new ArgumentNullException(nameof(paraManager));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _bulkEdit = bulkEdit ?? throw new ArgumentNullException(nameof(bulkEdit));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        ParamsGrid.ItemsSource = _rows;
        CategoriesList.ItemsSource = _shownCategories;
        BulkPreviewGrid.ItemsSource = _bulkPreview;

        // Inicializar combos.
        CreateDataType.ItemsSource = Enum.GetValues(typeof(ParameterDataTypeOption));
        CreateDataType.SelectedIndex = 0;
        CreateScope.ItemsSource = Enum.GetValues(typeof(ParameterScope));
        CreateScope.SelectedIndex = 0;
        CreatePropertyGroup.ItemsSource = new[]
        {
            "Datos", "Datos de identidad", "Texto", "Dimensiones",
            "Materiales y acabados", "Construccion", "Cotas y restricciones",
            "Fases", "Graficos", "General",
        };
        CreatePropertyGroup.SelectedIndex = 0;
        BulkScopeCombo.ItemsSource = Enum.GetValues(typeof(ParameterScope));
        BulkScopeCombo.SelectedIndex = 0;

        LoadCategories();
        LoadParameterList();
        LoadSharedFileTree();
        LoadBulkCategoryCombo();
    }

    // =====================================================================
    // Carga inicial
    // =====================================================================

    private void LoadCategories()
    {
        _allCategories.Clear();
        _shownCategories.Clear();
        try
        {
            foreach (var c in _paraManager.GetBindableCategories(_doc))
                _allCategories.Add(c);
            RefreshShownCategories();
        }
        catch (Exception ex) { ShowError("No se pudieron cargar las categorías.", ex); }
    }

    private void RefreshShownCategories()
    {
        var q = (CategorySearchBox?.Text ?? string.Empty).Trim();
        _shownCategories.Clear();
        foreach (var c in _allCategories)
        {
            if (string.IsNullOrWhiteSpace(q) ||
                c.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                _shownCategories.Add(c);
        }
    }

    private void OnCategorySearchChanged(object sender, TextChangedEventArgs e) => RefreshShownCategories();

    private void OnSelectAllCategoriesClick(object sender, RoutedEventArgs e)
    {
        foreach (var c in _allCategories) c.IsSelected = true;
        RefreshShownCategories();
    }

    private void OnSelectNoneCategoriesClick(object sender, RoutedEventArgs e)
    {
        foreach (var c in _allCategories) c.IsSelected = false;
        RefreshShownCategories();
    }

    private void LoadParameterList()
    {
        _rows.Clear();
        try
        {
            var parameters = _paraManager.ListProjectParameters(_doc);
            foreach (var p in parameters)
            {
                _rows.Add(new ParameterRow
                {
                    Underlying = p,
                    Name = p.Name,
                    Source = p.Source.ToString(),
                    Scope = p.Scope.ToString(),
                    GroupName = p.GroupName,
                    DataTypeName = p.DataTypeName,
                    CategoriesLabel = string.Join(", ", p.BoundCategoryNames.OrderBy(s => s)),
                });
            }
            ListInfoText.Text = $"{_rows.Count} parámetro(s) en el documento.";
        }
        catch (Exception ex) { ShowError("No se pudo leer la lista de parámetros.", ex); }
    }

    private void LoadSharedFileTree()
    {
        try
        {
            _sharedInfo = _paraManager.GetSharedParameterFile(_doc.Application);
            SharedFilePath.Text = _sharedInfo.Path;

            SharedTree.Items.Clear();
            foreach (var g in _sharedInfo.Groups)
            {
                var grpNode = new TreeViewItem { Header = $"📁 {g.Name}  ({g.Definitions.Count})", Tag = g, IsExpanded = false };
                foreach (var d in g.Definitions)
                    grpNode.Items.Add(new TreeViewItem { Header = $"• {d.Name}   [{d.DataTypeName}]", Tag = d });
                SharedTree.Items.Add(grpNode);
            }
            SharedStatus.Text = _sharedInfo.IsAvailable
                ? $"{_sharedInfo.Groups.Count} grupo(s) cargado(s)."
                : "No hay archivo de parámetros compartidos configurado.";
        }
        catch (Exception ex) { ShowError("No se pudo cargar el archivo de parámetros compartidos.", ex); }
    }

    private void LoadBulkCategoryCombo()
    {
        try
        {
            var cats = _filter.GetCategories(_doc).ToList();
            BulkCategoryCombo.ItemsSource = cats;
            BulkCategoryCombo.DisplayMemberPath = nameof(CategoryDescriptor.Name);
            if (cats.Count > 0) BulkCategoryCombo.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError("No se pudieron cargar las categorías para edición en lote.", ex); }
    }

    private void OnBulkCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BulkCategoryCombo.SelectedItem is not CategoryDescriptor cd) return;
        try
        {
            _bulkParameterCache = _filter.GetParametersForCategories(_doc, new[] { cd.BuiltInCategory }).ToList();
            BulkParamCombo.ItemsSource = _bulkParameterCache;
            BulkParamCombo.DisplayMemberPath = nameof(ParameterDescriptor.DisplayName);
            if (_bulkParameterCache.Count > 0) BulkParamCombo.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError("No se pudieron cargar los parámetros de la categoría.", ex); }
    }

    // =====================================================================
    // Tab 1 — Lista
    // =====================================================================

    private void OnReloadListClick(object sender, RoutedEventArgs e) => LoadParameterList();

    private void OnRecategorizeClick(object sender, RoutedEventArgs e)
    {
        if (ParamsGrid.SelectedItem is not ParameterRow row) { Info("Selecciona una fila."); return; }
        var current = row.Underlying;
        if (current is null) return;

        // Pre-marca las categorias que ya tiene.
        var preselect = new HashSet<long>(current.BoundCategoryIds);
        foreach (var c in _allCategories) c.IsSelected = preselect.Contains((long)c.BuiltInCategory);
        RefreshShownCategories();

        var msg = $"Marca las categorías a las que vinculas '{current.Name}' y pulsa OK.";
        if (MessageBox.Show(this, msg + "\n\n(En la pestaña 'Crear parámetro' a la derecha verás la selección actual.)",
                "ParaManager", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        var selected = _allCategories.Where(c => c.IsSelected).Select(c => c.BuiltInCategory).ToList();
        if (selected.Count == 0) { Info("No has marcado ninguna categoría."); return; }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var r = _paraManager.UpdateParameterBindings(_doc, current, selected, current.Scope);
            StatusText.Text = r.ToSummary();
            if (!r.Success) MessageBox.Show(this, r.ToSummary(), "ParaManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadParameterList();
        }
        catch (Exception ex) { ShowError("La recategorización falló.", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    private void OnDeleteParamClick(object sender, RoutedEventArgs e)
    {
        if (ParamsGrid.SelectedItem is not ParameterRow row) { Info("Selecciona una fila."); return; }
        var current = row.Underlying;
        if (current is null) return;

        if (MessageBox.Show(this,
                $"¿Eliminar '{current.Name}' del proyecto?\n\nSi es un parámetro compartido, su definición sigue en el .txt.",
                "ParaManager", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var r = _paraManager.DeleteProjectParameter(_doc, current);
            StatusText.Text = r.ToSummary();
            if (!r.Success) MessageBox.Show(this, r.ToSummary(), "ParaManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadParameterList();
        }
        catch (Exception ex) { ShowError("No se pudo eliminar el parámetro.", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    // =====================================================================
    // Tab 2 — Crear
    // =====================================================================

    private void OnCreateParamClick(object sender, RoutedEventArgs e)
    {
        var name = (CreateName.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) { Info("Indica un nombre para el parámetro."); return; }

        var selectedCats = _allCategories.Where(c => c.IsSelected).Select(c => c.BuiltInCategory).ToList();
        if (selectedCats.Count == 0) { Info("Marca al menos una categoría."); return; }

        var req = new CreateProjectParameterRequest
        {
            Name = name,
            DataType = (ParameterDataTypeOption)(CreateDataType.SelectedItem ?? ParameterDataTypeOption.Text),
            Scope = (ParameterScope)(CreateScope.SelectedItem ?? ParameterScope.Instance),
            CreateAsShared = CreateAsShared.IsChecked == true,
            SharedGroupName = (CreateSharedGroup.Text ?? "e3gbimtools").Trim(),
            PropertiesGroupName = (CreatePropertyGroup.SelectedItem as string) ?? "Datos",
            Categories = selectedCats,
        };

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var r = _paraManager.CreateProjectParameter(_doc, _doc.Application, req);
            CreateStatus.Text = r.ToSummary();
            StatusText.Text = r.ToSummary();
            if (!r.Success) MessageBox.Show(this, r.ToSummary(), "ParaManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadParameterList();
            LoadSharedFileTree();
        }
        catch (Exception ex) { ShowError("No se pudo crear el parámetro.", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    // =====================================================================
    // Tab 3 — Shared Parameter File
    // =====================================================================

    private void OnChangeSharedFileClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecciona el archivo de parámetros compartidos",
            Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        var r = _paraManager.SetSharedParameterFile(_doc.Application, dlg.FileName);
        StatusText.Text = r.ToSummary();
        if (!r.Success) MessageBox.Show(this, r.ToSummary(), "ParaManager", MessageBoxButton.OK, MessageBoxImage.Warning);
        LoadSharedFileTree();
    }

    private void OnReloadSharedFileClick(object sender, RoutedEventArgs e) => LoadSharedFileTree();

    private void OnNewGroupClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TextInputDialog("Nuevo grupo", "Nombre del grupo:", string.Empty) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var name = (dlg.Result ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) return;

        var r = _paraManager.AddGroupToSharedFile(_doc.Application, name);
        StatusText.Text = r.ToSummary();
        LoadSharedFileTree();
    }

    private void OnNewSharedDefinitionClick(object sender, RoutedEventArgs e)
    {
        // Recoge el grupo seleccionado (o pide uno).
        string? groupName = null;
        if (SharedTree.SelectedItem is TreeViewItem tvi)
        {
            groupName = tvi.Tag switch
            {
                SharedParameterGroupInfo g => g.Name,
                SharedParameterDefinitionInfo d => d.GroupName,
                _ => null,
            };
        }
        if (string.IsNullOrWhiteSpace(groupName))
        {
            var grpDlg = new TextInputDialog("Grupo destino", "Nombre del grupo (existente o nuevo):", "e3gbimtools") { Owner = this };
            if (grpDlg.ShowDialog() != true) return;
            groupName = (grpDlg.Result ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(groupName)) return;
        }

        var nameDlg = new TextInputDialog("Nuevo parámetro compartido", "Nombre del parámetro:", string.Empty) { Owner = this };
        if (nameDlg.ShowDialog() != true) return;
        var defName = (nameDlg.Result ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(defName)) return;

        var r = _paraManager.AddDefinitionToSharedFile(_doc.Application, groupName, defName, ParameterDataTypeOption.Text);
        StatusText.Text = r.ToSummary();
        LoadSharedFileTree();
    }

    private void OnBindSelectedSharedClick(object sender, RoutedEventArgs e)
    {
        if (SharedTree.SelectedItem is not TreeViewItem tvi || tvi.Tag is not SharedParameterDefinitionInfo def)
        {
            Info("Selecciona un parámetro del árbol para vincularlo.");
            return;
        }

        var selectedCats = _allCategories.Where(c => c.IsSelected).Select(c => c.BuiltInCategory).ToList();
        if (selectedCats.Count == 0)
        {
            Info("Marca las categorías a las que quieres vincular en la pestaña 'Crear parámetro' y vuelve aquí.");
            return;
        }

        var scope = (ParameterScope)(CreateScope.SelectedItem ?? ParameterScope.Instance);
        var grp = (CreatePropertyGroup.SelectedItem as string) ?? "Datos";

        var r = _paraManager.BindExistingSharedParameter(_doc, _doc.Application, def, scope, grp, selectedCats);
        StatusText.Text = r.ToSummary();
        if (!r.Success) MessageBox.Show(this, r.ToSummary(), "ParaManager", MessageBoxButton.OK, MessageBoxImage.Warning);
        LoadParameterList();
    }

    // =====================================================================
    // Tab 4 — Editar valores en lote
    // =====================================================================

    private void OnBulkPreviewClick(object sender, RoutedEventArgs e)
    {
        if (BulkCategoryCombo.SelectedItem is not CategoryDescriptor cd) { Info("Selecciona una categoría."); return; }
        if (BulkParamCombo.SelectedItem is not ParameterDescriptor pd) { Info("Selecciona un parámetro."); return; }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            _bulkPreview.Clear();
            var collector = new FilteredElementCollector(_doc)
                .OfCategory(cd.BuiltInCategory)
                .WhereElementIsNotElementType();

            int count = 0;
            foreach (var el in collector)
            {
                count++;
                Element target = el;
                if (pd.IsTypeParameter)
                {
                    var t = _doc.GetElement(el.GetTypeId());
                    if (t is null) continue;
                    target = t;
                }

                var p = target.LookupParameter(pd.Name);
                string current;
                try { current = p?.AsValueString() ?? p?.AsString() ?? string.Empty; }
                catch { current = string.Empty; }

                string family = string.Empty, type = string.Empty;
                try
                {
                    var typeEl = _doc.GetElement(el.GetTypeId()) as ElementType;
                    family = typeEl?.FamilyName ?? string.Empty;
                    type = typeEl?.Name ?? string.Empty;
                }
                catch { }

                _bulkPreview.Add(new BulkPreviewRow
                {
                    ElementId = el.Id.Value,
                    Name = el.Name ?? string.Empty,
                    Family = family,
                    Type = type,
                    CurrentValue = current,
                });
            }
            BulkInfoText.Text = $"{count:N0} elemento(s) en la categoría '{cd.Name}'. Parámetro '{pd.DisplayName}'.";
        }
        catch (Exception ex) { ShowError("La previsualización falló.", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    private void OnBulkApplyClick(object sender, RoutedEventArgs e)
    {
        if (BulkCategoryCombo.SelectedItem is not CategoryDescriptor cd) { Info("Selecciona una categoría."); return; }
        if (BulkParamCombo.SelectedItem is not ParameterDescriptor pd) { Info("Selecciona un parámetro."); return; }
        var raw = BulkValueBox.Text ?? string.Empty;
        var scope = (ParameterScope)(BulkScopeCombo.SelectedItem ?? ParameterScope.Instance);

        if (MessageBox.Show(this,
                $"Aplicar valor '{raw}' al parámetro '{pd.DisplayName}' en todos los elementos de '{cd.Name}'?\n\nUn solo Ctrl-Z deshace el cambio.",
                "ParaManager · Editar en lote",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var res = _bulkEdit.ApplyToCategory(_doc, cd.BuiltInCategory, pd.Name, raw, scope);
            StatusText.Text = "Editar en lote: " + res.ToSummary();
            if (res.Warnings.Count > 0)
                MessageBox.Show(this,
                    res.ToSummary() + "\n\nAvisos:\n  · " + string.Join("\n  · ", res.Warnings.Take(20)),
                    "ParaManager · Editar en lote",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            OnBulkPreviewClick(sender, e);
        }
        catch (Exception ex) { ShowError("La edición en lote falló.", ex); }
        finally { Mouse.OverrideCursor = null; }
    }

    // =====================================================================
    // Cerrar / utilidades
    // =====================================================================

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void Info(string text) =>
        MessageBox.Show(this, text, "ParaManager", MessageBoxButton.OK, MessageBoxImage.Information);

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

    // =====================================================================
    // Filas tabulares
    // =====================================================================

    /// <summary>Fila tabular de la pestaña 'Lista de parámetros'. Pública para que WPF enlace por reflexión.</summary>
    public sealed class ParameterRow
    {
        public ProjectParameterInfo? Underlying { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string DataTypeName { get; set; } = string.Empty;
        public string CategoriesLabel { get; set; } = string.Empty;
    }

    /// <summary>Fila tabular de la pestaña 'Editar valores en lote'.</summary>
    public sealed class BulkPreviewRow
    {
        public long ElementId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
    }
}
