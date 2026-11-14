// ============================================================================
//  SchematicPreviewWindow.xaml.cs
//  Ventana de previsualización del esquema MEP antes de generar en Revit.
//
//  · Renderiza el árbol de nodos MEP en un Canvas WPF con zoom y pan.
//  · "Generar en Revit" crea una ViewDrafting con DetailCurves y TextNotes.
//    La operación se encola en CalcExternalEventBus para ejecutarse en
//    contexto válido de la API (ventana abierta con Show(), no ShowDialog()).
//  · "Generar + Exportar DWG" añade exportación DWG tras la generación.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.RevitBridge;

using WpfLine      = System.Windows.Shapes.Line;
using WpfPolygon   = System.Windows.Shapes.Polygon;
using WpfEllipse   = System.Windows.Shapes.Ellipse;
using WpfPoint     = System.Windows.Point;
using WpfColor     = System.Windows.Media.Color;
using IoPath       = System.IO.Path;
using RevitLine    = Autodesk.Revit.DB.Line;

namespace e3gbimtools.Calc.Views
{
    public partial class SchematicPreviewWindow : Window
    {
        // ------------------------------------------------------------------ //
        //  Dependencias                                                        //
        // ------------------------------------------------------------------ //
        private readonly Document             _doc;
        private readonly CalcExternalEventBus _bus;   // puede ser null
        private readonly IList<ResultRow>     _rows;
        private readonly Brush                _systemBrush;
        private readonly CalcReport           _report;
        private readonly HashSet<long>        _criticalRevitIds; // puede ser null

        // ------------------------------------------------------------------ //
        //  Pan                                                                 //
        // ------------------------------------------------------------------ //
        private WpfPoint? _panStart;
        private double    _panStartH;
        private double    _panStartV;

        // ------------------------------------------------------------------ //
        //  Config (valores del panel izquierdo)                               //
        // ------------------------------------------------------------------ //
        private const double PxPerMm  = 2.8;   // conversión mm→px en el preview WPF
        private const double TopPad   = 16.0;
        private const double TrunkX   = 28.0;
        private const double LblPad   = 14.0;
        private const double LblPadFit= 20.0;

        // Derivados de los sliders (actualizados en OnLayoutChanged):
        private double _cfgRowH   = 8.0  * PxPerMm;   // px en el canvas WPF
        private double _cfgColW   = 25.0 * PxPerMm;
        private double _cfgMmRow  = 8.0;               // mm de papel (→ Revit)
        private double _cfgMmCol  = 25.0;
        private double _cfgMmText = 2.5;
        private int    _cfgScale  = 50;                 // denominador de escala (50 = 1:50)

        private bool   _cfgPipeLabels = true;
        private bool   _cfgFitLabels  = true;
        private bool   _cfgLegend     = true;
        private bool   _cfgCritPath   = false;
        private bool   _loaded;

        // ------------------------------------------------------------------ //
        //  Constructor                                                         //
        // ------------------------------------------------------------------ //
        public SchematicPreviewWindow(
            Document             doc,
            CalcExternalEventBus bus,
            IList<ResultRow>     rows,
            Brush                systemBrush,
            CalcReport           report,
            HashSet<long>        criticalRevitIds = null)
        {
            InitializeComponent();

            // Reskin Fluent Light: fusiona el tema para que E3gTitleBar y los
            // DynamicResource E3g.* resuelvan correctamente sus brushes/fuentes.
            E3G.BimTools.DiTools.UI.Controls.E3gWindowBase.ApplyTheme(this);

            _doc              = doc;
            _bus              = bus;
            _rows             = rows;
            _systemBrush      = systemBrush ?? Brushes.SteelBlue;
            _report           = report;
            _criticalRevitIds = criticalRevitIds;

            Loaded += (s, e) =>
            {
                _loaded = true;
                RenderPreview();
            };
        }

        // ------------------------------------------------------------------ //
        //  Panel de configuración — handlers                                   //
        // ------------------------------------------------------------------ //
        private void OnScaleChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboScale.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out int s))
                _cfgScale = s;
        }

        private void OnLayoutChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            _cfgMmRow  = SldRowH.Value;
            _cfgMmCol  = SldColW.Value;
            _cfgMmText = SldTextH.Value;
            _cfgRowH   = _cfgMmRow  * PxPerMm;
            _cfgColW   = _cfgMmCol  * PxPerMm;
            LblRowH.Text  = _cfgMmRow.ToString("F1")  + " mm";
            LblColW.Text  = _cfgMmCol.ToString("F1")  + " mm";
            LblTextH.Text = _cfgMmText.ToString("F1") + " mm";
            RenderPreview();
        }

        private void OnVisibilityChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            _cfgPipeLabels = ChkPipeLabels.IsChecked == true;
            _cfgFitLabels  = ChkFitLabels.IsChecked  == true;
            _cfgLegend     = ChkLegend.IsChecked     == true;
            _cfgCritPath   = ChkCritPath.IsChecked   == true;
            RenderPreview();
        }

        private void OnResetConfigClick(object sender, RoutedEventArgs e)
        {
            SldRowH.Value  = 8.0;
            SldColW.Value  = 25.0;
            SldTextH.Value = 2.5;
            ChkPipeLabels.IsChecked = true;
            ChkFitLabels.IsChecked  = true;
            ChkLegend.IsChecked     = true;
            ChkCritPath.IsChecked   = false;
            // OnLayoutChanged y OnVisibilityChanged se disparan solos
        }

        // ------------------------------------------------------------------ //
        //  Rendering                                                           //
        // ------------------------------------------------------------------ //
        private void RenderPreview()
        {
            PreviewCanvas.Children.Clear();

            if (_rows == null || _rows.Count == 0)
            {
                LblStatus.Text = "Sin datos para previsualizar.";
                return;
            }

            double rowH    = _cfgRowH;    // px en WPF
            double colW    = _cfgColW;

            var columnOf = ComputeColumns(_rows);
            int maxCol   = columnOf.Values.DefaultIfEmpty(0).Max();

            // Marcador fuente
            var srcTri = new WpfPolygon
            {
                Points = new PointCollection
                {
                    new WpfPoint(TrunkX - 7, TopPad - 7),
                    new WpfPoint(TrunkX + 7, TopPad - 7),
                    new WpfPoint(TrunkX,     TopPad + 5),
                },
                Fill = _systemBrush, Stroke = Brushes.Black, StrokeThickness = 0.8,
                ToolTip = "Fuente / Acometida",
            };
            PreviewCanvas.Children.Add(srcTri);
            var srcLbl = new TextBlock
            {
                Text = "Fuente", FontSize = 10, Foreground = _systemBrush,
            };
            System.Windows.Controls.Canvas.SetLeft(srcLbl, TrunkX + 12);
            System.Windows.Controls.Canvas.SetTop(srcLbl, TopPad - 9);
            PreviewCanvas.Children.Add(srcLbl);

            var nodePos = new Dictionary<string, (double X, double Y)>();

            for (int i = 0; i < _rows.Count; i++)
            {
                var row  = _rows[i];
                double y = TopPad + (i + 1) * rowH;
                int col  = columnOf.TryGetValue(row, out int c) ? c : 0;
                double x = TrunkX + col * colW;

                string inc  = GetInc(row.NodePath);
                string out_ = GetOut(row.NodePath);

                double pX, pY;
                if (!string.IsNullOrEmpty(inc) && nodePos.TryGetValue(inc, out var pp))
                { pX = pp.X; pY = pp.Y; }
                else { pX = TrunkX; pY = TopPad + 5; }

                bool isCont = Math.Abs(pX - x) < 0.1;
                double thick = col == 0 ? 3.0 : col == 1 ? 2.4 : 2.0;

                // Color de camino crítico
                bool isCrit = _cfgCritPath && _criticalRevitIds != null
                              && _criticalRevitIds.Contains(row.RevitId);
                Brush lineBrush = isCrit
                    ? new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x7B, 0x00))
                    : _systemBrush;

                if (isCont)
                {
                    var vLine = new WpfLine
                    {
                        X1 = x, Y1 = pY, X2 = x, Y2 = y,
                        Stroke = lineBrush,
                        StrokeThickness = row.IsFitting ? thick * 0.5 : thick,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    PreviewCanvas.Children.Add(vLine);
                }
                else
                {
                    var hLine = new WpfLine
                    {
                        X1 = pX, Y1 = y, X2 = x, Y2 = y,
                        Stroke = lineBrush, StrokeThickness = 1.4,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    PreviewCanvas.Children.Add(hLine);

                    var tee = new WpfEllipse
                    {
                        Width = 8, Height = 8,
                        Fill = lineBrush,
                        Stroke = Brushes.White, StrokeThickness = 1.2,
                    };
                    System.Windows.Controls.Canvas.SetLeft(tee, pX - 4);
                    System.Windows.Controls.Canvas.SetTop(tee, y - 4);
                    PreviewCanvas.Children.Add(tee);
                }

                if (!string.IsNullOrEmpty(out_))
                    nodePos[out_] = (x, y);

                // Flecha y etiqueta
                if (row.IsFitting)
                {
                    if (_cfgFitLabels)
                    {
                        string lbl = (row.EndNodeKind ?? "") + "  " + (row.NodePath ?? "");
                        var lblBlock = new TextBlock
                        {
                            Text = lbl, FontSize = 9, FontStyle = FontStyles.Italic,
                            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x77, 0x77, 0x77)),
                        };
                        System.Windows.Controls.Canvas.SetLeft(lblBlock, x + LblPadFit);
                        System.Windows.Controls.Canvas.SetTop(lblBlock,  y - 8);
                        PreviewCanvas.Children.Add(lblBlock);
                    }
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
                        Fill = lineBrush, Stroke = Brushes.Black, StrokeThickness = 0.5,
                        ToolTip = "#" + row.BfsIndex + "  " + row.NodePath + "  " + (row.Status ?? ""),
                    };
                    PreviewCanvas.Children.Add(arrow);

                    if (_cfgPipeLabels)
                    {
                        string lbl = "#" + row.BfsIndex
                            + (string.IsNullOrEmpty(row.NodePath) ? "" : " · " + row.NodePath)
                            + (string.IsNullOrEmpty(row.Size)     ? "" : " · " + row.Size)
                            + "  " + row.FlowLps.ToString("F2",
                                System.Globalization.CultureInfo.InvariantCulture) + " L/s";
                        var lblBlock = new TextBlock
                        {
                            Text = lbl, FontSize = 10, Foreground = Brushes.Black,
                        };
                        System.Windows.Controls.Canvas.SetLeft(lblBlock, x + LblPad);
                        System.Windows.Controls.Canvas.SetTop(lblBlock,  y - 8);
                        PreviewCanvas.Children.Add(lblBlock);
                    }
                }
            }

            // Leyenda en la esquina inferior derecha
            if (_cfgLegend && _rows.Count > 0)
            {
                double lx = TrunkX + (maxCol + 1) * colW + 30.0;
                double ly = TopPad + 10.0;
                AddLegend(lx, ly);
            }

            double canvasW = TrunkX + (maxCol + 1) * colW + 300.0;
            double canvasH = TopPad + (_rows.Count + 2) * rowH + 20.0;
            PreviewCanvas.Width  = Math.Max(canvasW, 600.0);
            PreviewCanvas.Height = Math.Max(canvasH, 400.0);

            LblStatus.Text = _rows.Count + " elementos · " + (maxCol + 1) + " columna(s)";
        }

        private void AddLegend(double x, double y)
        {
            // Caja de leyenda simplificada
            var items = new[]
            {
                ("━━━", "Tubería principal (tronco)", _systemBrush),
                ("- - -", "Rama lateral", _systemBrush),
                ("●", "Nodo de bifurcación (TEE)", _systemBrush),
                ("▼", "Dirección del flujo", _systemBrush),
            };
            double lineH = 14.0;
            for (int i = 0; i < items.Length; i++)
            {
                var (sym, desc, brush) = items[i];
                var symBlock = new TextBlock
                {
                    Text = sym, FontSize = 9, Foreground = (Brush)brush,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                };
                var descBlock = new TextBlock
                {
                    Text = desc, FontSize = 9, Foreground = Brushes.DimGray,
                };
                System.Windows.Controls.Canvas.SetLeft(symBlock,  x);
                System.Windows.Controls.Canvas.SetTop(symBlock,   y + i * lineH);
                System.Windows.Controls.Canvas.SetLeft(descBlock, x + 36);
                System.Windows.Controls.Canvas.SetTop(descBlock,  y + i * lineH);
                PreviewCanvas.Children.Add(symBlock);
                PreviewCanvas.Children.Add(descBlock);
            }
        }

        // ------------------------------------------------------------------ //
        //  Zoom                                                                //
        // ------------------------------------------------------------------ //
        private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomTransform == null) return;
            ZoomTransform.ScaleX = e.NewValue;
            ZoomTransform.ScaleY = e.NewValue;
            if (LblZoom != null) LblZoom.Text = (e.NewValue * 100.0).ToString("F0") + "%";
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            SldZoom.Value = Math.Max(0.2, Math.Min(4.0, SldZoom.Value * factor));
        }

        private void OnFitClick(object sender, RoutedEventArgs e)
        {
            double sx = (Scroll.ViewportWidth  - 30) / Math.Max(1, PreviewCanvas.ActualWidth);
            double sy = (Scroll.ViewportHeight - 30) / Math.Max(1, PreviewCanvas.ActualHeight);
            SldZoom.Value = Math.Max(0.2, Math.Min(4.0, Math.Min(sx, sy)));
        }

        private void OnActualSizeClick(object sender, RoutedEventArgs e)
            => SldZoom.Value = 1.0;

        // ------------------------------------------------------------------ //
        //  Pan (rueda central o Shift+arrastre izquierdo)                     //
        // ------------------------------------------------------------------ //
        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton != MouseButtonState.Pressed
                && !(e.LeftButton == MouseButtonState.Pressed
                     && Keyboard.Modifiers == ModifierKeys.Shift))
                return;
            _panStart  = e.GetPosition(Scroll);
            _panStartH = Scroll.HorizontalOffset;
            _panStartV = Scroll.VerticalOffset;
            Scroll.CaptureMouse();
            Cursor    = Cursors.SizeAll;
            e.Handled = true;
        }

        private void OnScrollMouseMove(object sender, MouseEventArgs e)
        {
            if (_panStart == null) return;
            var cur = e.GetPosition(Scroll);
            Scroll.ScrollToHorizontalOffset(_panStartH - (cur.X - _panStart.Value.X));
            Scroll.ScrollToVerticalOffset  (_panStartV - (cur.Y - _panStart.Value.Y));
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_panStart == null) return;
            _panStart = null;
            Scroll.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        // ------------------------------------------------------------------ //
        //  Botones                                                             //
        // ------------------------------------------------------------------ //
        private void OnGenerateClick(object sender, RoutedEventArgs e)
            => DoGenerate(exportDwg: false, dwgPath: null);

        private void OnExportDwgClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                FileName   = "Esquema_MEP_" + DateTime.Now.ToString("yyyyMMdd_HHmm"),
                DefaultExt = ".dwg",
                Filter     = "AutoCAD DWG (*.dwg)|*.dwg",
            };
            if (dlg.ShowDialog(this) != true) return;
            DoGenerate(exportDwg: true, dwgPath: dlg.FileName);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // ------------------------------------------------------------------ //
        //  Generación en Revit via ExternalEventBus                           //
        // ------------------------------------------------------------------ //
        private void DoGenerate(bool exportDwg, string dwgPath)
        {
            if (_bus == null)
            {
                MessageBox.Show(this,
                    "La ventana debe abrirse desde el botón de la cinta de Revit " +
                    "para poder generar la vista (se requiere contexto de API válido).",
                    "Revit no disponible",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_rows == null || _rows.Count == 0)
            {
                MessageBox.Show(this, "No hay datos calculados para generar el esquema.",
                    "Sin datos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnGenerate.IsEnabled  = false;
            BtnExportDwg.IsEnabled = false;
            LblStatus.Text = exportDwg ? "Generando vista y exportando DWG…"
                                       : "Generando vista en Revit…";

            var rows   = _rows;
            var dwpCpy = dwgPath;

            _bus.Enqueue(app =>
            {
                string resultMsg = null;
                bool   success   = false;

                try
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                    {
                        resultMsg = "No hay documento activo en Revit.";
                        goto done;
                    }

                    // 1 — Crear ViewDrafting
                    ViewDrafting view;
                    using (var tx = new Transaction(doc, "Crear vista esquema MEP"))
                    {
                        tx.Start();
                        view = CreateViewDrafting(doc,
                            "Esquema MEP " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
                        tx.Commit();
                    }

                    // 2 — Dibujar DetailCurves + TextNotes
                    using (var tx = new Transaction(doc, "Dibujar esquema MEP"))
                    {
                        tx.Start();
                        DrawSchemaInView(doc, view, rows);
                        tx.Commit();
                    }

                    // 3 — Exportar DWG si se pidió
                    string dwgInfo = "";
                    if (exportDwg && !string.IsNullOrEmpty(dwpCpy))
                    {
                        try
                        {
                            string folder    = IoPath.GetDirectoryName(dwpCpy);
                            string fileNoExt = IoPath.GetFileNameWithoutExtension(dwpCpy);
                            var    opts      = new DWGExportOptions { MergedViews = true };
                            bool   ok        = doc.Export(folder, fileNoExt,
                                                 new List<ElementId> { view.Id }, opts);
                            dwgInfo = ok
                                ? "\nDWG: " + dwpCpy
                                : "\nDWG: la exportación falló (Revit no informó el motivo).";
                        }
                        catch (Exception ex2)
                        {
                            dwgInfo = "\nDWG falló: " + ex2.Message;
                        }
                    }

                    // 4 — Cambiar a la vista creada
                    try { app.ActiveUIDocument?.RequestViewChange(view); } catch { }

                    resultMsg = "Vista creada: " + view.Name + dwgInfo;
                    success   = true;

                    done: ;
                }
                catch (Exception ex)
                {
                    resultMsg = "Error: " + ex.Message;
                }

                string msg = resultMsg;
                bool   ok2 = success;
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    LblStatus.Text         = msg;
                    BtnGenerate.IsEnabled  = true;
                    BtnExportDwg.IsEnabled = true;

                    if (ok2)
                        MessageBox.Show(this, msg, "Generación completada",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show(this,
                            "No se pudo generar el esquema:\n" + msg,
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            });
        }

        // ------------------------------------------------------------------ //
        //  Helpers de generación Revit                                        //
        // ------------------------------------------------------------------ //
        private static ViewDrafting CreateViewDrafting(Document doc, string name)
        {
            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);

            if (viewFamilyType == null)
                throw new InvalidOperationException(
                    "No se encontró ningún tipo de Vista de Detalle en el proyecto.");

            // Sanitizar nombre — caracteres válidos para nombres de vista Revit
            // (Revit prohíbe: \ / : * ? " < > |  y el punto al final)
            string safe = new string(name.Select(c =>
                (char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '-')
                    ? c : '_').ToArray()).TrimEnd('.');
            if (safe.Length > 80) safe = safe.Substring(0, 80);

            // Evitar duplicados
            var usedNames = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string candidate = safe;
            int    suffix    = 1;
            while (usedNames.Contains(candidate))
                candidate = safe + "_" + suffix++;

            var view = ViewDrafting.Create(doc, viewFamilyType.Id);
            view.Name = candidate;
            return view;
        }

        /// <summary>
        /// Dibuja el árbol MEP como DetailCurves + TextNotes en la ViewDrafting.
        /// Coordenadas: mm de papel × escala → mm de modelo → pies Revit.
        /// El eje Y de Revit crece hacia arriba, por eso los nodos se sitúan
        /// con Y decreciente conforme aumenta el índice de fila.
        /// </summary>
        private void DrawSchemaInView(
            Document doc, ViewDrafting view, IList<ResultRow> rows)
        {
            if (rows == null || rows.Count == 0) return;

            // Aplicar escala: mm papel × scale / 304.8 = pies Revit
            const double mmToFt = 1.0 / 304.8;
            double scale  = _cfgScale;
            double rowH   = _cfgMmRow  * scale * mmToFt;
            double colW   = _cfgMmCol  * scale * mmToFt;
            double textHt = _cfgMmText * scale * mmToFt;
            const double startX = 0.0;
            const double startY = 0.0;

            // Aplicar escala en la vista (afecta cómo se ve en la hoja)
            try { view.Scale = _cfgScale; } catch { /* algunos tipos de vista no admiten Scale */ }

            var columnOf = ComputeColumns(rows);
            var nodePos  = new Dictionary<string, (double X, double Y)>();

            // TextNoteType — tamaño más próximo a _cfgMmText
            var allTnTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .ToList();
            var tnType = allTnTypes.FirstOrDefault();

            for (int i = 0; i < rows.Count; i++)
            {
                var    row  = rows[i];
                int    col  = columnOf.TryGetValue(row, out int c) ? c : 0;
                double x    = startX + col * colW;
                double y    = startY - (i + 1) * rowH;   // Y decrece = abajo en Revit

                string inc  = GetInc(row.NodePath);
                string out_ = GetOut(row.NodePath);

                double pX, pY;
                if (!string.IsNullOrEmpty(inc) && nodePos.TryGetValue(inc, out var pp))
                { pX = pp.X; pY = pp.Y; }
                else { pX = startX; pY = startY; }

                bool isCont = Math.Abs(pX - x) < 1e-9;

                if (isCont)
                {
                    // Línea vertical
                    if (Math.Abs(pY - y) > 1e-9)
                        doc.Create.NewDetailCurve(view,
                            RevitLine.CreateBound(new XYZ(x, pY, 0), new XYZ(x, y, 0)));
                }
                else
                {
                    // Horizontal desde padre hacia rama
                    if (Math.Abs(pX - x) > 1e-9)
                        doc.Create.NewDetailCurve(view,
                            RevitLine.CreateBound(new XYZ(pX, y, 0), new XYZ(x, y, 0)));
                    // Vertical del padre hasta la bifurcación
                    if (Math.Abs(pY - y) > 1e-9)
                        doc.Create.NewDetailCurve(view,
                            RevitLine.CreateBound(new XYZ(pX, pY, 0), new XYZ(pX, y, 0)));
                }

                if (!string.IsNullOrEmpty(out_))
                    nodePos[out_] = (x, y);

                if (tnType == null) continue;

                // TextNote tuberías
                if (!row.IsFitting && _cfgPipeLabels)
                {
                    string txt = "#" + row.BfsIndex
                        + (string.IsNullOrEmpty(row.Size) ? "" : " " + row.Size)
                        + " " + row.FlowLps.ToString("F2",
                            System.Globalization.CultureInfo.InvariantCulture) + " L/s";
                    TextNote.Create(doc, view.Id,
                        new XYZ(x + colW * 0.25, y + rowH * 0.1, 0),
                        txt, tnType.Id);
                }

                // TextNote accesorios
                if (row.IsFitting && _cfgFitLabels)
                {
                    string txt = (row.EndNodeKind ?? "FIT") + " " + (row.NodePath ?? "");
                    TextNote.Create(doc, view.Id,
                        new XYZ(x + colW * 0.25, y + rowH * 0.1, 0),
                        txt, tnType.Id);
                }
            }

            // Leyenda
            if (_cfgLegend)
            {
                int maxCol = columnOf.Values.DefaultIfEmpty(0).Max();
                double lx = startX + (maxCol + 1) * colW + colW * 0.5;
                double ly = startY - rowH;
                var legendLines = new[]
                {
                    "LEYENDA",
                    "━ Tronco principal",
                    "- Rama lateral",
                    "● TEE / bifurcación",
                };
                for (int k = 0; k < legendLines.Length; k++)
                    TextNote.Create(doc, view.Id,
                        new XYZ(lx, ly - k * rowH, 0),
                        legendLines[k], tnType.Id);
            }
        }

        // ------------------------------------------------------------------ //
        //  Cálculo de columnas (branch-first, mismo algoritmo que CalcWindow) //
        // ------------------------------------------------------------------ //
        private static Dictionary<ResultRow, int> ComputeColumns(IList<ResultRow> rows)
        {
            var result = new Dictionary<ResultRow, int>();
            if (rows == null) return result;

            var pipeRows = rows.Where(r => !r.IsFitting).ToList();

            var rowByOut   = new Dictionary<string, ResultRow>();
            var childrenOf = new Dictionary<string, List<ResultRow>>();

            foreach (var p in pipeRows)
            {
                string o = GetOut(p.NodePath);
                if (!string.IsNullOrEmpty(o)) rowByOut[o] = p;
                string inc = GetInc(p.NodePath);
                if (!string.IsNullOrEmpty(inc))
                {
                    if (!childrenOf.TryGetValue(inc, out var lst))
                        childrenOf[inc] = lst = new List<ResultRow>();
                    lst.Add(p);
                }
            }

            var roots = pipeRows
                .Where(p =>
                {
                    string inc = GetInc(p.NodePath);
                    return string.IsNullOrEmpty(inc) || !rowByOut.ContainsKey(inc);
                })
                .OrderBy(p => p.BfsIndex)
                .ToList();

            void Assign(ResultRow p, int col)
            {
                result[p] = col;
                string o = GetOut(p.NodePath);
                if (string.IsNullOrEmpty(o) || !childrenOf.TryGetValue(o, out var children)) return;
                var sorted = children.OrderByDescending(ch => ch.FlowLps).ThenBy(ch => ch.BfsIndex).ToList();
                for (int i = 0; i < sorted.Count; i++)
                    Assign(sorted[i], col + i);
            }
            foreach (var r in roots) Assign(r, 0);

            // Accesorios: misma columna que su tramo padre
            foreach (var row in rows)
            {
                if (!row.IsFitting) continue;
                string inc = GetInc(row.NodePath);
                if (!string.IsNullOrEmpty(inc)
                    && rowByOut.TryGetValue(inc, out var parent)
                    && result.TryGetValue(parent, out int col))
                    result[row] = col;
            }
            return result;
        }

        // ------------------------------------------------------------------ //
        //  NodePath helpers                                                    //
        // ------------------------------------------------------------------ //
        private static string GetInc(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int idx = path.IndexOf('→');
            return idx > 0 ? path.Substring(0, idx).Trim() : path.Trim();
        }

        private static string GetOut(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int idx = path.IndexOf('→');
            return (idx > 0 && idx + 1 < path.Length)
                ? path.Substring(idx + 1).Trim()
                : path.Trim();
        }
    }
}
