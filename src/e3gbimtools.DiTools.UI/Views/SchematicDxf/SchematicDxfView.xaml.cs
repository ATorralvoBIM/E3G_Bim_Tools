using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Models.Schematic;
using E3G.BimTools.DiTools.Core.Services.DxfLegend;
using E3G.BimTools.DiTools.Core.Services.Schematic;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Hosting;
using E3G.BimTools.DiTools.UI.Views.DxfLegend;
using Mouse = System.Windows.Input.Mouse;

namespace E3G.BimTools.DiTools.UI.Views.SchematicDxf;

/// <summary>
/// «Esquema de principio desde DXF» (pagina Climatizacion del panel MEP, 2026-11-12).
///
/// Dos modos:
///   • Estilo E3G: si se reconoce el software de origen (perfil; hoy LG LATS)
///     el esquema se interpreta como equipos + tramos + rotulos, se redibuja con
///     estilo E3G (Calibri, tuberias 50_12 / 50_13) y se compacta para caber en
///     el formato de plano elegido (<see cref="SchematicComposer"/>).
///   • Copia fiel: el dibujo tal cual, con el lector de «Leyenda desde DXF».
/// En los dos casos se dibuja con lineas, textos y rellenos nativos en una vista
/// de dibujo nueva o existente.  Habla con Revit por <see cref="IRevitInvoker"/>
/// porque el panel no es modal.
/// </summary>
public partial class SchematicDxfView : UserControl
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private const string Titulo = "Esquema de principio";

    /// <summary>Area util por formato: hoja menos margenes (25 izq., 10 resto) y franja de cajetin.</summary>
    public sealed record FormatOption(string Label, double Width, double Height);

    private static readonly FormatOption[] Formats =
    {
        new("A0 (1189 × 841)", 1110, 790),
        new("A1 (841 × 594)", 780, 540),
        new("A2 (594 × 420)", 540, 370),
        new("A3 (420 × 297)", 370, 250),
    };

    private readonly IDxfLegendService _service;
    private readonly IRevitInvoker _revit;
    private readonly ILegendTextMeasurer _measurer = new WpfLegendTextMeasurer();
    private readonly ObservableCollection<DxfLegendWindow.LayerRow> _layers = new();
    private readonly Settings _settings;
    private SchematicDiagram? _diagram;
    private SchematicComposition? _composition;
    private IReadOnlyList<LegendTextTypeInfo> _textTypes = Array.Empty<LegendTextTypeInfo>();
    private IReadOnlyDictionary<string, ElementId>? _textTypeIds;
    private bool _ocupado;
    private bool _iniciando = true;

    public SchematicDxfView(IDxfLegendService service, IRevitInvoker revit, bool integrada)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _revit = revit ?? throw new ArgumentNullException(nameof(revit));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        Cabecera.Visibility = integrada ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        _settings = Settings.Load();
        UnitsCombo.ItemsSource = Enum.GetValues(typeof(LegendUnits)).Cast<LegendUnits>()
            .Select(u => new DxfLegendWindow.UnitOption(u, u.Label())).ToList();
        UnitsCombo.SelectedIndex = 0;
        FormatCombo.ItemsSource = Formats;
        FormatCombo.SelectedItem = Formats.FirstOrDefault(f => f.Label == _settings.Format) ?? Formats[1];
        ScaleBox.Text = _settings.Scale.ToString(CultureInfo.InvariantCulture);
        PrefixBox.Text = _settings.Prefix;
        WidthFactorBox.Text = _settings.TextWidthFactor.ToString("0.###", Es);
        LayersList.ItemsSource = _layers;
        _iniciando = false;

        Loaded += async (_, _) => await CargarVistasAsync(null);
    }

    private bool EstiloE3g => E3gRadio.IsChecked == true && _diagram?.IsRecognized == true;

    /// <summary>Dibujo que se previsualiza y se genera.</summary>
    private LegendDrawing? Actual => EstiloE3g ? _composition?.Drawing : _diagram?.Source;

    // ============================================================
    // Vistas del proyecto activo
    // ============================================================

    private async Task CargarVistasAsync(ElementId? seleccionar)
    {
        try
        {
            var datos = await _revit.InvokeAsync(app =>
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc?.Document is null || uidoc.Document.IsFamilyDocument) return null;
                return new { Vistas = _service.GetTargetViews(uidoc.Document, uidoc.ActiveView), Textos = _service.GetTextTypes(uidoc.Document) };
            });
            var vistas = datos?.Vistas;
            if (datos is not null) CargarTiposTexto(datos.Textos);
            ViewCombo.ItemsSource = vistas ?? Array.Empty<LegendTargetView>();
            if (vistas is null)
            {
                StatusText.Text = "Abre un proyecto de Revit (no el editor de familias) para generar el esquema.";
                return;
            }
            ViewCombo.SelectedItem = vistas.FirstOrDefault(v => seleccionar is not null && v.Id == seleccionar)
                                     ?? vistas.FirstOrDefault(v => v.Kind == "Dibujo")
                                     ?? vistas.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo leer el proyecto activo: " + ex.Message;
        }
        ActualizarBotones();
    }

    /// <summary>Rellena la lista de tipos de texto conservando la eleccion (por nombre).</summary>
    private void CargarTiposTexto(IReadOnlyList<LegendTextTypeInfo> tipos)
    {
        string? actual = (TextTypeCombo.SelectedItem as LegendTextTypeInfo)?.Name ?? _settings.TextType;
        _textTypes = tipos;
        _iniciando = true;
        TextTypeCombo.ItemsSource = tipos;
        TextTypeCombo.SelectedItem =
            tipos.FirstOrDefault(t => t.Name == actual)
            ?? tipos.Where(t => !t.Bold && t.Font.Equals("Calibri", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => Math.Abs(t.SizeMm - 2.0)).FirstOrDefault()
            ?? tipos.FirstOrDefault(t => !t.Bold)
            ?? tipos.FirstOrDefault();
        _iniciando = false;
        if (_diagram is not null) Recomponer(resetLayers: false);
    }

    /// <summary>Variante en negrita (misma fuente y tamaño) y tipo para el titulo (misma fuente, mayor).</summary>
    private (LegendTextTypeInfo? Bold, LegendTextTypeInfo? Title) Variantes(LegendTextTypeInfo sel)
    {
        bool MismaFuente(LegendTextTypeInfo t) => t.Font.Equals(sel.Font, StringComparison.OrdinalIgnoreCase);
        var bold = sel.Bold ? sel : _textTypes
            .Where(t => MismaFuente(t) && t.Bold && Math.Abs(t.SizeMm - sel.SizeMm) < 0.01)
            .OrderBy(t => Math.Abs(t.WidthFactor - sel.WidthFactor)).FirstOrDefault();
        var title = _textTypes
            .Where(t => MismaFuente(t) && t.SizeMm >= sel.SizeMm * 1.3 && t.SizeMm <= sel.SizeMm * 2.6)
            .OrderByDescending(t => t.Bold)
            .ThenBy(t => Math.Abs(t.SizeMm - sel.SizeMm * 1.75)).FirstOrDefault();
        return (bold, title);
    }

    private void OnTargetModeChanged(object sender, RoutedEventArgs e)
    {
        if (ViewCombo is null) return;
        bool nueva = NewViewRadio.IsChecked == true;
        NewViewNameBox.IsEnabled = nueva;
        ViewCombo.IsEnabled = !nueva;
        ClearViewCheck.IsEnabled = !nueva;
        UpdateInfo();
    }

    // ============================================================
    // Archivo
    // ============================================================

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Esquema de principio de AutoCAD",
            Filter = "Dibujos de AutoCAD (*.dxf;*.dwg)|*.dxf;*.dwg|DXF (*.dxf)|*.dxf|DWG (*.dwg)|*.dwg",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            _diagram = new SchematicReader(_measurer).Read(path);
        }
        catch (Exception ex)
        {
            _diagram = null;
            _composition = null;
            StatusText.Text = "No se pudo leer el archivo: " + ex.Message;
            ActualizarBotones();
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        FileBox.Text = path;
        FileBox.ToolTip = path;
        _settings.LastFolder = Path.GetDirectoryName(path) ?? _settings.LastFolder;
        NewViewNameBox.Text = Path.GetFileNameWithoutExtension(path);

        var src = _diagram.Source;
        foreach (DxfLegendWindow.UnitOption u in UnitsCombo.Items)
            if (u.Value == src.SuggestedUnits) UnitsCombo.SelectedItem = u;

        FileInfo.Text =
            $"{src.Count<LegendCurve>().ToString("N0", Es)} líneas/arcos, {src.Count<LegendFill>().ToString("N0", Es)} rellenos, " +
            $"{src.Count<LegendText>().ToString("N0", Es)} líneas de texto en {src.Layers.Count} capas.";

        if (_diagram.IsRecognized)
        {
            ProfileInfo.Text = _diagram.Summary() +
                (_diagram.Warnings.Count > 0 ? "  " + string.Join(" ", _diagram.Warnings) : string.Empty);
            E3gRadio.IsEnabled = true;
            E3gRadio.IsChecked = true;
        }
        else
        {
            ProfileInfo.Text = "No se reconoce el software de origen (perfiles: LG LATS). Solo está disponible la copia fiel.";
            E3gRadio.IsEnabled = false;
            RawRadio.IsChecked = true;
        }

        PreviewHint.Visibility = System.Windows.Visibility.Collapsed;
        Recomponer(resetLayers: true);
    }

    // ============================================================
    // Modo / estilo
    // ============================================================

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (_iniciando || E3gOptions is null) return;
        Recomponer(resetLayers: true);
    }

    private void OnStyleChanged(object sender, RoutedEventArgs e)
    {
        if (_iniciando) return;
        Recomponer(resetLayers: false);
    }

    /// <summary>
    /// Opciones del estilo E3G segun la pagina: formato, escala y tipo de texto del
    /// proyecto (con su negrita y su titulo).  Sin proyecto: Calibri 2 mm, solo vista previa.
    /// </summary>
    private (SchematicStyleOptions Options, IReadOnlyDictionary<string, ElementId>? TextTypeIds, string Info) OpcionesE3g()
    {
        var sel = TextTypeCombo.SelectedItem as LegendTextTypeInfo;
        var (bold, title) = sel is null ? (null, null) : Variantes(sel);
        IReadOnlyDictionary<string, ElementId>? ids = sel is null ? null : new Dictionary<string, ElementId>
        {
            [SchematicTextStyles.Regular] = sel.Id,
            [SchematicTextStyles.Bold] = (bold ?? sel).Id,
            [SchematicTextStyles.Title] = (title ?? bold ?? sel).Id,
        };
        string info = sel is null
            ? "Abre un proyecto para elegir un tipo de texto."
            : (bold is null ? "Sin variante en negrita de esta fuente y tamaño: la primera línea de las fichas irá normal. " : $"Negrita: {bold.Name}. ")
              + (title is null ? "Sin tipo mayor de la misma fuente: el título usa el mismo tamaño." : $"Título: {title.Name}.");
        var fmt = FormatCombo.SelectedItem as FormatOption ?? Formats[1];
        var opts = new SchematicStyleOptions
        {
            PaperWidthMm = fmt.Width,
            PaperHeightMm = fmt.Height,
            Scale = TryScale(out int s) ? s : 50,
            TextMm = sel?.SizeMm ?? 2.0,
            TitleTextMm = title?.SizeMm ?? sel?.SizeMm ?? 3.5,
            BoldAvailable = sel is null || bold is not null,
            TitleBold = title?.Bold ?? bold is not null || sel is null,
            FontFamily = sel?.Font is { Length: > 0 } f ? f : "Calibri",
            TextWidthFactor = sel?.WidthFactor ?? 1.0,
            Compact = CompactCheck.IsChecked == true,
        };
        return (opts, ids, info);
    }

    /// <summary>Recalcula el dibujo E3G (si toca) y refresca capas, vista previa y datos.</summary>
    private void Recomponer(bool resetLayers)
    {
        bool e3g = EstiloE3g;
        E3gOptions.IsEnabled = e3g;
        UnitsCombo.IsEnabled = !e3g;
        _composition = null;
        ComposeInfo.Text = string.Empty;

        if (e3g && _diagram is not null)
        {
            var (opts, ids, info) = OpcionesE3g();
            _textTypeIds = ids;
            TextTypeInfo.Text = info;
            var fmt = FormatCombo.SelectedItem as FormatOption ?? Formats[1];
            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                _composition = new SchematicComposer(_measurer).Compose(_diagram, opts);
                var c = _composition;
                ComposeInfo.Text =
                    $"Ocupa {c.WidthMm.ToString("0", Es)} × {c.HeightMm.ToString("0", Es)} mm de los " +
                    $"{fmt.Width.ToString("0", Es)} × {fmt.Height.ToString("0", Es)} útiles del {fmt.Label.Split(' ')[0]}" +
                    (c.Fits ? "." : " (no cabe).") + "  " + string.Join("  ", c.Notes);
            }
            catch (Exception ex)
            {
                ComposeInfo.Text = "No se pudo aplicar el estilo E3G: " + ex.Message;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        if (resetLayers)
        {
            _layers.Clear();
            if (Actual is { } dwg)
                foreach (var l in dwg.Layers)
                    _layers.Add(new DxfLegendWindow.LayerRow(l));
        }
        RefreshPreview();
        ActualizarBotones();
    }

    // ============================================================
    // Capas / opciones / vista previa
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

    private void OnFitClick(object sender, RoutedEventArgs e) => Preview.ResetView();

    private void OnOptionChanged(object sender, EventArgs e)
    {
        if (_iniciando) return;
        // La escala cambia las coordenadas del dibujo E3G (mm de papel × escala).
        if (sender == ScaleBox && EstiloE3g) { Recomponer(resetLayers: false); return; }
        UpdateInfo();
    }

    private HashSet<string> ExcludedLayers() =>
        new(_layers.Where(l => !l.IsIncluded).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);

    private void RefreshPreview()
    {
        Preview.SetDrawing(Actual, ExcludedLayers());
        UpdateInfo();
    }

    private LegendUnits UnidadesActuales =>
        EstiloE3g ? LegendUnits.Millimeters
                  : (UnitsCombo.SelectedItem as DxfLegendWindow.UnitOption)?.Value ?? LegendUnits.Meters;

    private void UpdateInfo()
    {
        if (PaperInfo is null) return;
        var dwg = Actual;
        if (dwg is null) { PaperInfo.Text = string.Empty; return; }

        var excluded = ExcludedLayers();
        var b = LegendGeometry.Bounds(dwg.Items.Where(i => !excluded.Contains(i.Layer)));
        if (b is null) { PaperInfo.Text = "No hay nada que dibujar con las capas marcadas."; return; }
        var (minX, minY, maxX, maxY) = b.Value;
        double w = maxX - minX, h = maxY - minY;
        var units = UnidadesActuales;

        PaperInfo.Text = TryScale(out int scale)
            ? $"En papel a 1:{scale}: {(w * units.ToMm() / scale).ToString("0", Es)} × {(h * units.ToMm() / scale).ToString("0", Es)} mm."
            : string.Empty;

        if (NewViewRadio.IsChecked != true && ViewCombo.SelectedItem is LegendTargetView v && TryScale(out int s2) && v.Scale != s2)
            StatusText.Text = $"La vista '{v.Name}' está a 1:{v.Scale}; se cambiará a 1:{s2}.";
        else
            StatusText.Text = "Revisa la vista previa y pulsa «Generar esquema».";
    }

    private bool TryScale(out int scale) =>
        int.TryParse(ScaleBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) && scale > 0 && scale <= 100000;

    private void ActualizarBotones()
    {
        if (GenerateButton is null) return;
        GenerateButton.IsEnabled = !_ocupado && Actual is not null;
    }

    // ============================================================
    // Generar
    // ============================================================

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var drawing = Actual;
        if (drawing is null || _ocupado) return;

        bool nueva = NewViewRadio.IsChecked == true;
        string nombre = (NewViewNameBox.Text ?? string.Empty).Trim();
        var existente = ViewCombo.SelectedItem as LegendTargetView;
        if (nueva && nombre.Length == 0) { Avisar("Escribe el nombre de la vista de dibujo nueva."); return; }
        if (!nueva && existente is null) { Avisar("Elige la vista de destino."); return; }
        if (!TryScale(out int scale)) { Avisar("La escala debe ser un número entero (p. ej. 50 para 1:50)."); return; }

        var wfText = (WidthFactorBox.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(wfText, NumberStyles.Float, CultureInfo.InvariantCulture, out double wf) || wf < 0.5 || wf > 1.5)
        { Avisar("El ancho de texto debe estar entre 0,5 y 1,5."); return; }

        var prefix = string.IsNullOrWhiteSpace(PrefixBox.Text) ? "E3G_ESQ" : PrefixBox.Text.Trim();
        var prohibidos = new[] { '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', ':', '\\' };
        if (prefix.IndexOfAny(prohibidos) >= 0 || (nueva && nombre.IndexOfAny(prohibidos) >= 0))
        { Avisar("El prefijo o el nombre de la vista contienen caracteres que Revit no admite."); return; }

        bool e3g = EstiloE3g;
        var units = UnidadesActuales;
        var excluded = ExcludedLayers();
        bool clear = !nueva && ClearViewCheck.IsChecked == true;
        bool origin = MoveToOriginCheck.IsChecked == true;
        var styleNames = e3g ? _composition?.LineStyleNames : null;
        var weights = e3g ? _composition?.LineWeights : null;
        var textTypes = e3g ? _textTypeIds : null;
        if (e3g && textTypes is null) { Avisar("Elige el tipo de texto del proyecto que quieres usar."); return; }

        _settings.Scale = scale;
        _settings.Prefix = prefix;
        _settings.TextWidthFactor = wf;
        _settings.Format = (FormatCombo.SelectedItem as FormatOption)?.Label ?? _settings.Format;
        if (TextTypeCombo.SelectedItem is LegendTextTypeInfo tt) _settings.TextType = tt.Name;
        _settings.Save();

        _ocupado = true;
        ActualizarBotones();
        StatusText.Text = "Generando…";
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        (DxfLegendResult Result, ElementId? ViewId) salida;
        try
        {
            salida = await _revit.InvokeAsync(app =>
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc is null || doc.IsFamilyDocument)
                    return (new DxfLegendResult { Success = false, ErrorMessage = "No hay un proyecto de Revit activo." }, (ElementId?)null);

                DxfLegendRequest Peticion(ElementId viewId) => new()
                {
                    ViewId = viewId,
                    Units = units,
                    Scale = scale,
                    ExcludedLayers = excluded,
                    StylePrefix = prefix,
                    TextWidthFactor = wf,
                    ClearView = clear,
                    MoveToOrigin = origin,
                    LineStyleNames = styleNames,
                    LineWeights = weights,
                    TextTypes = textTypes,
                };

                if (!nueva)
                    return (_service.Generate(doc, drawing, Peticion(existente!.Id), _measurer), (ElementId?)existente.Id);

                // Vista nueva + dibujo en un solo grupo: un Ctrl+Z lo deshace todo.
                using var grupo = new TransactionGroup(doc, "Esquema de principio desde DXF");
                grupo.Start();
                ElementId viewId;
                using (var tx = new Transaction(doc, "Crear vista de dibujo"))
                {
                    tx.Start();
                    var tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
                    if (tipo is null)
                    {
                        tx.RollBack();
                        grupo.RollBack();
                        return (new DxfLegendResult { Success = false, ErrorMessage = "El proyecto no tiene ningún tipo de vista de dibujo." }, (ElementId?)null);
                    }
                    var vista = ViewDrafting.Create(doc, tipo.Id);
                    vista.Name = NombreLibre(doc, nombre);
                    vista.Scale = scale;
                    viewId = vista.Id;
                    tx.Commit();
                }

                var r = _service.Generate(doc, drawing, Peticion(viewId), _measurer);
                if (r.Success) grupo.Assimilate(); else grupo.RollBack();
                return (r, r.Success ? viewId : (ElementId?)null);
            });
        }
        catch (Exception ex)
        {
            salida = (new DxfLegendResult { Success = false, ErrorMessage = ex.Message }, null);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _ocupado = false;
            ActualizarBotones();
        }

        var result = salida.Result;
        if (!result.Success)
        {
            StatusText.Text = "Error: " + result.ErrorMessage;
            MessageBox.Show(Window.GetWindow(this), result.ErrorMessage, Titulo + " · error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusText.Text = result.ToSummary();
        if (result.Warnings.Count > 0)
        {
            var body = result.ToSummary() + "\n\nAvisos:\n  · " + string.Join("\n  · ", result.Warnings.Take(15)) +
                       (result.Warnings.Count > 15 ? $"\n  … y {result.Warnings.Count - 15} más (ver log)." : string.Empty);
            MessageBox.Show(Window.GetWindow(this), body, Titulo + " · con avisos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Abre la vista generada.
        if (salida.ViewId is { } id)
        {
            try
            {
                await _revit.InvokeAsync(app =>
                {
                    var uidoc = app.ActiveUIDocument;
                    if (uidoc?.Document.GetElement(id) is View v && uidoc.ActiveView?.Id != id)
                        uidoc.RequestViewChange(v);
                    return 0;
                });
            }
            catch { /* no critico */ }
            await CargarVistasAsync(id);
        }
    }

    private static string NombreLibre(Document doc, string nombre)
    {
        var usados = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.DraftingView).Select(v => v.Name),
            StringComparer.CurrentCultureIgnoreCase);
        if (!usados.Contains(nombre)) return nombre;
        for (int i = 2; ; i++)
            if (!usados.Contains($"{nombre} ({i})")) return $"{nombre} ({i})";
    }

    private void Avisar(string texto) =>
        MessageBox.Show(Window.GetWindow(this), texto, Titulo, MessageBoxButton.OK, MessageBoxImage.Information);

    // ============================================================
    // Ajustes (%APPDATA%\e3g\DiTools\EsquemaDxf\settings.json)
    // ============================================================

    private sealed class Settings
    {
        public string LastFolder { get; set; } = string.Empty;
        public int Scale { get; set; } = 50;
        public string Prefix { get; set; } = "E3G_ESQ";
        public double TextWidthFactor { get; set; } = 0.98;
        public string Format { get; set; } = "A1 (841 × 594)";
        public string TextType { get; set; } = string.Empty;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "e3g", "DiTools", "EsquemaDxf", "settings.json");

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
