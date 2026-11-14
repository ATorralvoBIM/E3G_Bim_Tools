// ============================================================================
//  CalcWindow.xaml.cs
//  Ventana principal del motor MEP (Fase 5+).
//  1200 x 740 px, modal anclada al main handle de Revit.
//  5 pestañas: Hidráulica, Aeráulica, Camino crítico, Esquema, Self-test.
//
//  Funciones:
//    · Cálculo hidráulico + aeráulico con BFS/DFS y ResultRow completos.
//    · Esquema árbol inline (lado derecho de la tabla) con glyphs por tipo.
//    · Color de fila por StatusKind (OK=verde, Warning=amarillo, Error=rojo).
//    · Menú contextual (clic derecho cabecera) para visibilidad de columnas.
//    · Badges de aviso ⚠ en los títulos de columna con datos problemáticos.
//    · Sincronización bidireccional Revit ↔ grid: selección + doble clic zoom.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using WpfComboBox     = System.Windows.Controls.ComboBox;
using WpfContextMenu  = System.Windows.Controls.ContextMenu;
using WpfMenuItem     = System.Windows.Controls.MenuItem;
using WpfSeparator    = System.Windows.Controls.Separator;
using WpfVisibility   = System.Windows.Visibility;
using WpfColor     = System.Windows.Media.Color;
using WpfColors    = System.Windows.Media.Colors;
using WpfPoint     = System.Windows.Point;
using WpfLine      = System.Windows.Shapes.Line;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfEllipse   = System.Windows.Shapes.Ellipse;
using WpfPolygon   = System.Windows.Shapes.Polygon;
using WpfPath      = System.Windows.Shapes.Path;
using CalcFluidType = e3gbimtools.Calc.Core.Common.FluidType;
using e3gbimtools.Calc.Abstractions;
using e3gbimtools.Calc.Core.Aeraulics;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using e3gbimtools.Calc.RevitBridge;
using e3gbimtools.Calc.Tests;
// Alias para evitar colisión con Autodesk.Revit.DB.Plumbing.PipeSegment
using PipeSegment = e3gbimtools.Calc.Core.Hydraulics.PipeSegment;
using PipeNode    = e3gbimtools.Calc.Core.Hydraulics.PipeNode;
using PipeNetwork = e3gbimtools.Calc.Core.Hydraulics.PipeNetwork;
using DuctSegment = e3gbimtools.Calc.Core.Aeraulics.DuctSegment;
using DuctNode    = e3gbimtools.Calc.Core.Aeraulics.DuctNode;
using DuctNetwork = e3gbimtools.Calc.Core.Aeraulics.DuctNetwork;
using DuctShape   = e3gbimtools.Calc.Core.Aeraulics.DuctShape;

namespace e3gbimtools.Calc.Views
{
    /// <summary>ViewModel row for the Critical Path grid.</summary>
    public class CritPathRow
    {
        public int    Position        { get; set; }
        public long   RevitElementId  { get; set; }
        public double CumLossPa       { get; set; }
        public double VelocityMs      { get; set; }
        public string RecommendedSize { get; set; }
    }

    public partial class CalcWindow : Window
    {
        // ------------------------------------------------------------------ //
        //  Servicios inyectados                                                //
        // ------------------------------------------------------------------ //
        private readonly UIApplication           _uiApp;
        private readonly Document                _doc;
        private readonly ICalcEngineService      _calcEngine;
        private readonly ICriticalPathService    _critPath;
        private readonly ISchematicLayoutService _schematic;

        // ------------------------------------------------------------------ //
        //  Estado de cálculo                                                   //
        // ------------------------------------------------------------------ //
        private CalcReport _lastHydReport;
        private CalcReport _lastAerReport;

        // Redes almacenadas para BFS post-cálculo
        private List<PipeSegment> _lastHydBfsOrder;
        private List<DuctSegment> _lastAerBfsOrder;

        // Filas ResultRow del grid (en orden DFS branch-first)
        private IList<ResultRow> _lastHydRows;
        private IList<ResultRow> _lastAerRows;

        // Fluido y criterio del último cálculo (para la memoria de cálculo).
        private FluidProperties _lastHydFluid;
        private FluidProperties _lastAerFluid;
        private SizingCriteriaConfig _lastHydCriteria;
        private SizingCriteriaConfig _lastAerCriteria;

        // Color del sistema MEP extraído del tipo Revit
        private Brush _hydSystemBrush = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x3A, 0x5E));
        private Brush _aerSystemBrush = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x3A, 0x5E));

        private static readonly Brush DefaultSystemBrush
            = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x3A, 0x5E));

        private CancellationTokenSource _cts;

        // ------------------------------------------------------------------ //
        //  Esquema — estado del canvas                                         //
        // ------------------------------------------------------------------ //
        private const double SchemaRowHeight    = 22.0;
        private const double SchemaTopPad       = 12.0;
        private const double SchemaTrunkX       = 24.0;
        private const double SchemaIndentMin    = 50.0;
        private const double SchemaIndentMax    = 140.0;
        private const double SchemaLabelPad     = 12.0;
        private const double SchemaLabelReserve = 60.0;
        private const double SchemaGlyphOffset  = 8.0;
        private const double SchemaLabelPadFitting = 18.0;

        // Mapa fila → glifo del esquema (para highlight al seleccionar)
        private readonly Dictionary<ResultRow, FrameworkElement> _hydSchemaGlyphs
            = new Dictionary<ResultRow, FrameworkElement>();
        private readonly Dictionary<ResultRow, FrameworkElement> _aerSchemaGlyphs
            = new Dictionary<ResultRow, FrameworkElement>();
        private FrameworkElement _hydHighlightedGlyph;
        private FrameworkElement _aerHighlightedGlyph;

        // Indentación dinámica recalculada en cada BuildSchematic
        private double _hydSchemaIndentPx = 36.0;
        private double _aerSchemaIndentPx = 36.0;

        // Mapa fila → línea del esquema (para overlay camino crítico)
        private readonly Dictionary<ResultRow, WpfLine> _hydSchemaLines
            = new Dictionary<ResultRow, WpfLine>();
        private readonly Dictionary<ResultRow, WpfLine> _aerSchemaLines
            = new Dictionary<ResultRow, WpfLine>();
        // IDs de Revit que forman el camino crítico activo (para overlay)
        private HashSet<long> _activeCriticalRevitIds;

        // ------------------------------------------------------------------ //
        //  Hover highlight — sincronización hover sobre grid → esquema         //
        // ------------------------------------------------------------------ //
        private DispatcherTimer _hydHoverTimer;
        private DispatcherTimer _aerHoverTimer;
        private ResultRow _hydHoveredRow;
        private ResultRow _aerHoveredRow;

        // ------------------------------------------------------------------ //
        //  Sincronización Revit ↔ Grid                                         //
        // ------------------------------------------------------------------ //
        // Mapa bidireccional ElementId ↔ ResultRow para sincronización.
        private readonly Dictionary<long, ResultRow>  _revitIdToRow  = new Dictionary<long, ResultRow>();
        private readonly Dictionary<ResultRow, ElementId> _rowToElem = new Dictionary<ResultRow, ElementId>();

        // Flags para evitar bucles de selección cruzada
        private bool _suppressRevitEvent;
        private bool _suppressUiEvent;

        // ExternalEvent para enviar selecciones a Revit desde hilo UI
        private CalcExternalEventBus _externalBus;

        // Throttle para el evento de selección entrante desde Revit
        private DispatcherTimer _revitSelThrottle;
        private IList<ElementId> _pendingRevitSelection;

        // ------------------------------------------------------------------ //
        //  Constructor                                                         //
        // ------------------------------------------------------------------ //
        public CalcWindow(
            UIApplication uiApp,
            ICalcEngineService calcEngine,
            ICriticalPathService critPath,
            ISchematicLayoutService schematic)
        {
            _uiApp      = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc        = uiApp.ActiveUIDocument?.Document
                          ?? throw new InvalidOperationException("No hay documento activo.");
            _calcEngine = calcEngine ?? throw new ArgumentNullException(nameof(calcEngine));
            _critPath   = critPath   ?? throw new ArgumentNullException(nameof(critPath));
            _schematic  = schematic  ?? throw new ArgumentNullException(nameof(schematic));

            InitializeComponent();

            // Reskin Fluent Light: fusiona el tema completo para que E3gTitleBar
            // resuelva todos sus brushes/fuentes (CalcWindow inlinea solo un subset).
            E3G.BimTools.DiTools.UI.Controls.E3gWindowBase.ApplyTheme(this);

            // Crear ExternalEventBus en contexto válido de la API de Revit
            try { _externalBus = new CalcExternalEventBus(); }
            catch { /* no sync si falla */ }

            // Suscribirse al evento de selección de Revit (bidireccional)
            try { _uiApp.SelectionChanged += OnRevitSelectionChanged; }
            catch { }

            // Throttle para coalescir eventos rápidos de selección
            _revitSelThrottle = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _revitSelThrottle.Tick += OnRevitSelThrottleTick;

            // Hover timers para colorear el esquema al pasar el ratón por el grid
            _hydHoverTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _hydHoverTimer.Tick += OnHydHoverTick;

            _aerHoverTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _aerHoverTimer.Tick += OnAerHoverTick;

            // Limpieza al cerrar
            Closed += (s, e) =>
            {
                try { _uiApp.SelectionChanged -= OnRevitSelectionChanged; } catch { }
                _revitSelThrottle?.Stop();
                _hydHoverTimer?.Stop();
                _aerHoverTimer?.Stop();
                _externalBus?.Dispose();
            };

            // Recalcular esquema cuando el splitter cambia el ancho disponible
            HydSchematicScroll.SizeChanged += (s, e) => OnSchematicScrollResized(
                HydSchematicCanvas, HydSchematicScroll, _lastHydRows, _hydSystemBrush,
                _hydSchemaGlyphs, ref _hydHighlightedGlyph, ref _hydSchemaIndentPx);
            AerSchematicScroll.SizeChanged += (s, e) => OnSchematicScrollResized(
                AerSchematicCanvas, AerSchematicScroll, _lastAerRows, _aerSystemBrush,
                _aerSchemaGlyphs, ref _aerHighlightedGlyph, ref _aerSchemaIndentPx);

            // Wire hover events on the data grids after the XAML is parsed
            Loaded += (s, e) =>
            {
                HydResultsGrid.MouseMove  += OnHydGridMouseMove;
                HydResultsGrid.MouseLeave += OnHydGridMouseLeave;
                AerResultsGrid.MouseMove  += OnAerGridMouseMove;
                AerResultsGrid.MouseLeave += OnAerGridMouseLeave;
            };

            Loaded += OnLoaded;
        }

        // ------------------------------------------------------------------ //
        //  Inicialización                                                      //
        // ------------------------------------------------------------------ //
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateHydPresets();
            PopulateAerPresets();
            ReloadHydSystems();
            ReloadAerSystems();
        }

        private void PopulateHydPresets()
        {
            HydPresetCombo.Items.Clear();
            foreach (var kv in SizingCriteriaConfig.Presets)
            {
                if (kv.Value.Fluid == CalcFluidType.Water || kv.Value.Fluid == CalcFluidType.GlycolMix)
                    HydPresetCombo.Items.Add(new ComboBoxItem
                    {
                        Content = kv.Value.Description ?? kv.Key,
                        Tag     = kv.Key
                    });
            }
            if (HydPresetCombo.Items.Count > 0) HydPresetCombo.SelectedIndex = 0;
        }

        private void PopulateAerPresets()
        {
            AerPresetCombo.Items.Clear();
            foreach (var kv in SizingCriteriaConfig.Presets)
            {
                if (kv.Value.Fluid == CalcFluidType.Air || kv.Value.Fluid == CalcFluidType.OtherGas)
                    AerPresetCombo.Items.Add(new ComboBoxItem
                    {
                        Content = kv.Value.Description ?? kv.Key,
                        Tag     = kv.Key
                    });
            }
            if (AerPresetCombo.Items.Count > 0) AerPresetCombo.SelectedIndex = 0;
        }

        private void ReloadHydSystems()
        {
            HydSystemCombo.Items.Clear();
            try
            {
                var systems = new FilteredElementCollector(_doc)
                    .OfClass(typeof(PipingSystem))
                    .Cast<PipingSystem>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.Name)
                    .ToList();
                foreach (var s in systems)
                    HydSystemCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
                SetStatus(systems.Count + " PipingSystem encontrados.");
            }
            catch (Exception ex) { SetStatus("Error cargando sistemas hidráulicos: " + ex.Message); }
        }

        private void ReloadAerSystems()
        {
            AerSystemCombo.Items.Clear();
            try
            {
                var systems = new FilteredElementCollector(_doc)
                    .OfClass(typeof(MechanicalSystem))
                    .Cast<MechanicalSystem>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                    .OrderBy(s => s.Name)
                    .ToList();
                foreach (var s in systems)
                    AerSystemCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
            }
            catch (Exception ex) { SetStatus("Error cargando sistemas aeráulicos: " + ex.Message); }
        }

        // ------------------------------------------------------------------ //
        //  Tab Hidráulica                                                      //
        // ------------------------------------------------------------------ //
        private void OnHydReloadClick(object sender, RoutedEventArgs e) => ReloadHydSystems();

        private void OnHydSystemChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HydSystemCombo.SelectedItem is ComboBoxItem item && item.Tag is PipingSystem sys)
            {
                string type = TryGetPipingSystemType(_doc, sys);
                SetStatus("Sistema seleccionado: " + sys.Name + (type != "" ? " (" + type + ")" : ""));
            }
            // Al cambiar de sistema, descartamos los cambios de diámetro pendientes.
            ClearHydManualSizes();
        }

        private async void OnHydCalcClick(object sender, RoutedEventArgs e)
        {
            var system = GetSelectedSystem<PipingSystem>(HydSystemCombo);
            if (system == null) { ShowError("Selecciona un PipingSystem antes de calcular."); return; }

            string presetKey = GetSelectedPresetKey(HydPresetCombo);
            if (string.IsNullOrEmpty(presetKey)) { ShowError("Selecciona un preset de dimensionado."); return; }

            SetStatus("Leyendo red hidráulica…");
            try
            {
                var reader  = new PipeNetworkReader();
                var network = reader.ReadFromSystem(_doc, system);
                if (network.Segments.Count == 0)
                {
                    HydSummaryText.Text = "El sistema no contiene tramos válidos.\n" +
                        string.Join("\n", reader.Messages.Select(m => m.ToString()));
                    SetStatus("Sin tramos."); return;
                }

                // Captura el catálogo de tallas por elemento (para el editor de
                // diámetro por fila) y aplica los cambios manuales pendientes
                // sobre los diámetros ANTES de calcular (recálculo en memoria).
                CaptureHydCatalogs(network);
                ApplyHydManualSizes(network);

                ApplySourceFallback(network);
                network.OrientFromSource();

                // BFS antes del await (hilo UI, contexto Revit)
                _lastHydBfsOrder = ComputePipeBfsOrder(network);

                var criteria = SizingCriteriaConfig.FromPreset(presetKey);
                var fluid    = ChooseHydFluid(presetKey);
                _lastHydFluid = fluid; _lastHydCriteria = criteria;
                _hydSystemBrush = GetSystemBrush(system);

                SetStatus("Calculando…");
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var req = new PipeNetworkRequest { Network = network, Fluid = fluid, Criteria = criteria };
                _lastHydReport = await _calcEngine.CalculatePipingAsync(_doc, req, _cts.Token);

                foreach (var m in reader.Messages) _lastHydReport.AddMessage(m);

                BindHydResults(_lastHydReport, criteria, fluid);
                SetStatus("Cálculo hidráulico completado: " + _lastHydReport.Segments.Count
                    + " tramos, " + _lastHydReport.CountErrors + " errores, "
                    + _lastHydReport.CountWarnings + " avisos.");
            }
            catch (OperationCanceledException) { SetStatus("Cancelado."); }
            catch (Exception ex)
            {
                HydSummaryText.Text = "Error: " + ex.Message;
                SetStatus("Error durante el cálculo hidráulico.");
            }
        }

        private void OnHydWriteClick(object sender, RoutedEventArgs e)
        {
            if (_lastHydReport == null) { ShowError("Ejecuta el cálculo primero."); return; }
            if (_externalBus == null)
            {
                ShowError("La ventana debe abrirse desde el botón de la cinta de Revit para " +
                          "poder escribir al modelo (se requiere contexto de API válido).");
                return;
            }
            // CORRECCIÓN: la Transaction debe ejecutarse dentro del contexto de API de Revit.
            // Encolamos la operación en el ExternalEventBus; Revit la ejecutará en el
            // siguiente idle cycle, dentro de IExternalEventHandler.Execute.
            var report = _lastHydReport;
            SetStatus("Enviando resultados hidráulicos al modelo…");
            ScheduleRevitAction(app =>
            {
                try
                {
                    var writer = new ResultWriter();
                    writer.WriteToRevit(app.ActiveUIDocument.Document, report, "MEP Calc - Hidráulica");
                    int upd = writer.Updated, skip = writer.Skipped;
                    Dispatcher.BeginInvoke((Action)(() =>
                        SetStatus($"Escrito al modelo: {upd} elementos actualizados, {skip} sin parámetros MEP_CALC_*.")));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke((Action)(() => ShowError("Error al escribir al modelo: " + msg)));
                }
            });
        }

        private void OnHydExportCsvClick(object sender, RoutedEventArgs e)
            => ExportReportToCsv(_lastHydReport, "hidraulica");

        // ------------------------------------------------------------------ //
        //  Localizar en Revit los tramos desconectados de la fuente            //
        // ------------------------------------------------------------------ //
        private void OnHydLocateDisconnectedClick(object sender, RoutedEventArgs e)
            => LocateDisconnected(_lastHydRows, "hidráulico");

        private void OnAerLocateDisconnectedClick(object sender, RoutedEventArgs e)
            => LocateDisconnected(_lastAerRows, "aeráulico");

        private void LocateDisconnected(IList<ResultRow> rows, string label)
        {
            if (rows == null) { ShowError("Ejecuta primero el cálculo " + label + "."); return; }

            var seen = new HashSet<long>();
            var ids  = new List<ElementId>();
            foreach (var r in rows)
            {
                if (r == null || !r.Disconnected || r.RevitId <= 0) continue;
                if (seen.Add(r.RevitId)) ids.Add(new ElementId((long)r.RevitId));
            }

            if (ids.Count == 0) { SetStatus("No hay tramos desconectados."); return; }
            if (_externalBus == null)
            {
                ShowError("La ventana debe abrirse desde el botón de la cinta de Revit " +
                          "para localizar los elementos en el modelo.");
                return;
            }

            SetStatus("Localizando " + ids.Count + " tramo(s) desconectado(s) en Revit…");
            ScheduleRevitAction(uiApp =>
            {
                try
                {
                    _suppressRevitEvent = true;
                    var uidoc = uiApp.ActiveUIDocument;
                    uidoc?.Selection?.SetElementIds(ids);
                    uidoc?.ShowElements(ids);   // encuadra/zoom a los elementos
                }
                finally { _suppressRevitEvent = false; }
            });

            // Reflejar también la selección en el grid/esquema.
            ApplyRevitSelectionToGrids(ids);
        }

        // ------------------------------------------------------------------ //
        //  Tab Aeráulica                                                       //
        // ------------------------------------------------------------------ //
        private void OnAerReloadClick(object sender, RoutedEventArgs e) => ReloadAerSystems();

        private void OnAerSystemChanged(object sender, SelectionChangedEventArgs e) { }

        private async void OnAerCalcClick(object sender, RoutedEventArgs e)
        {
            var system = GetSelectedSystem<MechanicalSystem>(AerSystemCombo);
            if (system == null) { ShowError("Selecciona un MechanicalSystem antes de calcular."); return; }

            string presetKey = GetSelectedPresetKey(AerPresetCombo);
            if (string.IsNullOrEmpty(presetKey)) { ShowError("Selecciona un preset de dimensionado."); return; }

            SetStatus("Leyendo red aeráulica…");
            try
            {
                var reader  = new DuctNetworkReader();
                var network = reader.ReadFromSystem(_doc, system);
                if (network.Segments.Count == 0)
                {
                    AerSummaryText.Text = "El sistema no contiene tramos válidos.\n" +
                        string.Join("\n", reader.Messages.Select(m => m.ToString()));
                    SetStatus("Sin tramos."); return;
                }

                ApplySourceFallbackDuct(network);
                network.OrientFromSource();

                // BFS antes del await
                _lastAerBfsOrder = ComputeDuctBfsOrder(network);

                var criteria = SizingCriteriaConfig.FromPreset(presetKey);
                var fluid    = AirProperties.AsFluid(20.0, 0.0);
                _lastAerFluid = fluid; _lastAerCriteria = criteria;
                _aerSystemBrush = GetSystemBrush(system);

                SetStatus("Calculando…");
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                var req = new DuctNetworkRequest { Network = network, Fluid = fluid, Criteria = criteria };
                _lastAerReport = await _calcEngine.CalculateDuctAsync(_doc, req, _cts.Token);

                foreach (var m in reader.Messages) _lastAerReport.AddMessage(m);

                BindAerResults(_lastAerReport, criteria, fluid);
                SetStatus("Cálculo aeráulico completado: " + _lastAerReport.Segments.Count
                    + " tramos, " + _lastAerReport.CountErrors + " errores, "
                    + _lastAerReport.CountWarnings + " avisos.");
            }
            catch (OperationCanceledException) { SetStatus("Cancelado."); }
            catch (Exception ex)
            {
                AerSummaryText.Text = "Error: " + ex.Message;
                SetStatus("Error durante el cálculo aeráulico.");
            }
        }

        private void OnAerWriteClick(object sender, RoutedEventArgs e)
        {
            if (_lastAerReport == null) { ShowError("Ejecuta el cálculo primero."); return; }
            if (_externalBus == null)
            {
                ShowError("La ventana debe abrirse desde el botón de la cinta de Revit para " +
                          "poder escribir al modelo (se requiere contexto de API válido).");
                return;
            }
            var report = _lastAerReport;
            SetStatus("Enviando resultados aeráulicos al modelo…");
            ScheduleRevitAction(app =>
            {
                try
                {
                    var writer = new ResultWriter();
                    writer.WriteToRevit(app.ActiveUIDocument.Document, report, "MEP Calc - Aeráulica");
                    int upd = writer.Updated, skip = writer.Skipped;
                    Dispatcher.BeginInvoke((Action)(() =>
                        SetStatus($"Escrito al modelo: {upd} elementos actualizados, {skip} sin parámetros MEP_CALC_*.")));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke((Action)(() => ShowError("Error al escribir al modelo: " + msg)));
                }
            });
        }

        private void OnAerExportCsvClick(object sender, RoutedEventArgs e)
            => ExportReportToCsv(_lastAerReport, "aeraulica");

        // ------------------------------------------------------------------ //
        //  Tab Camino crítico                                                  //
        // ------------------------------------------------------------------ //
        // ------------------------------------------------------------------ //
        //  Panel de filtros inline (sobre el esquema árbol en cada pestaña)  //
        // ------------------------------------------------------------------ //

        // Renderer de dimming (atenuación) para el esquema hidráulico/aeráulico
        private Filtering.SchemeFilterRenderer _hydFilterRenderer;
        private Filtering.SchemeFilterRenderer _aerFilterRenderer;

        private void OnHydFilterChanged(object sender, RoutedEventArgs e)
            => ApplySchemaFilter(
                HydFilterVMax?.Text, HydFilterDpMax?.Text,
                HydFilterOnlyIssues?.IsChecked == true,
                _lastHydRows, _hydSchemaGlyphs, _hydSchemaLines,
                ref _hydFilterRenderer);

        private void OnAerFilterChanged(object sender, RoutedEventArgs e)
            => ApplySchemaFilter(
                AerFilterVMax?.Text, AerFilterDpMax?.Text,
                AerFilterOnlyIssues?.IsChecked == true,
                _lastAerRows, _aerSchemaGlyphs, _aerSchemaLines,
                ref _aerFilterRenderer);

        private void OnHydFilterClearClick(object sender, RoutedEventArgs e)
        {
            HydFilterVMax.Text  = "";
            HydFilterDpMax.Text = "";
            HydFilterOnlyIssues.IsChecked = false;
            _activeCriticalRevitIds = null;
            _hydFilterRenderer?.Reset();
            // Restaurar color original del esquema
            ApplyCriticalPathOverlay(HydSchematicCanvas, _hydSchemaLines,
                _lastHydRows, new HashSet<long>());
        }

        private void OnAerFilterClearClick(object sender, RoutedEventArgs e)
        {
            AerFilterVMax.Text  = "";
            AerFilterDpMax.Text = "";
            AerFilterOnlyIssues.IsChecked = false;
            _activeCriticalRevitIds = null;
            _aerFilterRenderer?.Reset();
            ApplyCriticalPathOverlay(AerSchematicCanvas, _aerSchemaLines,
                _lastAerRows, new HashSet<long>());
        }

        // Botones de camino crítico del panel inline (alias hacia la lógica existente)
        private void OnHydCritPathClick(object sender, RoutedEventArgs e)
            => ComputeCriticalPath(_lastHydReport, "hidráulico");

        private void OnAerCritPathClick(object sender, RoutedEventArgs e)
            => ComputeCriticalPath(_lastAerReport, "aeráulico");

        private void ApplySchemaFilter(
            string vMaxText, string dpMaxText, bool onlyIssues,
            IList<ResultRow> rows,
            Dictionary<ResultRow, FrameworkElement> glyphMap,
            Dictionary<ResultRow, WpfLine> lineMap,
            ref Filtering.SchemeFilterRenderer renderer)
        {
            if (rows == null || rows.Count == 0) return;

            // Construir/actualizar renderer
            if (renderer == null)
                renderer = new Filtering.SchemeFilterRenderer();
            renderer.Glyphs = glyphMap;
            renderer.Lines  = lineMap;

            // Parsear umbrales
            bool hasVMax  = double.TryParse(vMaxText,  System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double vMax);
            bool hasDpMax = double.TryParse(dpMaxText, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double dpMax);

            bool criteriaEmpty = !hasVMax && !hasDpMax && !onlyIssues;

            if (criteriaEmpty)
            {
                renderer.Reset();
                return;
            }

            // Filas visibles = las que cumplen TODOS los criterios activos
            var visible = new HashSet<ResultRow>();
            foreach (var row in rows)
            {
                if (row.IsFitting) { visible.Add(row); continue; }  // accesorios siguen siempre al padre
                bool ok = true;
                if (hasVMax  && row.Velocity    > vMax)  ok = false;
                if (hasDpMax && row.FrictionPaPm > dpMax) ok = false;
                if (onlyIssues && row.StatusKind == "OK") ok = false;
                if (ok) visible.Add(row);
            }
            renderer.Apply(visible, criteriaEmpty);
        }

        // ------------------------------------------------------------------ //
        //  Tab Camino crítico (pestaña separada — mantener para acceso rápido)//
        // ------------------------------------------------------------------ //
        private void OnCriticalPathHydClick(object sender, RoutedEventArgs e)
            => ComputeCriticalPath(_lastHydReport, "hidráulico");

        private void OnCriticalPathAerClick(object sender, RoutedEventArgs e)
            => ComputeCriticalPath(_lastAerReport, "aeráulico");

        private void ComputeCriticalPath(CalcReport report, string label)
        {
            if (report == null) { ShowError("Ejecuta primero el cálculo " + label + "."); return; }
            try
            {
                var resultRows = report.Segments.Values.Select(c => new ResultRow
                {
                    RevitId         = c.RevitElementId,
                    BfsIndex        = c.BfsIndex,
                    LevelName       = c.LevelName ?? "",
                    FlowLps         = c.FlowM3s * 1000.0,
                    Velocity        = c.VelocityMs,
                    FrictionPaPm    = c.FrictionPaPm,
                    TotalLoss       = c.TotalLossPa,
                    RecommendedSize = c.RecommendedSize ?? "",
                    IsTerminal      = c.DownstreamFixtureCount == 0,
                }).ToList();

                var result = _critPath.Analyze(resultRows);

                var rows = new List<CritPathRow>();
                int pos = 1;
                double cumPa = 0.0;
                foreach (var seg in result.Path)
                {
                    cumPa += seg.TotalLoss;
                    rows.Add(new CritPathRow
                    {
                        Position        = pos++,
                        RevitElementId  = seg.RevitId,
                        CumLossPa       = cumPa,
                        VelocityMs      = seg.Velocity,
                        RecommendedSize = seg.RecommendedSize ?? "",
                    });
                }
                CritPathGrid.ItemsSource = rows;
                CritPathSummaryText.Text =
                    "Tramos en ruta crítica: " + result.Path.Count + "\n"
                    + "Pérdida total: " + result.TotalDeltaP.ToString("0") + " Pa\n"
                    + "Velocidad máxima: " + (rows.Count > 0 ? rows.Max(r => r.VelocityMs).ToString("0.00") : "—") + " m/s";
                SetStatus("Camino crítico " + label + " calculado.");

                // Overlay: colorea en el esquema inline los tramos del camino crítico
                _activeCriticalRevitIds = new HashSet<long>(result.Path.Select(r => r.RevitId));
                bool isHyd = label.Contains("hidr");
                ApplyCriticalPathOverlay(
                    isHyd ? HydSchematicCanvas : AerSchematicCanvas,
                    isHyd ? _hydSchemaLines    : _aerSchemaLines,
                    isHyd ? _lastHydRows       : _lastAerRows,
                    _activeCriticalRevitIds);
            }
            catch (Exception ex) { ShowError("Error camino crítico: " + ex.Message); }
        }

        private void OnCriticalPathExportClick(object sender, RoutedEventArgs e)
        {
            var rows = CritPathGrid.ItemsSource as List<CritPathRow>;
            if (rows == null || rows.Count == 0) { ShowError("No hay datos para exportar."); return; }
            var path = PickSavePath("camino_critico");
            if (path == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("Posicion;Id Revit;dP acum. (Pa);v (m/s);Tamano recomendado");
            foreach (var r in rows)
                sb.AppendLine($"{r.Position};{r.RevitElementId};{r.CumLossPa:0};{r.VelocityMs:0.00};{r.RecommendedSize}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            SetStatus("Exportado: " + path);
        }

        // ------------------------------------------------------------------ //
        //  Tab Esquema standalone                                              //
        // ------------------------------------------------------------------ //
        private void OnSchematicHydClick(object sender, RoutedEventArgs e)
        {
            if (_lastHydRows == null || _lastHydRows.Count == 0)
            { ShowError("Ejecuta primero el cálculo hidráulico."); return; }
            // Renderiza en el canvas del tab Esquema Y abre la ventana de previsualización
            BuildSchematic(SchematicCanvas, null, _lastHydRows, _hydSystemBrush,
                null, ref _hydSchemaIndentPx);
            OpenSchematicPreview(_lastHydRows, _hydSystemBrush, _lastHydReport);
        }

        private void OnSchematicAerClick(object sender, RoutedEventArgs e)
        {
            if (_lastAerRows == null || _lastAerRows.Count == 0)
            { ShowError("Ejecuta primero el cálculo aeráulico."); return; }
            BuildSchematic(SchematicCanvas, null, _lastAerRows, _aerSystemBrush,
                null, ref _aerSchemaIndentPx);
            OpenSchematicPreview(_lastAerRows, _aerSystemBrush, _lastAerReport);
        }

        private void OnOpenSchematicPreviewClick(object sender, RoutedEventArgs e)
        {
            var rows   = _lastHydRows   ?? _lastAerRows;
            var brush  = _hydSystemBrush ?? _aerSystemBrush;
            var report = _lastHydReport ?? _lastAerReport;
            if (rows == null || rows.Count == 0)
            { ShowError("Ejecuta primero un cálculo hidráulico o aeráulico."); return; }
            OpenSchematicPreview(rows, brush, report);
        }

        private void OpenSchematicPreview(
            IList<ResultRow> rows, Brush brush, CalcReport report)
        {
            var win = new SchematicPreviewWindow(
                _doc, _externalBus, rows,
                brush ?? Brushes.SteelBlue,
                report,
                _activeCriticalRevitIds);
            win.Owner = this;
            win.Show();
        }

        private void OnSchematicExportClick(object sender, RoutedEventArgs e)
        {
            var report = _lastHydReport ?? _lastAerReport;
            if (report == null) { ShowError("Ejecuta primero un cálculo."); return; }
            var path = PickSavePath("esquema_nodos");
            if (path == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("Id Revit;v (m/s);dP (Pa);DN recomendado;OK");
            foreach (var s in report.Segments.Values)
                sb.AppendLine($"{s.RevitElementId};{s.VelocityMs:0.00};{s.TotalLossPa:0};{s.RecommendedSize};{(s.IsOk ? "SI" : "NO")}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            SetStatus("Exportado: " + path);
        }

        // ------------------------------------------------------------------ //
        //  Tab Self-test                                                       //
        // ------------------------------------------------------------------ //
        private void OnRunSelfTestClick(object sender, RoutedEventArgs e)
        {
            SelfTestOutputText.Text = "Ejecutando…";
            SelfTestResultText.Text = "";
            try
            {
                var report = CalcSelfTest.Run();
                SelfTestOutputText.Text = report.FullText;
                SelfTestResultText.Text = report.AllPassed
                    ? "✓ " + report.Passed + "/" + report.Total + " OK"
                    : "✗ " + report.Failed + " FALLIDOS de " + report.Total;
                SelfTestResultText.Foreground = report.AllPassed
                    ? new SolidColorBrush(WpfColor.FromRgb(0x1D, 0x9E, 0x75))
                    : new SolidColorBrush(WpfColor.FromRgb(0xD9, 0x53, 0x4F));
            }
            catch (Exception ex)
            {
                SelfTestOutputText.Text = "Excepcion: " + ex;
                SelfTestResultText.Text = "ERROR";
            }
        }

        // ------------------------------------------------------------------ //
        //  Bottom bar                                                          //
        // ------------------------------------------------------------------ //
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ------------------------------------------------------------------ //
        //  Binding helpers                                                     //
        // ------------------------------------------------------------------ //
        private void BindHydResults(CalcReport report, SizingCriteriaConfig criteria, FluidProperties fluid)
        {
            // Construir filas ResultRow desde el orden BFS calculado antes del await
            var rows = BuildResultRowsFromPipeBfs(_lastHydBfsOrder, report);
            rows = ReorderDfsBranchFirst(rows);
            _lastHydRows = rows;
            HydResultsGrid.ItemsSource = rows;

            // Esquema inline
            _hydSchemaGlyphs.Clear();
            _hydHighlightedGlyph = null;
            _activeCriticalRevitIds = null;          // limpiar overlay crítico previo
            _hydFilterRenderer = null;               // reiniciar renderer de filtros
            BuildSchematic(HydSchematicCanvas, HydSchematicScroll, rows, _hydSystemBrush,
                _hydSchemaGlyphs, ref _hydSchemaIndentPx);

            // Registro sync Revit ↔ grid
            RegisterRowsForSync(rows);

            // Badges de aviso en cabeceras de columna
            UpdateColumnWarningBadges(HydResultsGrid, rows);

            // Tramos desconectados de la fuente: botón de localización + aviso.
            int disconnected = rows.Count(r => r.Disconnected);
            HydLocateDisconnectedButton.Visibility = disconnected > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            if (disconnected > 0)
                HydLocateDisconnectedButton.Content = "⚠ Localizar desconectados (" + disconnected + ")";

            // Resumen
            var sb = new StringBuilder();
            if (disconnected > 0)
                sb.AppendLine("⚠ " + disconnected + " CORTE(S) EN LA RED — usa «Localizar desconectados» para ir al tramo exacto.");
            sb.AppendLine("Tramos calculados:    " + report.Segments.Count);
            sb.AppendLine("Worst-case ΔP:        " + report.WorstCasePressureDropPa.ToString("0") + " Pa");
            sb.AppendLine("Presión mín. terminal:" + report.MinTerminalPressurePa.ToString("0") + " Pa");
            sb.AppendLine("Errores / Avisos:     " + report.CountErrors + " / " + report.CountWarnings);
            sb.AppendLine();
            foreach (var m in report.AllMessages.Take(30))
                sb.AppendLine(m.ToString());
            HydSummaryText.Text = sb.ToString();

            int ok = rows.Count(r => r.StatusKind == "OK");
            HydStatsText.Text   = ok + "/" + rows.Count(r => !r.IsFitting) + " tramos OK"
                + (disconnected > 0 ? "  ·  ⚠ " + disconnected + " corte(s)" : "");
            HydFluidText.Text   = fluid.Label ?? "Agua";
            HydCriteriaText.Text = criteria.Description ?? criteria.Standard + " " + criteria.Service;
        }

        private void BindAerResults(CalcReport report, SizingCriteriaConfig criteria, FluidProperties fluid)
        {
            var rows = BuildResultRowsFromDuctBfs(_lastAerBfsOrder, report);
            rows = ReorderDfsBranchFirst(rows);
            _lastAerRows = rows;
            AerResultsGrid.ItemsSource = rows;

            _aerSchemaGlyphs.Clear();
            _aerHighlightedGlyph = null;
            _aerFilterRenderer = null;               // reiniciar renderer de filtros
            BuildSchematic(AerSchematicCanvas, AerSchematicScroll, rows, _aerSystemBrush,
                _aerSchemaGlyphs, ref _aerSchemaIndentPx);

            RegisterRowsForSync(rows);
            UpdateColumnWarningBadges(AerResultsGrid, rows);

            int disconnected = rows.Count(r => r.Disconnected);
            AerLocateDisconnectedButton.Visibility = disconnected > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            if (disconnected > 0)
                AerLocateDisconnectedButton.Content = "⚠ Localizar desconectados (" + disconnected + ")";

            var sb = new StringBuilder();
            if (disconnected > 0)
                sb.AppendLine("⚠ " + disconnected + " CORTE(S) EN LA RED — usa «Localizar desconectados» para ir al tramo exacto.");
            sb.AppendLine("Tramos calculados:    " + report.Segments.Count);
            sb.AppendLine("Worst-case ΔP:        " + report.WorstCasePressureDropPa.ToString("0") + " Pa");
            sb.AppendLine("Presión mín. terminal:" + report.MinTerminalPressurePa.ToString("0") + " Pa");
            sb.AppendLine("Errores / Avisos:     " + report.CountErrors + " / " + report.CountWarnings);
            sb.AppendLine();
            foreach (var m in report.AllMessages.Take(30))
                sb.AppendLine(m.ToString());
            AerSummaryText.Text = sb.ToString();

            int ok = rows.Count(r => r.StatusKind == "OK");
            AerStatsText.Text    = ok + "/" + rows.Count(r => !r.IsFitting) + " tramos OK"
                + (disconnected > 0 ? "  ·  ⚠ " + disconnected + " corte(s)" : "");
            AerFluidText.Text    = fluid.Label ?? "Aire";
            AerCriteriaText.Text = criteria.Description ?? criteria.Standard + " " + criteria.Service;
        }

        // ------------------------------------------------------------------ //
        //  Construcción de filas ResultRow desde BFS                          //
        // ------------------------------------------------------------------ //
        private static IList<ResultRow> BuildResultRowsFromPipeBfs(
            IList<PipeSegment> bfsOrder, CalcReport report)
        {
            var rows = new List<ResultRow>();
            if (bfsOrder == null) return rows;
            foreach (var seg in bfsOrder)
            {
                report.Segments.TryGetValue(seg.RevitId, out var r);
                var pipeRow = MakeRowFromPipe(seg, r);
                rows.Add(pipeRow);
                var fitRow = MakeFittingRowFromPipe(seg, pipeRow);
                if (fitRow != null) rows.Add(fitRow);
            }
            return rows;
        }

        private static IList<ResultRow> BuildResultRowsFromDuctBfs(
            IList<DuctSegment> bfsOrder, CalcReport report)
        {
            var rows = new List<ResultRow>();
            if (bfsOrder == null) return rows;
            foreach (var seg in bfsOrder)
            {
                report.Segments.TryGetValue(seg.RevitId, out var r);
                var ductRow = MakeRowFromDuct(seg, r);
                rows.Add(ductRow);
                var fitRow = MakeFittingRowFromDuct(seg, ductRow);
                if (fitRow != null) rows.Add(fitRow);
            }
            return rows;
        }

        // ------------------------------------------------------------------ //
        //  BFS sobre la red de tuberías / conductos                           //
        // ------------------------------------------------------------------ //
        private static List<PipeSegment> ComputePipeBfsOrder(PipeNetwork network)
        {
            var ordered = new List<PipeSegment>();
            if (network == null || network.SourceNode == null) return ordered;

            var nodeRank = new Dictionary<PipeNode, int>();
            int nextNodeRank = 1;
            nodeRank[network.SourceNode] = nextNodeRank++;
            if (string.IsNullOrEmpty(network.SourceNode.Label))
                network.SourceNode.Label = "Acometida";

            var visited = new HashSet<PipeNode> { network.SourceNode };
            var queue   = new Queue<PipeNode>();
            queue.Enqueue(network.SourceNode);
            int seqIndex = 0;
            var depthOf  = new Dictionary<PipeNode, int> { [network.SourceNode] = 0 };

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                int curDepth  = depthOf[node];
                int branchCount = node.ConnectedSegments.Count(
                    s => ReferenceEquals(s.NodeStart, node) && s.NodeEnd != null
                         && !visited.Contains(s.NodeEnd));
                // Sort children by descending flow so the trunk (highest flow) gets the lowest BfsIndex
                var childSegs = node.ConnectedSegments
                    .Where(s => ReferenceEquals(s.NodeStart, node) && s.NodeEnd != null && !visited.Contains(s.NodeEnd))
                    .OrderByDescending(s => s.FlowM3s)
                    .ToList();
                foreach (var seg in childSegs)
                {
                    var child = seg.NodeEnd;
                    if (!nodeRank.ContainsKey(child)) nodeRank[child] = nextNodeRank++;
                    seqIndex++;
                    seg.BfsIndex             = seqIndex;
                    seg.FromNodeId           = nodeRank[node];
                    seg.ToNodeId             = nodeRank[child];
                    seg.Depth                = curDepth + 1;
                    seg.IsSourceSegment      = (curDepth == 0);
                    seg.IsTerminalSegment    = child.IsTerminal;
                    seg.DownstreamBranchCount = branchCount;
                    depthOf[child]           = curDepth + 1;
                    visited.Add(child);
                    queue.Enqueue(child);
                    ordered.Add(seg);
                }
            }
            foreach (var kv in nodeRank) kv.Key.Id = kv.Value;

            // Tramos NO alcanzados por el BFS desde la fuente = desconectados.
            // Antes simplemente desaparecían del grid; ahora los añadimos al
            // final para que el usuario los vea (el motor los marca como
            // DISCONNECTED y la fila saldrá resaltada).
            var inOrder = new HashSet<PipeSegment>(ordered);
            foreach (var seg in network.Segments)
            {
                if (inOrder.Contains(seg)) continue;
                if (seg.FromNodeId == 0 && seg.NodeStart != null) seg.FromNodeId = seg.NodeStart.Id;
                if (seg.ToNodeId   == 0 && seg.NodeEnd   != null) seg.ToNodeId   = seg.NodeEnd.Id;
                ordered.Add(seg);
            }
            return ordered;
        }

        private static List<DuctSegment> ComputeDuctBfsOrder(DuctNetwork network)
        {
            var ordered = new List<DuctSegment>();
            if (network == null || network.SourceNode == null) return ordered;

            var nodeRank = new Dictionary<DuctNode, int>();
            int nextNodeRank = 1;
            nodeRank[network.SourceNode] = nextNodeRank++;
            if (string.IsNullOrEmpty(network.SourceNode.Label))
                network.SourceNode.Label = "Ventilador";

            var visited = new HashSet<DuctNode> { network.SourceNode };
            var queue   = new Queue<DuctNode>();
            queue.Enqueue(network.SourceNode);
            int seqIndex = 0;
            var depthOf  = new Dictionary<DuctNode, int> { [network.SourceNode] = 0 };

            while (queue.Count > 0)
            {
                var node      = queue.Dequeue();
                int curDepth  = depthOf[node];
                int branchCount = node.ConnectedSegments.Count(
                    s => ReferenceEquals(s.NodeStart, node) && s.NodeEnd != null
                         && !visited.Contains(s.NodeEnd));
                // Sort children by descending flow so the trunk (highest flow) gets the lowest BfsIndex
                var childSegs = node.ConnectedSegments
                    .Where(s => ReferenceEquals(s.NodeStart, node) && s.NodeEnd != null && !visited.Contains(s.NodeEnd))
                    .OrderByDescending(s => s.FlowM3s)
                    .ToList();
                foreach (var seg in childSegs)
                {
                    var child = seg.NodeEnd;
                    if (!nodeRank.ContainsKey(child)) nodeRank[child] = nextNodeRank++;
                    seqIndex++;
                    seg.BfsIndex             = seqIndex;
                    seg.FromNodeId           = nodeRank[node];
                    seg.ToNodeId             = nodeRank[child];
                    seg.Depth                = curDepth + 1;
                    seg.IsSourceSegment      = (curDepth == 0);
                    seg.IsTerminalSegment    = child.IsTerminal;
                    seg.DownstreamBranchCount = branchCount;
                    depthOf[child]           = curDepth + 1;
                    visited.Add(child);
                    queue.Enqueue(child);
                    ordered.Add(seg);
                }
            }
            foreach (var kv in nodeRank) kv.Key.Id = kv.Value;

            // Tramos desconectados de la fuente: añadir al final para que se vean.
            var inOrder = new HashSet<DuctSegment>(ordered);
            foreach (var seg in network.Segments)
            {
                if (inOrder.Contains(seg)) continue;
                if (seg.FromNodeId == 0 && seg.NodeStart != null) seg.FromNodeId = seg.NodeStart.Id;
                if (seg.ToNodeId   == 0 && seg.NodeEnd   != null) seg.ToNodeId   = seg.NodeEnd.Id;
                ordered.Add(seg);
            }
            return ordered;
        }

        // ------------------------------------------------------------------ //
        //  Construcción de ResultRow desde segmento + CalcResult              //
        // ------------------------------------------------------------------ //
        private static ResultRow MakeRowFromPipe(PipeSegment seg, CalcResult r)
        {
            var row = new ResultRow
            {
                RevitId       = seg.RevitId,
                BfsIndex      = seg.BfsIndex,
                FromNode      = "N" + seg.FromNodeId,
                ToNode        = "N" + seg.ToNodeId,
                NodePath      = "N" + seg.FromNodeId + " → N" + seg.ToNodeId,
                Depth         = seg.Depth,
                LevelName     = seg.LevelName ?? "",
                IsSource      = seg.IsSourceSegment,
                IsTerminal    = seg.IsTerminalSegment,
                BranchCount   = seg.DownstreamBranchCount,
                EndNodeKind   = ClassifyPipeEndNode(seg),
                Size          = "DN " + (seg.DiameterM * 1000.0).ToString("F0"),
                FixtureCount  = seg.DownstreamFixtureCount,
                KFactor       = seg.SimultaneityFactor,
                RawFlowLps    = seg.RawFlowM3s * 1000.0,
                FlowLps       = seg.FlowM3s * 1000.0,
                Velocity      = r?.VelocityMs ?? seg.VelocityMs,
                FrictionPaPm  = r?.FrictionPaPm ?? seg.FrictionPaPm,
                TotalLoss     = r?.TotalLossPa ?? seg.TotalLossPa,
                Reynolds      = r?.ReynoldsNumber ?? 0.0,
                RecommendedSize = r?.RecommendedSize ?? "—",
            };
            row.SetStatusFrom(r);
            return row;
        }

        private static ResultRow MakeRowFromDuct(DuctSegment seg, CalcResult r)
        {
            var row = new ResultRow
            {
                RevitId       = seg.RevitId,
                BfsIndex      = seg.BfsIndex,
                FromNode      = "N" + seg.FromNodeId,
                ToNode        = "N" + seg.ToNodeId,
                NodePath      = "N" + seg.FromNodeId + " → N" + seg.ToNodeId,
                Depth         = seg.Depth,
                LevelName     = seg.LevelName ?? "",
                IsSource      = seg.IsSourceSegment,
                IsTerminal    = seg.IsTerminalSegment,
                BranchCount   = seg.DownstreamBranchCount,
                EndNodeKind   = ClassifyDuctEndNode(seg),
                Size          = seg.SizeLabel,
                FlowLps       = seg.FlowM3s * 1000.0,
                Velocity      = r?.VelocityMs ?? seg.VelocityMs,
                FrictionPaPm  = r?.FrictionPaPm ?? seg.FrictionPaPm,
                TotalLoss     = r?.TotalLossPa ?? seg.TotalLossPa,
                Reynolds      = r?.ReynoldsNumber ?? 0.0,
                RecommendedSize = r?.RecommendedSize ?? seg.SizeLabel ?? "—",
            };
            row.SetStatusFrom(r);
            return row;
        }

        private static ResultRow MakeFittingRowFromPipe(PipeSegment seg, ResultRow pipeRow)
        {
            string kind = pipeRow?.EndNodeKind;
            if (string.IsNullOrEmpty(kind) || kind == "JUNCTION") return null;
            return new ResultRow
            {
                ElementKind = kind,
                EndNodeKind = kind,
                Depth       = seg.Depth,
                LevelName   = seg.LevelName ?? "",
                NodePath    = "@N" + seg.ToNodeId,
                BfsIndex    = 0,
                IsTerminal  = (kind == "TERMINAL"),
                BranchCount = pipeRow.BranchCount,
                Status      = "",
                StatusKind  = "",
            };
        }

        private static ResultRow MakeFittingRowFromDuct(DuctSegment seg, ResultRow ductRow)
        {
            string kind = ductRow?.EndNodeKind;
            if (string.IsNullOrEmpty(kind) || kind == "JUNCTION") return null;
            return new ResultRow
            {
                ElementKind = kind,
                EndNodeKind = kind,
                Depth       = seg.Depth,
                LevelName   = seg.LevelName ?? "",
                NodePath    = "@N" + seg.ToNodeId,
                BfsIndex    = 0,
                IsTerminal  = (kind == "TERMINAL"),
                BranchCount = ductRow.BranchCount,
                Status      = "",
                StatusKind  = "",
            };
        }

        // ------------------------------------------------------------------ //
        //  Clasificación del nudo final (topología, sin acceder a familias)    //
        // ------------------------------------------------------------------ //
        private static string ClassifyPipeEndNode(PipeSegment seg)
        {
            var end = seg?.NodeEnd;
            if (end == null) return "";
            if (end.IsTerminal && end.DemandM3s > 0.0) return "TERMINAL";
            int degree = end.ConnectedSegments.Count;
            if (degree == 1) return end.IsTerminal ? "TERMINAL" : "FIN";
            if (degree == 2)
            {
                var other = end.ConnectedSegments.FirstOrDefault(s => !ReferenceEquals(s, seg));
                if (other != null && Math.Abs(other.DiameterM - seg.DiameterM) > 1e-6)
                    return "REDUCER";
                return "ELBOW";
            }
            if (degree == 3) return "TEE";
            if (degree == 4) return "CROSS";
            return "JUNCTION";
        }

        private static string ClassifyDuctEndNode(DuctSegment seg)
        {
            var end = seg?.NodeEnd;
            if (end == null) return "";
            if (end.IsTerminal && end.DemandM3s > 0.0) return "TERMINAL";
            int degree = end.ConnectedSegments.Count;
            if (degree == 1) return end.IsTerminal ? "TERMINAL" : "FIN";
            if (degree == 2)
            {
                var other = end.ConnectedSegments.FirstOrDefault(s => !ReferenceEquals(s, seg));
                if (other != null)
                {
                    double a = SegSizeDuct(seg), b = SegSizeDuct(other);
                    if (a > 0 && b > 0 && Math.Abs(a - b) > 1e-6) return "REDUCER";
                }
                return "ELBOW";
            }
            if (degree == 3) return "TEE";
            if (degree == 4) return "CROSS";
            return "JUNCTION";
        }

        private static double SegSizeDuct(DuctSegment s)
        {
            if (s == null) return 0.0;
            return s.Shape == DuctShape.Circular
                ? s.DiameterM
                : Math.Max(s.WidthM, s.HeightM);
        }

        // ------------------------------------------------------------------ //
        //  Re-orden DFS branch-first                                          //
        // ------------------------------------------------------------------ //
        private static List<ResultRow> ReorderDfsBranchFirst(IList<ResultRow> rowsBfs)
        {
            var result = new List<ResultRow>();
            if (rowsBfs == null || rowsBfs.Count == 0) return result;

            var pipeRows = new List<ResultRow>();
            var fitOf    = new Dictionary<ResultRow, ResultRow>();
            for (int i = 0; i < rowsBfs.Count; i++)
            {
                var r = rowsBfs[i];
                if (r.IsFitting) continue;
                pipeRows.Add(r);
                if (i + 1 < rowsBfs.Count && rowsBfs[i + 1].IsFitting)
                    fitOf[r] = rowsBfs[i + 1];
            }

            var rowByOutgoing = new Dictionary<string, ResultRow>();
            var childrenOf    = new Dictionary<string, List<ResultRow>>();
            foreach (var p in pipeRows)
            {
                string outgoing = GetOutgoingNode(p.NodePath);
                if (!string.IsNullOrEmpty(outgoing)) rowByOutgoing[outgoing] = p;
                string incoming = GetIncomingNode(p.NodePath);
                if (!string.IsNullOrEmpty(incoming))
                {
                    if (!childrenOf.TryGetValue(incoming, out var lst))
                        childrenOf[incoming] = lst = new List<ResultRow>();
                    lst.Add(p);
                }
            }

            var rootPipes = pipeRows
                .Where(p =>
                {
                    string inc = GetIncomingNode(p.NodePath);
                    return string.IsNullOrEmpty(inc) || !rowByOutgoing.ContainsKey(inc);
                })
                .OrderBy(p => p.BfsIndex)
                .ToList();

            var visited = new HashSet<ResultRow>();
            void Dfs(ResultRow p)
            {
                if (!visited.Add(p)) return;
                result.Add(p);
                if (fitOf.TryGetValue(p, out var fit)) result.Add(fit);
                string outgoing = GetOutgoingNode(p.NodePath);
                if (string.IsNullOrEmpty(outgoing)) return;
                if (!childrenOf.TryGetValue(outgoing, out var children)) return;
                var sorted = children
                    .OrderBy(c => c.FlowLps)               // ramas (menor Q) primero → tronco al final
                    .ThenByDescending(c => c.BfsIndex)     // desempate por BFS inverso
                    .ToList();
                foreach (var c in sorted) Dfs(c);
            }
            foreach (var root in rootPipes) Dfs(root);

            // Pipes no alcanzados (red desconectada) al final
            foreach (var p in pipeRows)
            {
                if (visited.Contains(p)) continue;
                result.Add(p);
                if (fitOf.TryGetValue(p, out var fit)) result.Add(fit);
            }
            return result;
        }

        // ------------------------------------------------------------------ //
        //  Columnas por tronco/rama (para el esquema)                         //
        // ------------------------------------------------------------------ //
        private static Dictionary<ResultRow, int> ComputeColumnsByTrunk(IList<ResultRow> rows)
        {
            var columnOf = new Dictionary<ResultRow, int>();
            if (rows == null || rows.Count == 0) return columnOf;

            var rowByOutgoing    = new Dictionary<string, ResultRow>();
            var pipesIncomingAt  = new Dictionary<string, List<ResultRow>>();
            foreach (var row in rows)
            {
                if (row.IsFitting) continue;
                string outgoing = GetOutgoingNode(row.NodePath);
                if (!string.IsNullOrEmpty(outgoing)) rowByOutgoing[outgoing] = row;
                string incoming = GetIncomingNode(row.NodePath);
                if (!string.IsNullOrEmpty(incoming))
                {
                    if (!pipesIncomingAt.TryGetValue(incoming, out var list))
                        pipesIncomingAt[incoming] = list = new List<ResultRow>();
                    list.Add(row);
                }
            }

            foreach (var row in rows)
            {
                int col;
                if (row.IsFitting)
                {
                    string atNode = GetIncomingNode(row.NodePath);
                    if (!string.IsNullOrEmpty(atNode)
                        && rowByOutgoing.TryGetValue(atNode, out var endingPipe)
                        && columnOf.TryGetValue(endingPipe, out int pipeCol))
                        col = pipeCol;
                    else
                        col = 0;
                }
                else
                {
                    string incoming = GetIncomingNode(row.NodePath);
                    if (string.IsNullOrEmpty(incoming)
                        || !rowByOutgoing.TryGetValue(incoming, out var parentRow)
                        || !columnOf.TryGetValue(parentRow, out int parentCol))
                    {
                        col = 0;
                    }
                    else if (!pipesIncomingAt.TryGetValue(incoming, out var siblings)
                             || siblings.Count <= 1)
                    {
                        col = parentCol;
                    }
                    else
                    {
                        var ordered = siblings
                            .OrderByDescending(r => r.FlowLps)
                            .ThenByDescending(r => r.FixtureCount)
                            .ThenBy(r => r.BfsIndex)
                            .ToList();
                        int rank = ordered.IndexOf(row);
                        col = (rank == 0) ? parentCol : parentCol + 1;
                    }
                }
                columnOf[row] = col;
            }
            return columnOf;
        }

        // ------------------------------------------------------------------ //
        //  Nodo entrante / saliente desde NodePath                            //
        // ------------------------------------------------------------------ //
        private static string GetIncomingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0) return nodePath.Substring(0, idx).Trim();
            return nodePath.Trim();
        }

        private static string GetOutgoingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0 && idx + 1 < nodePath.Length)
                return nodePath.Substring(idx + 1).Trim();
            return nodePath.Trim();
        }

        // ------------------------------------------------------------------ //
        //  Esquema árbol (canvas WPF)                                         //
        // ------------------------------------------------------------------ //
        private void BuildSchematic(
            Canvas canvas,
            ScrollViewer scroll,
            IList<ResultRow> rows,
            Brush systemBrush,
            Dictionary<ResultRow, FrameworkElement> glyphMap,
            ref double indentPx)
        {
            canvas.Children.Clear();
            if (glyphMap != null) glyphMap.Clear();

            // Pick the correct line-map and clear it for this rebuild
            Dictionary<ResultRow, WpfLine> lineMap = null;
            if (ReferenceEquals(canvas, HydSchematicCanvas))
            {
                lineMap = _hydSchemaLines;
                lineMap.Clear();
            }
            else if (ReferenceEquals(canvas, AerSchematicCanvas))
            {
                lineMap = _aerSchemaLines;
                lineMap.Clear();
            }
            if (rows == null || rows.Count == 0) return;

            var columnOf = ComputeColumnsByTrunk(rows);
            int maxCol   = columnOf.Values.DefaultIfEmpty(0).Max();

            double availableWidth = scroll?.ViewportWidth ?? 0;
            if (availableWidth < 150) availableWidth = canvas.Width;
            if (availableWidth < 150) availableWidth = 460;
            if (maxCol >= 1)
            {
                double targetIndent = (availableWidth - SchemaTrunkX - SchemaLabelReserve)
                                      / Math.Max(1, maxCol);
                indentPx = Math.Min(SchemaIndentMax, Math.Max(SchemaIndentMin, targetIndent));
            }
            else
            {
                indentPx = 40.0;
            }

            // Marcador de fuente
            double topY = SchemaTopPad;
            var srcMarker = new WpfPolygon
            {
                Points = new PointCollection
                {
                    new WpfPoint(SchemaTrunkX - 6, topY - 6),
                    new WpfPoint(SchemaTrunkX + 6, topY - 6),
                    new WpfPoint(SchemaTrunkX,     topY + 4),
                },
                Fill = systemBrush,
                Stroke = Brushes.Black,
                StrokeThickness = 0.8,
                ToolTip = "Fuente / Acometida",
            };
            canvas.Children.Add(srcMarker);
            var srcLabel = new TextBlock
            {
                Text = "Fuente", FontSize = 10, Foreground = systemBrush,
            };
            Canvas.SetLeft(srcLabel, SchemaTrunkX + 10);
            Canvas.SetTop(srcLabel, topY - 8);
            canvas.Children.Add(srcLabel);

            var nodePositions = new Dictionary<string, (double X, double Y, int Col)>();
            var fittingByNode = new Dictionary<string, ResultRow>();
            foreach (var r in rows)
            {
                if (!r.IsFitting) continue;
                string at = GetIncomingNode(r.NodePath);
                if (!string.IsNullOrEmpty(at)) fittingByNode[at] = r;
            }

            var teeJunctions = new List<(double X, double Y, ResultRow ParentFit)>();

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                double y   = SchemaTopPad + (i + 1) * SchemaRowHeight;
                int col    = columnOf.TryGetValue(row, out int c) ? c : 0;
                double x   = SchemaTrunkX + col * indentPx;

                string incoming = GetIncomingNode(row.NodePath);
                string outgoing = GetOutgoingNode(row.NodePath);

                double pX, pY;
                if (!string.IsNullOrEmpty(incoming)
                    && nodePositions.TryGetValue(incoming, out var pp))
                { pX = pp.X; pY = pp.Y; }
                else
                { pX = SchemaTrunkX; pY = topY + 4; }

                bool isContinuation = Math.Abs(pX - x) < 0.1;
                double trunkThick   = col == 0 ? 3.0 : (col == 1 ? 2.4 : 2.0);

                if (isContinuation)
                {
                    var pipeLine = new WpfLine
                    {
                        X1 = x, Y1 = pY, X2 = x, Y2 = y,
                        Stroke = systemBrush,
                        StrokeThickness = row.IsFitting ? trunkThick * 0.5 : trunkThick,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    canvas.Children.Add(pipeLine);
                    if (!row.IsFitting && lineMap != null) lineMap[row] = pipeLine;
                }
                else
                {
                    var branchLine = new WpfLine
                    {
                        X1 = pX, Y1 = y, X2 = x, Y2 = y,
                        Stroke = systemBrush,
                        StrokeThickness = 1.4,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    canvas.Children.Add(branchLine);
                    if (!row.IsFitting && lineMap != null) lineMap[row] = branchLine;
                    ResultRow parentFit = null;
                    if (!string.IsNullOrEmpty(incoming))
                        fittingByNode.TryGetValue(incoming, out parentFit);
                    teeJunctions.Add((pX, y, parentFit));
                }

                if (!string.IsNullOrEmpty(outgoing))
                    nodePositions[outgoing] = (x, y, col);

                FrameworkElement glyph;
                string lblText;
                if (row.IsFitting)
                {
                    string tip  = (row.EndNodeKind ?? "?") + "  " + row.NodePath
                                + (string.IsNullOrEmpty(row.LevelName) ? "" : "\nNivel: " + row.LevelName);
                    string kind = row.EndNodeKind ?? "";
                    bool isBifFit = (kind == "TEE" || kind == "CROSS");
                    glyph   = isBifFit
                        ? null
                        : DrawNodeGlyph(canvas, row, x + SchemaGlyphOffset, y, systemBrush, tip);
                    lblText = (row.EndNodeKind ?? "") + " " + (row.NodePath ?? "");
                }
                else
                {
                    var arrow = new WpfPolygon
                    {
                        Points = new PointCollection
                        {
                            new WpfPoint(x - 3, y - 3),
                            new WpfPoint(x + 3, y - 3),
                            new WpfPoint(x,     y + 3),
                        },
                        Fill = systemBrush,
                        Stroke = Brushes.Black, StrokeThickness = 0.5,
                        ToolTip = "Tramo #" + row.BfsIndex + "  " + row.NodePath
                                + (string.IsNullOrEmpty(row.LevelName) ? "" : "\nNivel: " + row.LevelName)
                                + "\n" + (row.Status ?? ""),
                        Tag = row,
                    };
                    // Capturar canvas y glyphMap para el click handler
                    var captCanvas  = canvas;
                    var captGlyphs  = glyphMap;
                    var captGrid    = canvas == HydSchematicCanvas ? HydResultsGrid : AerResultsGrid;
                    arrow.MouseLeftButtonDown += (s, ev) => OnGlyphClicked(s, ev, captGrid);
                    canvas.Children.Add(arrow);
                    glyph = arrow;

                    var sb2 = new StringBuilder();
                    sb2.Append("#").Append(row.BfsIndex);
                    if (!string.IsNullOrEmpty(row.NodePath))
                        sb2.Append(" · ").Append(row.NodePath);
                    if (!string.IsNullOrEmpty(row.Size))
                        sb2.Append(" · ").Append(row.Size);
                    sb2.Append(" · ").Append(row.FlowLps.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture)).Append(" L/s");
                    lblText = sb2.ToString();
                }

                if (glyphMap != null && glyph != null) glyphMap[row] = glyph;

                if (!string.IsNullOrEmpty(lblText))
                {
                    var lbl = new TextBlock
                    {
                        Text       = lblText,
                        FontSize   = row.IsFitting ? 9 : 10,
                        Foreground = row.IsFitting
                            ? new SolidColorBrush(WpfColor.FromRgb(0x66, 0x66, 0x66))
                            : Brushes.Black,
                        FontStyle  = row.IsFitting ? FontStyles.Italic : FontStyles.Normal,
                    };
                    Canvas.SetLeft(lbl, x + (row.IsFitting ? SchemaLabelPadFitting : SchemaLabelPad));
                    Canvas.SetTop(lbl, y - 8);
                    canvas.Children.Add(lbl);
                }
            }

            // Marcadores de bifurcación (TEE/CROSS)
            var fittingDrawnAtJunction = new HashSet<ResultRow>();
            foreach (var (jx, jy, parentFit) in teeJunctions)
            {
                bool useFitGlyph = parentFit != null
                    && (parentFit.EndNodeKind == "TEE" || parentFit.EndNodeKind == "CROSS")
                    && !fittingDrawnAtJunction.Contains(parentFit);
                if (useFitGlyph)
                {
                    string tip2 = (parentFit.EndNodeKind ?? "?") + "  " + parentFit.NodePath
                                + (string.IsNullOrEmpty(parentFit.LevelName) ? "" : "\nNivel: " + parentFit.LevelName);
                    var fitGlyph = DrawNodeGlyph(canvas, parentFit, jx, jy, systemBrush, tip2);
                    if (glyphMap != null) glyphMap[parentFit] = fitGlyph;
                    fittingDrawnAtJunction.Add(parentFit);
                }
                else
                {
                    var teeMark = new WpfEllipse
                    {
                        Width = 8, Height = 8,
                        Fill = systemBrush,
                        Stroke = Brushes.White, StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(teeMark, jx - 4);
                    Canvas.SetTop(teeMark, jy - 4);
                    canvas.Children.Add(teeMark);
                }
            }

            double naturalWidth = SchemaTrunkX + maxCol * indentPx + SchemaLabelReserve;
            double panelWidth   = scroll?.ViewportWidth ?? 0;
            double targetWidth  = (panelWidth > 50) ? panelWidth - 4 : 460;
            canvas.Width  = Math.Max(targetWidth, naturalWidth);
            canvas.Height = SchemaTopPad + (rows.Count + 1) * SchemaRowHeight + 20;
        }

        // Rerender al cambiar tamaño del panel esquema
        private void OnSchematicScrollResized(
            Canvas canvas, ScrollViewer scroll,
            IList<ResultRow> rows, Brush brush,
            Dictionary<ResultRow, FrameworkElement> glyphs,
            ref FrameworkElement highlighted,
            ref double indentPx)
        {
            if (rows == null || rows.Count == 0) return;
            highlighted = null;
            BuildSchematic(canvas, scroll, rows, brush, glyphs, ref indentPx);

            // Restaurar overlay de camino crítico si estaba activo
            if (_activeCriticalRevitIds != null && _activeCriticalRevitIds.Count > 0)
            {
                bool isHyd = ReferenceEquals(canvas, HydSchematicCanvas);
                ApplyCriticalPathOverlay(
                    canvas,
                    isHyd ? _hydSchemaLines : _aerSchemaLines,
                    rows,
                    _activeCriticalRevitIds);
            }
        }

        // ------------------------------------------------------------------ //
        //  Glyphs de nudo                                                      //
        // ------------------------------------------------------------------ //
        private FrameworkElement DrawNodeGlyph(Canvas canvas, ResultRow row,
            double x, double y, Brush color, string tip)
        {
            FrameworkElement primary;
            var captGrid = canvas == HydSchematicCanvas ? HydResultsGrid : AerResultsGrid;
            switch (row.EndNodeKind)
            {
                case "TERMINAL":
                {
                    var sq = new WpfRectangle
                    {
                        Width = 11, Height = 11, Fill = color,
                        Stroke = Brushes.Black, StrokeThickness = 0.8,
                        ToolTip = tip, Tag = row,
                    };
                    Canvas.SetLeft(sq, x - 5.5); Canvas.SetTop(sq, y - 5.5);
                    sq.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(sq);
                    primary = sq;
                    break;
                }
                case "TEE":
                {
                    var tee = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} M {4} {5} L {6} {7}",
                            x - 7, y, x + 7, y, x, y, x, y + 7)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    tee.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(tee);
                    primary = tee;
                    break;
                }
                case "CROSS":
                {
                    var cross = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} M {4} {5} L {6} {7}",
                            x - 7, y, x + 7, y, x, y - 7, x, y + 7)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    cross.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(cross);
                    primary = cross;
                    break;
                }
                case "ELBOW":
                {
                    var elbow = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} L {4} {5}",
                            x, y - 6, x, y, x + 6, y)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    elbow.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(elbow);
                    primary = elbow;
                    break;
                }
                case "REDUCER":
                {
                    var trap = new WpfPolygon
                    {
                        Points = new PointCollection
                        {
                            new WpfPoint(x - 7, y - 4), new WpfPoint(x + 7, y - 2),
                            new WpfPoint(x + 7, y + 2), new WpfPoint(x - 7, y + 4),
                        },
                        Fill = Brushes.White, Stroke = color, StrokeThickness = 1.6,
                        ToolTip = tip, Tag = row,
                    };
                    trap.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(trap);
                    primary = trap;
                    break;
                }
                case "FIN":
                {
                    var cap = new WpfLine
                    {
                        X1 = x - 6, X2 = x + 6, Y1 = y, Y2 = y,
                        Stroke = color, StrokeThickness = 3.0,
                        ToolTip = tip, Tag = row,
                    };
                    cap.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(cap);
                    primary = cap;
                    break;
                }
                default:
                {
                    var dot = new WpfEllipse
                    {
                        Width = 8, Height = 8, Fill = color,
                        Stroke = Brushes.Black, StrokeThickness = 0.6,
                        ToolTip = tip, Tag = row,
                    };
                    Canvas.SetLeft(dot, x - 4); Canvas.SetTop(dot, y - 4);
                    dot.MouseLeftButtonDown += (s, e) => OnGlyphClicked(s, e, captGrid);
                    canvas.Children.Add(dot);
                    primary = dot;
                    break;
                }
            }
            return primary;
        }

        private void OnGlyphClicked(object sender, MouseButtonEventArgs e, DataGrid targetGrid)
        {
            if (sender is FrameworkElement fe && fe.Tag is ResultRow row)
            {
                targetGrid.SelectedItem = row;
                targetGrid.ScrollIntoView(row);
            }
        }

        // ------------------------------------------------------------------ //
        //  Selección en el grid → highlight en esquema + sync a Revit         //
        // ------------------------------------------------------------------ //
        private void OnHydGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HighlightGlyph(HydResultsGrid, _hydSchemaGlyphs, HydSchematicScroll,
                ref _hydHighlightedGlyph);
            UpdateHydSizeEditor();
            if (!_suppressUiEvent)
                ScheduleRevitSelect(HydResultsGrid, zoom: false);
        }

        private void OnAerGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HighlightGlyph(AerResultsGrid, _aerSchemaGlyphs, AerSchematicScroll,
                ref _aerHighlightedGlyph);
            if (!_suppressUiEvent)
                ScheduleRevitSelect(AerResultsGrid, zoom: false);
        }

        private void OnHydRowDoubleClick(object sender, MouseButtonEventArgs e)
            => ScheduleRevitSelect(HydResultsGrid, zoom: true);

        private void OnAerRowDoubleClick(object sender, MouseButtonEventArgs e)
            => ScheduleRevitSelect(AerResultsGrid, zoom: true);

        private void HighlightGlyph(
            DataGrid grid,
            Dictionary<ResultRow, FrameworkElement> glyphs,
            ScrollViewer scroll,
            ref FrameworkElement highlighted)
        {
            if (highlighted != null)
            {
                highlighted.Effect = null;
                highlighted = null;
            }
            if (grid.SelectedItem is ResultRow row && glyphs.TryGetValue(row, out var glyph))
            {
                glyph.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = WpfColors.DodgerBlue,
                    BlurRadius  = 12,
                    ShadowDepth = 0,
                    Opacity     = 0.9,
                };
                highlighted = glyph;
                double top = Canvas.GetTop(glyph);
                if (!double.IsNaN(top) && scroll != null)
                    scroll.ScrollToVerticalOffset(
                        Math.Max(0, top - scroll.ViewportHeight / 2));
            }
        }

        // ------------------------------------------------------------------ //
        //  Menú contextual — visibilidad de columnas                          //
        // ------------------------------------------------------------------ //
        private void OnHydColumnVisibilityMenuOpened(object sender, RoutedEventArgs e)
            => BuildColumnVisibilityMenu(sender as WpfContextMenu, HydResultsGrid);

        private void OnAerColumnVisibilityMenuOpened(object sender, RoutedEventArgs e)
            => BuildColumnVisibilityMenu(sender as WpfContextMenu, AerResultsGrid);

        private void BuildColumnVisibilityMenu(WpfContextMenu menu, DataGrid grid)
        {
            if (menu == null || grid == null) return;
            menu.Items.Clear();

            // Acciones de sincronización Revit (si hay sync activa)
            if (_externalBus != null && _rowToElem.Count > 0)
            {
                var showItem = new WpfMenuItem { Header = "Ver en Revit (zoom)" };
                showItem.Click += (s, a) =>
                {
                    if (grid.SelectedItem is ResultRow row && _rowToElem.TryGetValue(row, out var eid))
                        ScheduleRevitAction(uiApp =>
                        {
                            uiApp.ActiveUIDocument?.Selection?.SetElementIds(new List<ElementId> { eid });
                            uiApp.ActiveUIDocument?.ShowElements(eid);
                        });
                };
                menu.Items.Add(showItem);

                var selItem = new WpfMenuItem { Header = "Seleccionar en Revit" };
                selItem.Click += (s, a) =>
                {
                    if (grid.SelectedItem is ResultRow row && _rowToElem.TryGetValue(row, out var eid))
                        ScheduleRevitAction(uiApp =>
                            uiApp.ActiveUIDocument?.Selection?.SetElementIds(new List<ElementId> { eid }));
                };
                menu.Items.Add(selItem);
                menu.Items.Add(new WpfSeparator());
            }

            // Checkbox por columna
            foreach (var col in grid.Columns)
            {
                string header = col.Header?.ToString() ?? "(sin nombre)";
                var item = new WpfMenuItem
                {
                    Header          = header,
                    IsCheckable     = true,
                    IsChecked       = col.Visibility == WpfVisibility.Visible,
                    StaysOpenOnClick = true,
                };
                var capturedCol = col;
                item.Click += (s, a) =>
                {
                    capturedCol.Visibility = ((WpfMenuItem)s).IsChecked
                        ? WpfVisibility.Visible
                        : WpfVisibility.Collapsed;
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new WpfSeparator());

            var showAll = new WpfMenuItem { Header = "Mostrar todas" };
            showAll.Click += (s, a) =>
            {
                foreach (var col in grid.Columns)
                    col.Visibility = WpfVisibility.Visible;
            };
            menu.Items.Add(showAll);

            var hideAll = new WpfMenuItem { Header = "Ocultar todas excepto #" };
            hideAll.Click += (s, a) =>
            {
                foreach (var col in grid.Columns)
                    col.Visibility = (col.Header?.ToString() == "#")
                        ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            };
            menu.Items.Add(hideAll);
        }

        // ------------------------------------------------------------------ //
        //  Badges de aviso en cabeceras de columna                            //
        // ------------------------------------------------------------------ //
        /// <summary>
        /// Añade "⚠" al header de las columnas que tengan alguna fila con Warning/Error.
        /// Primero limpia cualquier badge previo (strip ⚠ anterior).
        /// </summary>
        private static void UpdateColumnWarningBadges(DataGrid grid, IList<ResultRow> rows)
        {
            if (grid == null || rows == null) return;

            // Columnas con advertencias: v, ΔP/m, ΔP tot, Estado
            var warnRows = new HashSet<ResultRow>(
                rows.Where(r => r.StatusKind == "Warning" || r.StatusKind == "Error"));

            foreach (var col in grid.Columns)
            {
                string hdr = col.Header?.ToString() ?? "";
                // Quitar badge previo
                if (hdr.EndsWith(" ⚠")) hdr = hdr.Substring(0, hdr.Length - 2);

                bool addBadge = false;
                if (warnRows.Count > 0)
                {
                    // Columnas de métricas que se ven afectadas por avisos
                    addBadge = hdr == "v [m/s]"     || hdr == "v (m/s)"
                            || hdr == "ΔP/m [Pa/m]" || hdr == "ΔP/m (Pa/m)"
                            || hdr == "ΔP tot [Pa]"  || hdr == "ΔP tot (Pa)"
                            || hdr == "Estado";
                }
                col.Header = addBadge ? hdr + " ⚠" : hdr;
            }
        }

        // ------------------------------------------------------------------ //
        //  Sincronización Revit ↔ grid                                        //
        // ------------------------------------------------------------------ //
        private void RegisterRowsForSync(IList<ResultRow> rows)
        {
            _rowToElem.Clear();
            _revitIdToRow.Clear();
            if (rows == null) return;
            foreach (var r in rows)
            {
                if (r == null || r.IsFitting || r.RevitId <= 0) continue;
                var eid = new ElementId((long)r.RevitId);
                _rowToElem[r]        = eid;
                _revitIdToRow[r.RevitId] = r;
            }
        }

        private void ScheduleRevitSelect(DataGrid grid, bool zoom)
        {
            if (_externalBus == null) return;
            if (!(grid.SelectedItem is ResultRow row)) return;
            if (!_rowToElem.TryGetValue(row, out var eid)) return;
            ScheduleRevitAction(uiApp =>
            {
                try
                {
                    _suppressRevitEvent = true;
                    var sel = uiApp.ActiveUIDocument?.Selection;
                    sel?.SetElementIds(new List<ElementId> { eid });
                    if (zoom) uiApp.ActiveUIDocument?.ShowElements(eid);
                }
                finally { _suppressRevitEvent = false; }
            });
        }

        private void ScheduleRevitAction(Action<UIApplication> action)
        {
            _externalBus?.Enqueue(action);
        }

        // Revit → grid (viene de hilo API de Revit)
        private void OnRevitSelectionChanged(object sender,
            Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
        {
            if (_suppressRevitEvent) return;
            try
            {
                // Leemos la selección activa en el hilo de Revit (igual que el
                // antiguo RevitSyncService / RevitEventBridge).
                var sel = _uiApp.ActiveUIDocument?.Selection?.GetElementIds();
                _pendingRevitSelection = sel != null ? new List<ElementId>(sel) : null;
            }
            catch { return; }
            Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)StartRevitSelThrottle);
        }

        private void StartRevitSelThrottle()
        {
            _revitSelThrottle.Stop();
            _revitSelThrottle.Start();
        }

        private void OnRevitSelThrottleTick(object sender, EventArgs e)
        {
            _revitSelThrottle.Stop();
            var ids = _pendingRevitSelection;
            if (ids == null || ids.Count == 0) return;
            ApplyRevitSelectionToGrids(ids);
        }

        private void ApplyRevitSelectionToGrids(IList<ElementId> ids)
        {
            ResultRow found = null;
            foreach (var eid in ids)
            {
                long id = eid.Value;
                if (_revitIdToRow.TryGetValue(id, out var row))
                { found = row; break; }
            }
            if (found == null) return;

            _suppressUiEvent = true;
            try
            {
                // Actualizar ambos grids si la fila pertenece a alguno
                foreach (var grid in new[] { HydResultsGrid, AerResultsGrid })
                {
                    if (grid.ItemsSource is IList<ResultRow> list && list.Contains(found))
                    {
                        grid.SelectedItem = found;
                        grid.ScrollIntoView(found);
                    }
                }
            }
            finally { _suppressUiEvent = false; }

            // Highlight en el esquema correcto
            if (HydResultsGrid.ItemsSource is IList<ResultRow> hydList && hydList.Contains(found))
                HighlightGlyph(HydResultsGrid, _hydSchemaGlyphs, HydSchematicScroll,
                    ref _hydHighlightedGlyph);
            if (AerResultsGrid.ItemsSource is IList<ResultRow> aerList && aerList.Contains(found))
                HighlightGlyph(AerResultsGrid, _aerSchemaGlyphs, AerSchematicScroll,
                    ref _aerHighlightedGlyph);
        }

        // ------------------------------------------------------------------ //
        //  Hover highlight — grid → esquema inline                           //
        // ------------------------------------------------------------------ //
        private void OnHydGridMouseMove(object sender, MouseEventArgs e)
        {
            var row = GetRowUnderMouse(HydResultsGrid, e);
            if (ReferenceEquals(row, _hydHoveredRow)) return;
            _hydHoveredRow = row;
            _hydHoverTimer.Stop();
            if (row != null) _hydHoverTimer.Start();
            else ClearHoverHighlight(HydSchematicCanvas, _hydSchemaLines, _hydSystemBrush);
        }

        private void OnHydGridMouseLeave(object sender, MouseEventArgs e)
        {
            _hydHoveredRow = null;
            _hydHoverTimer.Stop();
            ClearHoverHighlight(HydSchematicCanvas, _hydSchemaLines, _hydSystemBrush);
        }

        private void OnAerGridMouseMove(object sender, MouseEventArgs e)
        {
            var row = GetRowUnderMouse(AerResultsGrid, e);
            if (ReferenceEquals(row, _aerHoveredRow)) return;
            _aerHoveredRow = row;
            _aerHoverTimer.Stop();
            if (row != null) _aerHoverTimer.Start();
            else ClearHoverHighlight(AerSchematicCanvas, _aerSchemaLines, _aerSystemBrush);
        }

        private void OnAerGridMouseLeave(object sender, MouseEventArgs e)
        {
            _aerHoveredRow = null;
            _aerHoverTimer.Stop();
            ClearHoverHighlight(AerSchematicCanvas, _aerSchemaLines, _aerSystemBrush);
        }

        private void OnHydHoverTick(object sender, EventArgs e)
        {
            _hydHoverTimer.Stop();
            ApplyHoverHighlight(HydSchematicCanvas, _hydSchemaLines,
                _hydHoveredRow, _hydSystemBrush, _activeCriticalRevitIds);
        }

        private void OnAerHoverTick(object sender, EventArgs e)
        {
            _aerHoverTimer.Stop();
            ApplyHoverHighlight(AerSchematicCanvas, _aerSchemaLines,
                _aerHoveredRow, _aerSystemBrush, _activeCriticalRevitIds);
        }

        private static ResultRow GetRowUnderMouse(DataGrid grid, MouseEventArgs e)
        {
            var hit = grid.InputHitTest(e.GetPosition(grid)) as DependencyObject;
            while (hit != null && !(hit is DataGridRow))
                hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            return (hit as DataGridRow)?.Item as ResultRow;
        }

        /// <summary>
        /// Resalta la línea de esquema correspondiente a <paramref name="row"/> en azul.
        /// Las líneas que no están en el hover recuperan el color base del sistema
        /// (<paramref name="systemBrush"/>).  Si hay un overlay de camino crítico activo
        /// (<paramref name="criticalIds"/>), ese overlay prevalece sobre el reset normal.
        /// </summary>
        private static void ApplyHoverHighlight(
            Canvas canvas,
            Dictionary<ResultRow, WpfLine> lineMap,
            ResultRow row,
            Brush systemBrush,
            HashSet<long> criticalIds = null)
        {
            if (canvas == null || lineMap == null) return;

            var criticalBrush = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x7B, 0x00));
            var dimBrush      = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC));

            foreach (var kv in lineMap)
            {
                if (ReferenceEquals(kv.Key, row)) continue;   // lo tratamos después

                bool isCritical = criticalIds != null && criticalIds.Contains(kv.Key.RevitId);
                var  line = kv.Value;

                if (isCritical)
                {
                    line.Stroke          = criticalBrush;
                    line.StrokeThickness = 3.5;
                    line.StrokeDashArray = null;
                    line.Effect          = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = WpfColor.FromRgb(0xFF, 0xA0, 0x00),
                        ShadowDepth = 0, BlurRadius = 10, Opacity = 0.85
                    };
                }
                else if (criticalIds != null && criticalIds.Count > 0)
                {
                    // Hay overlay activo pero esta línea no es crítica → atenuar
                    line.Stroke          = dimBrush;
                    line.StrokeThickness = 1.2;
                    line.Effect          = null;
                }
                else
                {
                    // Sin overlay — volver al color del sistema
                    line.Stroke          = systemBrush;
                    line.StrokeThickness = kv.Key.IsFitting ? 1.0 : 2.0;
                    line.StrokeDashArray = null;
                    line.Effect          = null;
                }
            }

            // Ahora la fila bajo el cursor
            if (row != null && lineMap.TryGetValue(row, out var target))
            {
                target.Stroke          = new SolidColorBrush(WpfColors.DodgerBlue);
                target.StrokeThickness = 3.5;
                target.StrokeDashArray = null;
                target.Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = WpfColors.DodgerBlue, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.9
                };
            }
        }

        private void ClearHoverHighlight(
            Canvas canvas,
            Dictionary<ResultRow, WpfLine> lineMap,
            Brush systemBrush)
        {
            ApplyHoverHighlight(canvas, lineMap, null, systemBrush, _activeCriticalRevitIds);
        }

        // ------------------------------------------------------------------ //
        //  Overlay camino crítico en el esquema inline                        //
        // ------------------------------------------------------------------ //
        private static void ApplyCriticalPathOverlay(
            Canvas canvas,
            Dictionary<ResultRow, WpfLine> lineMap,
            IList<ResultRow> allRows,
            HashSet<long> criticalRevitIds)
        {
            if (canvas == null || lineMap == null || criticalRevitIds == null) return;

            var criticalBrush = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x7B, 0x00)); // orange
            var dimBrush      = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC));

            foreach (var kv in lineMap)
            {
                bool isCritical = criticalRevitIds.Contains(kv.Key.RevitId);
                var line = kv.Value;
                if (isCritical)
                {
                    line.Stroke          = criticalBrush;
                    line.StrokeThickness = 3.5;
                    line.StrokeDashArray = null;
                    line.Effect          = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color       = WpfColor.FromRgb(0xFF, 0xA0, 0x00),
                        ShadowDepth = 0,
                        BlurRadius  = 10,
                        Opacity     = 0.85
                    };
                }
                else
                {
                    line.Stroke          = dimBrush;
                    line.StrokeThickness = 1.2;
                    line.Effect          = null;
                }
            }
        }

        // CalcExternalEventBus extraída a RevitBridge/CalcExternalEventBus.cs

        // ------------------------------------------------------------------ //
        //  CSV export                                                          //
        // ------------------------------------------------------------------ //
        private void ExportReportToCsv(CalcReport report, string tag)
        {
            if (report == null) { ShowError("Ejecuta el cálculo primero."); return; }
            var path = PickSavePath("calc_mep_" + tag);
            if (path == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("Id Revit;Nivel;DN recomendado;Q (l/s);v (m/s);dP/m (Pa/m);dP total (Pa);Re;OK");
            foreach (var s in report.Segments.Values)
            {
                sb.AppendLine(string.Join(";",
                    s.RevitElementId,
                    s.LevelName ?? "",
                    s.RecommendedSize ?? "",
                    (s.FlowM3s * 1000.0).ToString("0.000"),
                    s.VelocityMs.ToString("0.00"),
                    s.FrictionPaPm.ToString("0.0"),
                    s.TotalLossPa.ToString("0"),
                    s.ReynoldsNumber.ToString("0"),
                    s.IsOk ? "SI" : "NO"));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            SetStatus("CSV exportado: " + path);
        }

        private string PickSavePath(string defaultName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Guardar CSV",
                Filter     = "CSV (*.csv)|*.csv",
                FileName   = defaultName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv",
                DefaultExt = ".csv",
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ------------------------------------------------------------------ //
        //  Misc helpers                                                        //
        // ------------------------------------------------------------------ //
        private T GetSelectedSystem<T>(WpfComboBox combo) where T : class
            => (combo.SelectedItem as ComboBoxItem)?.Tag as T;

        private string GetSelectedPresetKey(WpfComboBox combo)
            => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

        private static FluidProperties ChooseHydFluid(string presetKey)
        {
            switch (presetKey)
            {
                case "CTE_ACS":      return FluidProperties.Water60C;
                case "RITE_HEATING": return FluidProperties.Water80C;
                case "CTE_RECIRC":   return FluidProperties.Water60C;
                default:             return FluidProperties.Water20C;
            }
        }

        private static void ApplySourceFallback(PipeNetwork network)
        {
            if (network.SourceNode != null) return;
            if (network.TerminalNodes.Count == 0) return;
            // Heurística fuente para redes de suministro a presión:
            // Los nodos sin demanda (DemandM3s <= 0) son extremos abiertos,
            // candidatos a ser la acometida.  De entre ellos, el de MENOR cota
            // es el que entra al edificio desde la red de calle (nivel de acera).
            // Si todos tienen demanda (caso raro), fallback al de mayor cota.
            var openEnds = network.TerminalNodes.Where(n => n.DemandM3s <= 0.0).ToList();
            PipeNode src = openEnds.Count > 0
                ? openEnds.OrderBy(n => n.ElevationM).First()       // menor cota = acometida
                : network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
            src.IsSource = true; src.IsTerminal = false;
            network.TerminalNodes.Remove(src);
            network.SourceNode = src;
            network.SourcePressurePa = 300000.0;
        }

        private static void ApplySourceFallbackDuct(DuctNetwork network)
        {
            if (network.SourceNode != null) return;
            if (network.TerminalNodes.Count == 0) return;
            // Heurística fuente para redes aeráulicas:
            // Los nodos sin demanda son extremos abiertos; el de MAYOR cota
            // suele ser la UTA/ventilador en cubierta.
            var openEnds = network.TerminalNodes.Where(n => n.DemandM3s <= 0.0).ToList();
            DuctNode src = openEnds.Count > 0
                ? openEnds.OrderByDescending(n => n.ElevationM).First()   // mayor cota = UTA/ventilador
                : network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
            src.IsSource = true; src.IsTerminal = false;
            network.TerminalNodes.Remove(src);
            network.SourceNode = src;
            network.SourcePressurePa = 250.0;
        }

        private Brush GetSystemBrush(Autodesk.Revit.DB.MEPSystem system)
        {
            if (system == null) return DefaultSystemBrush;
            try
            {
                if (!(_doc.GetElement(system.GetTypeId()) is Autodesk.Revit.DB.ElementType type))
                    return DefaultSystemBrush;
                Autodesk.Revit.DB.Color lc = null;
                if (type is Autodesk.Revit.DB.Plumbing.PipingSystemType pst)
                    lc = pst.LineColor;
                else if (type is Autodesk.Revit.DB.Mechanical.MechanicalSystemType mst)
                    lc = mst.LineColor;
                if (lc == null || !lc.IsValid) return DefaultSystemBrush;
                if (lc.Red == 0 && lc.Green == 0 && lc.Blue == 0) return DefaultSystemBrush;
                var brush = new SolidColorBrush(WpfColor.FromRgb(lc.Red, lc.Green, lc.Blue));
                brush.Freeze();
                return brush;
            }
            catch { return DefaultSystemBrush; }
        }

        private static string TryGetPipingSystemType(Document doc, PipingSystem sys)
        {
            try
            {
                if (doc.GetElement(sys.GetTypeId()) is PipingSystemType pst)
                    return pst.SystemClassification.ToString();
            }
            catch { }
            return sys.SystemType.ToString();
        }

        private void SetStatus(string msg) => StatusBarText.Text = msg;

        private void ShowError(string msg)
            => MessageBox.Show(msg, "e3gbimtools — Cálculo MEP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
