using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Parameters;
using E3G.BimTools.DiTools.Core.Models.ViewSheets;
using E3G.BimTools.DiTools.Core.Services.ViewSheets;
using E3G.BimTools.DiTools.UI.Controls;
// Alias para evitar ambigüedad con tipos homónimos de Autodesk.Revit.DB.
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfLine = System.Windows.Shapes.Line;
using WpfColor = System.Windows.Media.Color;
using IoPath = System.IO.Path;

namespace E3G.BimTools.DiTools.UI.Views.ViewSheetManager;

/// <summary>
/// Code-behind del módulo "View &amp; Sheet Manager".  Flujo: configurar →
/// previsualizar (dry-run, árbol con checks + mock de composición) → generar
/// los marcados → resultados con thumbnail real.  Toda la lógica de Revit vive
/// en <see cref="IViewSheetGenerator"/> (Core).
/// </summary>
public partial class ViewSheetManagerWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly IViewSheetGenerator _generator;
    private readonly InstalacionConfigRepository _repository;
    private readonly IParaManagerService _para;

    private List<NivelItem> _niveles = new();
    private List<SectorItem> _sectores = new();
    private ObservableCollection<InstalacionRow> _instalaciones = new();
    private List<ElementChoice> _titleBlocks = new();
    private List<ElementChoice> _viewFamilyTypes = new();
    private List<ElementChoice> _viewportTypes = new();
    private readonly ObservableCollection<ClasifRow> _clasifVistas = new();
    private readonly ObservableCollection<ClasifRow> _clasifPlanos = new();
    private List<ViewTemplateItem> _plantillas = new();

    private GeneracionPlan? _plan;
    private readonly List<LeafVm> _leaves = new();

    // Preview de composición: medición real cacheada + N viewports editables.
    private readonly Dictionary<string, ComposicionMedida> _composicionCache = new();
    private PlanItem? _itemActual;
    private readonly List<VpVisual> _vpVisuals = new();

    // Estado del arrastre.
    private bool _arrastrando;
    private VpVisual? _vpArrastrado;
    private double _mockOx, _mockOy, _mockSw, _mockSh, _cajPxW; // hoja dibujada (px)
    private int _gridDivisions = 20;                            // nº de celdas de la rejilla por eje
    private double GridStep => 1.0 / _gridDivisions;            // tamaño de celda (fracción de la hoja)

    /// <summary>Un rectángulo del mock asociado a un viewport del plan.</summary>
    private sealed class VpVisual
    {
        public PlanViewport Spec = null!;
        public ComposicionMedida Medida;
        public WpfRectangle Rect = null!;
        public System.Windows.Controls.TextBlock? Label;
        public WpfColor Color;
        public double PxW, PxH;
    }

    // Paleta para distinguir varios viewports en una misma hoja.
    private static readonly WpfColor[] PaletaVp =
    {
        WpfColor.FromRgb(0x2D, 0x7A, 0x5F), WpfColor.FromRgb(0x37, 0x8A, 0xDD),
        WpfColor.FromRgb(0xC2, 0x6A, 0x1F), WpfColor.FromRgb(0x7F, 0x53, 0xB3),
        WpfColor.FromRgb(0x1A, 0xA3, 0x9C), WpfColor.FromRgb(0xB0, 0x3A, 0x4A),
    };

    public ViewSheetManagerWindow(
        UIDocument uidoc, IViewSheetGenerator generator, InstalacionConfigRepository repository,
        IParaManagerService para)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _para = para ?? throw new ArgumentNullException(nameof(para));

        // Handlers de arrastre del viewport, enganchados una sola vez al canvas
        // (la captura del ratón es sobre el canvas, que nunca se recrea).
        MockCanvas.MouseMove += OnVpDragMove;
        MockCanvas.MouseLeftButtonUp += OnVpDragEnd;

        Loaded += (_, __) => CargarDocumento();
    }

    // =========================================================================
    // Carga inicial
    // =========================================================================

    private void CargarDocumento()
    {
        try
        {
            var doc = _uidoc.Document;

            _niveles = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new NivelItem(l.Id, l.Name))
                .ToList();
            NivelesList.ItemsSource = _niveles;

            // Scope Boxes (sectores).
            _sectores = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => new SectorItem(e.Id, e.Name))
                .ToList();
            SectoresList.ItemsSource = _sectores;

            _plantillas = new List<ViewTemplateItem> { ViewTemplateItem.Ninguna };
            _plantillas.AddRange(new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new ViewTemplateItem(v.Id.Value, v.Name)));

            _titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new ElementChoice(s.Id.Value, $"{s.FamilyName} : {s.Name}"))
                .ToList();
            TitleBlockCombo.ItemsSource = _titleBlocks;
            if (_titleBlocks.Count > 0) TitleBlockCombo.SelectedIndex = 0;

            _viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(v => v.ViewFamily == ViewFamily.FloorPlan)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new ElementChoice(v.Id.Value, v.Name))
                .ToList();
            ViewFamilyTypeCombo.ItemsSource = _viewFamilyTypes;
            if (_viewFamilyTypes.Count > 0) ViewFamilyTypeCombo.SelectedIndex = 0;

            // Tipos de viewport (categoría OST_Viewports) + opción por defecto.
            _viewportTypes = new List<ElementChoice> { new(-1, "(por defecto)") };
            _viewportTypes.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Viewports)
                .WhereElementIsElementType().Cast<ElementType>()
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new ElementChoice(t.Id.Value, t.Name)));
            ViewportTypeCombo.ItemsSource = _viewportTypes;
            ViewportTypeCombo.SelectedIndex = 0;

            _instalaciones = new ObservableCollection<InstalacionRow>(
                _repository.Load().Select(c => new InstalacionRow(c, _plantillas)));
            InstalacionesList.ItemsSource = _instalaciones;

            ClasifPlanosList.ItemsSource = _clasifPlanos;
            ClasifVistasList.ItemsSource = _clasifVistas;

            // Recuperar preferencias de sesiones anteriores.
            ApplyPrefs(_repository.LoadPrefs());

            StatusText.Text = $"{_niveles.Count} niveles · {_sectores.Count} scope boxes · " +
                              $"{_titleBlocks.Count} title blocks · {_plantillas.Count - 1} plantillas FloorPlan.";
        }
        catch (Exception ex)
        {
            TaskDialog.Show("View & Sheet Manager", "Error al cargar el documento:\n" + ex.Message);
        }
    }

    // =========================================================================
    // Niveles
    // =========================================================================

    private void OnSelectAllLevelsClick(object sender, RoutedEventArgs e)
    {
        foreach (var n in _niveles) n.IsSelected = true;
        NivelesList.Items.Refresh();
    }

    private void OnSelectNoneLevelsClick(object sender, RoutedEventArgs e)
    {
        foreach (var n in _niveles) n.IsSelected = false;
        NivelesList.Items.Refresh();
    }

    // =========================================================================
    // Instalaciones: añadir / quitar / guardar
    // =========================================================================

    private void OnAddInstalacionClick(object sender, RoutedEventArgs e)
    {
        _instalaciones.Add(new InstalacionRow(
            new InstalacionConfig { Nombre = "Nuevo grupo", PrefijoHoja = "XXX" }, _plantillas));
    }

    private void OnRemoveInstalacionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InstalacionRow row })
            _instalaciones.Remove(row);
    }

    private void OnSaveConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var lista = _instalaciones.Select(r => r.ToConfig()).ToList();
            _repository.Save(lista);
            _repository.SavePrefs(GatherPrefs());

            string ruta = IoPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "e3g", "DiTools", "ViewSheetManager");

            StatusText.Text = $"✔ {lista.Count} instalaciones + ajustes globales guardados.";
            TaskDialog.Show("View & Sheet Manager",
                $"Configuración y ajustes guardados ({lista.Count} instalaciones).\n\n{ruta}");
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo guardar la configuración: " + ex.Message;
            TaskDialog.Show("View & Sheet Manager", "No se pudo guardar la configuración:\n\n" + ex.Message);
        }
    }

    // =========================================================================
    // Previsualización (dry-run → árbol)
    // =========================================================================

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        var request = BuildRequest(out string? error);
        if (request is null) { PreviewInfo.Text = error; return; }

        try
        {
            _composicionCache.Clear(); // la config pudo cambiar desde el último preview
            _plan = _generator.PrepararPlan(_uidoc.Document, request);
            ConstruirArbol(_plan);
            int sectores = request.SectorIds.Count;
            PreviewInfo.Text = sectores > 0
                ? $"{_plan.Items.Count} planos previstos ({sectores} sectores). Marca/desmarca y pulsa Generar."
                : $"{_plan.Items.Count} planos previstos. Marca/desmarca y pulsa Generar.";
        }
        catch (Exception ex)
        {
            PreviewInfo.Text = "Error al previsualizar: " + ex.Message;
        }
    }

    private void ConstruirArbol(GeneracionPlan plan)
    {
        _leaves.Clear();
        var instGroups = new List<InstGroup>();

        foreach (var instGrupo in plan.Items.GroupBy(i => i.Instalacion))
        {
            var inst = new InstGroup
            {
                Header = $"{instGrupo.Key} ({instGrupo.First().Prefijo})",
                ColorHex = instGrupo.First().ColorHex,
            };
            foreach (var nivelGrupo in instGrupo.GroupBy(i => i.Nivel))
            {
                var nivel = new NivelGroup { Header = nivelGrupo.Key };
                foreach (var item in nivelGrupo)
                {
                    var leaf = new LeafVm(item);
                    _leaves.Add(leaf);
                    nivel.Leaves.Add(leaf);
                }
                inst.Niveles.Add(nivel);
            }
            instGroups.Add(inst);
        }

        PreviewTree.ItemsSource = instGroups;
    }

    private void OnCheckAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var l in _leaves) l.IsChecked = true;
    }

    private void OnUncheckAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var l in _leaves) l.IsChecked = false;
    }

    // =========================================================================
    // Mock de composición de hoja
    // =========================================================================

    private void OnPreviewTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not LeafVm leaf) return;
        if (SoloVistasCheck.IsChecked == true)
        {
            MockCanvas.Children.Clear();
            MockInfo.Text = "Modo «solo vistas»: no se generan hojas, no hay composición que previsualizar.";
            _itemActual = null;
            return;
        }
        MostrarComposicion(leaf.Item);
    }

    /// <summary>Mide cada viewport de la hoja (cacheado) y dibuja todos los recuadros.</summary>
    private void MostrarComposicion(PlanItem item)
    {
        _itemActual = item;
        _vpVisuals.Clear();

        long tbId = (TitleBlockCombo.SelectedItem as ElementChoice)?.Id ?? -1;
        long vpTypeId = _plan?.Request.ViewportTypeId ?? -1;

        foreach (var spec in item.Viewports)
        {
            string key = $"{tbId}|{vpTypeId}|{item.ViewFamilyTypeId}|{item.PlantillaVistaId}|{item.Escala}|{spec.SectorId}";
            if (!_composicionCache.TryGetValue(key, out var medida) && _plan is not null)
            {
                medida = _generator.MedirComposicion(_uidoc.Document, _plan, item, spec);
                _composicionCache[key] = medida;
            }
            _vpVisuals.Add(new VpVisual { Spec = spec, Medida = medida });
        }

        RedibujarMock();
    }

    /// <summary>Dibuja hoja + cajetín + (rejilla) + un recuadro por viewport.</summary>
    private void RedibujarMock()
    {
        MockCanvas.Children.Clear();
        if (_itemActual is null || _vpVisuals.Count == 0) return;

        double cw = MockCanvas.ActualWidth, ch = MockCanvas.ActualHeight;
        if (cw < 20 || ch < 20) return;

        var medidaHoja = _vpVisuals[0].Medida;
        double hojaW = medidaHoja.HojaMedida ? medidaHoja.HojaAnchoMm : 841;
        double hojaH = medidaHoja.HojaMedida ? medidaHoja.HojaAltoMm : 594;

        double margen = 14;
        double k = Math.Min((cw - 2 * margen) / hojaW, (ch - 2 * margen) / hojaH);
        double sw = hojaW * k, sh = hojaH * k;
        double ox = (cw - sw) / 2, oy = (ch - sh) / 2;
        _mockOx = ox; _mockOy = oy; _mockSw = sw; _mockSh = sh;

        var sheet = new WpfRectangle
        {
            Width = sw, Height = sh,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(0x33, 0x33, 0x33)), StrokeThickness = 1.5,
            Fill = Brushes.White,
        };
        Canvas.SetLeft(sheet, ox); Canvas.SetTop(sheet, oy);
        MockCanvas.Children.Add(sheet);

        _cajPxW = sw * 0.15;
        var caj = new WpfRectangle
        {
            Width = _cajPxW, Height = sh,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(0x99, 0x99, 0x99)), StrokeThickness = 1,
            Fill = new SolidColorBrush(WpfColor.FromRgb(0xF2, 0xF2, 0xF2)),
        };
        Canvas.SetLeft(caj, ox + sw - _cajPxW); Canvas.SetTop(caj, oy);
        MockCanvas.Children.Add(caj);

        GridInfo.Text = GridCheck.IsChecked == true ? $"rejilla {_gridDivisions}×{_gridDivisions}" : string.Empty;
        if (GridCheck.IsChecked == true)
        {
            var gridBrush = new SolidColorBrush(WpfColor.FromArgb(0x40, 0x55, 0x55, 0x55));
            for (double f = GridStep; f < 0.999; f += GridStep)
            {
                MockCanvas.Children.Add(new WpfLine { X1 = ox + f * sw, Y1 = oy, X2 = ox + f * sw, Y2 = oy + sh, Stroke = gridBrush, StrokeThickness = 0.5 });
                MockCanvas.Children.Add(new WpfLine { X1 = ox, Y1 = oy + f * sh, X2 = ox + sw, Y2 = oy + f * sh, Stroke = gridBrush, StrokeThickness = 0.5 });
            }
        }

        int idx = 0;
        foreach (var vv in _vpVisuals)
        {
            vv.Color = PaletaVp[idx % PaletaVp.Length];
            idx++;

            bool medido = vv.Medida.ViewportMedido;
            double vpMmW = medido ? vv.Medida.VpAnchoMm : hojaW * 0.55;
            double vpMmH = medido ? vv.Medida.VpAltoMm : hojaH * 0.55;
            vv.PxW = Math.Max(4, vpMmW * k);
            vv.PxH = Math.Max(4, vpMmH * k);

            vv.Rect = new WpfRectangle
            {
                Width = vv.PxW, Height = vv.PxH, StrokeThickness = 1.8,
                Fill = new SolidColorBrush(WpfColor.FromArgb(0x2E, vv.Color.R, vv.Color.G, vv.Color.B)),
                Cursor = Cursors.SizeAll, Tag = vv,
            };
            vv.Rect.MouseLeftButtonDown += OnVpDragStart;
            MockCanvas.Children.Add(vv.Rect);

            // Etiqueta con el nombre del sector (o "Vista" si no hay), para distinguir solapados.
            vv.Label = new System.Windows.Controls.TextBlock
            {
                Text = vv.Spec.TieneSector ? vv.Spec.Sector : "Vista",
                Foreground = new SolidColorBrush(vv.Color),
                FontWeight = FontWeights.SemiBold, FontSize = 11,
                IsHitTestVisible = false,
            };
            MockCanvas.Children.Add(vv.Label);

            PosicionarRect(vv);
        }

        ActualizarMockInfo();
    }

    /// <summary>Coloca un recuadro (y su etiqueta) según la posición de su viewport y refresca el estado.</summary>
    private void PosicionarRect(VpVisual vv)
    {
        double cx = _mockOx + Clamp01(vv.Spec.FracX) * _mockSw;
        double cy = _mockOy + (1 - Clamp01(vv.Spec.FracY)) * _mockSh;
        double left = cx - vv.PxW / 2, top = cy - vv.PxH / 2;
        Canvas.SetLeft(vv.Rect, left);
        Canvas.SetTop(vv.Rect, top);
        if (vv.Label is not null)
        {
            Canvas.SetLeft(vv.Label, left + 3);
            Canvas.SetTop(vv.Label, top + 2);
        }

        bool dentro = left >= _mockOx - 0.5 && top >= _mockOy - 0.5 &&
                      left + vv.PxW <= _mockOx + _mockSw + 0.5 && top + vv.PxH <= _mockOy + _mockSh + 0.5;
        bool solapaCajetin = left + vv.PxW > _mockOx + _mockSw - _cajPxW;
        bool ok = dentro && !solapaCajetin;

        // Color base del viewport; rojo/naranja si se sale o solapa el cajetín.
        vv.Rect.Stroke = !dentro ? Brushes.OrangeRed : (solapaCajetin ? Brushes.DarkOrange : new SolidColorBrush(vv.Color));
        vv.Rect.StrokeDashArray = ok ? null : new DoubleCollection { 3, 2 };
    }

    private void ActualizarMockInfo()
    {
        if (_itemActual is null || _vpVisuals.Count == 0) return;
        var m0 = _vpVisuals[0].Medida;
        double hojaW = m0.HojaMedida ? m0.HojaAnchoMm : 841;
        double hojaH = m0.HojaMedida ? m0.HojaAltoMm : 594;

        if (_vpVisuals.Count == 1)
        {
            var vv = _vpVisuals[0];
            bool medido = vv.Medida.ViewportMedido;
            double w = medido ? vv.Medida.VpAnchoMm : hojaW * 0.55;
            double h = medido ? vv.Medida.VpAltoMm : hojaH * 0.55;
            string sectorTxt = vv.Spec.TieneSector ? $" · sector '{vv.Spec.Sector}'" : " · sin sector";
            MockInfo.Text = $"Hoja ≈ {hojaW:0}×{hojaH:0} mm · viewport ≈ {w:0}×{h:0} mm @ 1:{_itemActual.Escala}{sectorTxt}.  " +
                            $"Pos X {vv.Spec.FracX * 100:0}% · Y {vv.Spec.FracY * 100:0}%.  Arrastra el recuadro.";
        }
        else
        {
            MockInfo.Text = $"Hoja ≈ {hojaW:0}×{hojaH:0} mm · {_vpVisuals.Count} viewports (sectores) @ 1:{_itemActual.Escala}.  " +
                            "Arrastra cada recuadro; su posición se aplica a ese sector en TODAS las hojas.";
        }
    }

    private void OnGridToggle(object sender, RoutedEventArgs e) => RedibujarMock();
    private void OnGridPlus(object sender, RoutedEventArgs e) { _gridDivisions = Math.Min(48, _gridDivisions + 2); if (GridCheck.IsChecked != true) GridCheck.IsChecked = true; RedibujarMock(); }
    private void OnGridMinus(object sender, RoutedEventArgs e) { _gridDivisions = Math.Max(4, _gridDivisions - 2); if (GridCheck.IsChecked != true) GridCheck.IsChecked = true; RedibujarMock(); }

    // ── Arrastre de un viewport (captura sobre el canvas) ──
    private void OnVpDragStart(object sender, MouseButtonEventArgs e)
    {
        _vpArrastrado = (sender as FrameworkElement)?.Tag as VpVisual;
        if (_vpArrastrado is null) return;
        _arrastrando = true;
        MockCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnVpDragMove(object sender, MouseEventArgs e)
    {
        if (!_arrastrando || _vpArrastrado is null || _mockSw <= 0 || _mockSh <= 0) return;
        var p = e.GetPosition(MockCanvas);
        double fx = (p.X - _mockOx) / _mockSw;
        double fy = 1 - (p.Y - _mockOy) / _mockSh;
        if (GridCheck.IsChecked == true)
        {
            double halfW = (_vpArrastrado.PxW / _mockSw) / 2;
            double halfH = (_vpArrastrado.PxH / _mockSh) / 2;
            double umbral = GridStep * 0.45;
            fx = SnapEje(fx, halfW, GridStep, umbral);
            fy = SnapEje(fy, halfH, GridStep, umbral);
        }
        fx = Clamp01(fx); fy = Clamp01(fy);

        // La posición se aplica a este sector en TODAS las hojas del plan.
        long sectorId = _vpArrastrado.Spec.SectorId;
        if (_plan is not null)
            foreach (var it in _plan.Items)
                foreach (var sp in it.Viewports)
                    if (sp.SectorId == sectorId) { sp.FracX = fx; sp.FracY = fy; }

        PosicionarRect(_vpArrastrado);
        ActualizarMockInfo();
    }

    private static double SnapEje(double centro, double medio, double paso, double umbral)
    {
        double e1 = centro - medio, e2 = centro + medio;
        double d1 = Math.Round(e1 / paso) * paso - e1;
        double d2 = Math.Round(e2 / paso) * paso - e2;
        double mejor = Math.Abs(d1) <= Math.Abs(d2) ? d1 : d2;
        return Math.Abs(mejor) <= umbral ? centro + mejor : centro;
    }

    private void OnVpDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_arrastrando) return;
        _arrastrando = false;
        _vpArrastrado = null;
        MockCanvas.ReleaseMouseCapture();
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // =========================================================================
    // Generar
    // =========================================================================

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null)
        {
            StatusText.Text = "Pulsa primero «Previsualizar».";
            return;
        }
        if (_plan.TotalMarcados == 0)
        {
            StatusText.Text = "No hay planos marcados.";
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;

            // Clasificación en dos bloques (Planos / Vistas), con valores propios.
            bool rellPlanos = RellenarPlanosCheck.IsChecked == true;
            bool rellVistas = RellenarVistasCheck.IsChecked == true;
            _plan.Request.RellenarClasifPlanos = rellPlanos;
            _plan.Request.RellenarClasifVistas = rellVistas;
            _plan.Request.ClasificacionPlanos = rellPlanos ? BloqueADict(_clasifPlanos) : null;
            _plan.Request.ClasificacionVistas = rellVistas ? BloqueADict(_clasifVistas) : null;

            if (rellPlanos || rellVistas)
            {
                var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (rellPlanos) foreach (var k in BloqueADict(_clasifPlanos).Keys) nombres.Add(k);
                if (rellVistas) foreach (var k in BloqueADict(_clasifVistas).Keys) nombres.Add(k);
                if (nombres.Count > 0)
                {
                    var (_, errores) = AsegurarParametrosClasificacion(nombres.ToList());
                    if (errores.Count > 0)
                        StatusText.Text = "Aviso clasificación: " + string.Join("; ", errores);
                }
            }

            var resumen = _generator.Generar(_uidoc.Document, _plan);
            ResultadosGrid.ItemsSource = resumen.Resultados;
            StatusText.Text = resumen.ToSummary();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error durante la generación: " + ex.Message;
            TaskDialog.Show("View & Sheet Manager", "Error durante la generación:\n" + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // =========================================================================
    // Thumbnail real
    // =========================================================================

    private void OnResultSelected(object sender, SelectionChangedEventArgs e) { /* el thumbnail se pide con el botón */ }

    private void OnThumbnailClick(object sender, RoutedEventArgs e)
    {
        if (ResultadosGrid.SelectedItem is not ResultadoGeneracion r || r.HojaId <= 0)
        {
            StatusText.Text = "Selecciona una hoja generada en la tabla.";
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            string dir = IoPath.Combine(IoPath.GetTempPath(), "e3g_vsm_preview");
            string? png = _generator.ExportarHojaPng(_uidoc.Document, r.HojaId, dir);
            if (png is null || !File.Exists(png))
            {
                StatusText.Text = "No se pudo exportar la imagen de la hoja.";
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(png, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            ThumbnailImage.Source = bmp;
            StatusText.Text = $"Imagen de la hoja {r.NumeroHoja}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error al generar la imagen: " + ex.Message;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // =========================================================================
    // Parámetros de clasificación (Capítulo / Subcapítulo 1) — se crean vacíos
    // =========================================================================

    /// <summary>Construye los nombres de parámetros: "Capítulo" + N × "Subcapítulo i".</summary>
    private List<string> NombresClasificacion()
    {
        int n = int.TryParse((SubcapNivelesBox.Text ?? "").Trim(), out int v) ? v : 1;
        n = Math.Max(0, Math.Min(10, n));
        var nombres = new List<string> { "Capítulo" };
        for (int i = 1; i <= n; i++) nombres.Add($"Subcapítulo {i}");
        return nombres;
    }

    private void OnRebuildClasifClick(object sender, RoutedEventArgs e) => RebuildClasifBlocks();

    /// <summary>Reconstruye ambos bloques (Planos y Vistas) según el nº de subcapítulos, preservando lo escrito.</summary>
    private void RebuildClasifBlocks()
    {
        RebuildBlock(_clasifPlanos, esVista: false);
        RebuildBlock(_clasifVistas, esVista: true);
    }

    private void RebuildBlock(ObservableCollection<ClasifRow> bloque, bool esVista)
    {
        var previos = bloque.ToDictionary(r => r.Parametro, r => r.Patron, StringComparer.OrdinalIgnoreCase);
        bloque.Clear();
        foreach (var nombre in NombresClasificacion())
            bloque.Add(new ClasifRow
            {
                Parametro = nombre,
                Patron = previos.TryGetValue(nombre, out var p) ? p : PatronPorDefecto(nombre),
            });
    }

    private static Dictionary<string, string> BloqueADict(ObservableCollection<ClasifRow> bloque) =>
        bloque.Where(r => !string.IsNullOrWhiteSpace(r.Patron))
              .ToDictionary(r => r.Parametro, r => r.Patron.Trim());

    // =========================================================================
    // Preferencias (persistencia entre sesiones)
    // =========================================================================

    private ViewSheetPrefs GatherPrefs() => new()
    {
        EscalaGlobal = ParseInt(EscalaGlobalBox.Text, 50),
        PaddingContador = ParseInt(PaddingBox.Text, 2),
        PatronNumeroHoja = (PatronNumeroBox.Text ?? string.Empty).Trim(),
        PatronNombreHoja = (PatronNombreBox.Text ?? string.Empty).Trim(),
        SubcapNiveles = int.TryParse((SubcapNivelesBox.Text ?? "").Trim(), out int sn) ? Math.Max(0, Math.Min(10, sn)) : 1,
        AgruparSectores = AgruparSectoresCheck.IsChecked == true,
        SoloVistas = SoloVistasCheck.IsChecked == true,
        RellenarClasifPlanos = RellenarPlanosCheck.IsChecked == true,
        RellenarClasifVistas = RellenarVistasCheck.IsChecked == true,
        ClasificacionPlanos = _clasifPlanos.ToDictionary(r => r.Parametro, r => r.Patron),
        ClasificacionVistas = _clasifVistas.ToDictionary(r => r.Parametro, r => r.Patron),
        TitleBlockName = (TitleBlockCombo.SelectedItem as ElementChoice)?.Name,
        ViewFamilyTypeName = (ViewFamilyTypeCombo.SelectedItem as ElementChoice)?.Name,
        ViewportTypeName = (ViewportTypeCombo.SelectedItem as ElementChoice)?.Name,
    };

    private void ApplyPrefs(ViewSheetPrefs p)
    {
        if (p is null) return;
        EscalaGlobalBox.Text = p.EscalaGlobal.ToString();
        PaddingBox.Text = p.PaddingContador.ToString();
        if (!string.IsNullOrWhiteSpace(p.PatronNumeroHoja)) PatronNumeroBox.Text = p.PatronNumeroHoja;
        if (!string.IsNullOrWhiteSpace(p.PatronNombreHoja)) PatronNombreBox.Text = p.PatronNombreHoja;
        SubcapNivelesBox.Text = p.SubcapNiveles.ToString();
        AgruparSectoresCheck.IsChecked = p.AgruparSectores;
        SoloVistasCheck.IsChecked = p.SoloVistas;
        RellenarPlanosCheck.IsChecked = p.RellenarClasifPlanos;
        RellenarVistasCheck.IsChecked = p.RellenarClasifVistas;

        SelectByName(TitleBlockCombo, _titleBlocks, p.TitleBlockName);
        SelectByName(ViewFamilyTypeCombo, _viewFamilyTypes, p.ViewFamilyTypeName);
        SelectByName(ViewportTypeCombo, _viewportTypes, p.ViewportTypeName);

        // Construir los dos bloques de clasificación desde las prefs (con defaults).
        LoadBlockFromPrefs(_clasifPlanos, p.ClasificacionPlanos);
        LoadBlockFromPrefs(_clasifVistas, p.ClasificacionVistas);
    }

    private void LoadBlockFromPrefs(ObservableCollection<ClasifRow> bloque, Dictionary<string, string> guardado)
    {
        bloque.Clear();
        foreach (var nombre in NombresClasificacion())
            bloque.Add(new ClasifRow
            {
                Parametro = nombre,
                Patron = guardado.TryGetValue(nombre, out var p) ? p : PatronPorDefecto(nombre),
            });
    }

    private static void SelectByName(System.Windows.Controls.ComboBox combo, List<ElementChoice> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var match = items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) combo.SelectedItem = match;
    }

    private static string PatronPorDefecto(string nombre) =>
        nombre.Equals("Capítulo", StringComparison.OrdinalIgnoreCase) ? "{instalacion}" :
        nombre.Equals("Subcapítulo 1", StringComparison.OrdinalIgnoreCase) ? "{nivel}" : string.Empty;

    /// <summary>Asegura (crea si faltan) los parámetros de clasificación. Devuelve creados y errores.</summary>
    private (List<string> creados, List<string> errores) AsegurarParametrosClasificacion(IReadOnlyList<string> nombres)
    {
        var doc = _uidoc.Document;
        var app = doc.Application;
        EnsureSharedFile(app);

        var existentes = _para.ListProjectParameters(doc)
            .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var creados = new List<string>();
        var errores = new List<string>();
        foreach (var nombre in nombres)
        {
            if (existentes.Contains(nombre)) continue;
            var req = new CreateProjectParameterRequest
            {
                Name = nombre,
                DataType = ParameterDataTypeOption.Text,
                Scope = ParameterScope.Instance,
                CreateAsShared = true,
                SharedGroupName = "e3gbimtools",
                PropertiesGroupName = "Clasificación",
                Categories = new List<BuiltInCategory> { BuiltInCategory.OST_Views, BuiltInCategory.OST_Sheets },
            };
            var res = _para.CreateProjectParameter(doc, app, req);
            if (res.Success) creados.Add(nombre);
            else errores.Add($"{nombre}: {res.Summary}");
        }
        return (creados, errores);
    }

    private void OnCrearClasificacionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var nombres = NombresClasificacion();
            var (creados, errores) = AsegurarParametrosClasificacion(nombres);
            var yaEstaban = nombres.Where(n => !creados.Contains(n) && !errores.Any(er => er.StartsWith(n + ":"))).ToList();

            string msg = "";
            if (creados.Count > 0) msg += "Creados: " + string.Join(", ", creados) + "\n";
            if (yaEstaban.Count > 0) msg += "Ya existían: " + string.Join(", ", yaEstaban) + "\n";
            if (errores.Count > 0) msg += "Errores:\n  " + string.Join("\n  ", errores);
            if (string.IsNullOrWhiteSpace(msg)) msg = "Sin cambios.";

            StatusText.Text = errores.Count == 0
                ? "Parámetros de clasificación listos (Vistas y Planos)."
                : "Clasificación con incidencias — ver detalle.";
            TaskDialog.Show("View & Sheet Manager — Clasificación", msg.Trim());
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudieron crear los parámetros: " + ex.Message;
            TaskDialog.Show("View & Sheet Manager", "Error al crear parámetros de clasificación:\n\n" + ex.Message);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>Garantiza un archivo .txt de parámetros compartidos; crea uno por defecto si no hay.</summary>
    private static void EnsureSharedFile(Autodesk.Revit.ApplicationServices.Application app)
    {
        string current = app.SharedParametersFilename;
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current)) return;

        string dir = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "e3g", "DiTools", "SharedParams");
        Directory.CreateDirectory(dir);
        string path = IoPath.Combine(dir, "e3gbimtools_shared.txt");

        if (!File.Exists(path))
        {
            // Cabecera mínima válida de un Shared Parameter File de Revit (campos separados por TAB).
            const string nl = "\n";
            string header =
                "# This is a Revit shared parameter file." + nl +
                "# Do not edit manually." + nl +
                "*META\tVERSION\tMINVERSION" + nl +
                "META\t2\t1" + nl +
                "*GROUP\tID\tNAME" + nl +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE" + nl;
            File.WriteAllText(path, header);
        }

        app.SharedParametersFilename = path;
    }

    // =========================================================================
    // Construcción del request
    // =========================================================================

    private GeneracionRequest? BuildRequest(out string? error)
    {
        error = null;

        var nivelIds = _niveles.Where(n => n.IsSelected).Select(n => n.Id).ToList();
        if (nivelIds.Count == 0) { error = "Marca al menos un nivel (pestaña Configuración)."; return null; }

        var instalaciones = _instalaciones.Where(r => r.Usar).Select(r => r.ToConfig()).ToList();
        if (instalaciones.Count == 0) { error = "Marca al menos una instalación."; return null; }

        if (TitleBlockCombo.SelectedItem is not ElementChoice tb) { error = "Selecciona un title block."; return null; }
        if (ViewFamilyTypeCombo.SelectedItem is not ElementChoice vft) { error = "Selecciona un tipo de vista de planta."; return null; }

        return new GeneracionRequest
        {
            NivelIds = nivelIds,
            Instalaciones = instalaciones,
            SectorIds = _sectores.Where(s => s.IsSelected).Select(s => s.Id.Value).ToList(),
            TitleBlockTypeId = tb.Id,
            ViewFamilyTypeId = vft.Id,
            EscalaGlobal = ParseInt(EscalaGlobalBox.Text, 50),
            PaddingContador = ParseInt(PaddingBox.Text, 2),
            PatronNumeroHoja = string.IsNullOrWhiteSpace(PatronNumeroBox.Text) ? "{prefijo}-{contador}" : PatronNumeroBox.Text.Trim(),
            PatronNombreHoja = string.IsNullOrWhiteSpace(PatronNombreBox.Text) ? "{instalacion} - {nivel}" : PatronNombreBox.Text.Trim(),
            ViewportTypeId = (ViewportTypeCombo.SelectedItem as ElementChoice)?.Id ?? -1,
            AgruparSectoresPorHoja = AgruparSectoresCheck.IsChecked == true,
            SoloVistas = SoloVistasCheck.IsChecked == true,
            // Posición inicial del viewport (centro). El usuario la ajusta arrastrando.
            ViewportFracX = 0.5,
            ViewportFracY = 0.5,
        };
    }

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0
            ? v : fallback;

    // =========================================================================
    // ViewModels
    // =========================================================================

    private sealed class NivelItem
    {
        public NivelItem(ElementId id, string name) { Id = id; Name = name; }
        public ElementId Id { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
    }

    private sealed class SectorItem
    {
        public SectorItem(ElementId id, string name) { Id = id; Name = name; }
        public ElementId Id { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
    }

    /// <summary>Fila editable de clasificación: nombre de parámetro + patrón de valor.</summary>
    public sealed class ClasifRow
    {
        public string Parametro { get; set; } = string.Empty;
        public string Patron { get; set; } = string.Empty;
    }

    public sealed class ElementChoice
    {
        public ElementChoice(long id, string name) { Id = id; Name = name; }
        public long Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    public sealed class ViewTemplateItem
    {
        public static readonly ViewTemplateItem Ninguna = new(-1, "(sin plantilla)");
        public ViewTemplateItem(long id, string name) { Id = id; Name = name; }
        public long Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    /// <summary>Fila editable de la lista de instalaciones.</summary>
    public sealed class InstalacionRow
    {
        public InstalacionRow(InstalacionConfig config, IReadOnlyList<ViewTemplateItem> templates)
        {
            Usar = true;
            Nombre = config.Nombre;
            PrefijoHoja = config.PrefijoHoja;
            Escala = config.Escala;
            PatronNombre = config.PatronNombre;
            ColorHex = config.ColorHex;
            Templates = templates;
            Plantilla = templates.FirstOrDefault(t => t.Id == config.PlantillaVistaId) ?? ViewTemplateItem.Ninguna;
        }

        public bool Usar { get; set; }
        public string Nombre { get; set; }
        public string PrefijoHoja { get; set; }
        public int Escala { get; set; }
        public string PatronNombre { get; set; }
        public string ColorHex { get; set; }
        public IReadOnlyList<ViewTemplateItem> Templates { get; }
        public ViewTemplateItem? Plantilla { get; set; }

        public InstalacionConfig ToConfig() => new()
        {
            Nombre = Nombre,
            PrefijoHoja = PrefijoHoja,
            Escala = Escala,
            PatronNombre = PatronNombre,
            ColorHex = ColorHex,
            PlantillaVistaId = Plantilla?.Id ?? -1,
            ViewFamilyTypeId = -1,
        };
    }

    // ── Nodos del árbol de previsualización ──
    private sealed class InstGroup
    {
        public string Header { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#2D7A5F";
        public List<NivelGroup> Niveles { get; } = new();
    }

    private sealed class NivelGroup
    {
        public string Header { get; set; } = string.Empty;
        public List<LeafVm> Leaves { get; } = new();
    }

    private sealed class LeafVm : INotifyPropertyChanged
    {
        public LeafVm(PlanItem item) { Item = item; }
        public PlanItem Item { get; }

        public string Display
        {
            get
            {
                var vps = Item.Viewports;
                string primera = vps.Count > 0 ? vps[0].NombreVista : string.Empty;
                if (string.IsNullOrEmpty(Item.NumeroHoja)) // modo solo vistas
                    return vps.Count == 1 ? $"Vista: {primera}" : $"{vps.Count} vistas · {Item.SectoresTexto}";
                if (Item.EsMulti)
                    return $"Hoja {Item.NumeroHoja} · {vps.Count} sectores ({Item.SectoresTexto})";
                string pre = vps.Count > 0 && vps[0].TieneSector ? $"[{vps[0].Sector}] " : string.Empty;
                return $"{pre}{primera}  →  {Item.NumeroHoja}";
            }
        }

        public string AvisoCorto => string.IsNullOrEmpty(Item.Aviso) ? string.Empty : "⚠";

        public bool IsChecked
        {
            get => Item.Seleccionado;
            set
            {
                if (Item.Seleccionado == value) return;
                Item.Seleccionado = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
