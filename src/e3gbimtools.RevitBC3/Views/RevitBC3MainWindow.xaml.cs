using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.RevitBC3.Mapping;
using E3G.BimTools.RevitBC3.Models;
using E3G.BimTools.RevitBC3.Services;
using Microsoft.Win32;
using SystemVisibility = System.Windows.Visibility;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Ventana principal del modulo Revit -> BC3.
///
/// Replanteada en mayo-2026 sobre la base del mockup UI: toolbar superior,
/// arbol BC3 a la izquierda, tabla de reglas (con sub-tabs Mapeo/Huerfanos)
/// en el centro, panel editor de regla colapsable a la derecha, franja
/// inferior fija con KPIs + tabla de mediciones (override editable).
///
/// Toda la logica de servicios (importer, mapping engine, extractor, writer,
/// persistencia) se mantiene; solo cambia el cableado de la UI.
/// </summary>
public partial class RevitBC3MainWindow : Window
{
    private readonly IBC3ImporterService _importer;
    private readonly IRevitCategoryProvider _categoryProvider;
    private readonly IRevitParameterInspector _parameterInspector;
    private readonly IMappingPersistenceService _persistence;
    private readonly IMappingRuleEngine _ruleEngine;
    private readonly IRevitDataExtractor _dataExtractor;
    private readonly IBC3WriterService _bc3Writer;
    private readonly IBimlinkExporter _bimlinkExporter;
    private readonly IBC3IfcExportPrepService _ifcExportPrep;

    // ExternalEvent para ejecutar WriteToModel en contexto de API valido
    // (la ventana es modeless → los handlers WPF estan fuera del contexto Revit).
    private readonly PrepareIfcEventHandler _prepIfcHandler = new();
    private ExternalEvent? _prepIfcEvent;  // se crea en el constructor si uiApp != null

    private readonly UIApplication? _uiApp;
    private readonly ObservableCollection<MappingRule> _rules = new();
    private readonly ObservableCollection<MeasurementResult> _measurements = new();
    private readonly ObservableCollection<OrphanGroup> _orphans = new();
    private readonly HashSet<string> _bc3CodesIndex = new(StringComparer.Ordinal);

    // ---- Sprint B: capitulos y subcapitulos como entidades de primera clase ----
    private readonly ObservableCollection<Chapter> _chapters = new();
    private readonly ObservableCollection<SubChapter> _subChapters = new();

    // Raices del arbol de capitulos (lo que ExportTree muestra como ItemsSource).
    private readonly ObservableCollection<MappingTreeNode> _chapterTreeRoots = new();
    // Nodo "Sin asignar" (singleton).  Solo se muestra si hay reglas huerfanas.
    private readonly UnassignedTreeNode _unassignedNode = new() { Header = "Sin asignar" };

    private BC3Document? _currentDocument;
    private string _lastBC3Path = string.Empty;
    private string _lastExportPath = string.Empty;
    private List<RevitCategoryInfo>? _cachedCategories;
    private ExtractionResult? _lastExtraction;

    // ----- Editor panel state -----
    private MappingRule? _editingRule;             // null = panel cerrado
    private bool _editingIsNew;                    // true si el panel crea regla nueva
    private CategoryParameters? _editorCategoryParams;
    private readonly Dictionary<string, (string Code, string Name)> _editorChapterByCode = new(StringComparer.Ordinal);
    private bool _editorChapterEditedByUser;
    private readonly ObservableCollection<MappingFilterRow> _editorFilters = new();

    /// <summary>Lista de parametros disponibles segun la categoria elegida en el editor.</summary>
    public ObservableCollection<string> EditorParameterOptions { get; } = new();

    /// <summary>Operadores expuestos para los ComboBox del editor.</summary>
    public IReadOnlyList<MappingOperator> OperatorOptions { get; } =
        (MappingOperator[])Enum.GetValues(typeof(MappingOperator));

    public RevitBC3MainWindow() : this(null) { }

    public RevitBC3MainWindow(UIApplication? uiApp)
    {
        _uiApp = uiApp;

        // Crear el ExternalEvent aqui, dentro de Execute() → contexto de API valido.
        // Solo si hay uiApp real (evita fallo en el diseñador WPF).
        if (uiApp is not null)
            _prepIfcEvent = ExternalEvent.Create(_prepIfcHandler);

        _importer = new BC3ImporterService();
        _categoryProvider = new RevitCategoryProvider();
        _parameterInspector = new RevitParameterInspector();
        _persistence = new MappingPersistenceService();
        _ruleEngine = new MappingRuleEngine();
        _dataExtractor = new RevitDataExtractor(_ruleEngine);
        _bc3Writer = new BC3WriterService();
        _bimlinkExporter = new BimlinkExporter();
        _ifcExportPrep = new BC3IfcExportPrepService();

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        E3gWindowBase.SetTitle(this, "Revit → BC3");

        // ---------- bindings ----------
        // Reglas con agrupacion doble: Capitulo > Subcapitulo (issue #4).
        var rulesView = new ListCollectionView(_rules);
        rulesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MappingRule.ChapterDisplay)));
        rulesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MappingRule.SubChapterDisplay)));
        rulesView.SortDescriptions.Add(new SortDescription(nameof(MappingRule.ChapterDisplay), ListSortDirection.Ascending));
        rulesView.SortDescriptions.Add(new SortDescription(nameof(MappingRule.SubChapterDisplay), ListSortDirection.Ascending));
        rulesView.SortDescriptions.Add(new SortDescription(nameof(MappingRule.Priority), ListSortDirection.Descending));
        RulesGrid.ItemsSource = rulesView;

        // Cuando cambian las reglas o las mediciones, repintar el arbol de salida.
        _rules.CollectionChanged += (s, e) => RefreshExportTree();
        _measurements.CollectionChanged += (s, e) => RefreshExportTree();
        _chapters.CollectionChanged += (s, e) => RefreshExportTree();
        _subChapters.CollectionChanged += (s, e) => RefreshExportTree();

        // Sprint B: enlazar el arbol y el handler de drag-and-drop.
        ExportTree.ItemsSource = _chapterTreeRoots;
        GongSolutions.Wpf.DragDrop.DragDrop.SetDropHandler(
            ExportTree, new ChapterTreeDropHandler(this));

        // Mediciones en la franja inferior (override editable).
        ReviewGrid.ItemsSource = _measurements;
        ReviewGrid.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateKpis));
        _measurements.CollectionChanged += (s, e) => UpdateKpis();

        // Huerfanos.
        OrphansGrid.ItemsSource = _orphans;
        _orphans.CollectionChanged += (s, e) => UpdateKpis();

        // Filas de filtros del editor.
        EditorFiltersList.ItemsSource = _editorFilters;
        _editorFilters.CollectionChanged += (s, e) =>
        {
            if (e.NewItems is null) return;
            foreach (MappingFilterRow row in e.NewItems)
                row.PropertyChanged += OnEditorFilterRowPropertyChanged;
        };

        // Carga inicial (reglas + ultimo BC3).
        InitialLoadFromDefaults();
        UpdateKpis();
    }

    private Document? ActiveDocument => _uiApp?.ActiveUIDocument?.Document;

    // ============================================================
    // Carga inicial
    // ============================================================

    private void InitialLoadFromDefaults()
    {
        try
        {
            var file = _persistence.Load(_persistence.DefaultPath);
            LoadMappingFileIntoState(file);
            _lastBC3Path = file.LastBC3Path ?? string.Empty;

            if (!string.IsNullOrEmpty(_lastBC3Path) && File.Exists(_lastBC3Path))
            {
                ImportBc3From(_lastBC3Path);
            }
            else
            {
                _lastBC3Path = string.Empty;
                UpdateHeaderBC3Badge();
                UpdateStatusBar();
                UpdateMappingBC3InfoText();
            }
        }
        catch
        {
            // sin reglas al arranque
        }
        RecomputeRuleConflicts();     // Sprint A: marca duplicados ya al cargar.
        UpdateRulesSummary();
        RefreshExportTree();
    }

    // ============================================================
    // Importacion BC3
    // ============================================================

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar archivo BC3 (FIEBDC-3)",
            Filter = "Archivos BC3 (*.bc3)|*.bc3|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = SafeDirectoryOf(_lastBC3Path),
        };

        if (dialog.ShowDialog(this) != true) return;
        ImportBc3From(dialog.FileName);
        TrySaveRulesSilent(_persistence.DefaultPath);
    }

    private void ImportBc3From(string filePath)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ImportButton.IsEnabled = false;

            _currentDocument = _importer.Load(filePath);
            _lastBC3Path = filePath;

            RebuildBC3CodesIndex(_currentDocument);

            PopulateTree(_currentDocument);
            UpdateSummary(_currentDocument);
            FileInfoText.Text = BuildFileInfo(_currentDocument);
            ClearButton.IsEnabled = true;

            // Fix #7: refrescar la cache de descripcion/unidad/precio que cada
            // regla guarda del catalogo.  Si el nuevo BC3 tiene el codigo, los
            // datos del nuevo prevalecen.  Si no lo tiene, mantenemos los del
            // BC3 anterior, asi la regla no se queda "huerfana" sin info.
            SyncAllRulesCatalogCache();

            RevalidateRules();
            UpdateHeaderBC3Badge();
            UpdateStatusBar();
            UpdateMappingBC3InfoText();
            RefreshExportTree();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "No se pudo importar el archivo BC3.\n\n" + ex.Message,
                "Error al importar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            FileInfoText.Text = "Error al importar el archivo.";
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ImportButton.IsEnabled = true;
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _currentDocument = null;
        _lastBC3Path = string.Empty;
        _bc3CodesIndex.Clear();
        ConceptsTree.Items.Clear();
        FileInfoText.Text = "Pulsa 'Importar BC3' arriba para empezar.";
        SummaryText.Text = "Sin BC3 cargado.";
        ClearButton.IsEnabled = false;

        RevalidateRules();
        UpdateHeaderBC3Badge();
        UpdateStatusBar();
        UpdateMappingBC3InfoText();
        TrySaveRulesSilent(_persistence.DefaultPath);
    }

    private void RebuildBC3CodesIndex(BC3Document doc)
    {
        _bc3CodesIndex.Clear();
        foreach (var code in doc.Concepts.Keys)
            _bc3CodesIndex.Add(code);
    }

    private void PopulateTree(BC3Document document)
    {
        ConceptsTree.Items.Clear();
        if (document.Roots.Count == 0)
        {
            ConceptsTree.Items.Add(new TreeViewItem { Header = "(El BC3 no contiene conceptos raiz)" });
            return;
        }
        foreach (var root in document.Roots)
            ConceptsTree.Items.Add(BuildNode(root, document, depth: 0));
    }

    private static TreeViewItem BuildNode(BC3Concept concept, BC3Document doc, int depth)
    {
        var item = new TreeViewItem
        {
            Header = FormatHeader(concept),
            IsExpanded = concept.Type == ConceptType.Capitulo && depth < 2,
            Tag = concept,
        };
        foreach (var dec in concept.Children)
        {
            if (!doc.Concepts.TryGetValue(dec.ChildCode, out var child)) continue;
            item.Items.Add(BuildNode(child, doc, depth + 1));
        }
        return item;
    }

    private static string FormatHeader(BC3Concept concept)
    {
        var summary = concept.Summary;
        if (summary.Length > 90) summary = summary.Substring(0, 87) + "...";
        var typeTag = concept.Type switch
        {
            ConceptType.Capitulo => "[CAP] ",
            ConceptType.Partida => "[PAR] ",
            ConceptType.Material => "[MAT] ",
            ConceptType.ManoDeObra => "[MO] ",
            ConceptType.Maquinaria => "[MAQ] ",
            ConceptType.Auxiliar => "[AUX] ",
            _ => "",
        };
        var unit = string.IsNullOrWhiteSpace(concept.Unit) ? "" : $" · {concept.Unit}";
        return $"{typeTag}{concept.Code}{unit}  —  {summary}";
    }

    private void UpdateSummary(BC3Document document)
    {
        var ci = CultureInfo.GetCultureInfo("es-ES");
        int caps = 0, parts = 0, mats = 0, mo = 0, maq = 0, otros = 0;
        foreach (var c in document.Concepts.Values)
        {
            switch (c.Type)
            {
                case ConceptType.Capitulo: caps++; break;
                case ConceptType.Partida: parts++; break;
                case ConceptType.Material: mats++; break;
                case ConceptType.ManoDeObra: mo++; break;
                case ConceptType.Maquinaria: maq++; break;
                default: otros++; break;
            }
        }
        SummaryText.Text =
            $"{document.TotalConcepts.ToString("N0", ci)} conceptos · " +
            $"{caps.ToString("N0", ci)} cap · " +
            $"{parts.ToString("N0", ci)} partidas · " +
            $"{mats + mo + maq + otros} otros";
    }

    private static string BuildFileInfo(BC3Document document)
    {
        var fileName = Path.GetFileName(document.SourcePath);
        var emitter = string.IsNullOrEmpty(document.Emitter) ? "(emisor no declarado)" : document.Emitter;
        var version = string.IsNullOrEmpty(document.FormatVersion) ? "(version no declarada)" : document.FormatVersion;
        return $"📄 {fileName}\n· FIEBDC {version}\n· Emisor: {emitter}";
    }

    // ============================================================
    // Badges / status bar
    // ============================================================

    private void UpdateHeaderBC3Badge()
    {
        if (_currentDocument is null || string.IsNullOrEmpty(_lastBC3Path))
        {
            HeaderBC3Badge.Visibility = SystemVisibility.Collapsed;
            PanelBC3FileBadge.Visibility = SystemVisibility.Collapsed;
            return;
        }
        var name = Path.GetFileName(_lastBC3Path);
        HeaderBC3Text.Text = "BC3 cargado: " + name;
        HeaderBC3Badge.Visibility = SystemVisibility.Visible;
        PanelBC3FileText.Text = name;
        PanelBC3FileBadge.Visibility = SystemVisibility.Visible;
    }

    private void UpdateStatusBar()
    {
        var ci = CultureInfo.GetCultureInfo("es-ES");

        if (_currentDocument is null || string.IsNullOrEmpty(_lastBC3Path))
            StatusBC3Text.Text = "Sin BC3 cargado";
        else
            StatusBC3Text.Text = $"BC3: {Path.GetFileName(_lastBC3Path)} · {_currentDocument.TotalConcepts.ToString("N0", ci)} conceptos";

        var rules = _rules.Count;
        var active = _rules.Count(r => r.Enabled);
        StatusRulesText.Text = rules == 0
            ? "Sin reglas"
            : $"Reglas: {rules.ToString("N0", ci)} · {active.ToString("N0", ci)} activas";

        var orphansTotal = _orphans.Sum(o => o.Count);
        StatusOrphansText.Text = orphansTotal == 0 ? "" : $"⚠ {orphansTotal.ToString("N0", ci)} elem. sin regla";
    }

    // ============================================================
    // Sub-tabs Mapeo / Huerfanos
    // ============================================================

    private void OnMappingTabChecked(object sender, RoutedEventArgs e)
    {
        if (OrphansTab is not null) OrphansTab.IsChecked = false;
        if (RulesGrid is not null) RulesGrid.Visibility = SystemVisibility.Visible;
        if (OrphansGrid is not null) OrphansGrid.Visibility = SystemVisibility.Collapsed;
    }

    private void OnOrphansTabChecked(object sender, RoutedEventArgs e)
    {
        if (MappingTab is not null) MappingTab.IsChecked = false;
        if (RulesGrid is not null) RulesGrid.Visibility = SystemVisibility.Collapsed;
        if (OrphansGrid is not null) OrphansGrid.Visibility = SystemVisibility.Visible;
    }

    private void OnRulesSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (RulesGrid.ItemsSource is not ICollectionView view) return;
        var q = (RulesSearchBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(q))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = obj =>
            {
                if (obj is not MappingRule r) return true;
                return (r.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (r.BC3Code ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (r.CategoryDisplayName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (r.ChapterDisplay ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (r.FiltersSummary ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }
    }

    // ============================================================
    // Mapeo de reglas
    // ============================================================

    private void UpdateMappingBC3InfoText()
    {
        if (_currentDocument is null || string.IsNullOrEmpty(_lastBC3Path))
        {
            MappingBC3InfoText.Text = "Sin BC3 cargado · las reglas no se validan hasta importar uno.";
            return;
        }
        var fileName = Path.GetFileName(_lastBC3Path);
        var ci = CultureInfo.GetCultureInfo("es-ES");
        var total = _currentDocument.TotalConcepts.ToString("N0", ci);
        var invalidCount = _rules.Count(r => !r.CodeIsValid);
        var invalidText = invalidCount == 0
            ? "todas validas"
            : $"⚠ {invalidCount} regla(s) con codigo no encontrado";
        MappingBC3InfoText.Text = $"Mapeo contra {fileName} · {total} partidas · {invalidText}";
    }

    private void RevalidateRules()
    {
        if (_bc3CodesIndex.Count == 0)
        {
            foreach (var r in _rules)
                r.CodeIsValid = string.IsNullOrEmpty(r.BC3Code);
        }
        else
        {
            foreach (var r in _rules)
                r.CodeIsValid = !string.IsNullOrEmpty(r.BC3Code) && _bc3CodesIndex.Contains(r.BC3Code);
        }
        RulesGrid.Items.Refresh();
    }

    private void OnNewRuleClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureActiveDocument(out var doc)) return;
        _ = GetOrLoadCategories(doc);
        OpenEditorFor(null, isNew: true);
    }

    private void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not MappingRule selected) return;
        if (!EnsureActiveDocument(out var doc)) return;
        _ = GetOrLoadCategories(doc);
        OpenEditorFor(selected, isNew: false);
    }

    private void OnDuplicateRuleClick(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not MappingRule selected) return;
        var copy = new MappingRule
        {
            Category = selected.Category,
            CategoryDisplayName = selected.CategoryDisplayName,
            Name = selected.Name + " (copia)",
            ChapterCode = selected.ChapterCode,
            ChapterName = selected.ChapterName,
            BC3Code = selected.BC3Code,
            Priority = selected.Priority,
            UnitFactor = selected.UnitFactor,
            Enabled = selected.Enabled,
            Notes = selected.Notes,
            Measurement = new MeasurementParameter
            {
                BuiltIn = selected.Measurement.BuiltIn,
                CustomParameterName = selected.Measurement.CustomParameterName,
            },
            Filters = selected.Filters.Select(f => new MappingFilter
            {
                ParameterName = f.ParameterName,
                ParameterDisplayName = f.ParameterDisplayName,
                Operator = f.Operator,
                Value = f.Value,
            }).ToList(),
        };
        _rules.Add(copy);
        RevalidateRules();
        RecomputeRuleConflicts();     // Sprint A: duplicar puede generar choque.
        UpdateRulesSummary();
        UpdateMappingBC3InfoText();
        UpdateStatusBar();
        TrySaveRulesSilent(_persistence.DefaultPath);
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not MappingRule selected) return;
        var ok = MessageBox.Show(this,
            $"¿Eliminar la regla '{selected.DisplayLabel}'?",
            "Confirmar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        _rules.Remove(selected);
        if (_editingRule == selected) CloseEditor();
        RecomputeRuleConflicts();     // Sprint A: al borrar puede resolverse un conflicto.
        UpdateRulesSummary();
        UpdateMappingBC3InfoText();
        UpdateStatusBar();
        TrySaveRulesSilent(_persistence.DefaultPath);
    }

    private void OnTestRuleClick(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not MappingRule selected)
        {
            MessageBox.Show(this,
                "Selecciona una regla de la tabla antes de pulsar 'Probar regla'.",
                "Sin regla seleccionada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (!EnsureActiveDocument(out var doc)) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var report = _ruleEngine.GenerateDiagnostic(doc, selected, sampleCount: 5);
            var dlg = new RuleDiagnosticDialog(report) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error generando diagnostico:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void OnSaveRulesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar mapeo como...",
            Filter = "Mapeo BC3 (*.json)|*.json|Todos los archivos (*.*)|*.*",
            FileName = "mapeo-bc3.json",
            InitialDirectory = Path.GetDirectoryName(_persistence.DefaultPath),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var file = BuildMappingFile();
            _persistence.Save(dialog.FileName, file);
            _persistence.Save(_persistence.DefaultPath, file);
            MessageBox.Show(this, $"Mapeo guardado en:\n{dialog.FileName}", "Guardado",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo guardar:\n" + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Sprint B — AC6.2: exporta el mapeo actual en formato legacy V1
    /// (sin Chapters/SubChapters como entidades; las reglas solo llevan
    /// los strings ChapterCode/Name/SubChapterCode/Name sincronizados
    /// desde las FKs).  Util para compatibilidad temporal con versiones
    /// anteriores del modulo o para usuarios que quieran abrir el JSON
    /// en una herramienta externa que ya conocia el formato antiguo.
    /// </summary>
    private void OnSaveRulesLegacyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar mapeo (formato legacy)...",
            Filter = "Mapeo BC3 legacy (*.json)|*.json|Todos los archivos (*.*)|*.*",
            FileName = "mapeo-bc3-legacy.json",
            InitialDirectory = Path.GetDirectoryName(_persistence.DefaultPath),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _persistence.SaveLegacy(dialog.FileName, BuildMappingFile());
            MessageBox.Show(this,
                $"Mapeo (legacy V1) exportado en:\n{dialog.FileName}",
                "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "No se pudo exportar:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnLoadRulesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Cargar mapeo desde...",
            Filter = "Mapeo BC3 (*.json)|*.json|Todos los archivos (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_persistence.DefaultPath),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var loaded = _persistence.Load(dialog.FileName);
            LoadMappingFileIntoState(loaded);

            if (!string.IsNullOrEmpty(loaded.LastBC3Path)
                && !string.Equals(loaded.LastBC3Path, _lastBC3Path, StringComparison.OrdinalIgnoreCase)
                && File.Exists(loaded.LastBC3Path))
            {
                var ask = MessageBox.Show(this,
                    "El mapeo referencia el BC3:\n\n" + Path.GetFileName(loaded.LastBC3Path) +
                    "\n\n¿Cargarlo tambien para validar?",
                    "BC3 asociado",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask == MessageBoxResult.Yes) ImportBc3From(loaded.LastBC3Path);
            }
            RevalidateRules();
            RecomputeRuleConflicts();     // Sprint A: marca duplicados tras cargar.
            UpdateRulesSummary();
            UpdateMappingBC3InfoText();
            UpdateStatusBar();
            RefreshExportTree();
            TrySaveRulesSilent(_persistence.DefaultPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo cargar:\n" + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateRulesSummary()
    {
        var ci = CultureInfo.GetCultureInfo("es-ES");
        var total = _rules.Count;
        var activas = _rules.Count(r => r.Enabled);
        var chapters = _rules
            .Select(r => r.ChapterDisplay)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "(sin capitulo)")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        RulesSummaryText.Text = total == 0
            ? "Sin reglas. Pulsa '＋ Nueva regla' para empezar."
            : $"{total.ToString("N0", ci)} reglas · {chapters.ToString("N0", ci)} capitulos · {activas.ToString("N0", ci)} activas (autoguardado).";

        MappingTabCount.Text = total.ToString("N0", ci);
    }

    private MappingFile BuildMappingFile() => new()
    {
        LastBC3Path = _lastBC3Path,
        Chapters    = _chapters.ToList(),
        SubChapters = _subChapters.ToList(),
        Rules       = _rules.ToList(),
    };

    /// <summary>
    /// Vuelca un MappingFile cargado del disco al estado de la ventana.
    /// Usado por carga inicial y por el dialogo "Cargar mapeo".  La
    /// migracion legacy ya la hizo MappingPersistenceService.Load().
    /// </summary>
    private void LoadMappingFileIntoState(MappingFile file)
    {
        _rules.Clear();
        if (file?.Rules is not null)
            foreach (var r in file.Rules) _rules.Add(r);

        _chapters.Clear();
        if (file?.Chapters is not null)
            foreach (var c in file.Chapters) _chapters.Add(c);

        _subChapters.Clear();
        if (file?.SubChapters is not null)
            foreach (var s in file.SubChapters) _subChapters.Add(s);
    }

    private void TrySaveRulesSilent(string path)
    {
        try { _persistence.Save(path, BuildMappingFile()); } catch { /* no propagar */ }
    }

    private static string? SafeDirectoryOf(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            return Directory.Exists(dir) ? dir : null;
        }
        catch { return null; }
    }

    private IReadOnlyList<RevitCategoryInfo> GetOrLoadCategories(Document doc)
    {
        if (_cachedCategories is not null) return _cachedCategories;
        _cachedCategories = _categoryProvider.GetCategoriesWithElements(doc).ToList();
        return _cachedCategories;
    }

    private bool EnsureActiveDocument(out Document doc)
    {
        doc = ActiveDocument!;
        if (doc is null)
        {
            MessageBox.Show(this,
                "Abre un proyecto en Revit antes de crear o editar reglas de mapeo.",
                "Sin documento activo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    // ============================================================
    // Selection on rules grid -> abre el panel editor
    // ============================================================

    private void OnRuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Si el panel esta abierto y estamos en modo edicion, sincroniza con la nueva regla.
        if (_editingRule is not null && !_editingIsNew)
        {
            if (RulesGrid.SelectedItem is MappingRule newSel && newSel != _editingRule)
            {
                if (!EnsureActiveDocument(out _)) return;
                OpenEditorFor(newSel, isNew: false);
            }
        }
    }

    // ============================================================
    // Calculo de mediciones
    // ============================================================

    private void OnCalculateClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureActiveDocument(out var doc)) return;

        var enabledCount = _rules.Count(r => r.Enabled);
        if (enabledCount == 0)
        {
            MessageBox.Show(this,
                "No hay reglas activas para calcular. Crea reglas antes de pulsar 'Calcular mediciones'.",
                "Sin reglas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            CalculateButton.IsEnabled = false;

            var overridesByCode = _measurements
                .Where(m => m.HasOverride)
                .ToDictionary(m => m.BC3Code, m => m.ManualOverride);

            _lastExtraction = _dataExtractor.ExtractAll(doc, _rules.ToList(), _currentDocument);

            _measurements.Clear();
            foreach (var m in _lastExtraction.Measurements)
            {
                if (overridesByCode.TryGetValue(m.BC3Code, out var ov))
                    m.ManualOverride = ov;
                _measurements.Add(m);
            }

            _orphans.Clear();
            foreach (var o in _lastExtraction.Orphans) _orphans.Add(o);

            UpdateKpis();
            UpdateStatusBar();

            // Habilitar "Preparar IFC" si hay mediciones con elementos
            PrepareIfcButton.IsEnabled = _measurements.Any(m => m.ElementCount > 0 && m.FinalQuantity > 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Error durante el calculo:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            CalculateButton.IsEnabled = true;
        }
    }

    private void UpdateKpis()
    {
        var ci = CultureInfo.GetCultureInfo("es-ES");
        var partidas = _measurements.Count;
        var elementos = _measurements.Sum(m => m.ElementCount);
        var importe = _measurements.Sum(m => m.Importe);
        var huerfanos = _orphans.Sum(o => o.Count);

        KpiPartidasValue.Text = partidas.ToString("N0", ci);
        KpiElementosValue.Text = elementos.ToString("N0", ci);
        KpiImporteValue.Text = importe.ToString("N2", ci) + " €";
        KpiOrphansValue.Text = huerfanos.ToString("N0", ci);

        OrphansTabCount.Text = huerfanos.ToString("N0", ci);
    }

    // ============================================================
    // Diagnostico / inspeccion sobre la preview de mediciones
    // ============================================================

    private void OnDiagnoseMeasurementClick(object sender, RoutedEventArgs e)
    {
        if (ReviewGrid?.SelectedItem is not MeasurementResult item)
        {
            MessageBox.Show(this,
                "Selecciona primero una partida en la previsualizacion.",
                "Sin partida seleccionada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DiagnoseRulesFor(item);
    }

    private void DiagnoseRulesFor(MeasurementResult item)
    {
        if (!EnsureActiveDocument(out var doc)) return;
        var rules = ResolveRulesForMeasurement(item);
        if (rules.Count == 0)
        {
            MessageBox.Show(this,
                "No hay regla activa asociada a esta partida.",
                "Sin reglas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < rules.Count; i++)
            {
                if (i > 0) sb.AppendLine().AppendLine(new string('=', 60)).AppendLine();
                sb.Append(_ruleEngine.GenerateDiagnostic(doc, rules[i], sampleCount: 5));
            }
            var dlg = new RuleDiagnosticDialog(sb.ToString()) { Owner = this };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error generando diagnostico:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private List<MappingRule> ResolveRulesForMeasurement(MeasurementResult item)
    {
        var found = new List<MappingRule>();
        if (item.ContributingRuleIds is { Count: > 0 })
        {
            foreach (var id in item.ContributingRuleIds)
            {
                var r = _rules.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
                if (r is not null && !found.Contains(r)) found.Add(r);
            }
        }
        if (found.Count == 0 && item.ContributingRules is { Count: > 0 })
        {
            foreach (var n in item.ContributingRules)
            {
                var r = _rules.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
                if (r is not null && !found.Contains(r)) found.Add(r);
            }
        }
        if (found.Count == 0 && !string.IsNullOrEmpty(item.BC3Code))
        {
            foreach (var r in _rules.Where(x => x.Enabled && x.BC3Code == item.BC3Code))
                if (!found.Contains(r)) found.Add(r);
        }
        return found;
    }

    // ============================================================
    // Acciones sobre la preview de mediciones
    // ============================================================

    private MeasurementResult? SelectedReviewItem => ReviewGrid?.SelectedItem as MeasurementResult;

    private void OnClearOverrideClick(object sender, RoutedEventArgs e)
    {
        var item = SelectedReviewItem;
        if (item is null)
        {
            MessageBox.Show(this, "Selecciona una partida en la previsualizacion.",
                "Sin partida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!item.HasOverride)
        {
            MessageBox.Show(this, "Esta partida no tiene override manual.",
                "Nada que limpiar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        item.ManualOverride = null;
        ReviewGrid.Items.Refresh();
        UpdateKpis();
    }

    private void OnSelectInRevitClick(object sender, RoutedEventArgs e)
    {
        var item = SelectedReviewItem;
        if (item is null)
        {
            MessageBox.Show(this, "Selecciona una partida en la previsualizacion.",
                "Sin partida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (item.ElementIds.Count == 0)
        {
            MessageBox.Show(this, "Esta partida no tiene elementos del modelo asociados.",
                "Sin elementos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_uiApp?.ActiveUIDocument is null)
        {
            MessageBox.Show(this, "No hay un documento Revit activo.",
                "Sin documento", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var uidoc = _uiApp.ActiveUIDocument;
            var ids = item.ElementIds.Select(id => new ElementId(id)).ToList();
            uidoc.Selection.SetElementIds(ids);
            try { uidoc.ShowElements(ids); } catch { /* no critico */ }
            MessageBox.Show(this,
                $"Seleccionados {ids.Count} elemento(s) en Revit para '{item.BC3Code}'.",
                "Seleccion aplicada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo seleccionar:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnViewContributingElementsClick(object sender, RoutedEventArgs e)
    {
        var item = SelectedReviewItem;
        if (item is null)
        {
            MessageBox.Show(this, "Selecciona una partida.", "Sin partida",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Partida: {item.BC3Code}");
        sb.AppendLine($"Descripcion: {item.Description}");
        sb.AppendLine();
        sb.AppendLine($"Reglas contribuyentes:");
        if (item.ContributingRules.Count == 0) sb.AppendLine("  (ninguna)");
        else foreach (var r in item.ContributingRules) sb.AppendLine("  - " + r);
        sb.AppendLine();
        sb.AppendLine($"Elementos del modelo: {item.ElementCount}");
        if (item.ElementIds.Count == 0)
        {
            sb.AppendLine("  (sin ids)");
        }
        else
        {
            var show = item.ElementIds.Take(200).ToList();
            foreach (var id in show) sb.AppendLine("  - " + id.ToString(CultureInfo.InvariantCulture));
            if (item.ElementIds.Count > show.Count)
                sb.AppendLine($"  ...y {item.ElementIds.Count - show.Count} mas");
        }
        var dlg = new RuleDiagnosticDialog(sb.ToString()) { Owner = this };
        dlg.ShowDialog();
    }

    private void OnRemoveMeasurementClick(object sender, RoutedEventArgs e)
    {
        var item = SelectedReviewItem;
        if (item is null)
        {
            MessageBox.Show(this, "Selecciona una partida.", "Sin partida",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ok = MessageBox.Show(this,
            $"¿Eliminar la partida '{item.BC3Code}' de los resultados?\n\n" +
            "Si vuelves a 'Calcular mediciones', la partida volvera a aparecer.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        _measurements.Remove(item);
        UpdateKpis();
    }

    // ============================================================
    // Exportar BC3 (inline, sin pestania dedicada)
    // ============================================================

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_measurements.Count == 0)
        {
            MessageBox.Show(this,
                "No hay mediciones calculadas. Pulsa 'Calcular mediciones' antes de exportar.",
                "Sin mediciones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Sprint A — Guardia previa al export.  Si hay reglas habilitadas que
        // comparten (Capitulo, Subcapitulo, BC3Code), avisar al usuario antes
        // de generar el BC3 con sufijo automatico.  El usuario decide.
        if (!ConfirmExportDespiteConflicts())
            return;

        // ── Guardia pre-exportacion ────────────────────────────────────────────
        // Si NINGUNA partida tiene medicion > 0 el archivo BC3 quedaria vacio
        // (solo cabecera ~V y raiz ##).  Avisamos antes de proceder para que
        // el usuario sepa exactamente por que el archivo no tendra contenido.
        var writableCount = _measurements.Count(m =>
            !string.IsNullOrWhiteSpace(m.BC3Code) && m.FinalQuantity > 0);

        if (writableCount == 0)
        {
            var ci2 = CultureInfo.GetCultureInfo("es-ES");
            var withElements = _measurements.Count(m => m.ElementCount > 0);
            var detail = withElements > 0
                ? $"Hay {withElements.ToString("N0", ci2)} partida(s) con elementos pero medicion 0.\n" +
                  "Posible causa: el tipo de medicion (Longitud/Area/Volumen) no aplica\n" +
                  "a la categoria seleccionada, o el factor multiplicador es 0."
                : "Las reglas activas no capturaron ningun elemento del modelo.\n" +
                  "Comprueba que la categoria de la regla coincide con la de los\n" +
                  "elementos del modelo (columna 'ELEM.' en la tabla de mediciones).";

            MessageBox.Show(this,
                "Ninguna partida tiene medicion calculada mayor que 0.\n\n" +
                detail + "\n\n" +
                "El archivo BC3 se exportaria vacio. Revisa las reglas y\n" +
                "vuelve a pulsar 'Calcular mediciones' antes de exportar.",
                "Sin mediciones exportables", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // ──────────────────────────────────────────────────────────────────────

        var dialog = new SaveFileDialog
        {
            Title = "Guardar BC3 como...",
            Filter = "Archivo BC3 (*.bc3)|*.bc3",
            FileName = string.IsNullOrEmpty(_lastExportPath)
                ? "presupuesto-e3gbimtools.bc3"
                : Path.GetFileName(_lastExportPath),
            InitialDirectory = !string.IsNullOrEmpty(_lastExportPath)
                ? Path.GetDirectoryName(_lastExportPath)
                : Path.GetDirectoryName(_lastBC3Path),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        _lastExportPath = dialog.FileName;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ExportButton.IsEnabled = false;

            var options = new BC3ExportOptions
            {
                ProjectName = "Presupuesto e3gbimtools",
            };

            _bc3Writer.Export(
                outputPath: _lastExportPath,
                sourceBc3: _currentDocument,
                chapters: _chapters.ToList(),
                subChapters: _subChapters.ToList(),
                rules: _rules.ToList(),
                measurements: _measurements.ToList(),
                options: options);

            // ── Generar archivo de vinculacion .bimlink.json ──────────────────
            // Se genera automaticamente junto al BC3, con el mismo nombre base.
            // Permite trazabilidad elemento a elemento (IfcGUID <-> partida BC3)
            // y es el insumo para el Sprint A4 (PSETs en el IFC).
            string bimlinkPath;
            string bimlinkNote;
            try
            {
                bimlinkPath = _bimlinkExporter.Export(
                    bc3OutputPath: _lastExportPath,
                    measurements: _measurements.ToList(),
                    rules: _rules.ToList(),
                    chapters: _chapters.ToList(),
                    subChapters: _subChapters.ToList(),
                    extraction: _lastExtraction);
                bimlinkNote = $"\nArchivo de vinculacion BIM: {Path.GetFileName(bimlinkPath)}";
            }
            catch (Exception bimlinkEx)
            {
                // No abortamos el export del BC3 si falla el bimlink —
                // el BC3 ya esta escrito y es el producto principal.
                bimlinkNote = $"\n(Aviso: no se pudo generar el .bimlink.json: {bimlinkEx.Message})";
            }
            // ─────────────────────────────────────────────────────────────────

            OpenExportFolderButton.IsEnabled = true;
            ValidateExportButton.IsEnabled  = true;
            var ci = CultureInfo.GetCultureInfo("es-ES");

            // Mostrar cuantas se escribieron REALMENTE (con FinalQuantity > 0),
            // no el total de filas del DataGrid (que incluye las de 0 elementos).
            var zeroCount = _measurements.Count - writableCount;
            var zeroNote  = zeroCount > 0
                ? $"\n({zeroCount.ToString("N0", ci)} partida(s) con medicion 0 no se incluyeron)"
                : string.Empty;

            // Informacion de cobertura de GUIDs si hay extraccion reciente
            var guidNote = string.Empty;
            if (_lastExtraction is not null && _lastExtraction.ElementsMatched > 0)
            {
                var coverage = _lastExtraction.GuidCoveragePercent;
                guidNote = coverage < 100.0
                    ? $"\nCobertura IfcGUID: {coverage:F1}% ({_lastExtraction.GuidFallback} elem. sin GUID fiable)"
                    : $"\nCobertura IfcGUID: 100% ({_lastExtraction.ElementsMatched} elementos)";
            }

            MessageBox.Show(this,
                $"Exportado correctamente: {Path.GetFileName(_lastExportPath)}\n" +
                $"{writableCount.ToString("N0", ci)} partidas con medicion exportadas.{zeroNote}" +
                bimlinkNote + guidNote + "\n\n" +
                "Importalo en Presto / Arquimedes para verlo.",
                "Export OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Error al exportar:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            ExportButton.IsEnabled = true;
        }
    }

    // ============================================================
    // Preparar exportacion IFC (Sprint A4)
    // ============================================================

    private async void OnPrepareIfcClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureActiveDocument(out var doc)) return;

        var app = _uiApp?.Application;
        if (app is null)
        {
            MessageBox.Show(this,
                "No se puede acceder a la aplicacion Revit.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Guardia: si no se creo el ExternalEvent (modo diseñador), abortamos.
        if (_prepIfcEvent is null)
        {
            MessageBox.Show(this,
                "El modulo no se ha iniciado correctamente desde el ribbon de Revit.\n" +
                "Cierra la ventana y vuelve a abrirla desde 'Revit → BC3'.",
                "Error de inicializacion", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var writableCount = _measurements.Count(m =>
            !string.IsNullOrWhiteSpace(m.BC3Code) && m.FinalQuantity > 0 && m.ElementCount > 0);
        if (writableCount == 0)
        {
            MessageBox.Show(this,
                "No hay partidas con medicion calculada mayor que 0.\n\n" +
                "Pulsa 'Calcular mediciones' primero.",
                "Sin mediciones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Confirmacion antes de tocar el modelo
        var confirm = MessageBox.Show(this,
            $"Se van a escribir los parametros E3G_BC3_Code y E3G_BC3_Quantity en {writableCount} partida(s) " +
            $"del modelo Revit activo.\n\n" +
            $"• Se creara un archivo de parametros compartidos en:\n" +
            $"  %APPDATA%\\e3g\\BC3\\E3G_BC3_Params.spf\n\n" +
            $"• Los parametros quedaran visibles en las propiedades de cada elemento.\n\n" +
            $"Despues de esto, exporta el IFC desde Revit normalmente y los parametros " +
            $"apareceran como propiedades IFC en cada elemento.\n\n" +
            $"¿Continuar?",
            "Preparar exportacion IFC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            PrepareIfcButton.IsEnabled = false;

            // Preparar la tarea ANTES de llamar a Raise().
            // InvokeInApiContext() encola el trabajo y devuelve un Task que se
            // completa cuando Revit ejecuta el handler (contexto de API valido).
            var task = _prepIfcHandler.InvokeInApiContext(
                doc:          doc,
                app:          app,
                measurements: _measurements.ToList(),
                rules:        _rules.ToList(),
                service:      _ifcExportPrep);

            // Raise() avisa a Revit de que hay trabajo pendiente.
            // Como esta ventana es modeless, el await cede el hilo principal
            // para que Revit procese el evento; el Task se completa cuando
            // el handler llama a IBC3IfcExportPrepService.WriteToModel().
            _prepIfcEvent.Raise();
            var result = await task;

            if (!result.Success)
            {
                MessageBox.Show(this,
                    "Error al escribir parametros en el modelo:\n\n" + result.ErrorMessage,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var ci = CultureInfo.GetCultureInfo("es-ES");
            var warnings = result.Warnings.Count > 0
                ? "\n\nAvisos:\n• " + string.Join("\n• ", result.Warnings)
                : string.Empty;
            var newParams = result.ParametersCreated > 0
                ? $"\n({result.ParametersCreated} parametro(s) compartido(s) creado(s))"
                : string.Empty;

            MessageBox.Show(this,
                $"Parametros BC3 escritos correctamente.\n\n" +
                $"{result.ElementsUpdated.ToString("N0", ci)} elemento(s) actualizados " +
                $"en {result.CategoriesProcessed} categoria(s).{newParams}" +
                $"\n\nAhora puedes exportar el IFC desde Revit (Archivo → Exportar → IFC).\n" +
                $"Los parametros E3G_BC3_Code y E3G_BC3_Quantity apareceran como propiedades " +
                $"en cada elemento IFC." +
                warnings,
                "IFC preparado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Error inesperado al preparar el IFC:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            PrepareIfcButton.IsEnabled = true;
        }
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(_lastExportPath);
            if (Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch { /* sin ruido */ }
    }

    // ============================================================
    // Validar exportacion BC3 + bimlink (Sprint A5)
    // ============================================================

    private void OnValidateExportClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastExportPath) || !File.Exists(_lastExportPath))
        {
            MessageBox.Show(this,
                "No hay ninguna exportacion reciente para validar.\n" +
                "Exporta el BC3 primero con '📤 Exportar BC3...'.",
                "Validar exportacion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Derivar ruta del .bimlink.json
        var bimlinkPath = Path.ChangeExtension(_lastExportPath, null) + ".bimlink.json";

        if (!File.Exists(bimlinkPath))
        {
            MessageBox.Show(this,
                $"No se encontro el archivo de vinculacion:\n{Path.GetFileName(bimlinkPath)}\n\n" +
                "El validador necesita el .bimlink.json generado junto al BC3. " +
                "Vuelve a exportar para regenerarlo.",
                "Validar exportacion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var report = Tests.BimLinkingValidationTests.Run(bimlinkPath, _lastExportPath);
            var summary = report.ToSummary();

            // Guardar el informe junto al BC3
            var reportPath = Path.ChangeExtension(_lastExportPath, null) + ".validation.txt";
            File.WriteAllText(reportPath, summary, System.Text.Encoding.UTF8);

            var icon = report.AllPassed ? MessageBoxImage.Information : MessageBoxImage.Warning;
            var titulo = report.AllPassed
                ? $"Validacion OK — {report.Passed}/{report.Total} checks"
                : $"Validacion con fallos — {report.Failed} error(es) de {report.Total}";

            MessageBox.Show(this,
                summary + $"\n\nInforme guardado en:\n{Path.GetFileName(reportPath)}",
                titulo, MessageBoxButton.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Error al ejecutar la validacion:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ============================================================
    // PANEL EDITOR DE REGLA (in-place, panel derecho colapsable)
    // ============================================================

    private void OpenEditorFor(MappingRule? rule, bool isNew)
    {
        _editingIsNew = isNew;
        _editingRule = rule ?? new MappingRule();
        _editorChapterEditedByUser = false;

        // Categorias.
        var doc = ActiveDocument;
        if (doc is null) return;
        var categories = GetOrLoadCategories(doc);
        EditorCategoryCombo.ItemsSource = categories;

        var match = categories.FirstOrDefault(c => c.BuiltInCategory == _editingRule.Category);
        if (match is not null) EditorCategoryCombo.SelectedItem = match;
        else EditorCategoryCombo.SelectedIndex = -1;

        // Codigos BC3 del archivo cargado + mapa de capitulos.
        PopulateEditorBC3Codes(_currentDocument);
        BuildEditorChapterMap(_currentDocument);

        // Sugerencias de capitulos a partir de los que ya hay en otras reglas.
        var existingChapters = _rules
            .Select(r => r.ChapterName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        EditorChapterNameCombo.ItemsSource = existingChapters;

        // Sugerencias de subcapitulos (issue #4).
        var existingSubChapters = _rules
            .Select(r => r.SubChapterName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        EditorSubChapterNameCombo.ItemsSource = existingSubChapters;

        // Campos.
        EditorNameBox.Text = _editingRule.Name ?? string.Empty;

        // Sprint B: si la regla tiene ChapterId, resolvemos los strings a
        // partir de la entidad actual (autoridad: la entidad, no los
        // strings legacy de la regla que pueden estar desincronizados tras
        // un drag-and-drop sin haber guardado).
        var resolvedCh = !string.IsNullOrEmpty(_editingRule.ChapterId)
            ? _chapters.FirstOrDefault(c => c.Id == _editingRule.ChapterId)
            : null;
        var resolvedSub = !string.IsNullOrEmpty(_editingRule.SubChapterId)
            ? _subChapters.FirstOrDefault(s => s.Id == _editingRule.SubChapterId)
            : null;

        EditorChapterCodeBox.Text   = resolvedCh?.Code ?? _editingRule.ChapterCode ?? string.Empty;
        EditorChapterNameCombo.Text = resolvedCh?.Name ?? _editingRule.ChapterName ?? string.Empty;
        EditorSubChapterCodeBox.Text   = resolvedSub?.Code ?? _editingRule.SubChapterCode ?? string.Empty;
        EditorSubChapterNameCombo.Text = resolvedSub?.Name ?? _editingRule.SubChapterName ?? string.Empty;
        EditorPriorityBox.Text = _editingRule.Priority.ToString(CultureInfo.InvariantCulture);
        EditorFactorBox.Text = _editingRule.UnitFactor.ToString("R", CultureInfo.InvariantCulture);
        EditorNotesBox.Text = _editingRule.Notes ?? string.Empty;
        EditorEnabledCheck.IsChecked = _editingRule.Enabled;

        if (!string.IsNullOrEmpty(_editingRule.ChapterCode) || !string.IsNullOrEmpty(_editingRule.ChapterName))
            _editorChapterEditedByUser = true;

        if (EditorBC3CodeCombo.ItemsSource is IEnumerable<BC3CodeOption> opts)
        {
            var sel = opts.FirstOrDefault(o => string.Equals(o.Code, _editingRule.BC3Code, StringComparison.Ordinal));
            if (sel is not null) EditorBC3CodeCombo.SelectedItem = sel;
            else EditorBC3CodeCombo.Text = _editingRule.BC3Code ?? string.Empty;
        }
        else
        {
            EditorBC3CodeCombo.Text = _editingRule.BC3Code ?? string.Empty;
        }

        // Filtros.
        _editorFilters.Clear();
        if (_editingRule.Filters is not null)
        {
            foreach (var f in _editingRule.Filters)
                _editorFilters.Add(MappingFilterRow.From(f));
        }

        // Fix #3: tras cargar los filtros, refrescar la lista de valores
        // sugeridos para cada uno (la categoria ya esta seleccionada).
        // Sin esto, el desplegable de Valor sale en blanco al editar/copiar.
        if (EditorCategoryCombo.SelectedItem is RevitCategoryInfo selCat)
        {
            foreach (var row in _editorFilters)
                RefreshEditorFilterValues(row, selCat.BuiltInCategory);
        }

        // Cabecera / badge activo.
        EditorTitle.Text = isNew ? "Nueva regla" : "Editar regla";
        EditorSubtitle.Text = string.IsNullOrEmpty(_editingRule.Id)
            ? "rule_new"
            : $"rule_{_editingRule.Id.Substring(0, Math.Min(8, _editingRule.Id.Length))}";
        EditorActiveBadge.Visibility = _editingRule.Enabled ? SystemVisibility.Visible : SystemVisibility.Collapsed;

        // Despliega el panel.
        ExpandRightPanel(true);
    }

    private void CloseEditor()
    {
        _editingRule = null;
        _editingIsNew = false;
        _editorFilters.Clear();
        ExpandRightPanel(false);
    }

    private void ExpandRightPanel(bool expand)
    {
        RightPanelColumn.Width = expand ? new GridLength(360) : new GridLength(0);
    }

    private void OnCloseEditorClick(object sender, RoutedEventArgs e) => CloseEditor();

    private void OnEditorCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorCategoryCombo.SelectedItem is not RevitCategoryInfo cat) return;
        if (_editingRule is null) return;
        var doc = ActiveDocument;
        if (doc is null) return;

        ReloadEditorParametersFor(cat.BuiltInCategory);
        foreach (var row in _editorFilters)
            RefreshEditorFilterValues(row, cat.BuiltInCategory);
    }

    private void ReloadEditorParametersFor(BuiltInCategory category)
    {
        var doc = ActiveDocument;
        if (doc is null) return;

        _editorCategoryParams = _parameterInspector.GetParameters(doc, category);

        EditorParameterOptions.Clear();
        var combined = _editorCategoryParams.Instance
            .Select(p => p.Name)
            .Concat(_editorCategoryParams.Type.Select(p => p.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in combined) EditorParameterOptions.Add(name);

        var measurementOptions = new List<string>
        {
            "Longitud (built-in)",
            "Area (built-in)",
            "Volumen (built-in)",
            "Cantidad (built-in)",
        };
        foreach (var p in _editorCategoryParams.Instance.Where(p => p.IsNumeric))
            measurementOptions.Add($"[Inst] {p.Name}");
        foreach (var p in _editorCategoryParams.Type.Where(p => p.IsNumeric))
            measurementOptions.Add($"[Tipo] {p.Name}");

        EditorMeasurementCombo.ItemsSource = measurementOptions;

        // Preseleccionar el actual.
        if (_editingRule?.Measurement is not null)
        {
            string sel = _editingRule.Measurement.BuiltIn switch
            {
                BuiltInMeasurement.Length => "Longitud (built-in)",
                BuiltInMeasurement.Area => "Area (built-in)",
                BuiltInMeasurement.Volume => "Volumen (built-in)",
                BuiltInMeasurement.Count => "Cantidad (built-in)",
                _ => string.IsNullOrEmpty(_editingRule.Measurement.CustomParameterName)
                    ? "Longitud (built-in)"
                    : "[Inst] " + _editingRule.Measurement.CustomParameterName,
            };
            EditorMeasurementCombo.SelectedItem = measurementOptions.FirstOrDefault(o =>
                string.Equals(o, sel, StringComparison.OrdinalIgnoreCase))
                ?? measurementOptions.First();
        }
        else if (measurementOptions.Count > 0)
        {
            EditorMeasurementCombo.SelectedIndex = 0;
        }
    }

    private void RefreshEditorFilterValues(MappingFilterRow row, BuiltInCategory category)
    {
        row.AvailableValues.Clear();
        if (category == BuiltInCategory.INVALID) return;
        if (string.IsNullOrWhiteSpace(row.ParameterName)) return;
        var doc = ActiveDocument;
        if (doc is null) return;
        try
        {
            var values = _parameterInspector.GetParameterValues(doc, category, row.ParameterName);
            foreach (var v in values) row.AvailableValues.Add(v);
        }
        catch { /* sin sugerencias */ }
    }

    private void OnEditorFilterRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MappingFilterRow row) return;
        if (e.PropertyName != nameof(MappingFilterRow.ParameterName)) return;
        if (EditorCategoryCombo.SelectedItem is RevitCategoryInfo cat)
            RefreshEditorFilterValues(row, cat.BuiltInCategory);
    }

    private void PopulateEditorBC3Codes(BC3Document? bc3Document)
    {
        if (bc3Document is null || bc3Document.Concepts.Count == 0)
        {
            EditorBC3CodeCombo.ItemsSource = null;
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
        EditorBC3CodeCombo.ItemsSource = options;
    }

    private void BuildEditorChapterMap(BC3Document? bc3Document)
    {
        _editorChapterByCode.Clear();
        if (bc3Document is null) return;
        foreach (var root in bc3Document.Roots)
            WalkEditorChapterMap(root, currentChapter: null, bc3Document);
    }

    private void WalkEditorChapterMap(BC3Concept node, BC3Concept? currentChapter, BC3Document doc)
    {
        if (node is null) return;
        var chapterForChildren = node.Type == ConceptType.Capitulo ? node : currentChapter;
        if (currentChapter is not null && !_editorChapterByCode.ContainsKey(node.Code))
            _editorChapterByCode[node.Code] = (currentChapter.Code, currentChapter.Summary ?? string.Empty);
        if (node.Type == ConceptType.Capitulo && !_editorChapterByCode.ContainsKey(node.Code))
            _editorChapterByCode[node.Code] = (node.Code, node.Summary ?? string.Empty);
        foreach (var dec in node.Children)
        {
            if (doc.Concepts.TryGetValue(dec.ChildCode, out var child))
                WalkEditorChapterMap(child, chapterForChildren, doc);
        }
    }

    private void OnEditorBC3CodeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_editorChapterEditedByUser) return;
        if (EditorBC3CodeCombo.SelectedItem is not BC3CodeOption opt) return;
        if (!_editorChapterByCode.TryGetValue(opt.Code, out var info)) return;
        EditorChapterCodeBox.Text = info.Code ?? string.Empty;
        EditorChapterNameCombo.Text = info.Name ?? string.Empty;
    }

    private void OnEditorAddFilterClick(object sender, RoutedEventArgs e)
    {
        _editorFilters.Add(new MappingFilterRow { Operator = MappingOperator.Equals });
    }

    private void OnEditorRemoveFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is MappingFilterRow row)
            _editorFilters.Remove(row);
    }

    private void OnSaveRuleEditorClick(object sender, RoutedEventArgs e)
    {
        if (_editingRule is null) return;

        if (EditorCategoryCombo.SelectedItem is not RevitCategoryInfo cat)
        {
            MessageBox.Show(this, "Selecciona una categoria Revit.", "Datos incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var bc3Code = GetEditorBC3Code();
        if (string.IsNullOrWhiteSpace(bc3Code))
        {
            MessageBox.Show(this, "Introduce o selecciona una partida BC3.", "Datos incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(EditorPriorityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
            priority = 100;
        if (!double.TryParse((EditorFactorBox.Text ?? "1").Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
            factor = 1.0;

        // Sprint B: resolver Chapter/SubChapter por (Code,Name) escritos en el
        // editor.  Si no existen y al menos uno de los campos no esta vacio,
        // se crean al vuelo.  Si ambos estan vacios la regla queda huerfana.
        var chCodeTxt = EditorChapterCodeBox.Text?.Trim() ?? string.Empty;
        var chNameTxt = EditorChapterNameCombo.Text?.Trim() ?? string.Empty;
        var subCodeTxt = EditorSubChapterCodeBox.Text?.Trim() ?? string.Empty;
        var subNameTxt = EditorSubChapterNameCombo.Text?.Trim() ?? string.Empty;

        var (chId, subId) = ResolveOrCreateChapter(chCodeTxt, chNameTxt, subCodeTxt, subNameTxt);

        // Sprint A — Construyo un "snapshot" de la regla resultante ANTES de
        // tocar _editingRule.  Asi la validacion ve los datos nuevos pero
        // _editingRule conserva los actuales por si el usuario cancela en
        // el aviso (no queremos dejar la regla a medio actualizar).
        var candidate = new MappingRule
        {
            Id = _editingRule.Id,
            Category = cat.BuiltInCategory,
            CategoryDisplayName = cat.DisplayName,
            Name = EditorNameBox.Text?.Trim() ?? string.Empty,
            ChapterId = chId,
            SubChapterId = subId,
            ChapterCode = chCodeTxt,
            ChapterName = chNameTxt,
            SubChapterCode = subCodeTxt,
            SubChapterName = subNameTxt,
            BC3Code = bc3Code,
            Priority = priority,
            UnitFactor = factor,
            Notes = EditorNotesBox.Text?.Trim() ?? string.Empty,
            Enabled = EditorEnabledCheck.IsChecked == true,
        };

        // Sprint B: garantizar que los strings legacy del resto de reglas
        // reflejan el estado actual de las FKs antes de la validacion (si
        // hubo drag-and-drop entre la ultima validacion y la edicion).
        SyncLegacyStringsForValidation();

        // Si la regla nueva entra en conflicto con otra habilitada, avisar.
        // El usuario decide: cancelar (volver al editor sin guardar) o forzar.
        if (!ConfirmEditorSaveDespiteConflict(candidate))
            return;

        _editingRule.Category = candidate.Category;
        _editingRule.CategoryDisplayName = candidate.CategoryDisplayName;
        _editingRule.Name = candidate.Name;
        _editingRule.ChapterId = candidate.ChapterId;
        _editingRule.SubChapterId = candidate.SubChapterId;
        _editingRule.ChapterCode = candidate.ChapterCode;
        _editingRule.ChapterName = candidate.ChapterName;
        _editingRule.SubChapterCode = candidate.SubChapterCode;
        _editingRule.SubChapterName = candidate.SubChapterName;
        _editingRule.BC3Code = candidate.BC3Code;
        _editingRule.Priority = candidate.Priority;
        _editingRule.UnitFactor = candidate.UnitFactor;
        _editingRule.Notes = candidate.Notes;
        _editingRule.Enabled = candidate.Enabled;
        _editingRule.Measurement = ParseEditorMeasurement(EditorMeasurementCombo.SelectedItem as string);
        _editingRule.Filters = _editorFilters
            .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName))
            .Select(r => r.ToFilter())
            .ToList();

        // Fix #7: si el codigo BC3 existe en el catalogo cargado, cacheamos
        // su descripcion/unidad/precio en la propia regla.  De este modo, si
        // mañana se importa otro BC3 sin este codigo, la regla sigue sabiendo
        // que es "Tuberia PVC DN50 - €15.30/m".
        SyncRuleCatalogCache(_editingRule);

        if (_editingIsNew)
            _rules.Add(_editingRule);

        RevalidateRules();
        RecomputeRuleConflicts();     // Sprint A: marca filas en conflicto.
        UpdateRulesSummary();
        UpdateMappingBC3InfoText();
        UpdateStatusBar();
        TrySaveRulesSilent(_persistence.DefaultPath);

        CloseEditor();
    }

    /// <summary>
    /// Sprint B: dado lo que el usuario escribio en los campos Chapter Code /
    /// Chapter Name / SubChapter Code / SubChapter Name del editor de regla,
    /// devuelve los Ids (ChapterId, SubChapterId) que la regla debe usar.
    ///
    /// Si los campos estan vacios, devuelve (null, null) — regla huerfana.
    /// Si los campos coinciden con un Chapter/SubChapter existente (por Code
    /// si lo hay; por Name como segunda opcion), lo reutiliza.  Si no existe
    /// ninguno y al menos hay codigo o nombre, crea uno nuevo al vuelo y
    /// devuelve su Id.
    /// </summary>
    private (string? chapterId, string? subId) ResolveOrCreateChapter(
        string chapterCode, string chapterName,
        string subCode, string subName)
    {
        chapterCode = (chapterCode ?? string.Empty).Trim();
        chapterName = (chapterName ?? string.Empty).Trim();
        subCode = (subCode ?? string.Empty).Trim();
        subName = (subName ?? string.Empty).Trim();

        if (chapterCode.Length == 0 && chapterName.Length == 0)
            return (null, null);

        // Buscar capitulo por Code (prioridad) o por Name (fallback).
        Chapter? chapter = null;
        if (chapterCode.Length > 0)
            chapter = _chapters.FirstOrDefault(c =>
                string.Equals((c.Code ?? string.Empty).Trim(), chapterCode, StringComparison.Ordinal));
        if (chapter is null && chapterName.Length > 0)
            chapter = _chapters.FirstOrDefault(c =>
                string.Equals((c.Name ?? string.Empty).Trim(), chapterName, StringComparison.OrdinalIgnoreCase));

        if (chapter is null)
        {
            var maxOrder = _chapters.Count == 0 ? -1 : _chapters.Max(c => c.Order);
            chapter = new Chapter
            {
                Code = chapterCode,
                Name = chapterName.Length > 0 ? chapterName : chapterCode,
                Order = maxOrder + 1,
            };
            _chapters.Add(chapter);
        }
        else
        {
            // Si el usuario completo el nombre y antes estaba vacio, lo
            // adoptamos (renombrado en sitio).
            if (string.IsNullOrEmpty(chapter.Name) && chapterName.Length > 0)
                chapter.Name = chapterName;
        }

        if (subCode.Length == 0 && subName.Length == 0)
            return (chapter.Id, null);

        // SubChapter scope: dentro del chapter resuelto.
        SubChapter? sub = null;
        if (subCode.Length > 0)
            sub = _subChapters.FirstOrDefault(s =>
                s.ChapterId == chapter.Id
                && string.Equals((s.Code ?? string.Empty).Trim(), subCode, StringComparison.Ordinal));
        if (sub is null && subName.Length > 0)
            sub = _subChapters.FirstOrDefault(s =>
                s.ChapterId == chapter.Id
                && string.Equals((s.Name ?? string.Empty).Trim(), subName, StringComparison.OrdinalIgnoreCase));

        if (sub is null)
        {
            var maxOrder = _subChapters.Where(s => s.ChapterId == chapter.Id)
                .Select(s => s.Order).DefaultIfEmpty(-1).Max();
            sub = new SubChapter
            {
                ChapterId = chapter.Id,
                Code = subCode,
                Name = subName.Length > 0 ? subName : subCode,
                Order = maxOrder + 1,
            };
            _subChapters.Add(sub);
        }
        else
        {
            if (string.IsNullOrEmpty(sub.Name) && subName.Length > 0)
                sub.Name = subName;
        }

        return (chapter.Id, sub.Id);
    }

    private string GetEditorBC3Code()
    {
        if (EditorBC3CodeCombo.SelectedValue is string sel && !string.IsNullOrWhiteSpace(sel))
            return sel.Trim();
        var text = EditorBC3CodeCombo.Text?.Trim() ?? string.Empty;
        return ExtractCodeFromDisplay(text);
    }

    private static string ExtractCodeFromDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        var seps = new[] { " ·", " —", " - " };
        var minIdx = -1;
        foreach (var sep in seps)
        {
            var idx = t.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && (minIdx < 0 || idx < minIdx)) minIdx = idx;
        }
        return minIdx < 0 ? t : t.Substring(0, minIdx).Trim();
    }

    private static MeasurementParameter ParseEditorMeasurement(string? selection)
    {
        if (string.IsNullOrEmpty(selection)) return new MeasurementParameter();
        if (selection.StartsWith("Longitud")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Length };
        if (selection.StartsWith("Area")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Area };
        if (selection.StartsWith("Volumen")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Volume };
        if (selection.StartsWith("Cantidad")) return new MeasurementParameter { BuiltIn = BuiltInMeasurement.Count };

        var pos = selection.IndexOf(']');
        var name = pos > 0 ? selection.Substring(pos + 1).Trim() : selection.Trim();
        return new MeasurementParameter
        {
            BuiltIn = BuiltInMeasurement.None,
            CustomParameterName = name,
        };
    }

    // ============================================================
    // FIX #2 — Casilla activar/desactivar regla
    // ============================================================

    /// <summary>
    /// Manejador del DataGrid de reglas cuando se termina de editar una celda.
    /// La unica columna editable es la casilla ACT (Enabled).  Tras un cambio:
    /// 1) Persistimos el mapeo silenciosamente.
    /// 2) Refrescamos la barra de estado y el resumen para que el contador de
    ///    "activas" se actualice.
    /// </summary>
    private void OnRuleCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        // Diferimos para que el binding aplique el valor antes de leerlo.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // Sprint A — el cambio mas comun en la celda es el toggle ACT
                // (Enabled).  Habilitar/deshabilitar una regla puede crear o
                // resolver un conflicto, asi que recalculamos siempre.
                RecomputeRuleConflicts();
                UpdateRulesSummary();
                UpdateStatusBar();
                TrySaveRulesSilent(_persistence.DefaultPath);
                RefreshExportTree();
            }
            catch { /* no propagar errores de refresco */ }
        }));
    }

    // ============================================================
    // FEATURE #6 — Ver IDs de elementos por regla (trazabilidad)
    // ============================================================

    private void OnShowRuleElementIdsClick(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not MappingRule selected)
        {
            MessageBox.Show(this,
                "Selecciona una regla de la tabla antes de pulsar 'Ver IDs elementos'.",
                "Sin regla seleccionada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureActiveDocument(out var doc)) return;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;

            // Intentar usar la ultima extraccion para mostrar IDs sin tocar Revit.
            // Si no hay extraccion previa o no tiene esta regla, usamos el motor
            // directamente sobre el documento activo (lo mismo que "Probar regla").
            var ids = new List<long>();

            if (_lastExtraction is not null)
            {
                foreach (var m in _lastExtraction.Measurements)
                {
                    if (m.ContributingRuleIds.Contains(selected.Id))
                        ids.AddRange(m.ElementIds);
                }
            }

            // Fallback: si no hay extraccion, evaluamos en caliente la regla
            // contra el modelo (puede ser mas lento pero no requiere "Calcular").
            if (ids.Count == 0)
            {
                var matchIds = _ruleEngine.MatchElements(doc, selected);
                ids.AddRange(matchIds.Select(id => (long)id.Value));
            }

            ids = ids.Distinct().OrderBy(x => x).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Regla: {selected.DisplayLabel}");
            sb.AppendLine($"Categoria: {selected.CategoryDisplayName}");
            sb.AppendLine($"Partida BC3: {selected.BC3Code}");
            sb.AppendLine();
            sb.AppendLine($"Elementos que coinciden con la regla: {ids.Count}");
            sb.AppendLine();
            if (ids.Count == 0)
            {
                sb.AppendLine("(Ninguno. Revisa la categoria y los filtros.)");
            }
            else
            {
                sb.AppendLine("IDs de elementos (Revit ElementId):");
                var show = ids.Take(500).ToList();
                foreach (var id in show)
                    sb.AppendLine("  · " + id.ToString(CultureInfo.InvariantCulture));
                if (ids.Count > show.Count)
                    sb.AppendLine($"  ...y {ids.Count - show.Count} mas (limitado a 500 para la vista).");
            }

            var dlg = new RuleDiagnosticDialog(sb.ToString()) { Owner = this };
            dlg.Title = $"IDs - {selected.DisplayLabel}";
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error generando lista de IDs:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    // ============================================================
    // FEATURE #5 — Arbol BC3 a exportar (panel izquierdo inferior)
    // ============================================================

    /// <summary>
    /// Repinta el TreeView del BC3 a exportar reflejando la estructura
    /// Capitulo > Subcapitulo > Partida del mapeo actual.  Si hay mediciones
    /// calculadas, anade la cantidad y el importe al texto de cada partida.
    /// </summary>
    /// <summary>
    /// Sprint B: reconstruye el arbol de capitulos a partir del modelo
    /// (_chapters, _subChapters, _rules) preservando la expansion y
    /// seleccion previa por Id de entidad.  Crea siempre los nodos
    /// "Sin asignar" si hay reglas huerfanas (ChapterId == null).
    /// </summary>
    private void RefreshExportTree()
    {
        if (ExportTree is null) return;

        // Snapshot de estado previo para preservar expansion / seleccion
        // por Id (la coleccion se reconstruye desde cero).
        var expandedChapterIds = new HashSet<string>(StringComparer.Ordinal);
        var expandedSubIds = new HashSet<string>(StringComparer.Ordinal);
        string? selectedChapterId = null;
        string? selectedSubId = null;
        string? selectedRuleId = null;
        foreach (var root in _chapterTreeRoots)
        {
            if (root is ChapterTreeNode cn)
            {
                if (cn.IsExpanded) expandedChapterIds.Add(cn.Chapter.Id);
                if (cn.IsSelected) selectedChapterId = cn.Chapter.Id;
                foreach (var child in cn.Children)
                {
                    if (child is SubChapterTreeNode sn)
                    {
                        if (sn.IsExpanded) expandedSubIds.Add(sn.SubChapter.Id);
                        if (sn.IsSelected) selectedSubId = sn.SubChapter.Id;
                        foreach (var grand in sn.Children)
                        {
                            if (grand is RuleTreeNode rn && rn.IsSelected)
                                selectedRuleId = rn.Rule.Id;
                        }
                    }
                    else if (child is RuleTreeNode rn && rn.IsSelected)
                    {
                        selectedRuleId = rn.Rule.Id;
                    }
                }
            }
            else if (root is UnassignedTreeNode un && un.IsSelected)
            {
                // marca para preservar seleccion del nodo Sin asignar.
            }
        }

        // Indices para resolver rapidamente.
        var measurementsByCode = _measurements
            .GroupBy(m => m.BC3Code ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var subsByChapter = _subChapters
            .Where(s => !string.IsNullOrEmpty(s.ChapterId))
            .GroupBy(s => s.ChapterId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Order).ToList(), StringComparer.Ordinal);

        // Particion de reglas por destino.
        var rulesByChapter = new Dictionary<string, List<MappingRule>>(StringComparer.Ordinal);  // ChapterId -> reglas sin sub
        var rulesBySub = new Dictionary<string, List<MappingRule>>(StringComparer.Ordinal);      // SubChapterId -> reglas
        var orphanRules = new List<MappingRule>();

        var validChapterIds = new HashSet<string>(_chapters.Select(c => c.Id), StringComparer.Ordinal);
        var validSubIds = new HashSet<string>(_subChapters.Select(s => s.Id), StringComparer.Ordinal);

        foreach (var rule in _rules)
        {
            // Sprint B: regla huerfana si no tiene ChapterId valido.
            if (string.IsNullOrEmpty(rule.ChapterId) || !validChapterIds.Contains(rule.ChapterId))
            {
                orphanRules.Add(rule);
                continue;
            }
            if (!string.IsNullOrEmpty(rule.SubChapterId) && validSubIds.Contains(rule.SubChapterId))
            {
                if (!rulesBySub.TryGetValue(rule.SubChapterId, out var list))
                    rulesBySub[rule.SubChapterId] = list = new List<MappingRule>();
                list.Add(rule);
            }
            else
            {
                if (!rulesByChapter.TryGetValue(rule.ChapterId, out var list))
                    rulesByChapter[rule.ChapterId] = list = new List<MappingRule>();
                list.Add(rule);
            }
        }

        _chapterTreeRoots.Clear();
        int totalPartidas = 0;

        foreach (var ch in _chapters.OrderBy(c => c.Order).ThenBy(c => c.Code, StringComparer.Ordinal))
        {
            var chNode = new ChapterTreeNode(ch)
            {
                IsExpanded = expandedChapterIds.Count == 0 || expandedChapterIds.Contains(ch.Id),
                IsSelected = string.Equals(selectedChapterId, ch.Id, StringComparison.Ordinal),
            };

            // Reglas sueltas (sin subcap) del capitulo.
            if (rulesByChapter.TryGetValue(ch.Id, out var directRules))
            {
                foreach (var r in directRules.OrderByDescending(x => x.Priority).ThenBy(x => x.BC3Code, StringComparer.Ordinal))
                {
                    chNode.Children.Add(BuildRuleNode(r, measurementsByCode, selectedRuleId));
                    if (r.Enabled) totalPartidas++;
                }
            }

            // Subcapitulos del capitulo.
            if (subsByChapter.TryGetValue(ch.Id, out var subs))
            {
                foreach (var sub in subs)
                {
                    var subNode = new SubChapterTreeNode(sub)
                    {
                        IsExpanded = expandedSubIds.Count == 0 || expandedSubIds.Contains(sub.Id),
                        IsSelected = string.Equals(selectedSubId, sub.Id, StringComparison.Ordinal),
                    };
                    if (rulesBySub.TryGetValue(sub.Id, out var subRules))
                    {
                        foreach (var r in subRules.OrderByDescending(x => x.Priority).ThenBy(x => x.BC3Code, StringComparer.Ordinal))
                        {
                            subNode.Children.Add(BuildRuleNode(r, measurementsByCode, selectedRuleId));
                            if (r.Enabled) totalPartidas++;
                        }
                    }
                    var ruleCountTxt = subNode.Children.Count == 0 ? " (0 reglas)" : "";
                    subNode.Header = sub.Display + ruleCountTxt;
                    chNode.Children.Add(subNode);
                }
            }

            var emptyTxt = chNode.Children.Count == 0 ? " (0 reglas)" : "";
            chNode.Header = ch.Display + emptyTxt;
            _chapterTreeRoots.Add(chNode);
        }

        // Nodo "Sin asignar": solo si hay huerfanas (AC3.1).
        if (orphanRules.Count > 0)
        {
            _unassignedNode.Children.Clear();
            foreach (var r in orphanRules.OrderByDescending(x => x.Priority).ThenBy(x => x.BC3Code, StringComparer.Ordinal))
            {
                _unassignedNode.Children.Add(BuildRuleNode(r, measurementsByCode, selectedRuleId));
            }
            _unassignedNode.Header = $"Sin asignar ({orphanRules.Count})";
            _unassignedNode.IsExpanded = true;
            _chapterTreeRoots.Add(_unassignedNode);
        }

        var ci = CultureInfo.GetCultureInfo("es-ES");
        ExportTreeCountText.Text = totalPartidas == 0
            ? "sin partidas"
            : $"{totalPartidas.ToString("N0", ci)} partidas";

        ExportTreeHint.Text = _chapters.Count == 0
            ? "Sin capitulos. Pulsa '＋ Cap.' para crear el primero."
            : "Arrastra reglas o subcapitulos entre capitulos para reorganizarlos.";

        UpdateNodeButtonsEnabled();
    }

    private static RuleTreeNode BuildRuleNode(
        MappingRule rule,
        Dictionary<string, MeasurementResult> measurementsByCode,
        string? selectedRuleId)
    {
        var code = rule.BC3Code ?? string.Empty;
        var summary = !string.IsNullOrEmpty(rule.CatalogSummary) ? rule.CatalogSummary : rule.Name;
        var enabledMark = rule.Enabled ? "" : "  (inactiva)";
        var header = $"{code}  —  {summary}{enabledMark}";
        if (measurementsByCode.TryGetValue(code, out var m) && m.FinalQuantity > 0)
            header = $"{code}  ·  {m.FinalQuantity:N3} {m.Unit}  —  {summary}{enabledMark}";
        return new RuleTreeNode(rule)
        {
            Header = header,
            IsSelected = string.Equals(selectedRuleId, rule.Id, StringComparison.Ordinal),
        };
    }

    // ============================================================
    // Sprint B — Gestion del arbol de capitulos
    // ============================================================

    private MappingTreeNode? _selectedTreeNode;

    private void OnExportTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedTreeNode = e.NewValue as MappingTreeNode;
        UpdateNodeButtonsEnabled();
    }

    private void UpdateNodeButtonsEnabled()
    {
        var node = _selectedTreeNode;
        AddSubChapterButton.IsEnabled = node is ChapterTreeNode || node is SubChapterTreeNode;
        RenameNodeButton.IsEnabled    = node is ChapterTreeNode || node is SubChapterTreeNode;
        DeleteNodeButton.IsEnabled    = node is ChapterTreeNode || node is SubChapterTreeNode;
        MoveNodeUpButton.IsEnabled    = node is ChapterTreeNode || node is SubChapterTreeNode;
        MoveNodeDownButton.IsEnabled  = node is ChapterTreeNode || node is SubChapterTreeNode;
    }

    private void OnAddChapterClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ChapterEditorDialog("Nuevo capitulo", string.Empty, string.Empty)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var maxOrder = _chapters.Count == 0 ? -1 : _chapters.Max(c => c.Order);
        var chapter = new Chapter
        {
            Code = dlg.CodeValue,
            Name = dlg.NameValue,
            Order = maxOrder + 1,
        };
        _chapters.Add(chapter);
        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
    }

    private void OnAddSubChapterClick(object sender, RoutedEventArgs e)
    {
        // AC2.1: solo activo con capitulo o subcap seleccionado.  Si esta
        // seleccionado un sub, usamos su capitulo padre.
        Chapter? parent = _selectedTreeNode switch
        {
            ChapterTreeNode ch => ch.Chapter,
            SubChapterTreeNode sn => _chapters.FirstOrDefault(c => c.Id == sn.SubChapter.ChapterId),
            _ => null,
        };
        if (parent is null)
        {
            MessageBox.Show(this,
                "Selecciona primero un capitulo en el arbol.",
                "Subcapitulo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new ChapterEditorDialog($"Nuevo subcapitulo en {parent.Display}", string.Empty, string.Empty)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var maxOrder = _subChapters.Where(s => s.ChapterId == parent.Id).Select(s => s.Order)
            .DefaultIfEmpty(-1).Max();
        var sub = new SubChapter
        {
            ChapterId = parent.Id,
            Code = dlg.CodeValue,
            Name = dlg.NameValue,
            Order = maxOrder + 1,
        };
        _subChapters.Add(sub);
        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
    }

    private void OnRenameNodeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedTreeNode is ChapterTreeNode chNode)
        {
            var dlg = new ChapterEditorDialog("Editar capitulo", chNode.Chapter.Code, chNode.Chapter.Name)
            {
                Owner = this,
            };
            if (dlg.ShowDialog() != true) return;
            chNode.Chapter.Code = dlg.CodeValue;
            chNode.Chapter.Name = dlg.NameValue;
            TrySaveRulesSilent(_persistence.DefaultPath);
            RefreshExportTree();
        }
        else if (_selectedTreeNode is SubChapterTreeNode subNode)
        {
            var dlg = new ChapterEditorDialog("Editar subcapitulo", subNode.SubChapter.Code, subNode.SubChapter.Name)
            {
                Owner = this,
            };
            if (dlg.ShowDialog() != true) return;
            subNode.SubChapter.Code = dlg.CodeValue;
            subNode.SubChapter.Name = dlg.NameValue;
            TrySaveRulesSilent(_persistence.DefaultPath);
            RefreshExportTree();
        }
    }

    private void OnDeleteNodeClick(object sender, RoutedEventArgs e)
    {
        if (_selectedTreeNode is ChapterTreeNode chNode)
        {
            var affected = _rules.Count(r => r.ChapterId == chNode.Chapter.Id);
            var subCount = _subChapters.Count(s => s.ChapterId == chNode.Chapter.Id);
            var ask = MessageBox.Show(this,
                $"Borrar el capitulo '{chNode.Chapter.Display}'?\n\n" +
                $"Se borraran tambien {subCount} subcapitulo(s).\n" +
                $"{affected} regla(s) quedaran como 'Sin asignar' (huerfanas) y no se exportaran hasta reasignarlas.",
                "Borrar capitulo",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;

            // Huerfaniza reglas y borra subcaps del capitulo.
            foreach (var r in _rules.Where(r => r.ChapterId == chNode.Chapter.Id).ToList())
            {
                r.ChapterId = null;
                r.SubChapterId = null;
            }
            foreach (var s in _subChapters.Where(s => s.ChapterId == chNode.Chapter.Id).ToList())
                _subChapters.Remove(s);
            _chapters.Remove(chNode.Chapter);
        }
        else if (_selectedTreeNode is SubChapterTreeNode subNode)
        {
            var affected = _rules.Count(r => r.SubChapterId == subNode.SubChapter.Id);
            var ask = MessageBox.Show(this,
                $"Borrar el subcapitulo '{subNode.SubChapter.Display}'?\n\n" +
                $"{affected} regla(s) volveran a colgar directamente del capitulo padre.",
                "Borrar subcapitulo",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ask != MessageBoxResult.Yes) return;

            foreach (var r in _rules.Where(r => r.SubChapterId == subNode.SubChapter.Id).ToList())
                r.SubChapterId = null;
            _subChapters.Remove(subNode.SubChapter);
        }
        else return;

        RecomputeRuleConflicts();
        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
    }

    private void OnMoveNodeUpClick(object sender, RoutedEventArgs e) => MoveSelectedNode(-1);
    private void OnMoveNodeDownClick(object sender, RoutedEventArgs e) => MoveSelectedNode(+1);

    private void MoveSelectedNode(int delta)
    {
        if (_selectedTreeNode is ChapterTreeNode chNode)
        {
            var ordered = _chapters.OrderBy(c => c.Order).ThenBy(c => c.Code, StringComparer.Ordinal).ToList();
            int idx = ordered.IndexOf(chNode.Chapter);
            int newIdx = idx + delta;
            if (idx < 0 || newIdx < 0 || newIdx >= ordered.Count) return;
            // Swap orders.
            var a = ordered[idx];
            var b = ordered[newIdx];
            (a.Order, b.Order) = (b.Order, a.Order);
        }
        else if (_selectedTreeNode is SubChapterTreeNode subNode)
        {
            var ordered = _subChapters
                .Where(s => s.ChapterId == subNode.SubChapter.ChapterId)
                .OrderBy(s => s.Order)
                .ToList();
            int idx = ordered.IndexOf(subNode.SubChapter);
            int newIdx = idx + delta;
            if (idx < 0 || newIdx < 0 || newIdx >= ordered.Count) return;
            var a = ordered[idx];
            var b = ordered[newIdx];
            (a.Order, b.Order) = (b.Order, a.Order);
        }
        else return;

        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
    }

    // ============================================================
    // Sprint B — Hooks invocados por ChapterTreeDropHandler
    // ============================================================

    /// <summary>
    /// Tras un drop de regla.  Reevalua conflictos (Sprint A: AC4.2 — no
    /// cancela el move, solo marca), persiste y refresca el arbol.
    /// </summary>
    public void OnRuleMovedByDrop(MappingRule rule)
    {
        RecomputeRuleConflicts();
        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
        // Refrescar el grid central para que el cambio de capitulo se vea.
        try { RulesGrid.Items.Refresh(); } catch { }
    }

    /// <summary>
    /// Mueve un subcapitulo a otro capitulo padre.  Las reglas que apuntaban
    /// a ese subcap mantienen su SubChapterId (siguen al sub) y se actualiza
    /// su ChapterId al del nuevo padre.
    /// </summary>
    public void MoveSubChapterToChapter(SubChapter sub, Chapter targetChapter)
    {
        if (sub is null || targetChapter is null) return;
        if (sub.ChapterId == targetChapter.Id) return;

        sub.ChapterId = targetChapter.Id;
        // Order al final del nuevo capitulo.
        sub.Order = _subChapters.Where(s => s.ChapterId == targetChapter.Id && !ReferenceEquals(s, sub))
            .Select(s => s.Order).DefaultIfEmpty(-1).Max() + 1;

        // Reglas que vivian en este sub: ahora pertenecen al nuevo capitulo
        // (su SubChapterId no cambia, sigue apuntando al mismo sub).
        foreach (var r in _rules.Where(r => r.SubChapterId == sub.Id))
            r.ChapterId = targetChapter.Id;

        RecomputeRuleConflicts();  // Sprint A: AC2.3
        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
        try { RulesGrid.Items.Refresh(); } catch { }
    }

    /// <summary>
    /// Reordena dos capitulos.  El usuario arrastra el origen sobre el
    /// destino; insertPosition indica si va antes o despues.
    /// </summary>
    public void ReorderChapter(Chapter source, Chapter target, GongSolutions.Wpf.DragDrop.RelativeInsertPosition position)
    {
        if (source is null || target is null || ReferenceEquals(source, target)) return;

        var ordered = _chapters.OrderBy(c => c.Order).ThenBy(c => c.Code, StringComparer.Ordinal).ToList();
        ordered.Remove(source);
        int targetIdx = ordered.IndexOf(target);
        if (targetIdx < 0) targetIdx = ordered.Count;
        bool insertAfter = (position & GongSolutions.Wpf.DragDrop.RelativeInsertPosition.AfterTargetItem) != 0
                           && (position & GongSolutions.Wpf.DragDrop.RelativeInsertPosition.BeforeTargetItem) == 0;
        if (insertAfter) targetIdx++;
        if (targetIdx > ordered.Count) targetIdx = ordered.Count;
        ordered.Insert(targetIdx, source);

        // Renumera Order de forma contigua para evitar drift.
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;

        TrySaveRulesSilent(_persistence.DefaultPath);
        RefreshExportTree();
    }

    // ============================================================
    // FIX #7 — Cache de datos del catalogo en la regla
    // ============================================================

    /// <summary>
    /// Si la regla tiene un BC3Code y el catalogo cargado lo contiene,
    /// copia descripcion/unidad/precio del catalogo a los campos Catalog*
    /// de la regla.  Si no lo contiene, deja el cache anterior intacto
    /// (asi sobrevive a un re-import de otro BC3).
    /// </summary>
    private void SyncRuleCatalogCache(MappingRule rule)
    {
        if (rule is null || string.IsNullOrEmpty(rule.BC3Code)) return;
        if (_currentDocument is null) return;
        if (!_currentDocument.Concepts.TryGetValue(rule.BC3Code, out var concept)) return;
        if (concept is null) return;

        if (!string.IsNullOrWhiteSpace(concept.Summary))
            rule.CatalogSummary = concept.Summary;
        if (!string.IsNullOrWhiteSpace(concept.Unit))
            rule.CatalogUnit = concept.Unit;
        if (concept.Price > 0)
            rule.CatalogPrice = concept.Price;
        if (!string.IsNullOrWhiteSpace(concept.Description))
            rule.CatalogDescription = concept.Description;
    }

    /// <summary>
    /// Recorre todas las reglas y sincroniza el cache de catalogo con el BC3
    /// actualmente cargado.  Se llama tras importar un BC3 para que las
    /// reglas refresquen su info con los datos del nuevo catalogo, pero sin
    /// borrar lo que tenian si el codigo ya no aparece.
    /// </summary>
    private void SyncAllRulesCatalogCache()
    {
        if (_currentDocument is null) return;
        foreach (var r in _rules) SyncRuleCatalogCache(r);
    }

    // ============================================================
    // Drag-and-drop: árbol BC3 → tabla de reglas
    // ============================================================
    //
    // Flujo:
    //   1. El usuario pulsa el ratón sobre un nodo del árbol izquierdo
    //      (ConceptsTree).  Guardamos el punto de inicio en _dragStartPoint.
    //   2. En el movimiento del ratón detectamos si se supera el umbral
    //      mínimo de arrastre de Windows.  Si es así, obtenemos el
    //      BC3Concept almacenado en TreeViewItem.Tag e iniciamos un
    //      DragDrop.DoDragDrop con ese concepto como payload.
    //   3. El DataGrid de reglas (RulesGrid) tiene AllowDrop=True.
    //      En DragOver: resaltamos la fila bajo el cursor seleccionándola,
    //      y comunicamos DragDropEffects.Copy (cursor "+").
    //      En Drop: localizamos la DataGridRow bajo el cursor,
    //      aplicamos el código BC3 + datos de catálogo a la MappingRule
    //      y persistimos el mapeo.
    //   4. En DragLeave limpiamos la selección visual si el ratón sale
    //      del grid sin soltar.
    //
    // Nota: no restringimos el tipo de concepto arrastrado (capítulo,
    // partida, material...).  El usuario es quien decide qué código
    // quiere usar en la regla.
    // ============================================================

    // Punto donde el usuario presionó el botón (para calcular umbral de arrastre).
    // Calificado explícitamente para evitar ambigüedad con Autodesk.Revit.DB.Point.
    private System.Windows.Point _dragStartPoint;

    // Fila que estamos resaltando durante el drag (para limpiarla al salir).
    private DataGridRow? _dragOverRow;

    /// <summary>
    /// Registra el punto de inicio del posible arrastre.
    /// </summary>
    private void OnConceptTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    /// <summary>
    /// Inicia el arrastre si el ratón se ha movido más del umbral mínimo
    /// de Windows y hay un BC3Concept bajo el cursor.
    /// </summary>
    private void OnConceptTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(null);
        var delta   = _dragStartPoint - current;

        // Umbral mínimo de Windows para distinguir "click" de "arrastre".
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Buscamos el TreeViewItem bajo el cursor original.
        var tvi = FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (tvi?.Tag is not BC3Concept concept) return;

        // Encapsulamos el concepto en un DataObject con clave tipada.
        var data = new DataObject(typeof(BC3Concept).FullName!, concept);
        DragDrop.DoDragDrop(tvi, data, DragDropEffects.Copy);
    }

    /// <summary>
    /// Durante el arrastre sobre el grid: resaltar la fila target y
    /// comunicar si el drop es válido.
    /// </summary>
    private void OnRulesGridDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!e.Data.GetDataPresent(typeof(BC3Concept).FullName!))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var row = FindDataGridRowAt(e.GetPosition(RulesGrid));

        // Resaltar la nueva fila target.
        if (row != _dragOverRow)
        {
            // Quitar resaltado anterior.
            if (_dragOverRow is not null)
                _dragOverRow.Background = null;

            _dragOverRow = row;

            if (row is not null)
                row.Background = FindResource("Mockup.Bg.RowSelected") as System.Windows.Media.Brush;
        }

        e.Effects = row?.DataContext is MappingRule
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// El ratón sale del grid sin soltar: limpiamos el resaltado.
    /// </summary>
    private void OnRulesGridDragLeave(object sender, DragEventArgs e)
    {
        ClearDragOverHighlight();
    }

    /// <summary>
    /// El usuario suelta la partida BC3 sobre una fila del grid.
    /// Aplica el código y los datos de catálogo a la regla correspondiente.
    /// </summary>
    private void OnRulesGridDrop(object sender, DragEventArgs e)
    {
        ClearDragOverHighlight();

        if (!e.Data.GetDataPresent(typeof(BC3Concept).FullName!)) return;
        if (e.Data.GetData(typeof(BC3Concept).FullName!) is not BC3Concept concept) return;

        var row = FindDataGridRowAt(e.GetPosition(RulesGrid));
        if (row?.DataContext is not MappingRule rule) return;

        // ── Aplicar el concepto a la regla ──────────────────────────────
        rule.BC3Code = concept.Code;

        // Rellenamos el caché de catálogo directamente desde el concepto
        // arrastrado (misma lógica que SyncRuleCatalogCache pero sin
        // necesitar el documento completo).
        if (!string.IsNullOrWhiteSpace(concept.Summary))
            rule.CatalogSummary = concept.Summary;
        if (!string.IsNullOrWhiteSpace(concept.Unit))
            rule.CatalogUnit = concept.Unit;
        if (concept.Price > 0)
            rule.CatalogPrice = concept.Price;
        if (!string.IsNullOrWhiteSpace(concept.Description))
            rule.CatalogDescription = concept.Description;

        // ── Actualizar UI y persistir ───────────────────────────────────
        RevalidateRules();
        UpdateRulesSummary();
        UpdateMappingBC3InfoText();
        UpdateStatusBar();
        RefreshExportTree();
        TrySaveRulesSilent(_persistence.DefaultPath);

        // Seleccionamos la regla modificada para que el usuario vea cuál cambió.
        RulesGrid.SelectedItem = rule;

        // Forzamos el refresco de la vista agrupada para que la celda
        // PARTIDA BC3 muestre el nuevo código de inmediato.
        (RulesGrid.ItemsSource as System.ComponentModel.ICollectionView)?.Refresh();

        e.Handled = true;
    }

    // ── Helpers D&D ─────────────────────────────────────────────────────

    private void ClearDragOverHighlight()
    {
        if (_dragOverRow is not null)
        {
            _dragOverRow.Background = null;
            _dragOverRow = null;
        }
    }

    /// <summary>
    /// Devuelve la <see cref="DataGridRow"/> que hay bajo el punto indicado
    /// dentro del <see cref="RulesGrid"/>, o null si no hay ninguna.
    /// </summary>
    private DataGridRow? FindDataGridRowAt(System.Windows.Point position)
    {
        var hit = VisualTreeHelper.HitTest(RulesGrid, position);
        return FindVisualParent<DataGridRow>(hit?.VisualHit);
    }

    // ============================================================
    //  Sprint A — Protocolo de avisos contra duplicados de partida
    // ============================================================
    //  Si dos reglas habilitadas comparten la misma terna
    //  (ChapterCode, SubChapterCode, BC3Code), el writer BC3 las
    //  sufija con .001/.002 para cumplir FIEBDC-3, pero TCQ las
    //  rechaza como duplicados.  El validador detecta esos casos.
    //  El usuario sigue siendo el dueño de la decision: solo
    //  informamos, no bloqueamos.
    // ============================================================

    /// <summary>
    /// Recalcula <c>HasConflict</c>/<c>ConflictTooltip</c> sobre todas las
    /// reglas y refresca el grid para repintar las filas en conflicto.
    /// Se llama tras cualquier mutacion que pueda crear o resolver una
    /// colision (alta, edicion, baja, duplicacion, toggle ACT).
    /// </summary>
    private void RecomputeRuleConflicts()
    {
        // Limpio el estado anterior en TODAS las reglas: cualquier regla
        // que ya no este en conflicto debe perder la marca.
        foreach (var r in _rules)
        {
            r.HasConflict = false;
            r.ConflictTooltip = string.Empty;
        }

        // Sprint B: el validador (Sprint A) opera sobre los strings
        // ChapterCode/SubChapterCode.  En el modelo nuevo esos strings son
        // derivados; los sincronizamos desde las FKs antes de validar para
        // que el validador detecte conflictos consistentes con el modelo
        // actual aunque el usuario haya hecho un drag-and-drop sin guardar.
        SyncLegacyStringsForValidation();

        var conflicts = MappingRuleValidator.DetectConflicts(_rules);
        if (conflicts.Count > 0)
        {
            // Indice rule.Id -> regla para componer tooltips legibles.
            // Usamos GroupBy para tolerar Ids duplicados (no deberia pasar,
            // pero blindarse contra ello evita un crash en UI).
            var byId = _rules
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var c in conflicts)
            {
                // Para cada regla implicada, su tooltip lista las OTRAS
                // reglas con las que choca dentro de este grupo.
                foreach (var rid in c.RuleIds)
                {
                    if (!byId.TryGetValue(rid, out var rule)) continue;
                    var others = c.RuleIds
                        .Where(id => !string.Equals(id, rid, StringComparison.Ordinal))
                        .Select(id => byId.TryGetValue(id, out var o)
                                        ? MappingRuleValidator.DescribeRule(o)
                                        : id)
                        .ToList();

                    var location = string.IsNullOrEmpty(c.SubChapterDisplay)
                        ? c.ChapterDisplay
                        : $"{c.ChapterDisplay} ▸ {c.SubChapterDisplay}";

                    rule.HasConflict = true;
                    rule.ConflictTooltip =
                        $"Conflicto: misma partida ({c.BC3Code}) en {location} " +
                        $"que la(s) regla(s): {string.Join(", ", others)}.";
                }
            }
        }

        try { RulesGrid.Items.Refresh(); } catch { /* ignore refresh races */ }
    }

    /// <summary>
    /// Sprint B helper: refresca los strings legacy de las reglas
    /// (<see cref="MappingRule.ChapterCode"/> / <c>ChapterName</c> /
    /// <c>SubChapterCode</c> / <c>SubChapterName</c>) a partir de las
    /// FKs y las entidades en <see cref="_chapters"/> / <see cref="_subChapters"/>.
    /// Necesario porque el <see cref="MappingRuleValidator"/> (Sprint A) opera
    /// sobre esos strings, no sobre FKs.
    /// </summary>
    private void SyncLegacyStringsForValidation()
    {
        var chById = _chapters
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var subById = _subChapters
            .Where(s => !string.IsNullOrEmpty(s.Id))
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var rule in _rules)
        {
            if (!string.IsNullOrEmpty(rule.ChapterId) && chById.TryGetValue(rule.ChapterId, out var ch))
            {
                rule.ChapterCode = ch.Code ?? string.Empty;
                rule.ChapterName = ch.Name ?? string.Empty;
            }
            else
            {
                rule.ChapterCode = string.Empty;
                rule.ChapterName = string.Empty;
            }

            if (!string.IsNullOrEmpty(rule.SubChapterId) && subById.TryGetValue(rule.SubChapterId, out var sub))
            {
                rule.SubChapterCode = sub.Code ?? string.Empty;
                rule.SubChapterName = sub.Name ?? string.Empty;
            }
            else
            {
                rule.SubChapterCode = string.Empty;
                rule.SubChapterName = string.Empty;
            }
        }
    }

    /// <summary>
    /// Antes de guardar una edicion de regla, comprueba si el resultado
    /// chocaria con otra regla habilitada.  Si choca, muestra el dialogo
    /// del AC1.1: el usuario puede cancelar (volver al editor sin guardar)
    /// o forzar el guardado (la regla queda persistida y marcada como
    /// conflictiva en el grid).
    /// Devuelve <c>true</c> si el guardado debe continuar; <c>false</c>
    /// si el usuario opto por cancelar.
    /// </summary>
    private bool ConfirmEditorSaveDespiteConflict(MappingRule candidate)
    {
        var conflict = MappingRuleValidator.FindConflictFor(candidate, _rules);
        if (conflict is null) return true;

        // Localiza una regla "vecina" en el conflicto para nombrarla en el aviso.
        var otherId = conflict.RuleIds.FirstOrDefault(
            id => !string.Equals(id, candidate.Id, StringComparison.Ordinal));
        var otherRule = _rules.FirstOrDefault(r => r.Id == otherId);
        var otherLabel = otherRule is null
            ? "(otra regla)"
            : MappingRuleValidator.DescribeRule(otherRule);

        var subPart = string.IsNullOrEmpty(conflict.SubChapterDisplay)
            ? string.Empty
            : $" ▸ subcapitulo '{conflict.SubChapterDisplay}'";

        var msg =
            $"Ya existe otra regla habilitada con codigo '{conflict.BC3Code}' " +
            $"en el capitulo '{conflict.ChapterDisplay}'{subPart}.\n\n" +
            $"Regla en conflicto: {otherLabel}\n\n" +
            "Si continuas, ambas partidas se exportaran al BC3 con sufijo " +
            "automatico (.001/.002) y TCQ NO podra importar la duplicada en " +
            "su modelo nativo.\n\n" +
            "¿Guardar de todas formas?  (No = volver al editor sin guardar)";

        var result = MessageBox.Show(
            this,
            msg,
            "Duplicado de partida en el mismo capitulo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Guardia pre-exportacion (AC3): si hay conflictos, ofrece cancelar o
    /// continuar.  Devuelve <c>true</c> si el export debe seguir adelante,
    /// <c>false</c> si el usuario eligio cancelar para revisar.
    /// Si no hay conflictos, no muestra dialogo y devuelve <c>true</c>.
    /// </summary>
    private bool ConfirmExportDespiteConflicts()
    {
        var conflicts = MappingRuleValidator.DetectConflicts(_rules);
        if (conflicts.Count == 0) return true;

        var byId = _rules
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Se han detectado {conflicts.Count} partida(s) duplicada(s) " +
                      "dentro del mismo capitulo/subcapitulo:");
        sb.AppendLine();

        int n = 0;
        foreach (var c in conflicts)
        {
            n++;
            var loc = string.IsNullOrEmpty(c.SubChapterDisplay)
                ? c.ChapterDisplay
                : $"{c.ChapterDisplay} ▸ {c.SubChapterDisplay}";
            sb.AppendLine($"  {n}. {loc} — partida {c.BC3Code}");
            foreach (var rid in c.RuleIds)
            {
                if (!byId.TryGetValue(rid, out var r)) continue;
                sb.AppendLine($"        • {MappingRuleValidator.DescribeRule(r)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Si exportas, el BC3 sufijara automaticamente las repetidas " +
                      "(.001/.002), pero TCQ las rechazara como duplicadas al importar.");
        sb.AppendLine();
        sb.AppendLine("¿Exportar igual con sufijo automatico?  (No = cancelar y revisar)");

        var result = MessageBox.Show(
            this,
            sb.ToString(),
            "Conflictos de partidas duplicadas",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Sube por el árbol visual buscando un ancestro de tipo T.
    /// Útil para localizar TreeViewItem o DataGridRow a partir del elemento
    /// exacto bajo el cursor.
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}

// ============================================================
//  PrepareIfcEventHandler
// ============================================================
//  Ejecuta BC3IfcExportPrepService.WriteToModel() en el contexto
//  de la API de Revit (IExternalEventHandler), evitando el error
//  "Starting a transaction from outside of API context".
//
//  Patron de uso:
//    var task = handler.InvokeInApiContext(doc, app, measurements, rules, service);
//    externalEvent.Raise();
//    var result = await task;
// ============================================================

internal sealed class PrepareIfcEventHandler : IExternalEventHandler
{
    private TaskCompletionSource<BC3IfcExportPrepResult>? _tcs;
    private Document?                                      _doc;
    private Autodesk.Revit.ApplicationServices.Application? _app;
    private List<MeasurementResult>?                       _measurements;
    private List<MappingRule>?                             _rules;
    private IBC3IfcExportPrepService?                      _service;

    /// <summary>
    /// Registra los parametros de la operacion y devuelve un Task que se
    /// completa cuando Revit ejecute <see cref="Execute"/> en contexto valido.
    /// Solo puede haber una operacion pendiente a la vez.
    /// </summary>
    public Task<BC3IfcExportPrepResult> InvokeInApiContext(
        Document doc,
        Autodesk.Revit.ApplicationServices.Application app,
        List<MeasurementResult> measurements,
        List<MappingRule> rules,
        IBC3IfcExportPrepService service)
    {
        if (_tcs is not null && !_tcs.Task.IsCompleted)
            return Task.FromException<BC3IfcExportPrepResult>(
                new InvalidOperationException("Ya hay una operacion de preparacion IFC en curso."));

        _doc          = doc;
        _app          = app;
        _measurements = measurements;
        _rules        = rules;
        _service      = service;
        _tcs          = new TaskCompletionSource<BC3IfcExportPrepResult>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
        return _tcs.Task;
    }

    // Revit llama a Execute() en el contexto de API valido.
    public void Execute(UIApplication uiApp)
    {
        var tcs = _tcs;
        if (tcs is null) return;
        _tcs = null;

        if (_doc is null || _app is null || _measurements is null
            || _rules is null || _service is null)
        {
            tcs.TrySetException(new InvalidOperationException("Parametros de PrepareIfc no inicializados."));
            return;
        }

        try
        {
            var result = _service.WriteToModel(_doc, _app, _measurements, _rules);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        finally
        {
            _doc = null; _app = null; _measurements = null; _rules = null; _service = null;
        }
    }

    public string GetName() => "E3G Preparar parametros IFC";
}
