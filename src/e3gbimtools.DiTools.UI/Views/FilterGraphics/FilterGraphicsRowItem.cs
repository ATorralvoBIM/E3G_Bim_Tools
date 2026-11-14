using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Fila del panel de grafismo de filtros: un filtro aplicado a la vista destino
/// con su resumen de proyección/superficie (como el recuadro de V/G) y toggles
/// Activo/Visible que se aplican al vuelo.
/// </summary>
public sealed class FilterGraphicsRowItem : INotifyPropertyChanged
{
    public FilterGraphicsRowItem(ElementId filterId, FilterGraphicsEntry entry)
    {
        FilterId = filterId;
        Entry    = entry;
        _isEnabled = entry.Enabled;
        _isVisible = entry.Visible;
    }

    public ElementId           FilterId { get; }
    public FilterGraphicsEntry Entry    { get; }
    public string              Name     => Entry.Name;

    // ---- Resumen proyección/superficie ----------------------------------

    /// <summary>Color de línea; gris si solo se modifica patrón/grosor (color del estilo de objeto).</summary>
    public Brush  LineBrush   => Entry.ProjectionLine is { } l
        ? (l.Color is null ? Brushes.Gray : ToBrush(l.Color))
        : Brushes.Transparent;
    public bool   HasLine     => Entry.ProjectionLine is not null;
    public string LineText
    {
        get
        {
            var l = Entry.ProjectionLine;
            if (l is null) return string.Empty;
            var pattern = l.Pattern is null or FilterGraphicsMapper.SolidLineToken ? "" : l.Pattern;
            var weight  = l.Weight is int w ? $" · {w}" : "";
            return (pattern + weight).Trim(' ', '·');
        }
    }
    public string LineTooltip
    {
        get
        {
            var l = Entry.ProjectionLine;
            if (l is null) return "Sin modificación · clic para elegir color";
            var parts = new[]
            {
                l.Color,
                l.Pattern is null ? null : (l.Pattern == FilterGraphicsMapper.SolidLineToken ? "continua" : l.Pattern),
                l.Weight is int w ? $"grosor {w}" : null,
            };
            return string.Join(" · ", parts.Where(p => p is not null)) + " · clic para cambiar color";
        }
    }

    /// <summary>Grosor aproximado de la muestra a partir del grosor de pluma.</summary>
    public double LineThickness => Entry.ProjectionLine?.Weight is int w ? 1 + Math.Min(w, 8) / 3.0 : 1.5;

    public bool   LineDashed  => Entry.ProjectionLine?.Pattern is { } p && p != FilterGraphicsMapper.SolidLineToken;

    public Brush  PatternBrush => Entry.SurfaceForeground is { Visible: true } f ? ToBrush(f.Color) : Brushes.Transparent;
    public bool   HasPattern   => Entry.SurfaceForeground is not null;
    public string PatternTooltip => Entry.SurfaceForeground is { } f
        ? $"{(f.Visible ? "" : "(oculto) ")}{f.Pattern ?? "patrón sin modificar"} {f.Color}".Trim()
        : "Sin modificación · clic para elegir color";

    public string TransparencyText => Entry.Transparency > 0 ? $"{Entry.Transparency}%" : string.Empty;

    // ---- Toggles ---------------------------------------------------------

    /// <summary>Si es <c>true</c>, los cambios de toggles no disparan eventos (refrescos).</summary>
    public bool Suppress { get; set; }

    private bool _isChecked;
    /// <summary>Marcado para exportar.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
    }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Entry.Enabled = value;
            OnPropertyChanged();
            if (!Suppress) Toggled?.Invoke(this);
        }
    }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            Entry.Visible = value;
            OnPropertyChanged();
            if (!Suppress) Toggled?.Invoke(this);
        }
    }

    /// <summary>Se dispara cuando el usuario cambia Activo o Visible.</summary>
    public event Action<FilterGraphicsRowItem>? Toggled;

    private static Brush ToBrush(string? hex)
    {
        if (FilterGraphicsMapper.ParseColor(hex) is not { } c) return Brushes.Transparent;
        var b = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue));
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
