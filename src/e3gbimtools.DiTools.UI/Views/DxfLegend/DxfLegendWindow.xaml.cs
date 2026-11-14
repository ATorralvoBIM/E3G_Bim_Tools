using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Services.DxfLegend;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Dialogs;
using Mouse = System.Windows.Input.Mouse;

namespace E3G.BimTools.DiTools.UI.Views.DxfLegend;

/// <summary>
/// Ventana "Leyenda desde DXF".  Carga una leyenda de AutoCAD (DXF o DWG),
/// muestra su vista previa con las capas que se van a traer y la dibuja en la
/// leyenda o vista de dibujo elegida con las mismas medidas: el DXF esta en
/// unidades reales y la escala de la vista (1:N, la de la ventana grafica de
/// AutoCAD) lo lleva al papel.
///
/// Modal (ShowDialog) desde el comando, asi que llama al servicio directamente
/// en el hilo de Revit.  Recuerda carpeta, escala y opciones en
/// %APPDATA%\e3g\DiTools\DxfLegend\settings.json.
/// </summary>
public partial class DxfLegendWindow : Window
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private readonly UIDocument _uidoc;
    private readonly IDxfLegendService _service;
    private readonly ILegendTextMeasurer _measurer = new WpfLegendTextMeasurer();
    private readonly ObservableCollection<LayerRow> _layers = new();
    private LegendDrawing? _drawing;
    private Settings _settings;

    public DxfLegendWindow(UIDocument uidoc, IDxfLegendService service)
    {
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        _settings = Settings.Load();
        UnitsCombo.ItemsSource = Enum.GetValues(typeof(LegendUnits)).Cast<LegendUnits>()
            .Select(u => new UnitOption(u, u.Label())).ToList();
        UnitsCombo.SelectedIndex = 0;
        ScaleBox.Text = _settings.Scale.ToString(CultureInfo.InvariantCulture);
        PrefixBox.Text = _settings.Prefix;
        WidthFactorBox.Text = _settings.TextWidthFactor.ToString("0.###", Es);
        LayersList.ItemsSource = _layers;

        LoadViews();
    }

    // ============================================================
    // Vistas
    // ============================================================

    private void LoadViews()
    {
        var views = _service.GetTargetViews(_uidoc.Document, _uidoc.ActiveView);
        ViewCombo.ItemsSource = views;
        if (views.Count == 0)
        {
            StatusText.Text = "El modelo no tiene leyendas ni vistas de dibujo. Crea una (Vista › Leyendas) y vuelve a abrir la herramienta.";
            return;
        }
        ViewCombo.SelectedIndex = 0;
        // Si la vista activa es valida se propone su escala; si no, la recordada.
        if (views[0].IsActive) ScaleBox.Text = views[0].Scale.ToString(CultureInfo.InvariantCulture);
    }

    private void OnViewChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateInfo();

    // ============================================================
    // Archivo
    // ============================================================

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Leyenda de AutoCAD",
            Filter = "Dibujos de AutoCAD (*.dxf;*.dwg)|*.dxf;*.dwg|DXF (*.dxf)|*.dxf|DWG (*.dwg)|*.dwg",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (dlg.ShowDialog(this) != true) return;
        LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            _drawing = _service.Read(path, _measurer);
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            ShowFriendlyError("No se pudo leer el archivo.", ex);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        FileBox.Text = path;
        _settings.LastFolder = Path.GetDirectoryName(path) ?? _settings.LastFolder;

        _layers.Clear();
        foreach (var l in _drawing.Layers)
            _layers.Add(new LayerRow(l));

        // Unidades: se propone la del tamaño del dibujo ($INSUNITS no es fiable en las plantillas).
        foreach (UnitOption u in UnitsCombo.Items)
            if (u.Value == _drawing.SuggestedUnits) UnitsCombo.SelectedItem = u;

        var d = _drawing;
        FileInfo.Text =
            $"{d.Count<LegendCurve>().ToString("N0", Es)} líneas/arcos, {d.Count<LegendFill>().ToString("N0", Es)} rellenos, " +
            $"{d.Count<LegendText>().ToString("N0", Es)} líneas de texto en {d.Layers.Count} capas." +
            (d.Warnings.Count > 0 ? "  Avisos: " + string.Join(" ", d.Warnings) : string.Empty);

        GenerateButton.IsEnabled = ViewCombo.Items.Count > 0;
        PreviewHint.Visibility = System.Windows.Visibility.Collapsed;
        RefreshPreview();
    }

    // ============================================================
    // Capas / opciones
    // ============================================================

    private void OnLayerToggled(object sender, RoutedEventArgs e) => RefreshPreview();

    private void OnAllLayersClick(object sender, RoutedEventArgs e)
    {
        foreach (var l in _layers) l.IsIncluded = true;
        RefreshPreview();
    }

    private void OnNoLayersClick(object sender, RoutedEventArgs e)
    {
        foreach (var l in _layers) l.IsIncluded = false;
        RefreshPreview();
    }

    private void OnOptionChanged(object sender, EventArgs e) => UpdateInfo();

    private HashSet<string> ExcludedLayers() =>
        new(_layers.Where(l => !l.IsIncluded).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);

    private void RefreshPreview()
    {
        Preview.SetDrawing(_drawing, ExcludedLayers());
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (PaperInfo is null) return;
        if (_drawing is null || UnitsCombo.SelectedItem is not UnitOption units)
        {
            PaperInfo.Text = string.Empty;
            return;
        }

        var excluded = ExcludedLayers();
        var b = LegendGeometry.Bounds(_drawing.Items.Where(i => !excluded.Contains(i.Layer)));
        if (b is null) { PaperInfo.Text = "No hay nada que dibujar con las capas marcadas."; return; }
        var (minX, minY, maxX, maxY) = b.Value;
        double w = maxX - minX, h = maxY - minY;

        string paper = TryScale(out int scale)
            ? $" → en papel a 1:{scale}: {(w * units.Value.ToMm() / scale).ToString("0.#", Es)} × {(h * units.Value.ToMm() / scale).ToString("0.#", Es)} mm"
            : string.Empty;
        string declared = _drawing.DeclaredUnits is { } du && du != units.Value
            ? $"  El DXF declara {du.Label().ToLowerInvariant()} ($INSUNITS), pero por su tamaño se proponen {_drawing.SuggestedUnits.Label().ToLowerInvariant()}."
            : string.Empty;
        PaperInfo.Text = $"Dibujo: {w.ToString("0.###", Es)} × {h.ToString("0.###", Es)} {units.Label.ToLowerInvariant()}{paper}.{declared}";

        if (ViewCombo.SelectedItem is LegendTargetView v && TryScale(out int s2) && v.Scale != s2)
            StatusText.Text = $"La vista '{v.Name}' está a 1:{v.Scale}; se cambiará a 1:{s2}.";
        else if (_drawing is not null)
            StatusText.Text = "Revisa la vista previa y pulsa «Generar leyenda».";
    }

    private bool TryScale(out int scale) =>
        int.TryParse(ScaleBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) && scale > 0 && scale <= 100000;

    // ============================================================
    // Generar
    // ============================================================

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (_drawing is null) return;
        if (ViewCombo.SelectedItem is not LegendTargetView target)
        {
            MessageBox.Show(this, "Elige la vista destino.", "Leyenda desde DXF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryScale(out int scale))
        {
            MessageBox.Show(this, "La escala debe ser un número entero (p. ej. 100 para 1:100).", "Leyenda desde DXF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var wfText = (WidthFactorBox.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(wfText, NumberStyles.Float, CultureInfo.InvariantCulture, out double wf) || wf < 0.5 || wf > 1.5)
        {
            MessageBox.Show(this, "El ancho de texto debe estar entre 0,5 y 1,5.", "Leyenda desde DXF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var prefix = string.IsNullOrWhiteSpace(PrefixBox.Text) ? "E3G_LEY" : PrefixBox.Text.Trim();
        if (prefix.IndexOfAny(new[] { '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', ':', '\\' }) >= 0)
        {
            MessageBox.Show(this, "El prefijo contiene caracteres que Revit no admite en nombres.", "Leyenda desde DXF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var units = ((UnitOption)UnitsCombo.SelectedItem).Value;
        var request = new DxfLegendRequest
        {
            ViewId = target.Id,
            Units = units,
            Scale = scale,
            ExcludedLayers = ExcludedLayers(),
            StylePrefix = prefix,
            TextWidthFactor = wf,
            ClearView = ClearViewCheck.IsChecked == true,
            MoveToOrigin = MoveToOriginCheck.IsChecked == true,
        };

        _settings.Scale = scale;
        _settings.Prefix = prefix;
        _settings.TextWidthFactor = wf;
        _settings.Save();

        DxfLegendResult result;
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            StatusText.Text = "Generando…";
            result = _service.Generate(_uidoc.Document, _drawing, request, _measurer);
        }
        catch (Exception ex)
        {
            ShowFriendlyError("La generación falló y se deshizo.", ex);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (!result.Success)
        {
            StatusText.Text = "Error: " + result.ErrorMessage;
            MessageBox.Show(this, result.ErrorMessage, "Leyenda desde DXF · error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text = result.ToSummary();
        var body = result.ToSummary();
        if (result.Warnings.Count > 0)
            body += "\n\nAvisos:\n  · " + string.Join("\n  · ", result.Warnings.Take(15)) +
                    (result.Warnings.Count > 15 ? $"\n  … y {result.Warnings.Count - 15} más (ver log)." : string.Empty);
        MessageBox.Show(this, body, result.Warnings.Count > 0 ? "Leyenda desde DXF · con avisos" : "Leyenda desde DXF · OK",
            MessageBoxButton.OK, result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        // Muestra la vista generada (efectivo al cerrar la ventana).
        if (!target.IsActive && _uidoc.Document.GetElement(target.Id) is View view)
        {
            try { _uidoc.RequestViewChange(view); } catch { /* no critico */ }
        }
        LoadViewsKeepingSelection(target.Id);
    }

    private void LoadViewsKeepingSelection(ElementId id)
    {
        var views = _service.GetTargetViews(_uidoc.Document, _uidoc.ActiveView);
        ViewCombo.ItemsSource = views;
        ViewCombo.SelectedItem = views.FirstOrDefault(v => v.Id == id) ?? views.FirstOrDefault();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ShowFriendlyError(string title, Exception ex)
    {
        try
        {
            var dlg = FriendlyErrorDialog.ForException(ex);
            dlg.Owner = this;
            dlg.ShowDialog();
        }
        catch
        {
            MessageBox.Show(this, $"{title}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ============================================================
    // Filas y ajustes
    // ============================================================

    public sealed record UnitOption(LegendUnits Value, string Label);

    public sealed class LayerRow : INotifyPropertyChanged
    {
        public LayerRow(LegendLayerInfo info)
        {
            Name = info.Name;
            CountText = $"({info.ItemCount})";
            var b = new SolidColorBrush(System.Windows.Media.Color.FromRgb(info.Color.R, info.Color.G, info.Color.B));
            b.Freeze();
            Swatch = b;
            _isIncluded = !info.ExcludedByDefault;
        }

        public string Name { get; }
        public string CountText { get; }
        public Brush Swatch { get; }

        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value) return;
                _isIncluded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class Settings
    {
        public string LastFolder { get; set; } = string.Empty;
        public int Scale { get; set; } = 100;
        public string Prefix { get; set; } = "E3G_LEY";
        public double TextWidthFactor { get; set; } = 0.98;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "e3g", "DiTools", "DxfLegend", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { /* ajustes corruptos: valores por defecto */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* no critico */ }
        }
    }
}
