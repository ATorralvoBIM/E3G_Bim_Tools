using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.ScheduleExcel;

/// <summary>
/// Ventana «TableLink».
///   • Carpeta de exportación habitual del proyecto (recordada por modelo).
///   • Exportar: listado de tablas del modelo con vista previa; una o varias
///     (una pestaña por tabla). Por defecto «como en Revit»; opcionalmente una
///     fila por elemento.
///   • Importar: vista previa del Excel con los cambios en salmón (clic para
///     incluir/excluir), aplicados en verde; o lista de cambios.
///   • Barra de progreso en las operaciones largas.
/// Modal (ShowDialog) desde el comando: todo corre en el hilo de Revit; la
/// barra se refresca bombeando el dispatcher mientras dura la operación.
/// </summary>
public partial class ScheduleExcelWindow : Window
{
    private const int ExportPreviewRows = 400;
    private const int ImportPreviewRows = 5000;

    private readonly UIDocument _uidoc;
    private readonly Document _doc;
    private readonly IScheduleExcelService _service;
    private readonly ScheduleExcelSettings _settings;
    private readonly string _projectKey;

    private readonly ObservableCollection<ScheduleItem> _schedules = new();
    private readonly ObservableCollection<ChangeItem> _changes = new();
    private readonly Dictionary<(long, bool), SheetGrid> _previewCache = new();
    private ICollectionView? _schedulesView;
    private bool _fileNameAuto = true;
    private bool _settingFileName;
    private string? _importPath;
    private IReadOnlyList<SheetGrid> _importGrids = Array.Empty<SheetGrid>();
    private Dictionary<(string Sheet, int Row, int Col), List<ChangeItem>> _changesByCell = new();

    public ScheduleExcelWindow(UIDocument uidoc, IScheduleExcelService service)
    {
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _doc = uidoc.Document;
        _service = service ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        _settings = ScheduleExcelSettings.Load();
        _projectKey = ScheduleExcelSettings.ProjectKey(_doc);

        FolderBox.Text = _settings.GetFolder(_projectKey) ?? "";
        FolderHint.Text = "Se recuerda para este proyecto (" + Path.GetFileName(_projectKey) + "). " +
                          "Las exportaciones van aquí directamente y las importaciones empiezan aquí.";
        PerElementCheck.IsChecked = _settings.Editable;
        DatePrefixCheck.IsChecked = _settings.DatePrefix;
        AskPathCheck.IsChecked = _settings.AskPath;
        OpenAfterCheck.IsChecked = _settings.OpenAfter;
        FileNameBox.TextChanged += OnFileNameTyped;

        ChangesGrid.ItemsSource = _changes;
        ImportPreview.SelectionToggled += (_, _) => RefreshApply();
        ImportPreview.ShowMessage("Elige un Excel para ver aquí la tabla con los cambios marcados en color.");

        LoadSchedules();
        Closing += (_, _) => SaveSettings();
        Loaded += (_, _) => ShowExportPreview();
    }

    private bool PerElement => PerElementCheck.IsChecked == true;

    // ====================================================================
    // Progreso (operaciones en el hilo de Revit)
    // ====================================================================

    private readonly Stopwatch _progressClock = new();
    private long _lastPump;

    private Action<double, string> BeginProgress(string text)
    {
        Tabs.IsEnabled = false;
        FolderPanel.IsEnabled = false;
        ProgressPanel.Visibility = System.Windows.Visibility.Visible;
        Mouse.OverrideCursor = Cursors.Wait;
        _progressClock.Restart();
        _lastPump = -1000;
        Report(0, text);
        return Report;
    }

    private void Report(double fraction, string text)
    {
        long now = _progressClock.ElapsedMilliseconds;
        if (now - _lastPump < 60 && fraction < 1) return;
        _lastPump = now;
        double pct = Math.Max(0, Math.Min(100, fraction * 100));
        ProgressBar.Value = pct;
        ProgressText.Text = text + "… " + pct.ToString("0", CultureInfo.CurrentCulture) + " %";
        // Deja que WPF repinte la barra mientras seguimos en el hilo de Revit
        Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
    }

    private void EndProgress()
    {
        Mouse.OverrideCursor = null;
        ProgressPanel.Visibility = System.Windows.Visibility.Collapsed;
        Tabs.IsEnabled = true;
        FolderPanel.IsEnabled = true;
    }

    // ====================================================================
    // Carpeta del proyecto
    // ====================================================================

    private string? ProjectFolder
    {
        get
        {
            var f = FolderBox.Text.Trim();
            return f.Length > 0 && Directory.Exists(f) ? f : null;
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Carpeta de exportación del proyecto",
            InitialDirectory = ProjectFolder ?? DefaultFolder(),
        };
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        OnFolderEdited(sender, e);
    }

    private void OnFolderEdited(object sender, RoutedEventArgs e)
    {
        var f = FolderBox.Text.Trim();
        if (f.Length > 0 && !Directory.Exists(f))
        {
            Status("La carpeta «" + f + "» no existe.");
            return;
        }
        _settings.SetFolder(_projectKey, f);
        _settings.Save();
        if (f.Length > 0) Status("Carpeta del proyecto guardada.");
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var f = ProjectFolder;
        if (f is null) { Status("Elige primero una carpeta existente."); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + f + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { Status("No se pudo abrir la carpeta: " + ex.Message); }
    }

    private static string DefaultFolder()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    // ====================================================================
    // Exportar: listado de tablas + vista previa
    // ====================================================================

    private void LoadSchedules()
    {
        var activeId = (_doc.ActiveView as ViewSchedule)?.Id;
        var selectedIds = new HashSet<ElementId>(_uidoc.Selection.GetElementIds()
            .Select(id => _doc.GetElement(id))
            .OfType<ScheduleSheetInstance>()
            .Select(s => s.ScheduleId));

        foreach (var info in _service.GetSchedules(_doc))
        {
            bool active = activeId is not null && info.Id == activeId;
            _schedules.Add(new ScheduleItem
            {
                Id = info.Id,
                Name = active ? info.Name : info.Name,
                Category = info.Category,
                IsActive = active,
                IsChecked = active || selectedIds.Contains(info.Id),
            });
        }
        _schedulesView = CollectionViewSource.GetDefaultView(_schedules);
        _schedulesView.Filter = o => o is ScheduleItem s && Matches(s, SearchBox.Text);
        SchedulesGrid.ItemsSource = _schedulesView;

        var first = _schedules.FirstOrDefault(s => s.IsActive) ?? _schedules.FirstOrDefault(s => s.IsChecked);
        if (first is not null)
        {
            SchedulesGrid.SelectedItem = first;
            SchedulesGrid.ScrollIntoView(first);
        }
        Status(_schedules.Count == 0
            ? "El modelo no tiene tablas de planificación."
            : _schedules.Count + " tablas en el modelo. Haz clic en una para verla.");
        RefreshSelection();
    }

    private static bool Matches(ScheduleItem s, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t =>
            s.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
            s.Category.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _schedulesView?.Refresh();

    private void OnCheckVisibleClick(object sender, RoutedEventArgs e)
    {
        if (_schedulesView is null) return;
        foreach (ScheduleItem s in _schedulesView) s.IsChecked = true;
        RefreshSelection();
    }

    private void OnUncheckAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var s in _schedules) s.IsChecked = false;
        RefreshSelection();
    }

    private void OnScheduleChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ScheduleItem clicked })
        {
            // Con varias filas seleccionadas, la casilla se aplica a todas
            if (SchedulesGrid.SelectedItems.Count > 1 && SchedulesGrid.SelectedItems.Contains(clicked))
                foreach (var s in SchedulesGrid.SelectedItems.OfType<ScheduleItem>()) s.IsChecked = clicked.IsChecked;
            else if (clicked.IsChecked)
                SchedulesGrid.SelectedItem = clicked; // y se previsualiza
        }
        RefreshSelection();
    }

    private void OnScheduleDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SchedulesGrid.SelectedItem is not ScheduleItem s) return;
        s.IsChecked = !s.IsChecked;
        RefreshSelection();
    }

    private void OnScheduleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ShowExportPreview();
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ShowExportPreview();
        RefreshSelection();
    }

    /// <summary>Vista previa de la tabla seleccionada en el listado, tal y como saldrá en el Excel.</summary>
    private void ShowExportPreview()
    {
        var item = SchedulesGrid.SelectedItem as ScheduleItem;
        if (item is null)
        {
            ExportPreviewTitle.Text = "Vista previa";
            ExportPreview.ShowMessage("Haz clic en una tabla del listado para verla aquí.");
            return;
        }

        ExportPreviewTitle.Text = "Vista previa — " + item.Name + (PerElement ? "  (una fila por elemento)" : "");
        var key = (item.Id.Value, PerElement);
        if (!_previewCache.TryGetValue(key, out var grid))
        {
            if (_doc.GetElement(item.Id) is not ViewSchedule vs)
            {
                ExportPreview.ShowMessage("La tabla ya no existe en el modelo.");
                return;
            }
            Mouse.OverrideCursor = Cursors.Wait;
            try { grid = _service.BuildPreview(vs, PerElement, ExportPreviewRows); }
            catch (Exception ex)
            {
                ExportPreview.ShowMessage("No se pudo leer la tabla: " + ex.Message);
                return;
            }
            finally { Mouse.OverrideCursor = null; }
            _previewCache[key] = grid;
        }
        ExportPreview.Show(grid);
    }

    private void OnFileOptionChanged(object sender, RoutedEventArgs e) => RefreshFileHint();

    private void OnFileNameTyped(object sender, TextChangedEventArgs e)
    {
        if (_settingFileName) return;
        _fileNameAuto = string.IsNullOrWhiteSpace(FileNameBox.Text);
        RefreshFileHint();
    }

    private List<ScheduleItem> Checked => _schedules.Where(s => s.IsChecked).ToList();

    private void RefreshSelection()
    {
        var sel = Checked;
        ExportButton.IsEnabled = sel.Count > 0;
        ExportButton.Content = sel.Count switch
        {
            0 => "Exportar",
            1 => "Exportar tabla",
            _ => "Exportar " + sel.Count + " tablas (un archivo)",
        };
        SelectionInfo.Text = sel.Count switch
        {
            0 => "Marca una o varias tablas. Varias van al mismo Excel, una pestaña por tabla.",
            1 => "1 tabla marcada.",
            _ => sel.Count + " tablas marcadas: irán al mismo Excel, una pestaña por tabla.",
        };

        if (_fileNameAuto)
        {
            _settingFileName = true;
            FileNameBox.Text = sel.Count switch
            {
                0 => "",
                1 => sel[0].Name,
                _ => Path.GetFileNameWithoutExtension(_doc.Title ?? "Proyecto") + "_Tablas",
            };
            _settingFileName = false;
        }
        RefreshFileHint();
    }

    private void RefreshFileHint()
    {
        var name = FinalFileName();
        FileNameHint.Text = name is null ? ".xlsx" : "Se guardará como: " + name;
    }

    private string? FinalFileName()
    {
        var baseName = SafeFileName(FileNameBox.Text);
        if (baseName.Length == 0) return null;
        if (baseName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) baseName = baseName[..^5];
        if (DatePrefixCheck.IsChecked == true) baseName = DateTime.Now.ToString("yyMMdd", CultureInfo.InvariantCulture) + "_" + baseName;
        return baseName + ".xlsx";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((name ?? "").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }

    // ====================================================================
    // Exportar
    // ====================================================================

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var sel = Checked;
        if (sel.Count == 0) return;
        var fileName = FinalFileName() ?? "Tablas.xlsx";

        string path;
        var folder = ProjectFolder;
        if (AskPathCheck.IsChecked == true || folder is null)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar a Excel",
                Filter = "Libro de Excel (*.xlsx)|*.xlsx",
                FileName = fileName,
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = folder ?? DefaultFolder(),
            };
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FileName;

            // Primera exportación del proyecto: su carpeta pasa a ser la habitual
            if (folder is null)
            {
                FolderBox.Text = Path.GetDirectoryName(path) ?? "";
                OnFolderEdited(sender, e);
            }
        }
        else
        {
            path = Path.Combine(folder, fileName);
            if (File.Exists(path) &&
                MessageBox.Show(this, "Ya existe «" + fileName + "» en la carpeta del proyecto.\n\n¿Reemplazarlo?",
                    "TableLink", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        var schedules = sel.Select(s => _doc.GetElement(s.Id)).OfType<ViewSchedule>().ToList();
        var report = BeginProgress("Exportando");
        ScheduleExcelExportResult result;
        try { result = _service.ExportMany(_doc, schedules, path, PerElement, report); }
        finally { EndProgress(); }

        if (!result.Success)
        {
            Status("No se pudo exportar.");
            MessageBox.Show(this, result.ErrorMessage, "TableLink", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var msg = "Exportado «" + Path.GetFileName(path) + "»: " +
                  (result.SheetCount == 1 ? "1 tabla" : result.SheetCount + " tablas (una pestaña por tabla)") +
                  ", " + result.RowCount + " filas.";
        if (result.SkippedFields.Count > 0)
            msg += " Columnas calculadas omitidas: " +
                   string.Join(", ", result.SkippedFields.Take(5)) + (result.SkippedFields.Count > 5 ? "…" : "") + ".";
        Status(msg);
        SaveSettings();

        if (OpenAfterCheck.IsChecked == true)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* sin Excel asociado */ }
        }
    }

    // ====================================================================
    // Importar
    // ====================================================================

    private void OnBrowseImportClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar datos desde Excel",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx",
            InitialDirectory = ProjectFolder ?? _settings.LastImportFolder ?? DefaultFolder(),
        };
        if (dlg.ShowDialog(this) != true) return;
        _importPath = dlg.FileName;
        ImportFileBox.Text = _importPath;
        _settings.LastImportFolder = Path.GetDirectoryName(_importPath);
        LoadPreview();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => LoadPreview();

    private void LoadPreview()
    {
        _changes.Clear();
        _changesByCell = new();
        _importGrids = Array.Empty<SheetGrid>();
        SheetCombo.ItemsSource = null;
        ApplyButton.IsEnabled = false;
        ReloadButton.IsEnabled = _importPath is not null;
        if (_importPath is null) return;

        var report = BeginProgress("Leyendo el Excel");
        ScheduleImportPreview preview;
        try
        {
            preview = _service.PreviewImport(_doc, _importPath, report);
            if (preview.ErrorMessage is null)
            {
                report(1, "Preparando la vista previa");
                _importGrids = _service.ReadWorkbookGrids(_importPath, ImportPreviewRows);
            }
        }
        finally { EndProgress(); }

        if (preview.ErrorMessage is not null)
        {
            ImportInfo.Text = preview.ErrorMessage;
            ImportPreview.ShowMessage("No se puede importar este archivo.");
            Status("No se puede importar este archivo (motivo arriba).");
            return;
        }

        foreach (var c in preview.Changes)
        {
            var item = new ChangeItem(c);
            item.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(ChangeItem.IsSelected)) RefreshApply(); };
            _changes.Add(item);
            foreach (var (row, col) in c.ExcelCells)
            {
                var key = (c.SheetName, row, col);
                if (!_changesByCell.TryGetValue(key, out var list)) _changesByCell[key] = list = new List<ChangeItem>();
                list.Add(item);
            }
        }

        var info = new List<string>
        {
            (preview.ScheduleNames.Count == 1 ? "Tabla «" + preview.ScheduleNames[0] + "»" : preview.ScheduleNames.Count + " tablas")
            + " · " + preview.RowsRead + " filas leídas"
            + (preview.ExportedAt is null ? "" : " · exportado " + preview.ExportedAt),
        };
        if (!preview.DocumentMatches)
            info.Add("⚠ El Excel se exportó desde otro modelo: los elementos solo coinciden si es el mismo proyecto.");
        info.AddRange(preview.Warnings.Take(4).Select(w => "⚠ " + w));
        if (preview.Warnings.Count > 4) info.Add("… y " + (preview.Warnings.Count - 4) + " avisos más.");
        ImportInfo.Text = string.Join("\n", info);

        // Pestañas con cambios primero
        var grids = _importGrids
            .OrderByDescending(g => _changesByCell.Keys.Any(k => k.Sheet == g.SheetName))
            .ToList();
        SheetCombo.ItemsSource = grids;
        bool many = grids.Count > 1;
        SheetCombo.Visibility = SheetLabel.Visibility = many ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (grids.Count > 0) SheetCombo.SelectedIndex = 0;
        else ImportPreview.ShowMessage("No se pudo dibujar la vista previa; usa «Lista de cambios».");

        int ready = preview.Changes.Count(c => c.Status == ChangeStatus.Ready);
        int bad = preview.Changes.Count - ready;
        Status(preview.Changes.Count == 0
            ? "No hay cambios: el Excel coincide con el modelo."
            : ready + " cambios listos para aplicar (en salmón)" + (bad > 0 ? ", " + bad + " con errores (en rojo, no se aplicarán)." : "."));
        RefreshApply();
    }

    private void OnSheetChanged(object sender, SelectionChangedEventArgs e) => ShowImportGrid();

    private void ShowImportGrid()
    {
        if (SheetCombo.SelectedItem is not SheetGrid grid) return;
        ImportPreview.Show(grid, (row, col) =>
            _changesByCell.TryGetValue((grid.SheetName, row, col), out var list) ? list : Array.Empty<ChangeItem>(),
            rowNumbers: true);
    }

    private void OnImportViewChanged(object sender, RoutedEventArgs e)
    {
        bool table = TableViewRadio.IsChecked == true;
        ImportPreview.Visibility = table ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ChangesGrid.Visibility = table ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    private void OnChangeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ChangeItem clicked } && ChangesGrid.SelectedItems.Contains(clicked))
            foreach (var c in ChangesGrid.SelectedItems.OfType<ChangeItem>().Where(x => x.CanSelect))
                c.IsSelected = clicked.IsSelected;
        RefreshApply();
    }

    private void OnSelectAllChangesClick(object sender, RoutedEventArgs e)
    {
        foreach (var c in _changes.Where(x => x.CanSelect)) c.IsSelected = true;
        RefreshApply();
    }

    private void OnSelectNoChangesClick(object sender, RoutedEventArgs e)
    {
        foreach (var c in _changes.Where(x => x.CanSelect)) c.IsSelected = false;
        RefreshApply();
    }

    private void RefreshApply()
    {
        int n = _changes.Count(c => c.IsSelected && c.CanSelect);
        ApplyButton.IsEnabled = n > 0;
        ApplyButton.Content = n == 0 ? "Aplicar cambios" : "Aplicar " + n + " cambios";
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var todo = _changes.Where(c => c.IsSelected && c.CanSelect).ToList();
        if (todo.Count == 0) return;
        int types = todo.Count(c => c.Change.IsType);
        int targets = todo.Sum(c => c.Change.TargetCount);
        var question = "Se van a aplicar " + todo.Count + " cambios" +
                       (targets != todo.Count ? " sobre " + targets + " elementos" : "") +
                       (types > 0 ? " (" + types + " son parámetros de tipo y afectan a todos los elementos de ese tipo)" : "") +
                       ".\n\nSe hace en una sola operación: un Ctrl+Z en Revit lo deshace todo.\n\n¿Continuar?";
        if (MessageBox.Show(this, question, "TableLink", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var report = BeginProgress("Aplicando cambios");
        ScheduleImportResult result;
        try { result = _service.ApplyImport(_doc, _changes.Select(c => c.Change).ToList(), report); }
        finally { EndProgress(); }

        foreach (var c in _changes) c.Refresh();
        RefreshApply();

        if (!result.Success)
        {
            Status("No se aplicaron los cambios.");
            MessageBox.Show(this, result.ErrorMessage, "TableLink", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Status(result.Applied + " cambios aplicados al modelo (en verde)" +
               (result.Failed > 0 ? ", " + result.Failed + " fallidos (en rojo, pasa el ratón para ver el motivo)." : ".") +
               " Ctrl+Z en Revit para deshacer.");
    }

    // ====================================================================

    private void Status(string text) => StatusText.Text = text;

    private void SaveSettings()
    {
        _settings.Editable = PerElement;
        _settings.DatePrefix = DatePrefixCheck.IsChecked == true;
        _settings.AskPath = AskPathCheck.IsChecked == true;
        _settings.OpenAfter = OpenAfterCheck.IsChecked == true;
        _settings.Save();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ====================================================================
    // Filas
    // ====================================================================

    public sealed class ScheduleItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public ElementId Id { get; init; } = ElementId.InvalidElementId;
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public bool IsActive { get; init; }

        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public sealed class ChangeItem : INotifyPropertyChanged
    {
        public ChangeItem(ScheduleCellChange change) => Change = change;

        public ScheduleCellChange Change { get; }
        public string ScheduleName => Change.ScheduleName;
        public string ElementLabel => Change.ElementLabel;
        public string ColumnHeading => Change.ColumnHeading;
        public string OldText => Change.OldText;
        public string NewText => Change.NewText;
        public string? Message => Change.Message;
        public string StatusCode => Change.Status.ToString();
        public bool CanSelect => Change.Status == ChangeStatus.Ready;

        public string StatusText => Change.Status switch
        {
            ChangeStatus.Ready => (Change.IsType ? "Listo (tipo)" : "Listo") + (Message is null ? "" : " · " + Message),
            ChangeStatus.Applied => "Aplicado" + (Message is null ? "" : " · " + Message),
            ChangeStatus.Conflict => "Conflicto: " + Change.Message,
            ChangeStatus.Error => "Error: " + Change.Message,
            ChangeStatus.Failed => "Falló: " + Change.Message,
            _ => Change.Status.ToString(),
        };

        public bool IsSelected
        {
            get => Change.IsSelected;
            set { if (Change.IsSelected != value) { Change.IsSelected = value; OnChanged(); } }
        }

        public void Refresh()
        {
            OnChanged(nameof(StatusCode));
            OnChanged(nameof(StatusText));
            OnChanged(nameof(CanSelect));
            OnChanged(nameof(IsSelected));
            OnChanged(nameof(Message));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
