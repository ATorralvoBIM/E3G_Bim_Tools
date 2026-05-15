// ============================================================================
//  SchematicPreviewWindow.xaml.cs
//  Ventana de previsualización del esquema antes de generar la vista en
//  Revit.  Tres bloques:
//    1) Toolbar superior: zoom (slider + rueda), ajuste a ventana, escala.
//    2) Canvas central con el dibujo (renderizado por SchematicPreviewRenderer).
//    3) Toolbar inferior: Generar / Generar+Exportar DWG / Cerrar.
//
//  La generación en Revit pasa por un ExternalEventBus inyectado desde fuera
//  (compartido con la sync de Fase 1) para asegurar que las transacciones
//  corren en un contexto válido de la API.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Core.Analytics;
using E3G.Saneamiento.Core.Schematic;
using E3G.Saneamiento.RevitBridge.Schematic;
using E3G.Saneamiento.RevitBridge.Sync;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;
// Point colisiona entre Autodesk.Revit.DB y System.Windows; aquí usamos WPF.
using Point = System.Windows.Point;
// Path colisiona entre System.IO y System.Windows.Shapes; aquí usamos System.IO.
using Path = System.IO.Path;

namespace E3G.Saneamiento.UI.Schematic
{
    public partial class SchematicPreviewWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly IRevitSyncService _sync;
        private readonly IList<ResultRow> _rows;
        private readonly IReadOnlyDictionary<ResultRow, int> _columnOf;
        private readonly CriticalPathResult _criticalPath;
        private readonly SchematicLegend _legend;
        private readonly ExternalEventBus _bus;

        private SchematicLayout _layout;
        private SchematicPreviewRenderer _renderer;
        private SchematicParameters _params;
        private readonly bool _ownsBus;

        // MVVM
        private readonly SchematicConfigViewModel _vm;
        private bool _loaded;     // evita repintar durante la inicialización

        // Pan
        private Point? _panStart;
        private double _panStartHOffset;
        private double _panStartVOffset;

        public SchematicPreviewWindow(
            UIApplication uiApp,
            IRevitSyncService sync,
            IList<ResultRow> rows,
            IReadOnlyDictionary<ResultRow, int> columnOf,
            CriticalPathResult criticalPath,
            SchematicLegend legend,
            ExternalEventBus injectedBus = null)
        {
            InitializeComponent();
            _uiApp = uiApp;
            _sync = sync;
            _rows = rows;
            _columnOf = columnOf;
            _criticalPath = criticalPath;
            _legend = legend ?? new SchematicLegend();
            // Si nos pasan un bus ya creado en contexto válido lo usamos;
            // si no, intentamos crear uno (puede fallar fuera de Execute).
            _bus = injectedBus;
            _ownsBus = (injectedBus == null);
            if (_bus == null)
            {
                try { _bus = new ExternalEventBus(); }
                catch { /* sin bus, "Generar" mostrará error */ }
            }

            // Selección inicial de escala según tamaño
            _params = new SchematicParameters();
            var recommended = SchematicParameters.RecommendScale(rows?.Count ?? 0);
            _params.Scale = recommended;
            if (recommended == SchematicScale.OneToHundred)
                ((ComboBoxItem)CboScale.Items[1]).IsSelected = true;

            // ── ViewModel del panel izquierdo ──────────────────────────────
            // Si hay sesión Revit, montamos un validador real; si no, el VM
            // queda con _validator=null y la validación se difiere a la
            // pasada de generación.
            IFamilyValidator validator = null;
            if (uiApp?.Application != null)
                validator = new RevitFamilyValidator(uiApp.Application);
            _vm = new SchematicConfigViewModel(validator);
            _vm.SeedFromRows(_rows);
            // Sembramos los sliders desde los valores actuales de _params para
            // que la primera vista coincida con lo que se va a dibujar.
            _vm.PaperMmLineSpacing   = _params.MmPerRow;
            _vm.PaperMmColumnIndent  = _params.MmPerColumn;
            _vm.PaperMmTextHeight    = _params.TextHeightMm;
            _vm.PaperMmSymbolSize    = _params.SymbolSizeMm;
            _vm.ShowPipeLabels       = _params.ShowPipeLabels;
            _vm.ShowFittingLabels    = _params.ShowFittingLabels;
            _vm.ShowLegend           = _params.ShowLegend;
            _vm.HighlightCriticalPath = _params.HighlightCriticalPath;

            _vm.PreviewRequested  += OnVmPreviewRequested;
            _vm.GenerateRequested += OnVmGenerateRequested;
            _vm.WarningRaised     += OnVmWarningRaised;

            ConfigPanel.DataContext = _vm;

            Loaded += (s, e) =>
            {
                _loaded = true;
                BuildAndRender();
            };
        }

        // ── Sincronización ViewModel → SchematicParameters ─────────────────

        private void ApplyVmToParams()
        {
            if (_vm == null || _params == null) return;
            _params.MmPerRow             = _vm.PaperMmLineSpacing;
            _params.MmPerColumn          = _vm.PaperMmColumnIndent;
            _params.TextHeightMm         = _vm.PaperMmTextHeight;
            _params.SymbolSizeMm         = _vm.PaperMmSymbolSize;
            _params.ShowPipeLabels       = _vm.ShowPipeLabels;
            _params.ShowFittingLabels    = _vm.ShowFittingLabels;
            _params.ShowLegend           = _vm.ShowLegend;
            _params.HighlightCriticalPath = _vm.HighlightCriticalPath;
            // Filtros adicionales (sin efecto hoy: el motor todavía no
            // distingue valves/meters/sensors — quedan listos para cuando lo haga).
            _params.ShowValves           = _vm.ShowValves;
            _params.ShowMeters           = _vm.ShowMeters;
            _params.ShowSensors          = _vm.ShowSensors;
        }

        private void OnVmPreviewRequested(object sender, EventArgs e)
        {
            if (!_loaded) return;
            ApplyVmToParams();
            BuildAndRender();
        }

        private void OnVmGenerateRequested(object sender, EventArgs e)
        {
            if (!_loaded) return;
            ApplyVmToParams();
            GenerateInRevit(exportDwg: false, dwgPath: null);
        }

        private void OnVmWarningRaised(object sender, FamilyWarningEventArgs e)
        {
            if (e == null) return;

            // Caso "carga masiva": mostrar dialog con tabla.
            if (e.HasDetail)
            {
                LblStatus.Text = e.Title + " — " + e.Body;
                var items = LibraryReportDialog.BuildItems(e.DetailedItems);
                var dlg = new LibraryReportDialog(e.Title, e.Body, items) { Owner = this };
                dlg.ShowDialog();
                return;
            }

            // Caso simple: status bar (info) o MessageBox (aviso/error).
            switch (e.Severity)
            {
                case FamilyWarningSeverity.Info:
                    LblStatus.Text = e.Title + " — " + e.Body.Replace("\n", " · ");
                    break;
                case FamilyWarningSeverity.Warning:
                    MessageBox.Show(this, e.Body, e.Title,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                case FamilyWarningSeverity.Error:
                    MessageBox.Show(this, e.Body, e.Title,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
            }
        }

        private void OnScaleChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (CboScale.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out int s))
            {
                _params.Scale = (SchematicScale)s;
                BuildAndRender();
            }
        }

        private void BuildAndRender()
        {
            try
            {
                var engine = new SchematicLayoutEngine();
                var critSet = new HashSet<ResultRow>();
                if (_criticalPath?.Path != null)
                    foreach (var r in _criticalPath.Path) critSet.Add(r);
                _layout = engine.Build(_rows, _columnOf, _params, critSet, _legend);

                _renderer = new SchematicPreviewRenderer(PreviewCanvas, _layout, _sync);
                _renderer.Render();

                LblStatus.Text = $"{_layout.Nodes.Count} elementos · ancho {_layout.WidthMm:F0} mm · alto {_layout.HeightMm:F0} mm";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error al construir layout: " + ex.Message;
            }
        }

        // ── Zoom ───────────────────────────────────────────────────────────

        private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomTransform == null) return;
            ZoomTransform.ScaleX = e.NewValue;
            ZoomTransform.ScaleY = e.NewValue;
            if (LblZoom != null) LblZoom.Text = $"{e.NewValue * 100:F0}%";
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newZoom = Math.Max(0.2, Math.Min(4.0, SldZoom.Value * factor));
            SldZoom.Value = newZoom;
        }

        private void OnFitClick(object sender, RoutedEventArgs e)
        {
            if (_layout == null) return;
            double sx = (Scroll.ViewportWidth - 30) / PreviewCanvas.ActualWidth;
            double sy = (Scroll.ViewportHeight - 30) / PreviewCanvas.ActualHeight;
            double s = Math.Max(0.2, Math.Min(sx, sy));
            SldZoom.Value = s;
        }

        private void OnActualSizeClick(object sender, RoutedEventArgs e)
            => SldZoom.Value = 1.0;

        // ── Pan ────────────────────────────────────────────────────────────

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton != MouseButtonState.Pressed &&
                !(e.LeftButton == MouseButtonState.Pressed && Keyboard.Modifiers == ModifierKeys.Shift))
                return;
            _panStart = e.GetPosition(Scroll);
            _panStartHOffset = Scroll.HorizontalOffset;
            _panStartVOffset = Scroll.VerticalOffset;
            Scroll.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_panStart == null) return;
            var cur = e.GetPosition(Scroll);
            var dx = cur.X - _panStart.Value.X;
            var dy = cur.Y - _panStart.Value.Y;
            Scroll.ScrollToHorizontalOffset(_panStartHOffset - dx);
            Scroll.ScrollToVerticalOffset(_panStartVOffset - dy);
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_panStart == null) return;
            _panStart = null;
            Scroll.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        // ── Botones ────────────────────────────────────────────────────────

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            ApplyVmToParams();
            GenerateInRevit(exportDwg: false, dwgPath: null);
        }

        private void OnExportDwgClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName = $"Esquema_{_legend.SystemName}_{DateTime.Now:yyyyMMdd_HHmm}",
                DefaultExt = ".dwg",
                Filter = "AutoCAD DWG (*.dwg)|*.dwg",
            };
            if (dlg.ShowDialog(this) != true) return;
            ApplyVmToParams();
            GenerateInRevit(exportDwg: true, dwgPath: dlg.FileName);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void GenerateInRevit(bool exportDwg, string dwgPath)
        {
            if (_uiApp == null)
            {
                MessageBox.Show(this,
                    "No hay sesión activa de Revit. La preview se puede usar para revisar el esquema pero no se puede generar la vista.",
                    "Revit no disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BtnGenerate.IsEnabled = false;
            BtnExportDwg.IsEnabled = false;
            LblStatus.Text = exportDwg ? "Generando vista y exportando DWG..." : "Generando vista en Revit...";

            // Aplicar la escala elegida en el combo (por si cambió entre
            // OnScaleChanged y el clic — no debería, pero por seguridad).
            if (CboScale.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out int s))
                _params.Scale = (SchematicScale)s;

            // Reconstruir el layout con los parámetros más recientes.
            BuildAndRender();

            // Snapshot del mapping antes de saltar al hilo de Revit.
            var symbolPaths = _vm?.CollectSymbolPaths();

            _bus.Enqueue(app =>
            {
                try
                {
                    var uidoc = app.ActiveUIDocument;
                    var svc = new SchematicGeneratorService();
                    var result = svc.Generate(uidoc, _layout, symbolPaths);

                    string dwgInfo = "";
                    if (result.Success && exportDwg && !string.IsNullOrEmpty(dwgPath))
                    {
                        var folder = Path.GetDirectoryName(dwgPath);
                        var fileNoExt = Path.GetFileNameWithoutExtension(dwgPath);
                        var dwgResult = DwgExporter.Export(uidoc.Document,
                            result.ViewId, folder, fileNoExt);
                        if (dwgResult.Success)
                            dwgInfo = "\nDWG: " + dwgResult.Path;
                        else
                            dwgInfo = "\nDWG falló: " + dwgResult.Error;
                    }

                    if (result.Success)
                        svc.OpenView(uidoc, result.ViewId);

                    Dispatcher.Invoke(() =>
                    {
                        if (result.Success)
                        {
                            LblStatus.Text = "Vista creada: " + result.ViewName + dwgInfo;
                            var msg = "Esquema generado: " + result.ViewName + dwgInfo;
                            if (!string.IsNullOrWhiteSpace(result.SymbolReport))
                                msg += "\n\nFamilias .rfa:\n" + result.SymbolReport;
                            MessageBox.Show(this, msg,
                                "Generación completada", MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            LblStatus.Text = "Error: " + result.Error;
                            MessageBox.Show(this,
                                "No se pudo generar el esquema:\n" + result.Error,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        BtnGenerate.IsEnabled = true;
                        BtnExportDwg.IsEnabled = true;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        LblStatus.Text = "Excepción: " + ex.Message;
                        BtnGenerate.IsEnabled = true;
                        BtnExportDwg.IsEnabled = true;
                    });
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            // Desuscribir eventos del VM para no dejar refs colgadas.
            if (_vm != null)
            {
                _vm.PreviewRequested  -= OnVmPreviewRequested;
                _vm.GenerateRequested -= OnVmGenerateRequested;
                _vm.WarningRaised     -= OnVmWarningRaised;
            }
            // Solo cerramos el bus si lo creamos nosotros; si lo inyectaron
            // (CalcWindow) lo gestiona el dueño.
            if (_ownsBus) _bus?.Dispose();
            base.OnClosed(e);
        }
    }
}
