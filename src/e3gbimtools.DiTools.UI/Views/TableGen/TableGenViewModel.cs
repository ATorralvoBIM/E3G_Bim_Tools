using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.UI.Views.TableGen;

/// <summary>
/// ViewModel de TableGenWindow (Sprint 8 P1 + Sprint 9 P2).
/// Gestiona los tres tabs: Excel→Vista, Lote+ColocarEnHoja, Schedule→Vista.
/// </summary>
internal sealed class TableGenViewModel : INotifyPropertyChanged
{
    private readonly UIDocument       _uidoc;
    private readonly ITableGenService _service;

    // ══════════════════════════════════════════════════════════════════════
    //  SHARED / GLOBAL
    // ══════════════════════════════════════════════════════════════════════

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    private string _globalStatus = "";
    public string GlobalStatus
    {
        get => _globalStatus;
        set { _globalStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasGlobalStatus)); }
    }
    public bool HasGlobalStatus => !string.IsNullOrEmpty(_globalStatus);

    // ══════════════════════════════════════════════════════════════════════
    //  TAB 1 — Excel → Vista
    // ══════════════════════════════════════════════════════════════════════

    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); LoadSheets(); }
    }

    private ObservableCollection<string> _sheetNames = new();
    public ObservableCollection<string> SheetNames
    {
        get => _sheetNames;
        private set { _sheetNames = value; OnPropertyChanged(); }
    }

    private string? _selectedSheet;
    public string? SelectedSheet
    {
        get => _selectedSheet;
        set { _selectedSheet = value; OnPropertyChanged(); if (value != null) LoadDefaultRange(); }
    }

    private string _rangeAddress = "";
    public string RangeAddress
    {
        get => _rangeAddress;
        set { _rangeAddress = value; OnPropertyChanged(); }
    }

    private string _viewName = "";
    public string ViewName
    {
        get => _viewName;
        set { _viewName = value; OnPropertyChanged(); }
    }

    private bool _overwriteExisting;
    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set { _overwriteExisting = value; OnPropertyChanged(); }
    }

    private bool _placeAfterGenerate;
    public bool PlaceAfterGenerate
    {
        get => _placeAfterGenerate;
        set { _placeAfterGenerate = value; OnPropertyChanged(); if (value) EnsureDrawingSheets(); }
    }

    private SheetPickerInfo? _selectedSheet1;
    public SheetPickerInfo? SelectedSheet1
    {
        get => _selectedSheet1;
        set { _selectedSheet1 = value; OnPropertyChanged(); }
    }

    private string _placeXOffsetMm = "0";
    public string PlaceXOffsetMm
    {
        get => _placeXOffsetMm;
        set { _placeXOffsetMm = value; OnPropertyChanged(); }
    }

    private string _placeYOffsetMm = "0";
    public string PlaceYOffsetMm
    {
        get => _placeYOffsetMm;
        set { _placeYOffsetMm = value; OnPropertyChanged(); }
    }

    private ObservableCollection<PreviewRow> _previewRows = new();
    public ObservableCollection<PreviewRow> PreviewRows
    {
        get => _previewRows;
        private set { _previewRows = value; OnPropertyChanged(); }
    }

    private string _tab1Status = "Selecciona un archivo .xlsx para comenzar.";
    public string Tab1Status
    {
        get => _tab1Status;
        set { _tab1Status = value; OnPropertyChanged(); }
    }

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        set { _isGenerating = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _canGenerate;
    public bool CanGenerateView
    {
        get => _canGenerate;
        set { _canGenerate = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public ICommand BrowseFileCommand   { get; }
    public ICommand LoadPreviewCommand  { get; }
    public ICommand GenerateCommand     { get; }

    // ══════════════════════════════════════════════════════════════════════
    //  TAB 2 — Lote + Colocar en hoja
    // ══════════════════════════════════════════════════════════════════════

    private string _batchFilePath = "";
    public string BatchFilePath
    {
        get => _batchFilePath;
        set { _batchFilePath = value; OnPropertyChanged(); }
    }

    private string _batchNamingPattern = "";
    public string BatchNamingPattern
    {
        get => _batchNamingPattern;
        set { _batchNamingPattern = value; OnPropertyChanged(); }
    }

    private bool _batchOverwrite;
    public bool BatchOverwrite
    {
        get => _batchOverwrite;
        set { _batchOverwrite = value; OnPropertyChanged(); }
    }

    private bool _isBatchRunning;
    public bool IsBatchRunning
    {
        get => _isBatchRunning;
        set { _isBatchRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _tab2Status = "Selecciona un .xlsx y pulsa 'Generar lote'.";
    public string Tab2Status
    {
        get => _tab2Status;
        set { _tab2Status = value; OnPropertyChanged(); }
    }

    // Picker "Colocar en hoja"
    private ObservableCollection<SheetPickerInfo> _drawingSheets = new();
    public ObservableCollection<SheetPickerInfo> DrawingSheets
    {
        get => _drawingSheets;
        private set { _drawingSheets = value; OnPropertyChanged(); }
    }

    private ObservableCollection<DraftingViewInfo> _draftingViews = new();
    public ObservableCollection<DraftingViewInfo> DraftingViews
    {
        get => _draftingViews;
        private set { _draftingViews = value; OnPropertyChanged(); }
    }

    private DraftingViewInfo? _selectedDraftingView;
    public DraftingViewInfo? SelectedDraftingView
    {
        get => _selectedDraftingView;
        set { _selectedDraftingView = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private SheetPickerInfo? _selectedSheet2;
    public SheetPickerInfo? SelectedSheet2
    {
        get => _selectedSheet2;
        set { _selectedSheet2 = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _placeXMm = "0";
    public string PlaceXMm
    {
        get => _placeXMm;
        set { _placeXMm = value; OnPropertyChanged(); }
    }

    private string _placeYMm = "0";
    public string PlaceYMm
    {
        get => _placeYMm;
        set { _placeYMm = value; OnPropertyChanged(); }
    }

    private ObservableCollection<BatchResultRow> _batchResults = new();
    public ObservableCollection<BatchResultRow> BatchResults
    {
        get => _batchResults;
        private set { _batchResults = value; OnPropertyChanged(); }
    }

    public ICommand BatchBrowseCommand    { get; }
    public ICommand GenerateBatchCommand  { get; }
    public ICommand PlaceOnSheetCommand   { get; }
    public ICommand ReloadViewsCommand    { get; }

    // ══════════════════════════════════════════════════════════════════════
    //  TAB 3 — Schedule → Vista
    // ══════════════════════════════════════════════════════════════════════

    private ObservableCollection<ScheduleInfo> _schedules = new();
    public ObservableCollection<ScheduleInfo> Schedules
    {
        get => _schedules;
        private set { _schedules = value; OnPropertyChanged(); }
    }

    private ScheduleInfo? _selectedSchedule;
    public ScheduleInfo? SelectedSchedule
    {
        get => _selectedSchedule;
        set
        {
            _selectedSchedule = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasScheduleSelected));
            OnPropertyChanged(nameof(ScheduleInfoText));
            CommandManager.InvalidateRequerySuggested();
            if (value != null && string.IsNullOrWhiteSpace(ScheduleViewName))
                ScheduleViewName = $"TableGen_{value.Name}";
        }
    }

    public bool HasScheduleSelected => _selectedSchedule != null;

    public string ScheduleInfoText => _selectedSchedule is null ? ""
        : $"Categoría: {_selectedSchedule.Category}  ·  " +
          $"Filas de datos: {_selectedSchedule.RowCount}";

    private string _scheduleViewName = "";
    public string ScheduleViewName
    {
        get => _scheduleViewName;
        set { _scheduleViewName = value; OnPropertyChanged(); }
    }

    private string _schedColMultiplier = "1.0";
    public string SchedColMultiplier
    {
        get => _schedColMultiplier;
        set { _schedColMultiplier = value; OnPropertyChanged(); }
    }

    private string _schedHeaderHeightMm = "6";
    public string SchedHeaderHeightMm
    {
        get => _schedHeaderHeightMm;
        set { _schedHeaderHeightMm = value; OnPropertyChanged(); }
    }

    private string _schedBodyHeightMm = "5";
    public string SchedBodyHeightMm
    {
        get => _schedBodyHeightMm;
        set { _schedBodyHeightMm = value; OnPropertyChanged(); }
    }

    private bool _scheduleOverwrite;
    public bool ScheduleOverwrite
    {
        get => _scheduleOverwrite;
        set { _scheduleOverwrite = value; OnPropertyChanged(); }
    }

    private bool _isScheduleRunning;
    public bool IsScheduleRunning
    {
        get => _isScheduleRunning;
        set { _isScheduleRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _tab3Status = "Selecciona un schedule y pulsa 'Generar desde schedule'.";
    public string Tab3Status
    {
        get => _tab3Status;
        set { _tab3Status = value; OnPropertyChanged(); }
    }

    private string _scheduleResultText = "";
    public string ScheduleResultText
    {
        get => _scheduleResultText;
        set { _scheduleResultText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasScheduleResult)); }
    }
    public bool HasScheduleResult => !string.IsNullOrEmpty(_scheduleResultText);

    public ICommand ReloadSchedulesCommand  { get; }
    public ICommand ExportScheduleCommand   { get; }

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════

    public TableGenViewModel(UIDocument uidoc, ITableGenService service)
    {
        _uidoc   = uidoc   ?? throw new ArgumentNullException(nameof(uidoc));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        // Tab 1
        BrowseFileCommand   = new RelayCommand(_ => OnBrowseFile());
        LoadPreviewCommand  = new RelayCommand(_ => OnLoadPreview(),
                                              _ => !string.IsNullOrWhiteSpace(SelectedSheet));
        GenerateCommand     = new RelayCommand(_ => OnGenerate(),
                                              _ => CanGenerateView && !IsGenerating);

        // Tab 2
        BatchBrowseCommand   = new RelayCommand(_ => OnBatchBrowse());
        GenerateBatchCommand = new RelayCommand(_ => OnGenerateBatch(),
                                               _ => !string.IsNullOrWhiteSpace(BatchFilePath)
                                                    && !IsBatchRunning);
        PlaceOnSheetCommand  = new RelayCommand(_ => OnPlaceOnSheet(),
                                               _ => SelectedDraftingView != null
                                                    && SelectedSheet2 != null);
        ReloadViewsCommand   = new RelayCommand(_ => LoadDraftingViewsAndSheets());

        // Tab 3
        ReloadSchedulesCommand = new RelayCommand(_ => LoadSchedules());
        ExportScheduleCommand  = new RelayCommand(_ => OnExportSchedule(),
                                                 _ => SelectedSchedule != null
                                                      && !IsScheduleRunning);

        // Carga inicial
        LoadSchedules();
        LoadDraftingViewsAndSheets();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Logica Tab 1
    // ══════════════════════════════════════════════════════════════════════

    private void OnBrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Seleccionar archivo Excel",
            Filter = "Archivos Excel (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() == true) FilePath = dlg.FileName;
    }

    private void LoadSheets()
    {
        SheetNames.Clear();
        SelectedSheet = null; RangeAddress = ""; PreviewRows.Clear();
        CanGenerateView = false;
        if (string.IsNullOrWhiteSpace(FilePath)) return;

        try
        {
            var names = _service.GetSheetNames(FilePath);
            foreach (var n in names) SheetNames.Add(n);
            if (SheetNames.Count > 0) SelectedSheet = SheetNames[0];
            Tab1Status = $"{SheetNames.Count} hoja(s) encontrada(s).";
        }
        catch (Exception ex) { Tab1Status = $"Error al leer el archivo: {ex.Message}"; }
    }

    private void LoadDefaultRange()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || SelectedSheet is null) return;
        try
        {
            RangeAddress = _service.GetUsedRangeAddress(FilePath, SelectedSheet);
            ViewName     = $"TableGen_{System.IO.Path.GetFileNameWithoutExtension(FilePath)}_{SelectedSheet}";
        }
        catch { /* no critico */ }
    }

    private void OnLoadPreview()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || SelectedSheet is null) return;
        try
        {
            Tab1Status = "Cargando preview...";
            var req  = BuildTab1Request();
            var data = _service.ReadPreview(req);
            PreviewRows.Clear();
            foreach (var cell in data.Cells.Take(200))
            {
                PreviewRows.Add(new PreviewRow
                {
                    RowCol = $"({cell.Row},{cell.Column})",
                    Value  = cell.Value ?? "",
                    Size   = $"{cell.FontSizePt:F0}pt",
                    BG     = cell.HasBackground ? $"#{cell.BackgroundColor:X6}" : "",
                    Merged = cell.MergedArea is not null
                             ? $"{cell.MergedArea.RowSpan}×{cell.MergedArea.ColumnSpan}" : "",
                });
            }
            CanGenerateView = true;
            Tab1Status = $"Preview: {data.RowCount} filas × {data.ColumnCount} columnas.";
        }
        catch (Exception ex) { Tab1Status = $"Error en preview: {ex.Message}"; CanGenerateView = false; }
    }

    private void OnGenerate()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || SelectedSheet is null) return;
        IsGenerating = true;
        Tab1Status   = "Generando Drafting View...";

        try
        {
            var req    = BuildTab1Request();
            var result = _service.Generate(_uidoc.Document, req);

            if (result.Success)
            {
                Tab1Status = $"Vista '{result.ViewName}' creada en {result.ElapsedMs} ms.";
                if (result.Warnings.Count > 0)
                    Tab1Status += $" ({result.Warnings.Count} advertencia(s)).";

                // Colocar en hoja si está marcado.
                if (PlaceAfterGenerate && SelectedSheet1 != null
                    && result.DraftingViewId != null)
                {
                    var placeReq = new PlaceOnSheetRequest
                    {
                        DraftingViewId = result.DraftingViewId,
                        SheetId        = SelectedSheet1.Id,
                        XOffsetMm      = ParseDouble(PlaceXOffsetMm, 0),
                        YOffsetMm      = ParseDouble(PlaceYOffsetMm, 0),
                    };
                    var placeResult = _service.PlaceOnSheet(_uidoc.Document, placeReq);
                    Tab1Status += placeResult.Success
                        ? $" Colocada en hoja '{SelectedSheet1.DisplayName}'."
                        : $" Error al colocar: {placeResult.ErrorMessage}";
                }

                // Actualizar lista de vistas en Tab 2.
                LoadDraftingViewsAndSheets();
                GlobalStatus = "";
            }
            else
            {
                Tab1Status  = $"Error al generar: {result.ErrorMessage}";
                GlobalStatus = result.ErrorMessage ?? "";
            }
        }
        catch (Exception ex) { Tab1Status = $"Error inesperado: {ex.Message}"; }
        finally { IsGenerating = false; }
    }

    private TableGenRequest BuildTab1Request() => new()
    {
        FilePath         = FilePath,
        SheetName        = SelectedSheet!,
        RangeAddress     = string.IsNullOrWhiteSpace(RangeAddress) ? null : RangeAddress,
        ViewName         = string.IsNullOrWhiteSpace(ViewName)     ? null : ViewName,
        OverwriteExisting = OverwriteExisting,
    };

    // ══════════════════════════════════════════════════════════════════════
    //  Logica Tab 2 — Lote
    // ══════════════════════════════════════════════════════════════════════

    private void OnBatchBrowse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Seleccionar archivo Excel para lote",
            Filter = "Archivos Excel (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() == true) BatchFilePath = dlg.FileName;
    }

    private void OnGenerateBatch()
    {
        if (string.IsNullOrWhiteSpace(BatchFilePath)) return;

        IsBatchRunning = true;
        Tab2Status     = "Generando lote...";
        BatchResults.Clear();

        try
        {
            var req = new BatchTableGenRequest
            {
                FilePath        = BatchFilePath,
                NamingPattern   = string.IsNullOrWhiteSpace(BatchNamingPattern)
                                  ? null : BatchNamingPattern,
                OverwriteExisting = BatchOverwrite,
            };

            var result = _service.GenerateBatch(_uidoc.Document, req);

            // Poblar la tabla de resultados.
            var sheetNames = _service.GetSheetNames(BatchFilePath);
            for (int i = 0; i < result.Results.Count; i++)
            {
                var r = result.Results[i];
                BatchResults.Add(new BatchResultRow
                {
                    SheetName = i < sheetNames.Count ? sheetNames[i] : $"Hoja {i + 1}",
                    ViewName  = r.ViewName ?? "(sin nombre)",
                    Status    = r.Success ? "OK" : $"Error: {r.ErrorMessage}",
                    ElapsedMs = r.ElapsedMs,
                });
            }

            Tab2Status = $"Lote completado: {result.SuccessCount} OK, " +
                         $"{result.FailCount} errores — {result.TotalElapsedMs} ms.";

            LoadDraftingViewsAndSheets();
        }
        catch (Exception ex) { Tab2Status = $"Error inesperado en el lote: {ex.Message}"; }
        finally { IsBatchRunning = false; }
    }

    private void OnPlaceOnSheet()
    {
        if (SelectedDraftingView is null || SelectedSheet2 is null) return;

        try
        {
            var req = new PlaceOnSheetRequest
            {
                DraftingViewId = SelectedDraftingView.Id,
                SheetId        = SelectedSheet2.Id,
                XOffsetMm      = ParseDouble(PlaceXMm, 0),
                YOffsetMm      = ParseDouble(PlaceYMm, 0),
            };

            var result = _service.PlaceOnSheet(_uidoc.Document, req);
            Tab2Status = result.Success
                ? $"Vista '{SelectedDraftingView.Name}' colocada en '{SelectedSheet2.DisplayName}'."
                : $"Error al colocar: {result.ErrorMessage}";

            if (result.Success) LoadDraftingViewsAndSheets();
        }
        catch (Exception ex) { Tab2Status = $"Error inesperado: {ex.Message}"; }
    }

    private void LoadDraftingViewsAndSheets()
    {
        try
        {
            var sheets = _service.GetDrawingSheets(_uidoc.Document);
            DrawingSheets.Clear();
            foreach (var s in sheets) DrawingSheets.Add(s);

            var views = _service.GetDraftingViews(_uidoc.Document);
            DraftingViews.Clear();
            foreach (var v in views) DraftingViews.Add(v);
        }
        catch { /* no critico, UI sigue funcionando */ }
    }

    private void EnsureDrawingSheets()
    {
        if (DrawingSheets.Count == 0) LoadDraftingViewsAndSheets();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Logica Tab 3 — Schedule → Vista
    // ══════════════════════════════════════════════════════════════════════

    private void LoadSchedules()
    {
        try
        {
            var list = _service.GetSchedules(_uidoc.Document);
            Schedules.Clear();
            foreach (var s in list) Schedules.Add(s);
            Tab3Status = Schedules.Count > 0
                ? $"{Schedules.Count} schedules encontrados. Selecciona uno."
                : "No se encontraron ViewSchedules en el documento.";
        }
        catch (Exception ex) { Tab3Status = $"Error al cargar schedules: {ex.Message}"; }
    }

    private void OnExportSchedule()
    {
        if (SelectedSchedule is null) return;

        IsScheduleRunning = true;
        Tab3Status        = $"Exportando schedule '{SelectedSchedule.Name}'...";

        try
        {
            var req = new ScheduleToViewRequest
            {
                ScheduleId           = SelectedSchedule.Id,
                ViewName             = string.IsNullOrWhiteSpace(ScheduleViewName)
                                       ? null : ScheduleViewName,
                ColumnWidthMultiplier = ParseDouble(SchedColMultiplier, 1.0),
                HeaderRowHeightMm    = ParseDouble(SchedHeaderHeightMm, 6.0),
                BodyRowHeightMm      = ParseDouble(SchedBodyHeightMm, 5.0),
                OverwriteExisting    = ScheduleOverwrite,
            };

            var result = _service.ExportScheduleToView(_uidoc.Document, req);

            if (result.Success)
            {
                Tab3Status = $"Vista '{result.ViewName}' creada: " +
                             $"{result.RowCount} filas × {result.ColumnCount} cols " +
                             $"en {result.ElapsedMs} ms.";
                ScheduleResultText = $"✓ {result.ViewName}\n" +
                                     $"Filas: {result.RowCount}  |  Columnas: {result.ColumnCount}\n" +
                                     $"Tiempo: {result.ElapsedMs} ms" +
                                     (result.Warnings.Count > 0
                                         ? $"\nAdvertencias: {result.Warnings.Count}"
                                         : "");
                LoadDraftingViewsAndSheets();
            }
            else
            {
                Tab3Status         = $"Error: {result.ErrorMessage}";
                ScheduleResultText = $"✗ Error: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            Tab3Status         = $"Error inesperado: {ex.Message}";
            ScheduleResultText = $"✗ {ex.Message}";
        }
        finally { IsScheduleRunning = false; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture, out var v)
               ? v : fallback;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INotifyPropertyChanged
    // ══════════════════════════════════════════════════════════════════════

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── Modelos de fila para DataGrids ───────────────────────────────────────

/// <summary>Fila del DataGrid de preview (Tab 1).</summary>
internal sealed class PreviewRow
{
    public string RowCol  { get; init; } = "";
    public string Value   { get; init; } = "";
    public string Size    { get; init; } = "";
    public string BG      { get; init; } = "";
    public string Merged  { get; init; } = "";
}

/// <summary>Fila del DataGrid de resultados del lote (Tab 2).</summary>
internal sealed class BatchResultRow
{
    public string SheetName { get; init; } = "";
    public string ViewName  { get; init; } = "";
    public string Status    { get; init; } = "";
    public long   ElapsedMs { get; init; }
}

/// <summary>Implementacion minima de ICommand.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?>      _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter)    => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
