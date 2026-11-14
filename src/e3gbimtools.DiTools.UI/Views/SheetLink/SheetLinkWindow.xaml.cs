using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.SheetLink;
using E3G.BimTools.DiTools.UI.Controls;
using Microsoft.Win32;

namespace E3G.BimTools.DiTools.UI.Views.SheetLink;

/// <summary>
/// Code-behind de SheetLinkWindow (Sprint 11 + Sprint 12).
///
/// Tab 1 — Exportar: lista todos los planos del documento, permite seleccion
/// multiple y exporta un Excel (.xlsx) con hoja de metadatos para trazabilidad.
///
/// Tab 2 — Importar con Diff (Sprint 12): selecciona el .xlsx editado,
/// valida el esquema (ColumnHash), calcula el diff campo a campo
/// (Before = modelo actual / After = Excel), muestra un DataGrid con
/// coloreado por estado (Modified=amber, Missing=red) y permite aplicar
/// selectivamente solo las filas elegidas en una unica Transaction (Ctrl-Z).
/// </summary>
public partial class SheetLinkWindow : Window
{
    // ------------------------------------------------------------------
    // Dependencias
    // ------------------------------------------------------------------
    private readonly Autodesk.Revit.DB.Document _doc;
    private readonly ISheetLinkService          _service;

    // ------------------------------------------------------------------
    // Estado local — Tab 1
    // ------------------------------------------------------------------
    private readonly ObservableCollection<SheetRowVm> _exportRows = new();

    // ------------------------------------------------------------------
    // Estado local — Tab 2
    // ------------------------------------------------------------------
    private readonly ObservableCollection<SheetLinkDiffRowVm> _diffRows = new();
    private IReadOnlyList<SheetLinkDiffRow>?                   _currentDiff;

    // ------------------------------------------------------------------
    // Ctor
    // ------------------------------------------------------------------
    public SheetLinkWindow(UIDocument uidoc, ISheetLinkService service)
    {
        _doc     = uidoc?.Document ?? throw new ArgumentNullException(nameof(uidoc));
        _service = service         ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        ExportGrid.ItemsSource = _exportRows;
        DiffGrid.ItemsSource   = _diffRows;

        LoadSheets();
    }

    // ====================================================================
    // Carga inicial
    // ====================================================================
    private void LoadSheets()
    {
        try
        {
            _exportRows.Clear();
            var sheets = _service.ReadSheets(_doc);
            foreach (var r in sheets)
                _exportRows.Add(new SheetRowVm(r, isSelected: true));

            UpdateExportCountLabel();
            SetStatus($"{sheets.Count} planos cargados.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error al cargar planos: {ex.Message}");
        }
    }

    // ====================================================================
    // Tab 1 — Exportar
    // ====================================================================
    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var vm in _exportRows) vm.IsSelected = true;
        UpdateExportCountLabel();
    }

    private void OnDeselectAll(object sender, RoutedEventArgs e)
    {
        foreach (var vm in _exportRows) vm.IsSelected = false;
        UpdateExportCountLabel();
    }

    private void OnExportFilterChanged(object sender, TextChangedEventArgs e)
    {
        var filter = ExportFilter.Text.Trim().ToLowerInvariant();
        var view   = CollectionViewSource.GetDefaultView(_exportRows);
        if (string.IsNullOrEmpty(filter))
            view.Filter = null;
        else
            view.Filter = o =>
            {
                if (o is not SheetRowVm vm) return false;
                return vm.Row.SheetNumber.ToLowerInvariant().Contains(filter)
                    || vm.Row.SheetName.ToLowerInvariant().Contains(filter);
            };
        UpdateExportCountLabel();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var selected = _exportRows.Where(vm => vm.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un plano para exportar.",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title      = "Exportar planos — SheetLink",
            Filter     = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName   = $"SheetLink_{DateTime.Now:yyyyMMdd}.xlsx"
        };

        if (dlg.ShowDialog() != true) return;

        var options = new SheetLinkExportOptions
        {
            OutputPath             = dlg.FileName,
            IncludeRevisionFields  = ChkRevisionFields.IsChecked == true,
            IncludeSignatoryFields = ChkSignatoryFields.IsChecked == true,
            IncludeViewsPlaced     = ChkViewsPlaced.IsChecked == true,
            FilteredElementIds     = selected.Select(vm => vm.Row.ElementId).ToList()
        };

        var result = _service.ExportToXlsx(_doc, options);

        if (result.Success)
        {
            SetStatus($"Exportados {result.SheetsExported} planos a: {result.OutputPath}");
            if (MessageBox.Show(
                    $"Excel exportado correctamente ({result.SheetsExported} planos).\n\n" +
                    $"Ruta: {result.OutputPath}\n\n" +
                    "¿Deseas abrir el archivo ahora?",
                    "SheetLink — Exportacion completada",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.OutputPath) { UseShellExecute = true }); }
                catch { /* no critico */ }
            }
        }
        else
        {
            MessageBox.Show($"Error al exportar:\n{result.ErrorMessage}",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"Error de exportacion: {result.ErrorMessage}");
        }
    }

    private void UpdateExportCountLabel()
    {
        var sel   = _exportRows.Count(vm => vm.IsSelected);
        var total = _exportRows.Count;
        ExportCountLabel.Text = $"{sel} de {total} planos seleccionados";
    }

    // ====================================================================
    // Tab 2 — Paso 1: Examinar + Validar esquema
    // ====================================================================
    private void OnBrowseImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Seleccionar Excel exportado por SheetLink",
            Filter = "Excel (*.xlsx)|*.xlsx|Todos los archivos|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        // Reset UI
        ImportFilePath.Text          = dlg.FileName;
        BtnComputeDiff.IsEnabled     = false;
        BtnApply.IsEnabled           = false;
        MetaPanel.Visibility         = Visibility.Collapsed;
        DiffGroupBox.Visibility      = Visibility.Collapsed;
        DiffOptionsBar.Visibility    = Visibility.Collapsed;
        ImportSummaryPanel.Visibility = Visibility.Collapsed;
        _diffRows.Clear();
        _currentDiff = null;

        // Validar esquema del archivo
        var validation = _service.ValidateXlsxSchema(dlg.FileName);

        if (!validation.IsValid)
        {
            MessageBox.Show(
                $"El archivo no es compatible con SheetLink:\n\n{validation.ErrorMessage}",
                "SheetLink — Error de validacion",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus($"Archivo invalido: {validation.ErrorMessage}");
            return;
        }

        // Mostrar metadatos
        MetaExportDate.Text    = FormatExportDate(validation.ExportDate);
        MetaDocumentTitle.Text = validation.DocumentTitle ?? "(desconocido)";
        MetaSheetsCount.Text   = validation.SheetsExported.ToString();
        MetaPanel.Visibility   = Visibility.Visible;

        BtnComputeDiff.IsEnabled = true;
        SetStatus($"Archivo valido. Exportado: {FormatExportDate(validation.ExportDate)}  |  {validation.SheetsExported} planos. Pulsa \"Calcular diff\" para continuar.");
    }

    // ====================================================================
    // Tab 2 — Paso 2: Calcular diff
    // ====================================================================
    private void OnComputeDiff(object sender, RoutedEventArgs e)
    {
        var path = ImportFilePath.Text;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show("Selecciona un archivo Excel (.xlsx) valido.",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            _currentDiff = _service.ComputeDiff(_doc, path);

            // Poblar coleccion de VMs
            _diffRows.Clear();
            foreach (var dr in _currentDiff)
                _diffRows.Add(new SheetLinkDiffRowVm(dr));

            // Aplicar filtro inicial (solo modificados)
            ApplyDiffFilter();

            // Mostrar controles de diff
            DiffGroupBox.Visibility   = Visibility.Visible;
            DiffOptionsBar.Visibility = Visibility.Visible;
            BtnApply.IsEnabled        = _diffRows.Any(vm => vm.IsSelected);

            UpdateDiffCountLabel();
            ImportSummaryPanel.Visibility = Visibility.Collapsed;

            int modified  = _currentDiff.Count(r => r.Status == DiffStatus.Modified);
            int unchanged = _currentDiff.Count(r => r.Status == DiffStatus.Unchanged);
            int missing   = _currentDiff.Count(r => r.Status == DiffStatus.Missing);

            SetStatus($"Diff calculado: {modified} filas con cambios, {unchanged} sin cambios, {missing} no encontradas en el modelo.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al calcular el diff:\n{ex.Message}",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"Error al calcular diff: {ex.Message}");
        }
        finally
        {
            Cursor = null;
        }
    }

    // ====================================================================
    // Tab 2 — Opciones del diff
    // ====================================================================
    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        ApplyDiffFilter();
        UpdateDiffCountLabel();
    }

    private void OnSelectModified(object sender, RoutedEventArgs e)
    {
        foreach (var vm in _diffRows)
            if (vm.CanSelect)
                vm.IsSelected = true;
        BtnApply.IsEnabled = _diffRows.Any(vm => vm.IsSelected);
        UpdateDiffCountLabel();
    }

    private void OnDeselectAllDiff(object sender, RoutedEventArgs e)
    {
        foreach (var vm in _diffRows)
            vm.IsSelected = false;
        BtnApply.IsEnabled = false;
        UpdateDiffCountLabel();
    }

    private void ApplyDiffFilter()
    {
        var view = CollectionViewSource.GetDefaultView(_diffRows);
        bool soloMod = ChkSoloModificados.IsChecked == true;

        if (soloMod)
            view.Filter = o => o is SheetLinkDiffRowVm vm && vm.StatusCode != "Unchanged";
        else
            view.Filter = null;
    }

    private void UpdateDiffCountLabel()
    {
        if (_currentDiff is null) return;

        int modified  = _currentDiff.Count(r => r.Status == DiffStatus.Modified);
        int selected  = _diffRows.Count(vm => vm.IsSelected);
        int missing   = _currentDiff.Count(r => r.Status == DiffStatus.Missing);

        DiffCountLabel.Text = $"{modified} modificados  |  {missing} ausentes  |  {selected} seleccionados para aplicar";
    }

    // ====================================================================
    // Tab 2 — Paso 3: Aplicar cambios seleccionados
    // ====================================================================
    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_currentDiff is null || _currentDiff.Count == 0) return;

        int selectedCount = _diffRows.Count(vm => vm.IsSelected);
        if (selectedCount == 0)
        {
            MessageBox.Show("No hay filas seleccionadas para aplicar.",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Se aplicaran {selectedCount} cambios al modelo Revit.\n\n" +
            "La operacion se puede deshacer con Ctrl-Z en Revit.",
            "SheetLink — Confirmar importacion",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;
            var result = _service.ApplyDiff(_doc, _currentDiff);

            // Refrescar VMs para que se actualicen los colores de resultado
            foreach (var vm in _diffRows)
                vm.RefreshApplyStatus();

            // Mostrar resumen
            ImportSummaryPanel.Visibility = Visibility.Visible;
            ImportSummaryText.Text        = result.Summary;
            ImportSummaryPanel.Background = result.Success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xF5, 0xE9))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xEB, 0xEE));

            BtnApply.IsEnabled = false;
            SetStatus(result.Summary);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error inesperado al aplicar cambios:\n{ex.Message}",
                "SheetLink", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            Cursor = null;
        }
    }

    // ====================================================================
    // Helpers
    // ====================================================================
    private void SetStatus(string msg) => StatusBar.Text = msg;

    private static string FormatExportDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "(desconocida)";
        if (DateTime.TryParse(isoDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        return isoDate;
    }
}

// ========================================================================
// ViewModel de fila para la tab de exportacion (sin cambios Sprint 12)
// ========================================================================
internal sealed class SheetRowVm : INotifyPropertyChanged
{
    public SheetLinkRow Row { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public SheetRowVm(SheetLinkRow row, bool isSelected = true)
    {
        Row         = row;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ========================================================================
// ViewModel de fila de diff para la tab de importacion (Sprint 12)
// ========================================================================
internal sealed class SheetLinkDiffRowVm : INotifyPropertyChanged
{
    public SheetLinkDiffRow Row { get; }

    // ----------------------------------------------------------------
    // Seleccion (solo habilitada para filas Modified)
    // ----------------------------------------------------------------
    public bool IsSelected
    {
        get => Row.IsSelected;
        set
        {
            if (Row.IsSelected == value) return;
            Row.IsSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True si la fila puede seleccionarse (solo filas Modified).</summary>
    public bool CanSelect => Row.Status == DiffStatus.Modified;

    // ----------------------------------------------------------------
    // Estado del diff (para DataTrigger en XAML)
    // ----------------------------------------------------------------
    public string StatusCode  => Row.Status.ToString();  // "Unchanged" | "Modified" | "Missing"
    public string StatusLabel => Row.Status switch
    {
        DiffStatus.Modified  => "Modificado",
        DiffStatus.Unchanged => "Sin cambios",
        DiffStatus.Missing   => "No encontrado",
        _                    => string.Empty
    };

    // ----------------------------------------------------------------
    // Columnas de display — "Antes → Despues" cuando hay cambio
    // ----------------------------------------------------------------
    public string NumberDisplay
    {
        get
        {
            if (Row.Status == DiffStatus.Missing)
                return $"{Row.SheetNumber_After} (ausente)";
            if (Row.SheetNumber_Before != Row.SheetNumber_After)
                return $"{Row.SheetNumber_Before}  →  {Row.SheetNumber_After}";
            return Row.SheetNumber_After;
        }
    }

    public string NameDisplay
    {
        get
        {
            if (Row.Status == DiffStatus.Missing) return string.Empty;
            if (Row.SheetName_Before != Row.SheetName_After)
            {
                var b = Truncate(Row.SheetName_Before, 28);
                var a = Truncate(Row.SheetName_After,  28);
                return $"{b}  →  {a}";
            }
            return Row.SheetName_After;
        }
    }

    public int    ChangedCount => Row.ChangedFieldCount;

    // ----------------------------------------------------------------
    // Estado tras aplicar (para DataTrigger + columna "Resultado")
    // ----------------------------------------------------------------
    public string ApplyStatusCode  => Row.ApplyStatus.ToString();  // "Pending" | "Updated" | "Skipped" | "Error"
    public string ApplyStatusLabel => Row.ApplyStatus switch
    {
        SheetLinkImportStatus.Updated => "✓ Aplicado",
        SheetLinkImportStatus.Skipped => "Omitido",
        SheetLinkImportStatus.Error   => "⚠ Error",
        _                             => string.Empty
    };

    // ----------------------------------------------------------------
    // Tooltip — detalle de todos los campos que cambian
    // ----------------------------------------------------------------
    public string ChangesTooltip
    {
        get
        {
            if (Row.Status == DiffStatus.Missing)
                return $"ElementId {Row.ElementId} no existe en el modelo actual.";
            if (!Row.HasChanges)
                return "Sin cambios: el Excel coincide con el modelo.";

            var sb = new StringBuilder();
            AppendDiff(sb, "Numero",          Row.SheetNumber_Before,         Row.SheetNumber_After);
            AppendDiff(sb, "Nombre",          Row.SheetName_Before,           Row.SheetName_After);
            AppendDiff(sb, "Fecha revision",  Row.RevisionDate_Before,        Row.RevisionDate_After);
            AppendDiff(sb, "Num. revision",   Row.RevisionNumber_Before,      Row.RevisionNumber_After);
            AppendDiff(sb, "Desc. revision",  Row.RevisionDescription_Before, Row.RevisionDescription_After);
            AppendDiff(sb, "Emitido por",     Row.IssuedBy_Before,            Row.IssuedBy_After);
            AppendDiff(sb, "Emitido para",    Row.IssuedTo_Before,            Row.IssuedTo_After);
            AppendDiff(sb, "Aprobado por",    Row.ApprovedBy_Before,          Row.ApprovedBy_After);
            AppendDiff(sb, "Disenado por",    Row.DesignedBy_Before,          Row.DesignedBy_After);
            AppendDiff(sb, "Comprobado por",  Row.CheckedBy_Before,           Row.CheckedBy_After);
            AppendDiff(sb, "Dibujado por",    Row.DrawnBy_Before,             Row.DrawnBy_After);

            if (sb.Length == 0) sb.Append("Sin cambios en los campos monitorizados.");
            return sb.ToString().TrimEnd();
        }
    }

    // ----------------------------------------------------------------
    // Ctor
    // ----------------------------------------------------------------
    public SheetLinkDiffRowVm(SheetLinkDiffRow row) => Row = row;

    // ----------------------------------------------------------------
    // Notificacion tras ApplyDiff (llamada desde el code-behind)
    // ----------------------------------------------------------------
    public void RefreshApplyStatus()
    {
        OnPropertyChanged(nameof(ApplyStatusCode));
        OnPropertyChanged(nameof(ApplyStatusLabel));
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------
    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "…";

    private static void AppendDiff(StringBuilder sb, string label, string before, string after)
    {
        if (before == after) return;
        sb.AppendLine($"{label}: '{before}'  →  '{after}'");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
