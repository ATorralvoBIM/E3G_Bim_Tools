using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Dialogs;
using E3G.BimTools.DiTools.UI.Hosting;
using Mouse = System.Windows.Input.Mouse;
// Autodesk.Revit.DB tiene Grid y Autodesk.Revit.UI tiene TextBox/ComboBox: alias explicitos.
using Expander = System.Windows.Controls.Expander;
using Grid = System.Windows.Controls.Grid;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace E3G.BimTools.DiTools.UI.Views.DialuxImport;

/// <summary>
/// "DIALux → Revit".  Version en el plugin de la rutina Dynamo DIALux_a_Revit.dyn:
/// carga el DWG de luminarias de DIALux, asocia cada bloque a un tipo de luminaria
/// del modelo y ofrece sus modos:
///   PREVIA    → secciones de asociacion y vista previa al cargar el DWG,
///   SIMULAR   → boton Simular (no toca el modelo),
///   CREAR     → boton Crear (solo las nuevas),
///   REHACER   → boton Rehacer (borra las importadas de ese DWG y recoloca),
///   AUDITAR   → boton Auditar (+ Sincronizar / Borrar sobrantes).
/// La entrada "Config .json (opcional)" son los botones Cargar/Guardar config.
///
/// Vive en el panel acoplable MEP (llamadas a Revit por ExternalEvent) o dentro de
/// <see cref="DialuxImportWindow"/> (modal, llamadas directas): toda la API pasa por
/// <see cref="IRevitInvoker"/>.  Recuerda la ultima configuracion (y con ella la
/// biblioteca de asociaciones bloque → tipo) en %APPDATA%\e3g\DiTools\DialuxImport\settings.json.
/// </summary>
public partial class DialuxImportView : UserControl
{
    private const string Caption = "DIALux → Revit";
    private const double AnchoDosColumnas = 860;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDialuxImportService _service;
    private readonly IRevitInvoker _revit;
    private readonly ObservableCollection<LayerRow> _layers = new();
    private readonly ObservableCollection<MappingRow> _mappingRows = new();
    private readonly List<PlanRowView> _planRows = new();
    private readonly Settings _settings;
    private DialuxConfig _config;
    private DialuxDrawing? _drawing;
    private DialuxPlan? _plan;
    private bool _loadingUi;

    /// <summary>Documento cuyos niveles y tipos estan cargados (PathName|Title); null si no hay proyecto.</summary>
    private string? _docKey;
    private ElementId? _nivelVistaActiva;
    private bool _iniciado;
    private bool _ocupado;
    private bool? _dosColumnas;

    public ObservableCollection<TypeOption> TypeOptions { get; } = new();
    public IReadOnlyList<HeightOption> HeightSources { get; } = new[]
    {
        new HeightOption(DialuxHeightSource.Fixed, "Fija"),
        new HeightOption(DialuxHeightSource.DwgZ, "Z del DWG"),
    };

    /// <param name="integrada">true dentro del panel MEP (muestra su propia cabecera).</param>
    public DialuxImportView(IDialuxImportService service, IRevitInvoker revit, bool integrada)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _revit = revit ?? throw new ArgumentNullException(nameof(revit));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        DataContext = this;
        Cabecera.Visibility = integrada ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        _settings = Settings.Load();
        _config = _settings.Config ?? new DialuxConfig();

        UnitsCombo.ItemsSource = Enum.GetValues(typeof(LegendUnits)).Cast<LegendUnits>()
            .Select(u => new UnitOption(u, u.Label())).ToList();
        OriginCombo.ItemsSource = new[]
        {
            new OriginOption(DialuxOrigin.InternalOrigin, "Origen interno"),
            new OriginOption(DialuxOrigin.ProjectBasePoint, "Punto base del proyecto"),
            new OriginOption(DialuxOrigin.SharedCoordinates, "Coordenadas compartidas"),
        };
        LevelCombo.ItemsSource = Array.Empty<DialuxLevel>();
        StatusFilterCombo.ItemsSource = StatusFilter.All;
        StatusFilterCombo.SelectedIndex = 0;
        LayersList.ItemsSource = _layers;
        MappingList.ItemsSource = _mappingRows;
        TypeOptions.Add(TypeOption.None);

        Loaded += OnLoaded;
        Unloaded += (_, _) => GuardarAjustes();
        SizeChanged += (_, e) => AplicarDisposicion(e.NewSize.Width);
    }

    /// <summary>Guarda la configuracion actual (la ventana lo llama al cerrarse).</summary>
    public void GuardarAjustes()
    {
        try
        {
            _settings.Config = _config;
            _settings.Save();
        }
        catch { /* no critico */ }
    }

    // ============================================================
    // Documento activo
    // ============================================================

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AplicarDisposicion(ActualWidth);
        try
        {
            if (!_iniciado)
            {
                _iniciado = true;
                await CargarDocumentoAsync();
            }
            else
            {
                // Al volver al panel: si el proyecto activo ha cambiado, recarga niveles y tipos.
                string? activo = await _revit.InvokeAsync(app => ClaveActiva(app));
                if (activo != _docKey && await CargarDocumentoAsync() && _drawing is not null)
                    StatusText.Text = "Proyecto activo: " + NombreProyecto() + ". Se han recargado los niveles y los tipos.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo leer el proyecto activo: " + ex.Message;
        }
    }

    private static string Clave(Document doc) => doc.PathName + "|" + doc.Title;

    private static string? ClaveActiva(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document;
        return doc is null || doc.IsFamilyDocument ? null : Clave(doc);
    }

    private string NombreProyecto() => _docKey is null ? "—" : _docKey[(_docKey.LastIndexOf('|') + 1)..];

    private sealed record DatosDocumento(
        string Clave, IReadOnlyList<DialuxLevel> Niveles, ElementId? NivelVista, IReadOnlyList<DialuxFamilyType> Tipos);

    /// <summary>Lee niveles y tipos del proyecto activo.  false si no hay proyecto.</summary>
    private async Task<bool> CargarDocumentoAsync()
    {
        bool todas = AllCategoriesCheck.IsChecked == true;
        var datos = await _revit.InvokeAsync(app =>
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc is null || doc.IsFamilyDocument) return null;
            return new DatosDocumento(Clave(doc), _service.GetLevels(doc), uidoc!.ActiveView?.GenLevel?.Id,
                                      _service.GetFamilyTypes(doc, todas));
        });

        _docKey = datos?.Clave;
        _plan = null;
        if (datos is null)
        {
            LevelCombo.ItemsSource = Array.Empty<DialuxLevel>();
            MostrarTipos(Array.Empty<DialuxFamilyType>());
            StatusText.Text = "Abre un proyecto de Revit (no el editor de familias) para usar DIALux → Revit.";
            ActualizarBotones();
            return false;
        }

        _nivelVistaActiva = datos.NivelVista;
        LevelCombo.ItemsSource = datos.Niveles;
        MostrarTipos(datos.Tipos);
        ConfigToUi();
        ActualizarBotones();
        return true;
    }

    /// <summary>
    /// Ejecuta <paramref name="trabajo"/> en Revit si el proyecto activo sigue siendo el
    /// cargado.  Si ha cambiado, recarga niveles y tipos y devuelve Ok = false (la
    /// asociacion y el nivel pueden no valer para el otro proyecto).
    /// </summary>
    private async Task<(bool Ok, T? Valor)> EnDocumentoAsync<T>(Func<UIDocument, T> trabajo)
    {
        string? esperado = _docKey;
        bool mismo = false;
        T? valor = default;
        if (esperado is not null)
        {
            await _revit.InvokeAsync(app =>
            {
                mismo = ClaveActiva(app) == esperado;
                if (mismo) valor = trabajo(app.ActiveUIDocument!);
                return 0;
            });
        }
        if (mismo) return (true, valor);

        bool hay = await CargarDocumentoAsync();
        if (hay)
            StatusText.Text = $"El proyecto activo es otro ({NombreProyecto()}): se han recargado los niveles y los tipos. " +
                              "Revisa el nivel y la asociación y vuelve a pulsar.";
        return (false, default);
    }

    // ============================================================
    // Ocupado / botones / disposicion
    // ============================================================

    private bool Empezar(string texto)
    {
        if (_ocupado) return false;
        _ocupado = true;
        Columnas.IsEnabled = false;
        Acciones.IsEnabled = false;
        StatusText.Text = texto;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        return true;
    }

    private void Terminar()
    {
        Mouse.OverrideCursor = null;
        _ocupado = false;
        Columnas.IsEnabled = true;
        Acciones.IsEnabled = true;
        ActualizarBotones();
    }

    private void ActualizarBotones()
    {
        bool listo = _drawing is { Inserts.Count: > 0 } && _docKey is not null;
        foreach (var b in new[] { SimulateButton, AuditButton, CreateButton, RedoButton, SyncButton, AlignButton })
            b.IsEnabled = listo;
        DeleteExtraButton.IsEnabled = listo && _plan is not null && _plan.Count(DialuxRowStatus.Extra) > 0;
    }

    /// <summary>Una columna en el panel estrecho; dos en cuanto hay sitio.</summary>
    private void AplicarDisposicion(double ancho)
    {
        if (ancho <= 0) return;
        bool dos = ancho >= AnchoDosColumnas;
        if (_dosColumnas == dos) return;
        _dosColumnas = dos;

        ColIzquierda.Width = dos ? new GridLength(360) : new GridLength(1, GridUnitType.Star);
        ColDerecha.Width = dos ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(PilaDerecha, dos ? 0 : 1);
        Grid.SetColumn(PilaDerecha, dos ? 1 : 0);
        PilaDerecha.Margin = dos ? new Thickness(12, 0, 0, 0) : new Thickness(0);
        MappingList.MaxHeight = PlanList.MaxHeight = dos ? 620 : 440;
        PreviewHost.Height = dos ? 420 : 260;
    }

    private void Mostrar(Expander seccion)
    {
        seccion.IsExpanded = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => seccion.BringIntoView()));
    }

    private void ActualizarResumenes()
    {
        SeccionDwg.Tag = string.IsNullOrEmpty(FileBox.Text) ? "Sin archivo" : Path.GetFileName(FileBox.Text);

        var nivel = (LevelCombo.SelectedItem as DialuxLevel)?.Name;
        var unidades = (UnitsCombo.SelectedItem as UnitOption)?.Label;
        var origen = (OriginCombo.SelectedItem as OriginOption)?.Label;
        SeccionColocacion.Tag = string.Join(" · ", new[] { nivel, unidades?.ToLowerInvariant(), origen?.ToLowerInvariant() }
            .Where(s => !string.IsNullOrEmpty(s)));

        SeccionCapas.Tag = _layers.Count == 0 ? string.Empty : $"{_layers.Count(l => l.IsIncluded)} de {_layers.Count}";
        SeccionAsociar.Tag = _mappingRows.Count == 0
            ? string.Empty
            : $"{_mappingRows.Count(r => r.SelectedType != TypeOption.None)} de {_mappingRows.Count} con tipo";
        SeccionResultados.Tag = _plan is null
            ? string.Empty
            : _plan.Rows.Count == 0 ? "Sin filas" : _plan.ToSummary();
    }

    // ============================================================
    // Tipos de familia / config ↔ UI
    // ============================================================

    private void MostrarTipos(IReadOnlyList<DialuxFamilyType> types)
    {
        TypeOptions.Clear();
        TypeOptions.Add(TypeOption.None);
        foreach (var t in types) TypeOptions.Add(new TypeOption(t.FamilyName, t.TypeName, t.Label + (t.IsWorkPlaneBased ? "  · plano de trabajo" : string.Empty)));
        TypesInfo.Text = types.Count == 0
            ? "No hay familias de luminaria cargadas en el modelo. Cárgalas (Insertar › Cargar familia) o marca «Todas las categorías»."
            : $"{types.Count} tipos disponibles. Las familias alojadas en techo/muro no se ofrecen: usa versiones basadas en cara o en plano de trabajo.";
        foreach (var r in _mappingRows) r.Refresh();
        ActualizarResumenes();
    }

    /// <summary>Opcion de la lista para un mapeo; si el tipo no esta en el modelo se añade marcado.</summary>
    private TypeOption OptionFor(DialuxMapping m)
    {
        if (!m.IsMapped) return TypeOption.None;
        var opt = TypeOptions.FirstOrDefault(o => o.Matches(m.FamilyName, m.TypeName));
        if (opt is not null) return opt;
        opt = new TypeOption(m.FamilyName, m.TypeName, $"⚠ {m.TargetLabel} (no está en el modelo)");
        TypeOptions.Add(opt);
        return opt;
    }

    private void ConfigToUi()
    {
        _loadingUi = true;
        try
        {
            foreach (UnitOption u in UnitsCombo.Items)
                if (u.Value == (_config.Units ?? _drawing?.SuggestedUnits ?? LegendUnits.Meters)) UnitsCombo.SelectedItem = u;
            foreach (OriginOption o in OriginCombo.Items)
                if (o.Value == _config.Origin) OriginCombo.SelectedItem = o;
            OffsetXBox.Text = _config.OffsetXMm.ToString("0.##", Es);
            OffsetYBox.Text = _config.OffsetYMm.ToString("0.##", Es);
            RotationBox.Text = _config.RotationDeg.ToString("0.###", Es);
            ToleranceBox.Text = _config.ToleranceMm.ToString("0.##", Es);

            var levels = (IReadOnlyList<DialuxLevel>)LevelCombo.ItemsSource;
            LevelCombo.SelectedItem =
                levels.FirstOrDefault(l => string.Equals(l.Name, _config.LevelName, StringComparison.OrdinalIgnoreCase))
                ?? levels.FirstOrDefault(l => _nivelVistaActiva is not null && l.Id == _nivelVistaActiva)
                ?? levels.FirstOrDefault();
        }
        finally { _loadingUi = false; }

        if (_drawing is not null) BuildDrawingRows();
        ActualizarResumenes();
    }

    /// <summary>Vuelca la UI en <see cref="_config"/>.  Devuelve false (y avisa) si hay un numero mal escrito.</summary>
    private bool UiToConfig()
    {
        if (!TryNum(OffsetXBox.Text, out var ox) || !TryNum(OffsetYBox.Text, out var oy)
            || !TryNum(RotationBox.Text, out var rot) || !TryNum(ToleranceBox.Text, out var tol) || tol <= 0)
        {
            Mostrar(SeccionColocacion);
            Mensaje("Revisa los desfases, el giro y la tolerancia: deben ser números (tolerancia > 0).",
                Caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        _config.OffsetXMm = ox;
        _config.OffsetYMm = oy;
        _config.RotationDeg = rot;
        _config.ToleranceMm = tol;
        _config.Units = (UnitsCombo.SelectedItem as UnitOption)?.Value;
        _config.Origin = (OriginCombo.SelectedItem as OriginOption)?.Value ?? DialuxOrigin.InternalOrigin;
        _config.LevelName = (LevelCombo.SelectedItem as DialuxLevel)?.Name ?? string.Empty;
        if (_layers.Count > 0)
            _config.Layers = _layers.All(l => l.IsIncluded) ? new List<string>() : _layers.Where(l => l.IsIncluded).Select(l => l.Name).ToList();

        // Las altura/giro se editan en las tarjetas y ya estan en los DialuxMapping.
        GuardarAjustes();
        return true;
    }

    private static bool TryNum(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ============================================================
    // Archivo DWG
    // ============================================================

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "DWG de luminarias exportado de DIALux",
            Filter = "Dibujos de AutoCAD (*.dwg;*.dxf)|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (Dialogo(dlg) != true) return;

        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            _drawing = _service.Read(dlg.FileName);
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            ShowFriendlyError("No se pudo leer el archivo.", ex);
            return;
        }
        finally { Mouse.OverrideCursor = null; }

        FileBox.Text = dlg.FileName;
        _settings.LastFolder = Path.GetDirectoryName(dlg.FileName) ?? _settings.LastFolder;
        _plan = null;
        _planRows.Clear();
        PlanList.ItemsSource = null;

        // Unidades: la declarada en el DWG, o la recordada si el DWG no declara.
        if (_drawing.DeclaredUnits is { } du) _config.Units = du;
        ConfigToUi();

        int top = _drawing.Inserts.Count(i => i.Depth == 0);
        FileInfo.Text =
            $"{_drawing.Inserts.Count.ToString("N0", Es)} inserciones de {_drawing.Blocks.Count} bloques " +
            $"({top.ToString("N0", Es)} en espacio modelo) en {_drawing.Layers.Count} capas." +
            (_drawing.Warnings.Count > 0 ? "  " + string.Join(" ", _drawing.Warnings) : string.Empty);

        ActualizarBotones();
        ActualizarResumenes();
        Mostrar(SeccionAsociar);
        StatusText.Text = _docKey is null
            ? "Abre un proyecto de Revit para asociar los bloques con sus tipos."
            : "Asocia cada bloque de luminaria con un tipo de Revit (deja «— ignorar —» en el resto) y pulsa «Simular».";
    }

    /// <summary>Capas y filas de mapeo a partir del dibujo y la config actual.</summary>
    private void BuildDrawingRows()
    {
        if (_drawing is null) return;

        // Capas: las de la config si existen en este DWG; si no, en un DWG de DIALux solo la de luminarias.
        bool dialux = _drawing.HasDialuxLuminaires;
        var wanted = new HashSet<string>(_config.Layers, StringComparer.OrdinalIgnoreCase);
        if (!_drawing.Layers.Any(wanted.Contains))
            wanted = dialux
                ? new HashSet<string>(_drawing.Inserts.Where(i => i.IsDialuxLuminaire).Select(i => i.Layer), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(_drawing.Layers, StringComparer.OrdinalIgnoreCase);
        _layers.Clear();
        foreach (var l in _drawing.Layers)
            _layers.Add(new LayerRow(l, _drawing.Inserts.Count(i => string.Equals(i.Layer, l, StringComparison.OrdinalIgnoreCase)),
                wanted.Contains(l)));

        OnlyDialuxCheck.IsEnabled = dialux;
        bool onlyDialux = dialux && OnlyDialuxCheck.IsChecked == true;
        _mappingRows.Clear();
        foreach (var b in _drawing.Blocks)
        {
            if (onlyDialux && !b.IsDialuxLuminaire) continue;
            if (OnlyTopLevelCheck.IsChecked == true && b.AnyNested && _drawing.Inserts.All(i => i.Depth > 0 || i.BlockName != b.Name))
                continue;
            var m = _config.FindMapping(b.Name);
            if (m is null)
            {
                m = new DialuxMapping { BlockName = b.Name };
                _config.Mappings.Add(m);
            }
            _mappingRows.Add(new MappingRow(b, m, OptionFor, OnMappingEdited));
        }
        UpdateUnitsInfo();
        RefreshPreview();
        ActualizarResumenes();
    }

    private void OnMappingEdited()
    {
        _plan = null;
        RefreshPreview();
        ActualizarResumenes();
    }

    // ============================================================
    // Opciones
    // ============================================================

    private void OnOptionChanged(object sender, EventArgs e)
    {
        if (_loadingUi) return;
        _plan = null;
        UpdateUnitsInfo();
        RefreshPreview();
        ActualizarResumenes();
    }

    private void UpdateUnitsInfo()
    {
        if (UnitsInfo is null || _drawing is null || _drawing.Inserts.Count == 0) return;
        double ft = ((UnitsCombo.SelectedItem as UnitOption)?.Value ?? _drawing.SuggestedUnits).ToFeet() * 0.3048;
        double w = (_drawing.Inserts.Max(i => i.X) - _drawing.Inserts.Min(i => i.X)) * ft;
        double h = (_drawing.Inserts.Max(i => i.Y) - _drawing.Inserts.Min(i => i.Y)) * ft;
        string declared = _drawing.DeclaredUnits is { } du
            ? $"El DWG declara {du.Label().ToLowerInvariant()}."
            : $"El DWG no declara unidades; por su tamaño se proponen {_drawing.SuggestedUnits.Label().ToLowerInvariant()}.";
        UnitsInfo.Text = $"{declared} Las luminarias ocupan {w.ToString("0.#", Es)} × {h.ToString("0.#", Es)} m: si no cuadra con el edificio, cambia las unidades.";
    }

    private async void OnAlignClick(object sender, RoutedEventArgs e)
    {
        if (_drawing is null || !UiToConfig()) return;
        var dibujo = _drawing;
        if (!Empezar("Alineando con la arquitectura…")) return;

        DialuxAlignment? a;
        try
        {
            var (ok, valor) = await EnDocumentoAsync(uidoc => _service.AutoAlign(uidoc.Document, dibujo, _config));
            if (!ok) return;
            a = valor;
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo alinear.";
            ShowFriendlyError("No se pudo alinear.", ex);
            return;
        }
        finally { Terminar(); }

        if (a is null) return;
        AlignInfo.Text = a.Message;
        if (!a.Success)
        {
            StatusText.Text = a.Message;
            return;
        }

        // El encaje se expresa respecto al origen interno.
        _config.Origin = DialuxOrigin.InternalOrigin;
        _config.OffsetXMm = a.OffsetXMm;
        _config.OffsetYMm = a.OffsetYMm;
        _config.RotationDeg = a.RotationDeg;
        ConfigToUi();
        UiToConfig();
        _plan = null;
        RefreshPreview();
        StatusText.Text =
            $"{a.Message} Desfase {(a.OffsetXMm / 1000).ToString("0.###", Es)} ; {(a.OffsetYMm / 1000).ToString("0.###", Es)} m, " +
            $"giro {a.RotationDeg.ToString("0.##", Es)}°. Pulsa «Auditar»: las luminarias ya colocadas saldrán como «Movida» y «Sincronizar» las recoloca.";
    }

    private void OnLayerToggled(object sender, RoutedEventArgs e)
    {
        _plan = null;
        RefreshPreview();
        ActualizarResumenes();
    }

    private void OnAllLayersClick(object sender, RoutedEventArgs e) { foreach (var l in _layers) l.IsIncluded = true; RefreshPreview(); ActualizarResumenes(); }
    private void OnNoLayersClick(object sender, RoutedEventArgs e) { foreach (var l in _layers) l.IsIncluded = false; RefreshPreview(); ActualizarResumenes(); }

    private async void OnAllCategoriesChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _docKey is null) return;
        bool todas = AllCategoriesCheck.IsChecked == true;
        if (!Empezar("Leyendo los tipos de familia…")) return;
        try
        {
            var (ok, tipos) = await EnDocumentoAsync(uidoc => _service.GetFamilyTypes(uidoc.Document, todas));
            if (ok && tipos is not null)
            {
                MostrarTipos(tipos);
                StatusText.Text = TypesInfo.Text;
            }
        }
        catch (Exception ex) { ShowFriendlyError("No se pudieron leer los tipos de familia.", ex); }
        finally { Terminar(); }
    }

    private void OnBlockFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        BuildDrawingRows();
    }

    // ============================================================
    // Asociacion bloque → tipo
    // ============================================================

    private async void OnSuggestClick(object sender, RoutedEventArgs e)
    {
        if (_drawing is null) return;
        bool todas = AllCategoriesCheck.IsChecked == true;
        if (!Empezar("Buscando coincidencias por nombre…")) return;
        try
        {
            var (ok, types) = await EnDocumentoAsync(uidoc => _service.GetFamilyTypes(uidoc.Document, todas));
            if (!ok || types is null) return;
            int n = _service.SuggestMappings(_drawing, _config, types);
            foreach (var r in _mappingRows) r.Refresh();
            _plan = null;
            RefreshPreview();
            ActualizarResumenes();
            StatusText.Text = n == 0
                ? "No se ha encontrado ninguna coincidencia por nombre: asigna los tipos a mano."
                : $"Se han asignado {n} bloque(s) por coincidencia de nombre. Revísalos antes de crear.";
        }
        catch (Exception ex) { ShowFriendlyError("No se pudieron sugerir los tipos.", ex); }
        finally { Terminar(); }
    }

    private void OnAssignSelectedClick(object sender, RoutedEventArgs e)
    {
        // El modelo es el primer bloque seleccionado; los demas, los añadidos con Ctrl/Mayús.
        var seleccion = MappingList.SelectedItems.OfType<MappingRow>().ToList();
        if (seleccion.Count == 0)
        {
            StatusText.Text = "Haz clic en el bloque que quieres copiar y selecciona los demás con Ctrl/Mayús.";
            return;
        }
        var src = seleccion[0];
        int n = 0;
        foreach (var r in seleccion.Skip(1))
        {
            r.CopyFrom(src);
            n++;
        }
        StatusText.Text = n == 0 ? "Selecciona más bloques (Ctrl/Mayús + clic) para copiarles el primero." : $"Copiado «{src.SelectedType?.Label}» a {n} bloque(s).";
        OnMappingEdited();
    }

    // ============================================================
    // Simular / auditar
    // ============================================================

    private async void OnSimulateClick(object sender, RoutedEventArgs e)
    {
        if (!Empezar("Simulando…")) return;
        try
        {
            if (!await RunPlanAsync()) return;
            var p = _plan!;
            int existing = p.Rows.Count(r => r.ExistingId is not null);
            int placeable = p.Rows.Count(r => r.Source is not null && r.Status != DialuxRowStatus.Error);
            StatusText.Text =
                $"SIMULACIÓN · Crear colocaría {p.Count(DialuxRowStatus.New)} luminaria(s) nueva(s) " +
                $"({existing} ya colocadas de este DWG). Rehacer borraría {existing} y colocaría {placeable}. " +
                (p.Count(DialuxRowStatus.Error) > 0 ? $"{p.Count(DialuxRowStatus.Error)} con error. " : string.Empty) +
                "No se ha modificado el modelo.";
            StatusFilterCombo.SelectedIndex = 0;
            Mostrar(SeccionResultados);
        }
        finally { Terminar(); }
    }

    private async void OnAuditClick(object sender, RoutedEventArgs e)
    {
        if (!Empezar("Auditando…")) return;
        try
        {
            if (!await RunPlanAsync()) return;
            StatusText.Text = "AUDITORÍA · " + (_plan!.Rows.Count == 0 ? "nada que comparar." : _plan.ToSummary()) +
                              (_plan.Rows.All(r => r.Status == DialuxRowStatus.Ok) && _plan.Rows.Count > 0 ? "  Todo coincide con el DWG." : string.Empty);
            StatusFilterCombo.SelectedIndex = 1;
            Mostrar(SeccionResultados);
        }
        finally { Terminar(); }
    }

    private async Task<bool> RunPlanAsync()
    {
        if (_drawing is null || !UiToConfig()) return false;
        var dibujo = _drawing;
        try
        {
            var (ok, plan) = await EnDocumentoAsync(uidoc => _service.BuildPlan(uidoc.Document, dibujo, _config));
            if (!ok || plan is null) return false;
            _plan = plan;
        }
        catch (Exception ex)
        {
            ShowFriendlyError("No se pudo calcular el plan.", ex);
            return false;
        }

        _planRows.Clear();
        _planRows.AddRange(_plan.Rows.Select(r => new PlanRowView(r)));
        ApplyStatusFilter();
        PlanWarnings.Text = string.Join("  ", _plan.Warnings);
        RefreshPreview();
        ActualizarResumenes();
        return true;
    }

    private void OnStatusFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyStatusFilter();

    private void ApplyStatusFilter()
    {
        if (PlanList is null) return;
        var f = StatusFilterCombo.SelectedItem as StatusFilter ?? StatusFilter.All[0];
        PlanList.ItemsSource = _planRows.Where(r => f.Accepts(r.Row.Status)).ToList();
    }

    private async void OnSelectInRevitClick(object sender, RoutedEventArgs e)
    {
        var rows = PlanList.SelectedItems.Count > 0
            ? PlanList.SelectedItems.OfType<PlanRowView>()
            : (PlanList.ItemsSource as IEnumerable<PlanRowView>) ?? Enumerable.Empty<PlanRowView>();
        var ids = rows.Where(r => r.Row.ExistingId is not null).Select(r => r.Row.ExistingId!).Distinct().ToList();
        if (ids.Count == 0)
        {
            StatusText.Text = "Las filas elegidas no tienen luminarias colocadas en el modelo.";
            return;
        }

        if (!Empezar("Seleccionando en Revit…")) return;
        try
        {
            var (ok, n) = await EnDocumentoAsync(uidoc =>
            {
                var validos = ids.Where(id => uidoc.Document.GetElement(id) is not null).ToList();
                if (validos.Count == 0) return 0;
                uidoc.Selection.SetElementIds(validos);
                try { uidoc.ShowElements(validos); } catch { /* no critico */ }
                return validos.Count;
            });
            if (ok)
                StatusText.Text = n == 0
                    ? "Las luminarias de esas filas ya no están en el modelo: vuelve a auditar."
                    : $"{n} luminaria(s) seleccionadas en Revit.";
        }
        catch (Exception ex) { ShowFriendlyError("No se pudo seleccionar en Revit.", ex); }
        finally { Terminar(); }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (_planRows.Count == 0) { StatusText.Text = "Pulsa «Simular» o «Auditar» antes de exportar."; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar auditoría DIALux → Revit",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"Auditoria_DIALux_{_drawing?.SourceKey}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (Dialogo(dlg) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Estado;Bloque;Handle;Tipo destino;Tipo actual;X (m);Y (m);Altura (m);Giro (°);Delta (mm);Detalle");
        foreach (var r in _planRows)
            sb.AppendLine(string.Join(";", new[]
            {
                r.StatusLabel, r.Block, r.Handle, r.TargetType, r.Row.ExistingType, r.X, r.Y, r.H, r.Rot,
                r.Row.DeltaMm.ToString("0", Es), r.Detail,
            }.Select(Csv)));
        try
        {
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusText.Text = $"Exportado: {dlg.FileName}";
        }
        catch (Exception ex) { ShowFriendlyError("No se pudo guardar el CSV.", ex); }

        static string Csv(string s) => s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // ============================================================
    // Crear / rehacer / sincronizar / borrar sobrantes
    // ============================================================

    private void OnCreateClick(object sender, RoutedEventArgs e) => Execute(DialuxAction.Create);
    private void OnRedoClick(object sender, RoutedEventArgs e) => Execute(DialuxAction.Redo);
    private void OnSyncClick(object sender, RoutedEventArgs e) => Execute(DialuxAction.Sync);
    private void OnDeleteExtraClick(object sender, RoutedEventArgs e) => Execute(DialuxAction.DeleteExtra);

    private async void Execute(DialuxAction action)
    {
        if (!Empezar("Calculando…")) return;
        try
        {
            // Equivale a "Ejecutar = True" de la rutina: siempre se simula y se confirma antes de tocar el modelo.
            if (!await RunPlanAsync()) return;
            var p = _plan!;
            int existing = p.Rows.Count(r => r.ExistingId is not null);
            int placeable = p.Rows.Count(r => r.Source is not null && r.Status != DialuxRowStatus.Error);
            int toUpdate = p.Count(DialuxRowStatus.Moved) + p.Count(DialuxRowStatus.WrongType);

            string question = action switch
            {
                DialuxAction.Create => $"Se colocarán {p.Count(DialuxRowStatus.New)} luminaria(s) nueva(s).",
                DialuxAction.Redo => $"Se borrarán {existing} luminaria(s) importadas de «{_drawing!.SourceKey}» y se colocarán {placeable}.",
                DialuxAction.Sync => $"Se colocarán {p.Count(DialuxRowStatus.New)} nueva(s) y se recolocarán / cambiarán de tipo {toUpdate}.",
                _ => $"Se borrarán {p.Count(DialuxRowStatus.Extra)} luminaria(s) sobrantes.",
            };
            bool nothing = action switch
            {
                DialuxAction.Create => p.Count(DialuxRowStatus.New) == 0,
                DialuxAction.Redo => existing == 0 && placeable == 0,
                DialuxAction.Sync => p.Count(DialuxRowStatus.New) + toUpdate == 0,
                _ => p.Count(DialuxRowStatus.Extra) == 0,
            };
            if (nothing)
            {
                StatusText.Text = "No hay nada que hacer: " + (p.Rows.Count == 0 ? "ningún bloque tiene tipo asignado." : p.ToSummary());
                Mostrar(SeccionResultados);
                return;
            }

            Mouse.OverrideCursor = null;
            if (Mensaje(question + "\n\nSe puede deshacer con un solo Ctrl+Z. ¿Continuar?", Caption,
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                StatusText.Text = "Cancelado: no se ha modificado el modelo.";
                return;
            }
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            StatusText.Text = "Trabajando en Revit…";

            var dibujo = _drawing!;
            DialuxResult? result;
            try
            {
                var (ok, valor) = await EnDocumentoAsync(uidoc => _service.Apply(uidoc.Document, dibujo, _config, action));
                if (!ok) return;
                result = valor;
            }
            catch (Exception ex)
            {
                StatusText.Text = "La operación falló y se deshizo.";
                ShowFriendlyError("La operación falló y se deshizo.", ex);
                return;
            }
            if (result is null) return;

            Mouse.OverrideCursor = null;
            if (!result.Success)
            {
                StatusText.Text = "Error: " + result.ErrorMessage;
                Mensaje(result.ErrorMessage, Caption + " · error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var body = result.ToSummary();
            if (result.Warnings.Count > 0)
                body += "\n\nAvisos:\n  · " + string.Join("\n  · ", result.Warnings.Take(15)) +
                        (result.Warnings.Count > 15 ? $"\n  … y {result.Warnings.Count - 15} más (ver log)." : string.Empty);
            Mensaje(body, result.Warnings.Count > 0 ? Caption + " · con avisos" : Caption + " · OK",
                MessageBoxButton.OK, result.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            // Auditoria del resultado.
            if (await RunPlanAsync())
                StatusText.Text = result.ToSummary() + "  ·  Estado actual: " + _plan!.ToSummary();
            StatusFilterCombo.SelectedIndex = 0;
            Mostrar(SeccionResultados);
        }
        finally { Terminar(); }
    }

    // ============================================================
    // Config .json
    // ============================================================

    private void OnLoadConfigClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Configuración DIALux → Revit",
            Filter = "Configuración (*.json)|*.json",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (Dialogo(dlg) != true) return;
        try
        {
            _config = JsonSerializer.Deserialize<DialuxConfig>(File.ReadAllText(dlg.FileName), Json) ?? new DialuxConfig();
        }
        catch (Exception ex)
        {
            ShowFriendlyError("El archivo no es una configuración válida.", ex);
            return;
        }
        _plan = null;
        ConfigToUi();
        UiToConfig();
        StatusText.Text = $"Configuración cargada: {_config.Mappings.Count(m => m.IsMapped)} asociaciones bloque → tipo.";
    }

    private void OnSaveConfigClick(object sender, RoutedEventArgs e)
    {
        if (!UiToConfig()) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar configuración DIALux → Revit",
            Filter = "Configuración (*.json)|*.json",
            FileName = "DIALux_a_Revit.json",
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : string.Empty,
        };
        if (Dialogo(dlg) != true) return;
        try
        {
            // Solo las asociaciones utiles: las vacias no aportan al reutilizar la config.
            var copy = JsonSerializer.Deserialize<DialuxConfig>(JsonSerializer.Serialize(_config, Json), Json)!;
            copy.Mappings.RemoveAll(m => !m.IsMapped);
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(copy, Json));
            StatusText.Text = $"Configuración guardada: {dlg.FileName}";
        }
        catch (Exception ex) { ShowFriendlyError("No se pudo guardar la configuración.", ex); }
    }

    // ============================================================
    // Vista previa
    // ============================================================

    private void RefreshPreview()
    {
        if (Preview is null || _drawing is null) return;
        var layers = new HashSet<string>(_layers.Where(l => l.IsIncluded).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        var points = new List<DialuxPreview.Marker>();

        if (_plan is not null)
        {
            foreach (var r in _plan.Rows.Where(r => r.Source is not null))
                points.Add(new DialuxPreview.Marker(r.Source!.X, r.Source.Y, r.Source.Rotation, DialuxPreview.ColorFor(r.Status)));
            PreviewLegend.Text = "Verde: OK · Azul: nueva · Naranja: movida / tipo distinto · Rojo: error · (posiciones del DWG)";
        }
        else
        {
            foreach (var i in _drawing.Inserts.Where(i => layers.Contains(i.Layer)))
            {
                bool mapped = _config.FindMapping(i.BlockName) is { IsMapped: true };
                if (!mapped && i.Depth > 0) continue;
                points.Add(new DialuxPreview.Marker(i.X, i.Y, i.Rotation, mapped ? DialuxPreview.Mapped : DialuxPreview.Unmapped));
            }
            PreviewLegend.Text = "Azul: bloque con tipo asignado · Gris: bloque ignorado";
        }
        Preview.SetMarkers(points);
    }

    // ============================================================
    // Dialogos (en el panel puede no haber ventana propietaria)
    // ============================================================

    private MessageBoxResult Mensaje(string texto, string titulo, MessageBoxButton botones, MessageBoxImage icono)
    {
        var dueno = Window.GetWindow(this);
        return dueno is not null
            ? MessageBox.Show(dueno, texto, titulo, botones, icono)
            : MessageBox.Show(texto, titulo, botones, icono);
    }

    private bool? Dialogo(Microsoft.Win32.CommonDialog dlg)
    {
        var dueno = Window.GetWindow(this);
        return dueno is not null ? dlg.ShowDialog(dueno) : dlg.ShowDialog();
    }

    private void ShowFriendlyError(string title, Exception ex)
    {
        Mouse.OverrideCursor = null;
        try
        {
            var dlg = FriendlyErrorDialog.ForException(ex);
            var dueno = Window.GetWindow(this);
            if (dueno is not null) dlg.Owner = dueno;
            dlg.ShowDialog();
        }
        catch
        {
            Mensaje($"{title}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ============================================================
    // Filas y ajustes
    // ============================================================

    public sealed record UnitOption(LegendUnits Value, string Label);
    public sealed record OriginOption(DialuxOrigin Value, string Label);
    public sealed record HeightOption(DialuxHeightSource Value, string Label);

    /// <summary>Elemento de la lista de tipos; <see cref="None"/> = bloque ignorado.</summary>
    public sealed class TypeOption
    {
        public static readonly TypeOption None = new(string.Empty, string.Empty, "— ignorar —");

        public TypeOption(string family, string type, string label)
        {
            FamilyName = family;
            TypeName = type;
            Label = label;
        }

        public string FamilyName { get; }
        public string TypeName { get; }
        public string Label { get; }

        public bool Matches(string family, string type) =>
            string.Equals(FamilyName, family, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TypeName, type, StringComparison.OrdinalIgnoreCase);

        public override string ToString() => Label;
    }

    public sealed class StatusFilter
    {
        public static readonly IReadOnlyList<StatusFilter> All = new[]
        {
            new StatusFilter("Todas", _ => true),
            new StatusFilter("Pendientes (≠ OK)", s => s != DialuxRowStatus.Ok),
        }.Concat(Enum.GetValues(typeof(DialuxRowStatus)).Cast<DialuxRowStatus>()
            .Select(s => new StatusFilter(s.Label(), x => x == s))).ToList();

        private readonly Func<DialuxRowStatus, bool> _accepts;
        private StatusFilter(string label, Func<DialuxRowStatus, bool> accepts) { Label = label; _accepts = accepts; }
        public string Label { get; }
        public bool Accepts(DialuxRowStatus s) => _accepts(s);
    }

    public sealed class LayerRow : INotifyPropertyChanged
    {
        public LayerRow(string name, int count, bool included)
        {
            Name = name;
            CountText = $"({count})";
            _isIncluded = included;
        }

        public string Name { get; }
        public string CountText { get; }

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

    /// <summary>Tarjeta de asociacion: un bloque del DWG y su <see cref="DialuxMapping"/>.</summary>
    public sealed class MappingRow : INotifyPropertyChanged
    {
        private readonly DialuxBlockInfo _block;
        private readonly DialuxMapping _m;
        private readonly Func<DialuxMapping, TypeOption> _optionFor;
        private readonly Action _changed;

        public MappingRow(DialuxBlockInfo block, DialuxMapping mapping, Func<DialuxMapping, TypeOption> optionFor, Action changed)
        {
            _block = block;
            _m = mapping;
            _optionFor = optionFor;
            _changed = changed;
        }

        public string BlockName => _block.Name + (_block.AnyNested ? "  (anidado)" : string.Empty);
        public IReadOnlyList<DialuxSegment> Symbol => _block.Symbol;
        public int Count => _block.Count;
        public string Layers => string.Join(", ", _block.Layers);
        public string Attributes => string.IsNullOrEmpty(_block.AttributesSample) ? "Sin atributos." : "Atributos: " + _block.AttributesSample;

        public TypeOption SelectedType
        {
            get => _optionFor(_m);
            set
            {
                value ??= TypeOption.None;
                if (value.Matches(_m.FamilyName, _m.TypeName)) return;
                _m.FamilyName = value.FamilyName;
                _m.TypeName = value.TypeName;
                Notify(nameof(SelectedType));
                _changed();
            }
        }

        public DialuxHeightSource HeightSource
        {
            get => _m.HeightSource;
            set { _m.HeightSource = value; Notify(nameof(HeightSource)); _changed(); }
        }

        public double HeightMm
        {
            get => _m.HeightMm;
            set { _m.HeightMm = value; Notify(nameof(HeightMm)); _changed(); }
        }

        public double ExtraRotationDeg
        {
            get => _m.ExtraRotationDeg;
            set { _m.ExtraRotationDeg = value; Notify(nameof(ExtraRotationDeg)); _changed(); }
        }

        public void CopyFrom(MappingRow src)
        {
            _m.FamilyName = src._m.FamilyName;
            _m.TypeName = src._m.TypeName;
            _m.HeightSource = src._m.HeightSource;
            _m.HeightMm = src._m.HeightMm;
            _m.ExtraRotationDeg = src._m.ExtraRotationDeg;
            Refresh();
        }

        public void Refresh()
        {
            Notify(nameof(SelectedType));
            Notify(nameof(HeightSource));
            Notify(nameof(HeightMm));
            Notify(nameof(ExtraRotationDeg));
        }

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Tarjeta de simulacion/auditoria (valores ya formateados).</summary>
    public sealed class PlanRowView
    {
        public PlanRowView(DialuxPlanRow row)
        {
            Row = row;
            var t = row.Target;
            X = t is null ? string.Empty : (t.X * 0.3048).ToString("0.00", Es);
            Y = t is null ? string.Empty : (t.Y * 0.3048).ToString("0.00", Es);
            H = t is null ? string.Empty : (t.Z * 0.3048).ToString("0.00", Es);
            Rot = t is null ? string.Empty : (row.TargetRotation * 180 / Math.PI).ToString("0.#", Es);
        }

        public DialuxPlanRow Row { get; }
        public string StatusLabel => Row.Status.Label();
        public string Block => Row.BlockName;
        public string Handle => Row.Source?.Handle ?? string.Empty;
        public string TargetType => Row.Mapping?.TargetLabel ?? string.Empty;
        public string X { get; }
        public string Y { get; }
        public string H { get; }
        public string Rot { get; }
        public string Detail => Row.Detail ?? string.Empty;

        /// <summary>Posicion en una linea para la tarjeta estrecha.</summary>
        public string Coordenadas => Row.Target is null
            ? (string.IsNullOrEmpty(Handle) ? string.Empty : "Handle " + Handle)
            : $"X {X} · Y {Y} · h {H} m · giro {Rot}°" + (string.IsNullOrEmpty(Handle) ? string.Empty : " · " + Handle);

        public string Tone => Row.Status switch
        {
            DialuxRowStatus.Ok => "ok",
            DialuxRowStatus.Error => "error",
            DialuxRowStatus.New => "new",
            _ => "warn",
        };
    }

    private sealed class Settings
    {
        public string LastFolder { get; set; } = string.Empty;
        public DialuxConfig? Config { get; set; }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "e3g", "DiTools", "DialuxImport", "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
            }
            catch { /* ajustes corruptos: valores por defecto */ }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch { /* no critico */ }
        }
    }
}
