using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;

namespace E3G.BimTools.DiTools.UI.Views.ScheduleExcel;

/// <summary>
/// Rejilla de solo lectura que pinta una tabla como en Excel (anchos, colores,
/// combinadas). Virtualizada: aguanta miles de filas. Las celdas ligadas a
/// cambios de importacion se colorean por estado y se pueden marcar/desmarcar
/// con un clic.
/// </summary>
public partial class SheetGridView : UserControl
{
    private const double PxPerChar = 7.0;

    public SheetGridView()
    {
        InitializeComponent();
        ShowMessage("");
    }

    /// <summary>Se dispara al marcar/desmarcar un cambio con clic.</summary>
    public event EventHandler? SelectionToggled;

    public void ShowMessage(string text)
    {
        RowsHost.ItemsSource = null;
        EmptyHint.Text = text;
        EmptyHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Pinta la rejilla. <paramref name="changesAt"/> da los cambios de cada celda (fila Excel, columna).</summary>
    public IReadOnlyList<GridCellVm> Show(SheetGrid grid, Func<int, int, IReadOnlyList<ScheduleExcelWindow.ChangeItem>>? changesAt = null,
        bool rowNumbers = false)
    {
        var widths = grid.ColumnWidths.Select(w => Math.Max(40, w * PxPerChar)).ToList();
        double Width(int col, int span)
        {
            double sum = 0;
            for (int c = col; c < col + span; c++) sum += c - 1 < widths.Count ? widths[c - 1] : 80;
            return sum;
        }

        var all = new List<GridCellVm>();
        var rows = new List<GridRowVm>();
        foreach (var r in grid.Rows)
        {
            var vm = new GridRowVm();
            if (rowNumbers)
                vm.Cells.Add(GridCellVm.RowNumber(r.ExcelRow));
            foreach (var c in r.Cells)
            {
                var items = changesAt?.Invoke(r.ExcelRow, c.Column) ?? Array.Empty<ScheduleExcelWindow.ChangeItem>();
                var cell = new GridCellVm(c, Width(c.Column, Math.Max(1, c.ColSpan)), items);
                vm.Cells.Add(cell);
                all.Add(cell);
            }
            rows.Add(vm);
        }

        if (grid.Truncated)
        {
            var more = new GridRowVm();
            more.Cells.Add(GridCellVm.Note("… " + (grid.TotalRows - grid.Rows.Count) + " filas más (la vista previa muestra las primeras " + grid.Rows.Count + ")",
                Math.Max(300, widths.Sum())));
            rows.Add(more);
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        RowsHost.ItemsSource = rows;
        return all;
    }

    private void OnCellClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GridCellVm cell } && cell.Toggle())
            SelectionToggled?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class GridRowVm
{
    public List<GridCellVm> Cells { get; } = new();
}

/// <summary>Celda de la vista previa. Si tiene cambios ligados, su color refleja el estado.</summary>
public sealed class GridCellVm : INotifyPropertyChanged
{
    // Paleta de estados (importacion)
    private static readonly Brush ChangeBg  = Frozen("#F9B49B");   // salmón: cambio pendiente
    private static readonly Brush SkipBg    = Frozen("#E7E6E6");   // desmarcado
    private static readonly Brush ErrorBg   = Frozen("#F4A6A6");   // no se puede aplicar
    private static readonly Brush AppliedBg = Frozen("#C6EFCE");   // aplicado
    private static readonly Brush AppliedFg = Frozen("#006100");
    private static readonly Brush FailedBg  = Frozen("#FFC7CE");
    private static readonly Brush FailedFg  = Frozen("#9C0006");
    private static readonly Brush RowNumBg  = Frozen("#F3F3F3");
    private static readonly Brush MutedFg   = Frozen("#7F7F7F");

    private readonly Brush _baseBg;
    private readonly Brush _baseFg;
    private readonly IReadOnlyList<ScheduleExcelWindow.ChangeItem> _changes;

    public GridCellVm(GridCell c, double width, IReadOnlyList<ScheduleExcelWindow.ChangeItem> changes)
    {
        Text = c.Text;
        Width = width;
        _baseBg = c.Background is null ? Brushes.Transparent : Frozen("#" + c.Background);
        _baseFg = c.Foreground is null ? Brushes.Black : Frozen("#" + c.Foreground);
        FontWeight = c.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        BaseItalic = c.Italic;
        TextAlignment = c.Align switch
        {
            GridAlign.Center => TextAlignment.Center,
            GridAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        _changes = changes;
        foreach (var ch in _changes) ch.PropertyChanged += (_, _) => Refresh();
    }

    private GridCellVm(string text, double width, Brush bg, Brush fg, TextAlignment align, bool italic)
    {
        Text = text; Width = width; _baseBg = bg; _baseFg = fg; TextAlignment = align; BaseItalic = italic;
        _changes = Array.Empty<ScheduleExcelWindow.ChangeItem>();
    }

    internal static GridCellVm RowNumber(int excelRow) =>
        new(excelRow > 0 ? excelRow.ToString() : "", 36, RowNumBg, MutedFg, TextAlignment.Center, false);

    internal static GridCellVm Note(string text, double width) =>
        new(text, width, Brushes.Transparent, MutedFg, TextAlignment.Left, true);

    private bool BaseItalic { get; }
    public string Text { get; }
    public double Width { get; }
    public FontWeight FontWeight { get; }
    public TextAlignment TextAlignment { get; }
    public bool HasChange => _changes.Count > 0;

    private ScheduleExcelWindow.ChangeItem? Main =>
        _changes.FirstOrDefault(c => c.StatusCode != nameof(ChangeStatus.Ready)) ?? _changes.FirstOrDefault();

    public Brush Background => Main?.StatusCode switch
    {
        null => _baseBg,
        nameof(ChangeStatus.Ready) => Main.IsSelected ? ChangeBg : SkipBg,
        nameof(ChangeStatus.Applied) => AppliedBg,
        nameof(ChangeStatus.Failed) => FailedBg,
        _ => ErrorBg, // Error / Conflict
    };

    public Brush Foreground => Main?.StatusCode switch
    {
        nameof(ChangeStatus.Applied) => AppliedFg,
        nameof(ChangeStatus.Failed) => FailedFg,
        nameof(ChangeStatus.Ready) when !Main.IsSelected => MutedFg,
        _ => _baseFg,
    };

    public FontStyle FontStyle => BaseItalic ? FontStyles.Italic : FontStyles.Normal;

    public TextDecorationCollection? TextDecorations =>
        Main is { StatusCode: nameof(ChangeStatus.Ready), IsSelected: false } ? System.Windows.TextDecorations.Strikethrough : null;

    public Cursor? Cursor => _changes.Any(c => c.CanSelect) ? Cursors.Hand : null;

    public object? ToolTip
    {
        get
        {
            var m = Main;
            if (m is null) return null;
            var lines = new List<string>
            {
                m.ColumnHeading + ": «" + m.OldText + "» → «" + m.NewText + "»",
                m.ElementLabel,
                m.StatusText,
            };
            if (m.CanSelect) lines.Add(m.IsSelected ? "Clic para no aplicar este cambio." : "Clic para volver a incluirlo.");
            return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
    }

    /// <summary>Marca/desmarca los cambios de la celda. Devuelve true si cambió algo.</summary>
    internal bool Toggle()
    {
        var selectable = _changes.Where(c => c.CanSelect).ToList();
        if (selectable.Count == 0) return false;
        bool target = !selectable.All(c => c.IsSelected);
        foreach (var c in selectable) c.IsSelected = target;
        return true;
    }

    public void Refresh()
    {
        OnChanged(nameof(Background));
        OnChanged(nameof(Foreground));
        OnChanged(nameof(TextDecorations));
        OnChanged(nameof(Cursor));
        OnChanged(nameof(ToolTip));
    }

    private static Brush Frozen(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
