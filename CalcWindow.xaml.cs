// ============================================================================
//  CalcWindow.xaml.cs
//  Sprint 5 — UI principal del motor de calculo MEP.
//
//  Flujo:
//    1. Constructor recibe Document de Revit y carga PipingSystems +
//       MechanicalSystems en sus respectivos ComboBox.
//    2. Carga presets de criterio desde SizingCriteriaConfig.Presets.
//    3. Calcular: lee la red via PipeNetworkReader / DuctNetworkReader,
//       resuelve con HydraulicCalculator / AeraulicCalculator y popula el
//       DataGrid con las filas resultado.
//    4. Aplicar al modelo: llama ResultWriter dentro de su propia Transaction.
//
//  La ventana se abre modal desde un IExternalCommand: ya estamos dentro
//  del contexto de Revit, podemos abrir Transaction sin problema.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using E3G.Saneamiento.Core.Analytics;
using E3G.Saneamiento.Core.Common;
using E3G.Saneamiento.Core.Hydraulics;
using E3G.Saneamiento.Core.Aeraulics;
using E3G.Saneamiento.RevitBridge;
using E3G.Saneamiento.RevitBridge.Sync;
using E3G.Saneamiento.UI.Sync;
// Alias específico: importar el namespace entero Autodesk.Revit.UI choca con
// WPF (TextBox, ContextMenu, MenuItem, etc.) — solo necesitamos UIApplication.
using UIApplication = Autodesk.Revit.UI.UIApplication;

// Alias para resolver la ambiguedad con Autodesk.Revit.DB.Plumbing.PipeSegment
// (Revit usa ese nombre para los tipos de segmento de tuberia; aqui siempre
//  nos referimos al modelo del grafo hidraulico del Core).
using PipeSegment = E3G.Saneamiento.Core.Hydraulics.PipeSegment;
// Idem para DuctShape (colisiona con Autodesk.Revit.DB.Mechanical.DuctShape).
using DuctShape   = E3G.Saneamiento.Core.Aeraulics.DuctShape;

// Alias WPF: Autodesk.Revit.DB tiene Point/Line/Rectangle/Ellipse/Color con
// el mismo nombre.  En este archivo (Window) siempre nos referimos a los
// tipos WPF, por lo que fijamos el alias aqui para que los `new Line { ... }`,
// `new Point(...)`, etc. resuelvan sin ambiguedad.
using WpfPoint     = System.Windows.Point;
using WpfLine      = System.Windows.Shapes.Line;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfEllipse   = System.Windows.Shapes.Ellipse;
using WpfPolygon   = System.Windows.Shapes.Polygon;
using WpfPath      = System.Windows.Shapes.Path;
using WpfColor     = System.Windows.Media.Color;
using WpfColors    = System.Windows.Media.Colors;

namespace E3G.Saneamiento
{
    /// <summary>Ventana principal de calculo MEP.</summary>
    public partial class CalcWindow : Window
    {
        private readonly Document _doc;

        // [Fase 1] Servicio de sincronización Revit ↔ UI.  Si la ventana se
        // creó con la sobrecarga clásica (sólo Document), queda en null y
        // la sincronización está desactivada de forma silenciosa — la
        // herramienta sigue funcionando como antes.
        private readonly IRevitSyncService _sync;
        private GridSelectionController _gridSyncCtl;
        private HoverHighlightController _hoverSyncCtl;
        private SchemaSelectionController _schemaSyncCtl;

        // Estado tras Calcular: usado por Aplicar.
        private CalcReport _lastReport;
        private string _lastSystemName;
        private bool _lastIsPiping;

        // [v7] Color del sistema MEP — extraído del LineColor del
        // PipingSystemType / MechanicalSystemType activo.  Se usa como color
        // único en el esquema (todas las ramas, junctions y troncos pintan
        // del mismo color, en vez de la paleta por nivel de columna).
        private Brush _lastSystemBrush = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x3A, 0x5E));

        public ObservableCollection<ResultRow> Rows { get; } = new ObservableCollection<ResultRow>();

        /// <summary>
        /// Sobrecarga clásica — sólo Document.  La sincronización con Revit
        /// queda desactivada (la ventana se puede usar como modal sin
        /// problemas).  Para activar la sincronización bidireccional usa la
        /// otra sobrecarga pasando el UIApplication y abre la ventana con
        /// Show() (no ShowDialog()).
        /// </summary>
        public CalcWindow(Document doc) : this(doc, null) { }

        public CalcWindow(Document doc, UIApplication uiApp)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            InitializeComponent();
            DgResults.ItemsSource = Rows;

            LoadPipingSystems();
            LoadDuctSystems();
            LoadCriteriaPresets();

            // [v4] Recalcular el esquema cuando el usuario arrastra el
            // GridSplitter o redimensiona la ventana — para que la indentación
            // entre ramas se expanda/comprima proporcionalmente al ancho
            // disponible (estilo MagiCAD).  Throttle simple para evitar
            // redraws excesivos durante el drag.
            SchematicScroll.SizeChanged += OnSchematicScrollSizeChanged;

            // [Fase 3] Guardamos UIApplication para que el preview del esquema
            // pueda invocar a SchematicGeneratorService dentro del contexto
            // de Revit.  Es independiente de la sync — incluso sin sync
            // activa, mientras tengamos uiApp, podremos generar la vista.
            _uiAppOpt = uiApp;
            // El ExternalEventBus debe crearse en un contexto válido de la
            // API de Revit (es decir, durante Execute del comando).  Si lo
            // intentamos crear más tarde desde un click handler, falla.
            // Por eso lo creamos AQUÍ y lo pasamos al preview cuando se abra.
            if (uiApp != null)
            {
                try { _schematicBus = new RevitBridge.Sync.ExternalEventBus(); }
                catch { /* si falla, el preview lo dirá al pulsar Generar */ }
            }

            // [Fase 1] Sincronización bidireccional Revit ↔ UI.
            if (uiApp != null)
            {
                _sync = new RevitSyncService(uiApp);
                _gridSyncCtl   = new GridSelectionController(DgResults, _sync);
                _hoverSyncCtl  = new HoverHighlightController(DgResults, _sync);
                _schemaSyncCtl = new SchemaSelectionController(_sync,
                    row => _schemaGlyphs.TryGetValue(row, out var g) ? g : null);
                _sync.Start();
                Closed += (s, e) =>
                {
                    _gridSyncCtl?.Dispose();
                    _hoverSyncCtl?.Dispose();
                    _schemaSyncCtl?.Dispose();
                    _sync.Stop();
                    _sync.Dispose();
                    _schematicBus?.Dispose();
                };
            }
            else
            {
                // Si la sync no está activa pero quisiéramos limpiar el bus
                // del esquema de todos modos, lo registramos también.
                Closed += (s, e) => _schematicBus?.Dispose();
            }
        }

        private double _lastRebuildWidth = -1;
        private void OnSchematicScrollSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_lastSchemaRows == null || _lastSchemaRows.Count == 0) return;
            // Solo rebuild si el ancho ha cambiado significativamente (>8px)
            // para evitar rerenderizar en cada tick del drag.
            double w = e.NewSize.Width;
            if (Math.Abs(w - _lastRebuildWidth) < 8) return;
            _lastRebuildWidth = w;
            BuildSchematic(_lastSchemaRows);
        }

        // -------------------------------------------------------------------
        //  Carga inicial
        // -------------------------------------------------------------------

        private void LoadPipingSystems()
        {
            var systems = new FilteredElementCollector(_doc)
                .OfClass(typeof(PipingSystem))
                .Cast<PipingSystem>()
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name)
                .ToList();
            CboPipeSystems.ItemsSource = systems;
            if (systems.Count > 0) CboPipeSystems.SelectedIndex = 0;
        }

        private void LoadDuctSystems()
        {
            var systems = new FilteredElementCollector(_doc)
                .OfClass(typeof(MechanicalSystem))
                .Cast<MechanicalSystem>()
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Name)
                .ToList();
            CboDuctSystems.ItemsSource = systems;
            if (systems.Count > 0) CboDuctSystems.SelectedIndex = 0;
        }

        private void LoadCriteriaPresets()
        {
            var pipingPresets = new[]
            {
                new PresetItem("CTE_AF",       "CTE DB-HS4 — Agua fría sanitaria"),
                new PresetItem("CTE_ACS",      "CTE DB-HS4 — ACS"),
                new PresetItem("CTE_RECIRC",   "CTE DB-HS4 — Retorno ACS"),
                new PresetItem("RITE_HEATING", "RITE — Calefacción"),
                new PresetItem("RITE_COOLING", "RITE — Climatización"),
            };
            CboPipeCriteria.ItemsSource = pipingPresets;
            CboPipeCriteria.SelectedIndex = 0;

            // Tipos de edificio para el factor de simultaneidad (CTE / UNE 149201).
            var buildingTypes = new[]
            {
                new BuildingTypeItem(SimultaneityProfile.Residential, "Vivienda — K = 1/√(n-1)"),
                new BuildingTypeItem(SimultaneityProfile.Office,      "Oficinas — K = 1/√(n-1) [min 0.20]"),
                new BuildingTypeItem(SimultaneityProfile.Hospital,    "Hospital — K = 1.7/√n"),
                new BuildingTypeItem(SimultaneityProfile.Hotel,       "Hotel — K = 1/√(n-1) [min 0.25]"),
                new BuildingTypeItem(SimultaneityProfile.Commercial,  "Comercial / restaurante — K = 1.5/√n"),
                new BuildingTypeItem(SimultaneityProfile.None,        "Sin simultaneidad (suma directa)"),
            };
            CboPipeBuilding.ItemsSource = buildingTypes;
            CboPipeBuilding.SelectedIndex = 0;

            var ductPresets = new[]
            {
                new PresetItem("RITE_SUPPLY",   "RITE — Conducto impulsión (igual fricción)"),
                new PresetItem("RITE_RETURN",   "RITE — Conducto retorno"),
                new PresetItem("ASHRAE_SUPPLY", "ASHRAE — Conducto impulsión"),
            };
            CboDuctCriteria.ItemsSource = ductPresets;
            CboDuctCriteria.SelectedIndex = 0;
        }

        // -------------------------------------------------------------------
        //  Eventos UI
        // -------------------------------------------------------------------

        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            // Reset de resultados al cambiar de pestania
            if (e.OriginalSource == TabSystem)
            {
                Rows.Clear();
                BtnApply.IsEnabled = false;
                LblStatus.Text = "Listo.";
            }
        }

        private void OnPipeTempChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblPipeTemp != null) LblPipeTemp.Text = ((int)e.NewValue).ToString();
        }

        private void OnDuctTempChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblDuctTemp != null) LblDuctTemp.Text = ((int)e.NewValue).ToString();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnShowDiag(object sender, RoutedEventArgs e)
        {
            if (_lastReport == null)
            {
                MessageBox.Show(this, "Aun no se ha ejecutado ningun calculo.",
                    "Diagnostico", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Sistema: " + (_lastSystemName ?? "(sin nombre)"));
            sb.AppendLine("Tipo: " + (_lastIsPiping ? "Tuberias" : "Conductos"));
            sb.AppendLine("Tramos calculados: " + _lastReport.Segments.Count);
            sb.AppendLine("DP peor caso: " + _lastReport.WorstCasePressureDropPa.ToString("F0") + " Pa");
            sb.AppendLine("Avisos: " + _lastReport.CountWarnings + " · Errores: " + _lastReport.CountErrors);
            sb.AppendLine();
            sb.AppendLine("--- Mensajes ---");
            int i = 1;
            foreach (var m in _lastReport.AllMessages)
            {
                sb.AppendLine(i.ToString() + ". [" + m.Severity + "] " + m.Code);
                sb.AppendLine("   " + m.Message);
                if (m.RevitElementId.HasValue)
                    sb.AppendLine("   (Element Id: " + m.RevitElementId.Value + ")");
                i++;
            }
            // Usamos un TextBox dentro de una ventana auxiliar para que el
            // usuario pueda copiar el contenido (un MessageBox no permite
            // selección parcial cómoda en algunos sistemas).
            var diagWin = new Window
            {
                Title  = "MEP Calc — Diagnóstico",
                Width  = 700,
                Height = 480,
                Owner  = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var tb = new TextBox
            {
                Text = sb.ToString(),
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            diagWin.Content = tb;
            diagWin.ShowDialog();
        }

        // -------------------------------------------------------------------
        //  Calcular
        // -------------------------------------------------------------------

        private void OnCalculate(object sender, RoutedEventArgs e)
        {
            try
            {
                Rows.Clear();
                BtnExport.IsEnabled = false;
                if (TabSystem.SelectedItem == TabPiping) CalculatePiping();
                else if (TabSystem.SelectedItem == TabDucts) CalculateDucts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error durante el cálculo:\n\n" + ex.Message,
                    "MEP Calc", MessageBoxButton.OK, MessageBoxImage.Error);
                LblStatus.Text = "Error: " + ex.Message;
                BtnApply.IsEnabled = false;
                BtnExport.IsEnabled = false;
            }
        }

        private void CalculatePiping()
        {
            var system = CboPipeSystems.SelectedItem as PipingSystem;
            if (system == null) { LblStatus.Text = "Selecciona un PipingSystem."; return; }

            var preset = (CboPipeCriteria.SelectedItem as PresetItem)?.Key ?? "CTE_AF";
            double tempC = SldPipeTemp.Value;
            double sourceKpa = ParseDouble(TxtPipeSourceKpa.Text, 300.0);

            var reader = new PipeNetworkReader();
            var network = reader.ReadFromSystem(_doc, system);
            if (network.Segments.Count == 0)
            {
                LblStatus.Text = "El sistema no contiene tramos validos.";
                return;
            }
            // Heuristica fuente: tras leer las demandas de los aparatos, el
            // terminal sin demanda (extremo abierto sin aparato conectado) es
            // la acometida.  Cogemos el de menor cota — caso tipico de la red
            // que entra al edificio a nivel de calle.  Si no hubiera ninguno,
            // fallback al terminal de mayor cota (mejor que nada).
            if (network.SourceNode == null && network.TerminalNodes.Count > 0)
            {
                var openEnds = network.TerminalNodes.Where(n => n.DemandM3s <= 0.0).ToList();
                PipeNode src = openEnds.Count > 0
                    ? openEnds.OrderBy(n => n.ElevationM).First()
                    : network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
                src.IsSource = true; src.IsTerminal = false;
                network.TerminalNodes.Remove(src);
                network.SourceNode = src;
            }
            // Orientar segmentos desde la fuente para que NodeStart quede
            // siempre del lado aguas-arriba (necesario para que SolveTreeNetwork
            // propague correctamente los caudales).
            network.OrientFromSource();
            network.SourcePressurePa = sourceKpa * 1000.0;
            // BFS desde la fuente: numeracion estable de nudos y tramos en
            // sentido del flujo (acometida -> aparatos).
            var pipeBfsOrder = ComputePipeBfsOrder(network);

            // Diagnostico topologico: cuenta componentes alcanzables desde la
            // fuente (deberia ser igual a Nodes.Count si la red esta conectada).
            int reached = CountReachableFromSource(network.SourceNode);
            int withDemand = network.TerminalNodes.Count(n => n.DemandM3s > 0.0);
            int numOpenEnds = network.TerminalNodes.Count(n => n.DemandM3s <= 0.0);
            double totalDemandLps = network.TerminalNodes.Sum(n => n.DemandM3s) * 1000.0;
            reader.Messages.Add(CalcWarning.Info("NETWORK_DIAG",
                $"Topologia: {network.Nodes.Count} nodos, {network.Segments.Count} segs, "
                + $"{withDemand} term-con-demanda, {numOpenEnds} open-ends, "
                + $"alcanzables-desde-fuente={reached}/{network.Nodes.Count}, "
                + $"sumDemandas={totalDemandLps:F1} l/s, "
                + $"sourceCota={network.SourceNode?.ElevationM:F2} m."));

            var fluid = FluidProperties.Water(tempC);
            var criteria = SizingCriteriaConfig.FromPreset(preset);
            criteria.FluidTemperatureC = tempC;
            // Perfil de simultaneidad seleccionado por el usuario (override del preset).
            var building = CboPipeBuilding.SelectedItem as BuildingTypeItem;
            if (building != null) criteria.SimultaneityProfile = building.Profile;
            var calc = new HydraulicCalculator();
            var report = calc.RunFullCalculation(network, fluid, criteria);

            // Propagar los avisos del reader al informe (NO_FIXTURES, DEMANDS_OK, etc.)
            foreach (var m in reader.Messages) report.AddMessage(m);

            // Las filas se generan recorriendo la lista BFS para que el orden
            // visible coincida con el sentido del flujo y no pueda alterarse.
            // Tras cada tramo se intercala una fila de accesorio (TEE, codo,
            // terminal, ...) para que la lectura de la tabla refleje la
            // secuencia tuberia -> accesorio -> tuberia -> ...
            var pipeRows = new List<ResultRow>();
            foreach (var s in pipeBfsOrder)
            {
                var pipeRow = MakeRowFromPipe(s,
                    report.Segments.TryGetValue(s.RevitId, out var r) ? r : null);
                pipeRows.Add(pipeRow);
                var fitRow = MakeFittingRowFromPipe(s, pipeRow);
                if (fitRow != null) pipeRows.Add(fitRow);
            }
            // [v7.2] Reordenar en DFS preorder branch-first (menor Q primero)
            // para que cada sub-árbol quede contiguo en la tabla y en el
            // esquema. Esencial para que el render se lea como un árbol.
            pipeRows = ReorderDfsBranchFirst(pipeRows);
            PopulateGrid(pipeRows);
            // [v7] Color único del esquema = LineColor del PipingSystemType.
            _lastSystemBrush = GetSystemBrush(system);
            BuildSchematic(pipeRows);
            // [Fase 1] Registrar binding Row → ElementId para sync con Revit.
            RegisterRowsForSync(pipeRows);

            _lastReport = report;
            _lastSystemName = system.Name;
            _lastIsPiping = true;
            BtnApply.IsEnabled = true;
            BtnDiag.IsEnabled = true;
            BtnExport.IsEnabled = true;
            BtnSchema.IsEnabled = true;
            LblStatus.Text = $"OK · {network.Segments.Count} tramos · "
                + $"ΔP peor caso = {report.WorstCasePressureDropPa:F0} Pa · "
                + $"avisos {report.CountWarnings} · errores {report.CountErrors}";
            LblStatus.ToolTip = BuildDiagToolTip(report);
        }

        private void CalculateDucts()
        {
            var system = CboDuctSystems.SelectedItem as MechanicalSystem;
            if (system == null) { LblStatus.Text = "Selecciona un MechanicalSystem."; return; }

            var preset = (CboDuctCriteria.SelectedItem as PresetItem)?.Key ?? "RITE_SUPPLY";
            double tempC = SldDuctTemp.Value;
            double altitudeM = ParseDouble(TxtDuctAltitude.Text, 0.0);
            double sourcePa  = ParseDouble(TxtDuctSourcePa.Text, 250.0);

            var reader = new DuctNetworkReader();
            var network = reader.ReadFromSystem(_doc, system);
            if (network.Segments.Count == 0)
            {
                LblStatus.Text = "El sistema no contiene tramos validos.";
                return;
            }
            // Heuristica fuente: tras leer las demandas de los terminales de
            // aire, los nodos sin demanda (extremos abiertos sin terminal
            // conectado) son candidatos a "acometida del ventilador / UTA".
            // Cogemos el de mayor cota — caso tipico del techo / cubierta.
            // Si no hubiera ninguno, fallback al terminal de mayor cota.
            if (network.SourceNode == null && network.TerminalNodes.Count > 0)
            {
                var openEnds = network.TerminalNodes.Where(n => n.DemandM3s <= 0.0).ToList();
                DuctNode src = openEnds.Count > 0
                    ? openEnds.OrderByDescending(n => n.ElevationM).First()
                    : network.TerminalNodes.OrderByDescending(n => n.ElevationM).First();
                src.IsSource = true; src.IsTerminal = false;
                network.TerminalNodes.Remove(src);
                network.SourceNode = src;
            }
            // Orientar segmentos desde la fuente (ver comentario en CalculatePiping).
            network.OrientFromSource();
            network.SourcePressurePa = sourcePa;
            // BFS desde la fuente para orden de visualizacion.
            var ductBfsOrder = ComputeDuctBfsOrder(network);

            // Diagnostico topologico (ver explicacion en CalculatePiping).
            int dReached = CountReachableFromDuctSource(network.SourceNode);
            int dWithDemand = network.TerminalNodes.Count(n => n.DemandM3s > 0.0);
            int dOpenEnds = network.TerminalNodes.Count(n => n.DemandM3s <= 0.0);
            double dTotalDemandLps = network.TerminalNodes.Sum(n => n.DemandM3s) * 1000.0;
            reader.Messages.Add(CalcWarning.Info("NETWORK_DIAG",
                $"Topologia: {network.Nodes.Count} nodos, {network.Segments.Count} segs, "
                + $"{dWithDemand} term-con-demanda, {dOpenEnds} open-ends, "
                + $"alcanzables-desde-fuente={dReached}/{network.Nodes.Count}, "
                + $"sumDemandas={dTotalDemandLps:F1} l/s, "
                + $"sourceCota={network.SourceNode?.ElevationM:F2} m."));

            var fluid = AirProperties.AsFluid(tempC, altitudeM);
            var criteria = SizingCriteriaConfig.FromPreset(preset);
            criteria.FluidTemperatureC = tempC;
            var calc = new AeraulicCalculator();
            var report = calc.RunFullCalculation(network, fluid, criteria);

            // Propagar los avisos del reader al informe (NO_TERMINALS, DEMANDS_OK, TERMINAL_DP, etc.)
            foreach (var m in reader.Messages) report.AddMessage(m);

            var ductRows = new List<ResultRow>();
            foreach (var s in ductBfsOrder)
            {
                var ductRow = MakeRowFromDuct(s,
                    report.Segments.TryGetValue(s.RevitId, out var r) ? r : null);
                ductRows.Add(ductRow);
                var fitRow = MakeFittingRowFromDuct(s, ductRow);
                if (fitRow != null) ductRows.Add(fitRow);
            }
            // [v7.2] Reordenar en DFS preorder branch-first (ver explicación en
            // CalculatePiping).
            ductRows = ReorderDfsBranchFirst(ductRows);
            PopulateGrid(ductRows);
            // [v7] Color único del esquema = LineColor del MechanicalSystemType.
            _lastSystemBrush = GetSystemBrush(system);
            BuildSchematic(ductRows);
            // [Fase 1] Registrar binding Row → ElementId para sync con Revit.
            RegisterRowsForSync(ductRows);

            _lastReport = report;
            _lastSystemName = system.Name;
            _lastIsPiping = false;
            BtnApply.IsEnabled = true;
            BtnDiag.IsEnabled = true;
            BtnExport.IsEnabled = true;
            BtnSchema.IsEnabled = true;
            LblStatus.Text = $"OK · {network.Segments.Count} tramos · "
                + $"ΔP peor caso = {report.WorstCasePressureDropPa:F0} Pa · "
                + $"avisos {report.CountWarnings} · errores {report.CountErrors}";
            LblStatus.ToolTip = BuildDiagToolTip(report);
        }

        // -------------------------------------------------------------------
        //  Helpers de diagnostico
        // -------------------------------------------------------------------

        private static int CountReachableFromSource(PipeNode source)
        {
            if (source == null) return 0;
            var visited = new HashSet<PipeNode> { source };
            var q = new Queue<PipeNode>();
            q.Enqueue(source);
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                foreach (var seg in n.ConnectedSegments)
                {
                    var other = ReferenceEquals(seg.NodeStart, n) ? seg.NodeEnd : seg.NodeStart;
                    if (other == null || visited.Contains(other)) continue;
                    visited.Add(other);
                    q.Enqueue(other);
                }
            }
            return visited.Count;
        }

        private static int CountReachableFromDuctSource(DuctNode source)
        {
            if (source == null) return 0;
            var visited = new HashSet<DuctNode> { source };
            var q = new Queue<DuctNode>();
            q.Enqueue(source);
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                foreach (var seg in n.ConnectedSegments)
                {
                    var other = ReferenceEquals(seg.NodeStart, n) ? seg.NodeEnd : seg.NodeStart;
                    if (other == null || visited.Contains(other)) continue;
                    visited.Add(other);
                    q.Enqueue(other);
                }
            }
            return visited.Count;
        }

        private static string BuildDiagToolTip(CalcReport report)
        {
            if (report == null) return null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Mensajes ===");
            foreach (var m in report.AllMessages)
                sb.AppendLine("[" + m.Severity + "] " + m.Code + ": " + m.Message);
            return sb.ToString();
        }

        // -------------------------------------------------------------------
        //  [Fase 3] Generar esquema de principio (preview WPF + Revit view)
        // -------------------------------------------------------------------

        private void OnGenerateSchema(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastSchemaRows == null || _lastSchemaRows.Count == 0)
                {
                    LblStatus.Text = "Calcula primero un sistema.";
                    return;
                }

                // Reconstruir columnOf y CriticalPath con el estado actual.
                var columnOf = ComputeColumnsByTrunk(_lastSchemaRows);
                var critRes = _activeCriticalPath;   // null si no está activo

                // Leyenda con los metadatos del último cálculo.
                var legend = new E3G.Saneamiento.Core.Schematic.SchematicLegend
                {
                    SystemName = _lastSystemName ?? "(sin nombre)",
                    CriteriaName = _lastIsPiping
                        ? (CboPipeCriteria.SelectedItem?.ToString() ?? "")
                        : (CboDuctCriteria.SelectedItem?.ToString() ?? ""),
                    PipeCount = _lastSchemaRows.Count(r => r != null && !r.IsFitting),
                    WorstCaseDpPa = _lastReport?.WorstCasePressureDropPa ?? 0,
                    WarningCount = _lastReport?.CountWarnings ?? 0,
                    ErrorCount = _lastReport?.CountErrors ?? 0,
                    GeneratedAt = DateTime.Now,
                };

                var preview = new E3G.Saneamiento.UI.Schematic.SchematicPreviewWindow(
                    _uiAppOpt, _sync, _lastSchemaRows, columnOf, critRes, legend,
                    _schematicBus)
                {
                    Owner = this,
                };
                preview.Show();
                LblStatus.Text = "Previsualización abierta.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Error al abrir preview: " + ex.Message;
                MessageBox.Show(this,
                    "Excepción al abrir el preview del esquema:\n\n" + ex,
                    "Generar esquema", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Campo de respaldo para la UIApplication recibida por el constructor —
        // el preview lo necesita pero la sobrecarga clásica no lo tiene.
        private UIApplication _uiAppOpt;
        // ExternalEventBus creado en constructor (contexto API válido) y
        // reutilizado por el preview para generar la vista en Revit.
        private RevitBridge.Sync.ExternalEventBus _schematicBus;

        // -------------------------------------------------------------------
        //  Aplicar al modelo
        // -------------------------------------------------------------------

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (_lastReport == null) return;
            try
            {
                var writer = new ResultWriter();
                writer.WriteToRevit(_doc, _lastReport,
                    "MEP Calc - " + (_lastSystemName ?? "sistema"));
                MessageBox.Show(this,
                    $"Aplicado.\n\nElementos actualizados: {writer.Updated}\n"
                    + $"Sin parametros MEP_CALC_*: {writer.Skipped}\n\n"
                    + (writer.Skipped > 0
                        ? "Para grabar todos los resultados crea los Shared Parameters MEP_CALC_*\n"
                          + "y vuelve a ejecutar."
                        : "Todos los tramos actualizados correctamente."),
                    "MEP Calc - Aplicado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LblStatus.Text = $"Aplicado · {writer.Updated} actualizados · {writer.Skipped} sin parametros.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error al escribir al modelo:\n\n" + ex.Message,
                    "MEP Calc", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------
        //  Exportar informe (Sprint 8)
        // -------------------------------------------------------------------

        private void OnExport(object sender, RoutedEventArgs e)
        {
            if (_lastReport == null || Rows.Count == 0)
            {
                MessageBox.Show(this, "Aún no hay resultados que exportar. Pulsa Calcular antes.",
                    "Exportar informe", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar informe MEP",
                FileName = "InformeMEP_"
                    + (string.IsNullOrEmpty(_lastSystemName) ? "sistema" : SanitizeForFilename(_lastSystemName))
                    + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm"),
                Filter = "Excel (CSV UTF-8)|*.csv|"
                       + "PDF imprimible (HTML)|*.html|"
                       + "Todos los archivos|*.*",
                DefaultExt = ".csv",
                AddExtension = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var meta = BuildExportMetadata();
                var reportRows = BuildExportRows();

                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext == ".html" || ext == ".htm" || dlg.FilterIndex == 2)
                {
                    ReportExporter.WriteHtml(dlg.FileName, meta, reportRows);
                }
                else
                {
                    ReportExporter.WriteCsv(dlg.FileName, meta, reportRows);
                }

                LblStatus.Text = "Informe exportado · " + dlg.FileName;

                // Ofrecer abrirlo (con la app por defecto: Excel para CSV,
                // navegador para HTML — desde donde Ctrl+P → Guardar como PDF).
                var openMsg = (ext == ".html" || ext == ".htm")
                    ? "Informe HTML generado.\n\n¿Abrirlo ahora? (desde el navegador puedes hacer Ctrl+P → Guardar como PDF.)"
                    : "Informe CSV generado.\n\n¿Abrirlo ahora con Excel?";
                if (MessageBox.Show(this, openMsg + "\n\n" + dlg.FileName,
                        "Exportar informe", MessageBoxButton.YesNo,
                        MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dlg.FileName,
                        UseShellExecute = true,
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error al exportar:\n\n" + ex.Message,
                    "Exportar informe", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ReportMetadata BuildExportMetadata()
        {
            bool piping = _lastIsPiping;
            var meta = new ReportMetadata
            {
                Domain      = piping ? "Tuberías" : "Conductos",
                SystemName  = _lastSystemName ?? "",
                Criterion   = piping
                    ? ((CboPipeCriteria.SelectedItem as PresetItem)?.Label ?? "")
                    : ((CboDuctCriteria.SelectedItem as PresetItem)?.Label ?? ""),
                FluidLabel  = piping ? "Agua" : "Aire",
                ProjectName = _doc?.Title ?? "",
                ProjectPath = _doc?.PathName ?? "",
                GeneratedAt = DateTime.Now,
            };
            if (piping)
            {
                meta.BuildingType   = (CboPipeBuilding.SelectedItem as BuildingTypeItem)?.Label ?? "";
                meta.TemperatureC   = SldPipeTemp.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                meta.SourcePressure = (TxtPipeSourceKpa.Text ?? "").Trim();
            }
            else
            {
                meta.TemperatureC = SldDuctTemp.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                meta.Altitude     = (TxtDuctAltitude.Text ?? "").Trim();
                meta.SourcePressure = ((TxtDuctSourcePa.Text ?? "").Trim()) + " Pa";
            }
            if (_lastReport != null)
            {
                meta.TotalSegments = _lastReport.Segments.Count.ToString();
                meta.WorstDpPa     = _lastReport.WorstCasePressureDropPa.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                meta.WarningCount  = _lastReport.CountWarnings.ToString();
                meta.ErrorCount    = _lastReport.CountErrors.ToString();
            }
            return meta;
        }

        private List<ReportRow> BuildExportRows()
        {
            var list = new List<ReportRow>(Rows.Count);
            foreach (var r in Rows)
            {
                list.Add(new ReportRow
                {
                    ElementKind     = r.ElementLabel ?? "",
                    BfsIndex        = r.BfsIndexDisplay ?? "",
                    NodePath        = r.NodePath ?? "",
                    LevelName       = r.LevelName ?? "",
                    RevitId         = r.RevitIdDisplay ?? "",
                    Size            = r.SizeDisplay ?? "",
                    RecommendedSize = r.RecommendedSizeDisplay ?? "",
                    FixtureCount    = r.FixtureCountDisplay ?? "",
                    KFactor         = r.KFactorDisplay ?? "",
                    RawFlowLps      = r.RawFlowLpsDisplay ?? "",
                    FlowLps         = r.FlowLpsDisplay ?? "",
                    Velocity        = r.VelocityDisplay ?? "",
                    FrictionPaPm    = r.FrictionPaPmDisplay ?? "",
                    TotalLoss       = r.TotalLossDisplay ?? "",
                    Status          = r.Status ?? "",
                    StatusKind      = r.StatusKind ?? "",
                    IsFitting       = r.IsFitting,
                });
            }
            return list;
        }

        private static string SanitizeForFilename(string s)
        {
            if (string.IsNullOrEmpty(s)) return "sistema";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // -------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------

        private void PopulateGrid(IEnumerable<ResultRow> rows)
        {
            Rows.Clear();
            // Importante: NO reordenar aqui.  La lista llega ya en orden BFS
            // desde la fuente (acometida) y debe mantenerse asi para reflejar
            // el sentido fisico del flujo del fluido en el sistema.
            foreach (var r in rows) Rows.Add(r);
        }

        // -------------------------------------------------------------------
        //  Visor esquematico (estilo MagiCAD)
        // -------------------------------------------------------------------

        // Dibujado a ~22 px por fila para que cada glifo case visualmente con
        // su fila correspondiente del DataGrid (DataGrid usa filas similares).
        private const double SchemaRowHeight = 22.0;
        private const double SchemaTopPad    = 12.0;
        private const double SchemaTrunkX    = 24.0;
        private const double SchemaIndentMin = 55.0;   // mínimo — fuerza ramales visiblemente separados
        private const double SchemaIndentMax = 140.0;
        private const double SchemaLabelPad  = 12.0;
        private const double SchemaLabelReserve = 60.0;
        // [v7.3] Offset al que se dibuja el glifo de accesorio respecto a la
        // línea (col x). Antes el glifo se solapaba con el tronco vertical y
        // no se leía. Lo desplazamos a x+SchemaGlyphOffset y el texto a
        // continuación con SchemaLabelPadFitting de margen.
        private const double SchemaGlyphOffset       = 8.0;
        private const double SchemaLabelPadFitting   = 18.0;

        // [v7] El esquema usa un único color = color del sistema MEP
        // (PipingSystemType.LineColor / MechanicalSystemType.LineColor).
        // Si el sistema no define LineColor (o es (0,0,0) "by category"),
        // se cae a un azul oscuro neutro coherente con el resto de la UI.
        private static readonly Brush DefaultSystemBrush
            = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x3A, 0x5E));

        /// <summary>
        /// Devuelve un Brush de WPF a partir del LineColor del tipo de
        /// sistema (PipingSystemType / MechanicalSystemType).  Cuando el
        /// color no está definido, devuelve <see cref="DefaultSystemBrush"/>.
        /// </summary>
        private Brush GetSystemBrush(Autodesk.Revit.DB.MEPSystem system)
        {
            if (system == null) return DefaultSystemBrush;
            try
            {
                Autodesk.Revit.DB.ElementType type =
                    _doc.GetElement(system.GetTypeId()) as Autodesk.Revit.DB.ElementType;
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
            catch
            {
                return DefaultSystemBrush;
            }
        }

        // [v4] Indentación dinámica — se recalcula con el ancho del panel para
        // que las ramas se separen proporcionalmente al redimensionar (estilo
        // MagiCAD).  Se actualiza al inicio de BuildSchematic y cuando el
        // ScrollViewer cambia de tamaño.
        private double _schemaIndentPx = 36.0;

        // Cache de la última lista de rows para poder rerender al redimensionar.
        private IList<ResultRow> _lastSchemaRows;

        // Mapa fila -> glifo principal, usado al seleccionar fila.  Indexamos
        // por ResultRow para soportar tanto filas de tuberia (BfsIndex>0) como
        // filas de accesorio (BfsIndex=0).
        private readonly Dictionary<ResultRow, FrameworkElement> _schemaGlyphs
            = new Dictionary<ResultRow, FrameworkElement>();
        private FrameworkElement _highlightedGlyph;

        // [Fase 2] Tracking de la WpfLine que representa cada pipe en el
        // esquema.  Lo poblamos en BuildSchematic y lo consumen los Apply
        // (camino crítico, heatmap) para recolorear sin reconstruir todo.
        private readonly Dictionary<ResultRow, WpfLine> _schemaLines
            = new Dictionary<ResultRow, WpfLine>();
        // Stroke original y grosor por línea — para poder revertir.
        private readonly Dictionary<WpfLine, (Brush stroke, double thickness)> _lineOriginalStrokes
            = new Dictionary<WpfLine, (Brush stroke, double thickness)>();

        // Servicios analíticos y estado activo.
        private readonly CriticalPathService _criticalPathSvc = new CriticalPathService();
        private readonly HeatmapService _heatmapSvc = new HeatmapService();
        private CriticalPathResult _activeCriticalPath;
        private HeatmapResult _activeHeatmap;

        /// <summary>
        /// Construye un esquema vertical estilo MagiCAD: cada fila del grid es
        /// un nudo en la canvas; la profundidad determina la indentación
        /// horizontal.  Cada bifurcación se dibuja como una T con linea
        /// horizontal hacia la rama y un punto en el tronco.  Cuando la
        /// profundidad disminuye (volvemos del ramal al tronco) la conexión
        /// se hace al ancestro real, no a la fila anterior.
        /// </summary>
        private void BuildSchematic(IList<ResultRow> rows)
        {
            SchematicCanvas.Children.Clear();
            _schemaGlyphs.Clear();
            _schemaLines.Clear();
            _lineOriginalStrokes.Clear();
            _highlightedGlyph = null;
            _lastSchemaRows = rows;
            if (rows == null || rows.Count == 0) return;

            // [v5] Asignar columna a cada fila según topología tronco/rama
            // (el ramal con mayor flujo a cada bifurcación continúa el tronco).
            var columnOf = ComputeColumnsByTrunk(rows);
            int maxCol = columnOf.Values.DefaultIfEmpty(0).Max();

            // [v4] Indentación dinámica en función del ancho disponible para
            // que las ramas se expandan al redimensionar (estilo MagiCAD).
            double availableWidth = SchematicScroll?.ViewportWidth ?? 0;
            if (availableWidth < 200) availableWidth = SchematicCanvas.Width;
            if (availableWidth < 200) availableWidth = 460;
            if (maxCol >= 1)
            {
                double targetIndent = (availableWidth - SchemaTrunkX - SchemaLabelReserve)
                                      / Math.Max(1, maxCol);
                _schemaIndentPx = Math.Min(SchemaIndentMax,
                                           Math.Max(SchemaIndentMin, targetIndent));
            }
            else
            {
                _schemaIndentPx = 40.0;   // un solo nivel — valor neutro
            }

            // [v7.3] Marcador de fuente — vértice apuntando HACIA ABAJO
            // para indicar el sentido del flujo (acometida → terminales).
            double topY = SchemaTopPad;
            var srcMarker = new WpfPolygon
            {
                Points = new PointCollection
                {
                    new WpfPoint(SchemaTrunkX - 6,    topY - 6),
                    new WpfPoint(SchemaTrunkX + 6,    topY - 6),
                    new WpfPoint(SchemaTrunkX,        topY + 4),
                },
                Fill = _lastSystemBrush,
                Stroke = Brushes.Black,
                StrokeThickness = 0.8,
                ToolTip = "Fuente / Acometida",
            };
            SchematicCanvas.Children.Add(srcMarker);
            var srcLabel = new TextBlock
            {
                Text = "Fuente", FontSize = 10, Foreground = _lastSystemBrush,
            };
            Canvas.SetLeft(srcLabel, SchemaTrunkX + 10);
            Canvas.SetTop(srcLabel, topY - 8);
            SchematicCanvas.Children.Add(srcLabel);

            // [v3] Conectividad real basada en nodos (no en profundidad):
            //   nodePositions[nombreNodo] = (X, Y, profundidad) del último
            //   row cuyo flujo sale por ese nodo. El padre de cada row es
            //   el row que escribió la entrada de éste (el "incoming"). Así
            //   detectamos correctamente hermanos (mismo padre, distinto
            //   incoming que el row previo) y evitamos el bug de "ramal
            //   nuevo saliendo de un TERMINAL".
            var nodePositions = new Dictionary<string, (double X, double Y, int Depth)>();

            // La fuente está siempre en (SchemaTrunkX, topY+4) — no hace
            // falta diccionario.

            // Las T-junctions se acumulan y se dibujan AL FINAL para que no
            // queden tapadas por troncos verticales redibujados posteriormente.
            // [v7.4] Cada junction lleva además el fitting del nodo padre
            // (TEE/CROSS) — si existe, su glifo se dibuja AHÍ en lugar del
            // círculo genérico, para que el dibujo refleje directamente el
            // tipo de accesorio en el punto físico de bifurcación.
            var teeJunctions = new List<(double X, double Y, ResultRow ParentFit)>();

            // Mapa nodo -> fitting cuyo nudo final es ese nodo.  Usado para
            // dibujar el glifo TEE/CROSS en el punto de bifurcación.
            var fittingByNode = new Dictionary<string, ResultRow>();
            foreach (var r in rows)
            {
                if (!r.IsFitting) continue;
                string node = GetIncomingNode(r.NodePath);
                if (!string.IsNullOrEmpty(node)) fittingByNode[node] = r;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                double y = SchemaTopPad + (i + 1) * SchemaRowHeight;

                // [v5] X según columna tronco/rama (no según profundidad BFS).
                // El tronco principal se queda en col 0 todo el recorrido;
                // sólo los ramales con menor flujo se desplazan a col+1.
                int col = columnOf.TryGetValue(row, out int c) ? c : 0;
                double x = SchemaTrunkX + col * _schemaIndentPx;

                // [v7.3] El esquema usa SIEMPRE el color del sistema. El
                // color de estado (OK/Warning/Error) se mantiene en la
                // tabla (DataGrid) pero no en el dibujo — el usuario quiere
                // un esquema mono-color como el HTML aprobado.
                var color = _lastSystemBrush;

                // Localizar al padre real usando el nodo de entrada del row.
                string incoming = GetIncomingNode(row.NodePath);
                string outgoing = GetOutgoingNode(row.NodePath);

                double pX, pY;
                if (!string.IsNullOrEmpty(incoming)
                    && nodePositions.TryGetValue(incoming, out var parentPos))
                {
                    pX = parentPos.X;
                    pY = parentPos.Y;
                }
                else
                {
                    // Primera fila / padre todavía no registrado: conectar al
                    // marcador de la fuente en la columna del tronco.
                    pX = SchemaTrunkX;
                    pY = topY + 4;
                }

                bool isContinuation = Math.Abs(pX - x) < 0.1;

                // [v7] Color único = color del sistema MEP.  Tronco y ramas
                // se pintan del mismo color; sólo varía el grosor (más fino
                // a medida que profundiza la rama).
                Brush colBrush = _lastSystemBrush;
                int parentCol = (int)Math.Round((pX - SchemaTrunkX) / _schemaIndentPx);
                double trunkThickness = col == 0 ? 3.0 : (col == 1 ? 2.4 : 2.0);

                if (isContinuation)
                {
                    // Continuación en la misma columna — vertical sólido del
                    // color del sistema. Más fino para fittings (línea sin
                    // longitud de tramo) y más grueso para tuberías.
                    var pipeLine = new WpfLine
                    {
                        X1 = x, Y1 = pY, X2 = x, Y2 = y,
                        Stroke = _lastSystemBrush,
                        StrokeThickness = row.IsFitting ? trunkThickness * 0.5 : trunkThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    SchematicCanvas.Children.Add(pipeLine);
                    // [Fase 2] Registrar la línea del tramo para overlay de
                    // camino crítico / heatmap (solo pipes, no fittings).
                    if (!row.IsFitting)
                    {
                        _schemaLines[row] = pipeLine;
                        _lineOriginalStrokes[pipeLine] = (pipeLine.Stroke, pipeLine.StrokeThickness);
                    }
                }
                else
                {
                    // [v7.1] Bifurcación: eliminamos el "primer segmento" del
                    // ramal (la T horizontal solida + la prolongación vertical
                    // del tronco padre hasta la fila actual). La continuidad
                    // del tronco la mantiene la propia fila siguiente de col 0.
                    // Aquí sólo dibujamos:
                    //   1) Una conexión HORIZONTAL discontinua al nivel del
                    //      ramal — del tronco padre (pX) a la columna hija (x)
                    //      a la Y de esta fila — para indicar visualmente la
                    //      salida del ramal sin saturar el dibujo.
                    //   2) Un círculo (acumulado en teeJunctions) en la esquina
                    //      exacta de la bifurcación (pX, y) sobre el tronco.
                    var branchLine = new WpfLine
                    {
                        X1 = pX, Y1 = y, X2 = x, Y2 = y,
                        Stroke = colBrush,
                        StrokeThickness = 1.4,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap   = PenLineCap.Round,
                    };
                    SchematicCanvas.Children.Add(branchLine);
                    // [Fase 2] El ramal usa la línea horizontal como su línea
                    // representativa (el tramo vertical vendrá en la fila
                    // siguiente, que ya será continuación). Aun así la
                    // registramos aquí para que el overlay pueda colorear la
                    // conexión de rama cuando se activa el heatmap o camino crítico.
                    if (!row.IsFitting)
                    {
                        _schemaLines[row] = branchLine;
                        _lineOriginalStrokes[branchLine] = (branchLine.Stroke, branchLine.StrokeThickness);
                    }
                    // El fitting del nodo padre (si es TEE/CROSS) se usará
                    // para dibujar SU glifo en (pX, y) en lugar del círculo
                    // genérico — el accesorio queda justo en el sitio donde
                    // físicamente está la bifurcación.
                    ResultRow parentFit = null;
                    if (!string.IsNullOrEmpty(incoming))
                        fittingByNode.TryGetValue(incoming, out parentFit);
                    teeJunctions.Add((pX, y, parentFit));
                }

                // Registrar la posición de SALIDA de este row para que sus
                // descendientes (filas posteriores con incoming == outgoing)
                // sepan dónde conectarse.
                if (!string.IsNullOrEmpty(outgoing))
                    nodePositions[outgoing] = (x, y, col);

                FrameworkElement glyph;
                string lblText;
                if (row.IsFitting)
                {
                    // Fila accesorio: glifo DESPLAZADO de la línea (a la
                    // derecha del tronco vertical) + etiqueta cursiva gris
                    // con "TEE @N5" / "ELBOW @N6" / "TERMINAL @N53".  Así el
                    // glifo no se superpone al vertical y se lee claro.
                    // [v7.4] Para TEE/CROSS el glifo NO se dibuja aquí — se
                    // dibujará en el punto exacto de bifurcación (ver bucle
                    // final de junctions). Aquí queda sólo la etiqueta.
                    string tip = (row.EndNodeKind ?? "?") + "  " + row.NodePath
                               + (string.IsNullOrEmpty(row.LevelName)
                                    ? "" : "\nNivel: " + row.LevelName);
                    string kind = row.EndNodeKind ?? "";
                    bool isBifurcationFit = (kind == "TEE" || kind == "CROSS");
                    glyph = isBifurcationFit
                        ? null
                        : DrawNodeGlyph(row, x + SchemaGlyphOffset, y, _lastSystemBrush, tip);
                    lblText = (row.EndNodeKind ?? "") + " " + (row.NodePath ?? "");
                }
                else
                {
                    // Fila tuberia: flecha de flujo sobre la línea + etiqueta
                    // completa "#N · NodePath · DN · Q L/s" (igual que el HTML).
                    // La flecha apunta hacia ABAJO (sentido del flujo en el
                    // esquema, de la fuente hacia los terminales).
                    var arrow = new WpfPolygon
                    {
                        Points = new PointCollection
                        {
                            new WpfPoint(x - 3, y - 3),
                            new WpfPoint(x + 3, y - 3),
                            new WpfPoint(x,     y + 3),
                        },
                        Fill = _lastSystemBrush,
                        Stroke = Brushes.Black, StrokeThickness = 0.5,
                        ToolTip = "Tramo #" + row.BfsIndex + "  " + row.NodePath
                                + "\n" + row.Size
                                + (string.IsNullOrEmpty(row.RecommendedSize)
                                    ? "" : "  (rec " + row.RecommendedSize + ")")
                                + (string.IsNullOrEmpty(row.LevelName)
                                    ? "" : "\nNivel: " + row.LevelName)
                                + "\n" + (row.Status ?? ""),
                        Tag = row,
                    };
                    arrow.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(arrow);
                    glyph = arrow;
                    // Etiqueta completa estilo HTML:
                    //   "#1 · N1 → N2 · DN50 · 2.12 L/s"
                    var sb = new System.Text.StringBuilder();
                    sb.Append("#").Append(row.BfsIndex);
                    if (!string.IsNullOrEmpty(row.NodePath))
                        sb.Append(" · ").Append(row.NodePath);
                    if (!string.IsNullOrEmpty(row.Size))
                        sb.Append(" · ").Append(row.Size);
                    if (!row.IsFitting)
                        sb.Append(" · ").Append(row.FlowLps.ToString("F2",
                            System.Globalization.CultureInfo.InvariantCulture)).Append(" L/s");
                    lblText = sb.ToString();
                }
                if (glyph != null) _schemaGlyphs[row] = glyph;

                if (!string.IsNullOrEmpty(lblText))
                {
                    // Color del texto adaptado al tema activo:
                    //   pipe → foreground principal (negro / casi-blanco)
                    //   fitting → gris atenuado (legible sobre fondo claro u oscuro)
                    Brush pipeFg = _isDarkTheme
                        ? new SolidColorBrush(WpfColor.FromRgb(0xE5, 0xE5, 0xE5))
                        : Brushes.Black;
                    Brush fitFg = _isDarkTheme
                        ? new SolidColorBrush(WpfColor.FromRgb(0xA0, 0xA0, 0xA0))
                        : new SolidColorBrush(WpfColor.FromRgb(0x66, 0x66, 0x66));
                    var lbl = new TextBlock
                    {
                        Text = lblText,
                        FontSize = row.IsFitting ? 9 : 10,
                        Foreground = row.IsFitting ? fitFg : pipeFg,
                        FontStyle = row.IsFitting ? FontStyles.Italic : FontStyles.Normal,
                        FontWeight = FontWeights.Normal,
                    };
                    // Para fittings la etiqueta empieza después del glifo
                    // desplazado; para pipes empieza justo al lado de la flecha.
                    Canvas.SetLeft(lbl, x + (row.IsFitting ? SchemaLabelPadFitting : SchemaLabelPad));
                    Canvas.SetTop(lbl, y - 8);
                    SchematicCanvas.Children.Add(lbl);
                }
            }

            // [v7.4] Marcadores de bifurcación — si el nodo padre tiene un
            // fitting TEE/CROSS, dibujamos SU glifo en el punto físico de
            // bifurcación (sustituyendo al círculo genérico). Así el glifo
            // del accesorio aparece exactamente donde está en la red, y la
            // fila del fitting queda sólo con la etiqueta italica.
            //
            // Para CROSS con varias ramas: el glifo se dibuja en la primera
            // (la de menor Q en DFS) y las subsiguientes mantienen el círculo
            // de bifurcación.
            var fittingDrawnAtJunction = new HashSet<ResultRow>();
            foreach (var (jx, jy, parentFit) in teeJunctions)
            {
                bool useFitGlyph = parentFit != null
                    && (parentFit.EndNodeKind == "TEE" || parentFit.EndNodeKind == "CROSS")
                    && !fittingDrawnAtJunction.Contains(parentFit);
                if (useFitGlyph)
                {
                    string tip = (parentFit.EndNodeKind ?? "?") + "  " + parentFit.NodePath
                               + (string.IsNullOrEmpty(parentFit.LevelName)
                                    ? "" : "\nNivel: " + parentFit.LevelName);
                    var fitGlyph = DrawNodeGlyph(parentFit, jx, jy, _lastSystemBrush, tip);
                    _schemaGlyphs[parentFit] = fitGlyph;
                    fittingDrawnAtJunction.Add(parentFit);
                }
                else
                {
                    var teeMark = new WpfEllipse
                    {
                        Width = 8, Height = 8,
                        Fill = _lastSystemBrush,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2,
                    };
                    Canvas.SetLeft(teeMark, jx - 4);
                    Canvas.SetTop(teeMark, jy - 4);
                    SchematicCanvas.Children.Add(teeMark);
                }
            }

            // Tamano del canvas: la indentación dinámica ya ha distribuido
            // las columnas dentro del ancho disponible. Si el número de
            // columnas máximo sobrepasa el panel (porque la indentación
            // quedó capada por SchemaIndentMax), el canvas crece y aparece
            // scroll horizontal automático.
            double naturalWidth = SchemaTrunkX
                                  + maxCol * _schemaIndentPx
                                  + SchemaLabelReserve;
            double panelWidth = SchematicScroll?.ViewportWidth ?? 0;
            double targetWidth = (panelWidth > 50) ? panelWidth - 4 : 460;
            SchematicCanvas.Width = Math.Max(targetWidth, naturalWidth);
            SchematicCanvas.Height = SchemaTopPad
                + (rows.Count + 1) * SchemaRowHeight + 20;
        }

        private static Brush ColorForStatus(string statusKind)
        {
            switch (statusKind)
            {
                case "OK":      return new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x7D, 0x32));
                case "Warning": return new SolidColorBrush(WpfColor.FromRgb(0xF5, 0x7C, 0x00));
                case "Error":   return new SolidColorBrush(WpfColor.FromRgb(0xC6, 0x28, 0x28));
                default:        return Brushes.Gray;
            }
        }

        /// <summary>Nodo donde entra el flujo de la fila (aguas-arriba).</summary>
        private static string GetIncomingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0) return nodePath.Substring(0, idx).Trim();
            return nodePath.Trim();
        }

        /// <summary>Nodo donde sale el flujo de la fila (aguas-abajo).</summary>
        private static string GetOutgoingNode(string nodePath)
        {
            if (string.IsNullOrEmpty(nodePath)) return null;
            if (nodePath.StartsWith("@")) return nodePath.Substring(1).Trim();
            int idx = nodePath.IndexOf('→');
            if (idx > 0 && idx + 1 < nodePath.Length)
                return nodePath.Substring(idx + 1).Trim();
            return nodePath.Trim();
        }

        /// <summary>
        /// [v5] Asigna a cada fila la columna donde se dibujará el glifo.
        /// En cada bifurcación (TEE/CROSS con varios hijos), el ramal con
        /// MAYOR flujo continúa el tronco en la misma columna del padre;
        /// los demás ramales se desplazan a una columna nueva (padre+1).
        /// Esto reproduce el estilo MagiCAD donde el tronco principal se
        /// mantiene a la izquierda y las ramificaciones quedan a la derecha,
        /// en vez de la escalera que generaba el sangrado por profundidad.
        /// </summary>
        private static Dictionary<ResultRow, int> ComputeColumnsByTrunk(IList<ResultRow> rows)
        {
            var columnOf = new Dictionary<ResultRow, int>();
            if (rows == null || rows.Count == 0) return columnOf;

            // Indexar tuberías por nodo de salida (para localizar padre) y
            // agrupar hijos por nodo de entrada (siblings comparten incoming).
            // IMPORTANTE: SOLO los pipes registran en rowByOutgoing. Los
            // fittings tienen outgoing == incoming == Nx y, si entraran, se
            // pisarían al pipe real que termina ahí, rompiendo la jerarquía
            // de columnas (todo el árbol acababa colapsado a col 0/1).
            var rowByOutgoing = new Dictionary<string, ResultRow>();
            var pipesIncomingAt = new Dictionary<string, List<ResultRow>>();
            foreach (var row in rows)
            {
                if (row.IsFitting) continue;
                string outgoing = GetOutgoingNode(row.NodePath);
                if (!string.IsNullOrEmpty(outgoing))
                    rowByOutgoing[outgoing] = row;
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
                    // Los accesorios heredan la columna de la tubería que
                    // termina en su nudo (la que les precede aguas-arriba).
                    string atNode = GetIncomingNode(row.NodePath);
                    if (!string.IsNullOrEmpty(atNode)
                        && rowByOutgoing.TryGetValue(atNode, out var endingPipe)
                        && columnOf.TryGetValue(endingPipe, out int pipeCol))
                    {
                        col = pipeCol;
                    }
                    else
                    {
                        col = 0;
                    }
                }
                else
                {
                    string incoming = GetIncomingNode(row.NodePath);
                    if (string.IsNullOrEmpty(incoming)
                        || !rowByOutgoing.TryGetValue(incoming, out var parentRow)
                        || !columnOf.TryGetValue(parentRow, out int parentCol))
                    {
                        // Tubería raíz (sin padre conocido) → tronco
                        col = 0;
                    }
                    else if (!pipesIncomingAt.TryGetValue(incoming, out var siblings)
                             || siblings.Count <= 1)
                    {
                        // Hijo único → continúa el tronco/sub-tronco
                        col = parentCol;
                    }
                    else
                    {
                        // Varios hermanos: el de mayor Q es la continuación
                        // de tronco; los demás son ramales (col+1).
                        // Desempate por BfsIndex (orden BFS más bajo = tronco).
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

        /// <summary>
        /// [v7.2] Re-ordena las filas (pipe+fitting alternados en orden BFS)
        /// en DFS preorder branch-first: en cada bifurcación se visita primero
        /// el ramal con MENOR caudal (su sub-árbol completo) y al final el de
        /// mayor caudal (continuación del tronco). Esto mantiene cada sub-árbol
        /// contiguo en la tabla y en el esquema, evitando que un descendiente
        /// profundo aparezca muchas filas después de su padre — que es lo que
        /// generaba las líneas diagonales largas en el render.
        /// La numeración BfsIndex se conserva (sigue siendo el identificador
        /// estable del tramo); sólo cambia el orden visual.
        /// </summary>
        private static List<ResultRow> ReorderDfsBranchFirst(IList<ResultRow> rowsBfs)
        {
            var result = new List<ResultRow>();
            if (rowsBfs == null || rowsBfs.Count == 0) return result;

            // Emparejar cada pipe con su fitting (la fila siguiente si IsFitting).
            var pipeRows = new List<ResultRow>();
            var fitOf = new Dictionary<ResultRow, ResultRow>();
            for (int i = 0; i < rowsBfs.Count; i++)
            {
                var r = rowsBfs[i];
                if (r.IsFitting) continue;
                pipeRows.Add(r);
                if (i + 1 < rowsBfs.Count && rowsBfs[i + 1].IsFitting)
                    fitOf[r] = rowsBfs[i + 1];
            }

            // Indexar por nodo de salida y agrupar hijos por nodo de entrada.
            var rowByOutgoing = new Dictionary<string, ResultRow>();
            var childrenOfNode = new Dictionary<string, List<ResultRow>>();
            foreach (var p in pipeRows)
            {
                string outgoing = GetOutgoingNode(p.NodePath);
                if (!string.IsNullOrEmpty(outgoing))
                    rowByOutgoing[outgoing] = p;
                string incoming = GetIncomingNode(p.NodePath);
                if (!string.IsNullOrEmpty(incoming))
                {
                    if (!childrenOfNode.TryGetValue(incoming, out var lst))
                        childrenOfNode[incoming] = lst = new List<ResultRow>();
                    lst.Add(p);
                }
            }

            // Raíces: pipes cuyo nodo de entrada no es nodo de salida de otro.
            var rootPipes = pipeRows
                .Where(p =>
                {
                    string incoming = GetIncomingNode(p.NodePath);
                    return string.IsNullOrEmpty(incoming)
                        || !rowByOutgoing.ContainsKey(incoming);
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
                if (!childrenOfNode.TryGetValue(outgoing, out var children)) return;
                // Menor Q primero (rama más fina) → su sub-árbol se dibuja
                // antes que la continuación del tronco. Desempate por BfsIndex
                // DESC (mismo criterio que el mockup aprobado).
                var sorted = children
                    .OrderBy(c => c.FlowLps)
                    .ThenByDescending(c => c.BfsIndex)
                    .ToList();
                foreach (var c in sorted) Dfs(c);
            }
            foreach (var root in rootPipes) Dfs(root);

            // Cualquier pipe no alcanzado (red desconectada) lo añadimos al final
            // para no perder filas.
            foreach (var p in pipeRows)
            {
                if (visited.Contains(p)) continue;
                result.Add(p);
                if (fitOf.TryGetValue(p, out var fit)) result.Add(fit);
            }
            return result;
        }

        /// <summary>
        /// Dibuja en SchematicCanvas el glifo correspondiente al nudo final
        /// del segmento — TERMINAL, TEE, CROSS, ELBOW, REDUCER o JUNCTION —
        /// y devuelve el FrameworkElement principal (para tracking de
        /// seleccion).
        /// </summary>
        private FrameworkElement DrawNodeGlyph(ResultRow row, double x, double y,
            Brush color, string tip)
        {
            FrameworkElement primary;
            switch (row.EndNodeKind)
            {
                case "TERMINAL":
                {
                    // Cuadrado relleno (consumidor / aparato).
                    var sq = new WpfRectangle
                    {
                        Width = 11, Height = 11, Fill = color,
                        Stroke = Brushes.Black, StrokeThickness = 0.8,
                        ToolTip = tip, Tag = row,
                    };
                    Canvas.SetLeft(sq, x - 5.5);
                    Canvas.SetTop(sq, y - 5.5);
                    sq.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(sq);
                    primary = sq;
                    break;
                }
                case "TEE":
                {
                    // T tumbada: linea horizontal + bajada hacia el ramal.
                    var tee = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} M {4} {5} L {6} {7}",
                            x - 7, y, x + 7, y,
                            x, y, x, y + 7)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    tee.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(tee);
                    primary = tee;
                    break;
                }
                case "CROSS":
                {
                    // Cruz simetrica.
                    var cross = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} M {4} {5} L {6} {7}",
                            x - 7, y, x + 7, y,
                            x, y - 7, x, y + 7)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    cross.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(cross);
                    primary = cross;
                    break;
                }
                case "ELBOW":
                {
                    // Codo: arco/L para indicar cambio de direccion.
                    var elbow = new WpfPath
                    {
                        Data = Geometry.Parse(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "M {0} {1} L {2} {3} L {4} {5}",
                            x, y - 6, x, y, x + 6, y)),
                        Stroke = color, StrokeThickness = 2.2,
                        ToolTip = tip, Tag = row,
                    };
                    elbow.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(elbow);
                    primary = elbow;
                    break;
                }
                case "REDUCER":
                {
                    // Trapecio: indicacion de cambio de seccion.
                    var trap = new WpfPolygon
                    {
                        Points = new PointCollection
                        {
                            new WpfPoint(x - 7, y - 4),
                            new WpfPoint(x + 7, y - 2),
                            new WpfPoint(x + 7, y + 2),
                            new WpfPoint(x - 7, y + 4),
                        },
                        Fill = Brushes.White,
                        Stroke = color, StrokeThickness = 1.6,
                        ToolTip = tip, Tag = row,
                    };
                    trap.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(trap);
                    primary = trap;
                    break;
                }
                case "FIN":
                {
                    // Tope / cap: barra perpendicular al final.
                    var cap = new WpfLine
                    {
                        X1 = x - 6, X2 = x + 6, Y1 = y, Y2 = y,
                        Stroke = color, StrokeThickness = 3.0,
                        ToolTip = tip, Tag = row,
                    };
                    cap.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(cap);
                    primary = cap;
                    break;
                }
                default:
                {
                    // JUNCTION u otros: punto.
                    var dot = new WpfEllipse
                    {
                        Width = 8, Height = 8, Fill = color,
                        Stroke = Brushes.Black, StrokeThickness = 0.6,
                        ToolTip = tip, Tag = row,
                    };
                    Canvas.SetLeft(dot, x - 4);
                    Canvas.SetTop(dot, y - 4);
                    dot.MouseLeftButtonDown += OnGlyphClicked;
                    SchematicCanvas.Children.Add(dot);
                    primary = dot;
                    break;
                }
            }
            return primary;
        }

        private void OnGlyphClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ResultRow row)
            {
                DgResults.SelectedItem = row;
                DgResults.ScrollIntoView(row);
            }
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Limpiar highlight previo.
            if (_highlightedGlyph != null)
            {
                _highlightedGlyph.Effect = null;
                _highlightedGlyph = null;
            }
            if (DgResults.SelectedItem is ResultRow row
                && _schemaGlyphs.TryGetValue(row, out var glyph))
            {
                glyph.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = WpfColors.DodgerBlue,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9,
                };
                _highlightedGlyph = glyph;
                // Asegurar visibilidad del glifo en el ScrollViewer.
                var top = Canvas.GetTop(glyph);
                if (!double.IsNaN(top)) SchematicScroll.ScrollToVerticalOffset(
                    Math.Max(0, top - SchematicScroll.ViewportHeight / 2));
            }
        }

        /// <summary>
        /// [v7.5] Click derecho sobre el DataGrid → menú contextual con un
        /// checkbox por cada columna. Permite mostrar/ocultar cualquier campo
        /// del cálculo (Nivel, Element Id, Talla, DN rec, N, K, Q raw, Q dis,
        /// v, ΔP/m, ΔP tot, Estado…) según interese al usuario.  Si solo
        /// quiere ver el caudal, basta con dejar marcado "Q dis [L/s]".
        /// El menú se reconstruye al abrirse para reflejar el estado real
        /// de Visibility de cada DataGridColumn.
        /// </summary>
        /// <summary>
        /// [v7.6] Alterna entre tema claro y oscuro. Cambia los SolidColorBrush
        /// de los recursos del Window (referenciados desde el XAML con
        /// DynamicResource), reconstruye el esquema para que sus textos y el
        /// fondo del canvas se repinten con la paleta correcta.
        /// </summary>
        private void OnToggleTheme(object sender, RoutedEventArgs e)
        {
            bool dark = BtnTheme != null && BtnTheme.IsChecked == true;
            ApplyTheme(dark);
        }

        private void ApplyTheme(bool dark)
        {
            // Paleta clara (valores por defecto del XAML)
            //   WindowBg=#F0F0F0  Fg=#222  Muted=#555  Border=#CCC
            //   PanelBg=#FFF  HeaderBg=#F5F5F5  AltRow=#FAFAFA  GridLine=#EEE
            //   Ok=#E8F5E9  Warn=#FFF8E1  Error=#FFEBEE  Splitter=#BDBDBD
            // Paleta oscura
            //   WindowBg=#1F1F1F  Fg=#E5E5E5  Muted=#A0A0A0  Border=#3A3A3A
            //   PanelBg=#2A2A2A  HeaderBg=#1A1A1A  AltRow=#252525  GridLine=#3A3A3A
            //   Ok=#1F3320  Warn=#3A2F0D  Error=#3A1A1A  Splitter=#3A3A3A
            void Set(string key, byte r, byte g, byte b)
            {
                var b1 = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
                b1.Freeze();
                Resources[key] = b1;
            }
            if (dark)
            {
                Set("ThemeWindowBg",  0x1F, 0x1F, 0x1F);
                Set("ThemeWindowFg",  0xE5, 0xE5, 0xE5);
                Set("ThemeMutedFg",   0xA0, 0xA0, 0xA0);
                Set("ThemeBorder",    0x3A, 0x3A, 0x3A);
                Set("ThemePanelBg",   0x2A, 0x2A, 0x2A);
                Set("ThemeHeaderBg",  0x1A, 0x1A, 0x1A);
                Set("ThemeAltRowBg",  0x25, 0x25, 0x25);
                Set("ThemeGridLine",  0x3A, 0x3A, 0x3A);
                Set("ThemeOkBg",      0x1F, 0x33, 0x20);
                Set("ThemeWarnBg",    0x3A, 0x2F, 0x0D);
                Set("ThemeErrorBg",   0x3A, 0x1A, 0x1A);
                Set("ThemeSplitter",  0x3A, 0x3A, 0x3A);
            }
            else
            {
                Set("ThemeWindowBg",  0xF0, 0xF0, 0xF0);
                Set("ThemeWindowFg",  0x22, 0x22, 0x22);
                Set("ThemeMutedFg",   0x55, 0x55, 0x55);
                Set("ThemeBorder",    0xCC, 0xCC, 0xCC);
                Set("ThemePanelBg",   0xFF, 0xFF, 0xFF);
                Set("ThemeHeaderBg",  0xF5, 0xF5, 0xF5);
                Set("ThemeAltRowBg",  0xFA, 0xFA, 0xFA);
                Set("ThemeGridLine",  0xEE, 0xEE, 0xEE);
                Set("ThemeOkBg",      0xE8, 0xF5, 0xE9);
                Set("ThemeWarnBg",    0xFF, 0xF8, 0xE1);
                Set("ThemeErrorBg",   0xFF, 0xEB, 0xEE);
                Set("ThemeSplitter",  0xBD, 0xBD, 0xBD);
            }

            // Repintar el esquema para que sus labels (creados en code-behind
            // con colores fijos Black/#666) reflejen la paleta del tema.
            _isDarkTheme = dark;
            if (_lastSchemaRows != null) BuildSchematic(_lastSchemaRows);
        }

        // Estado del tema actual — usado por BuildSchematic para elegir el
        // foreground de las etiquetas (texto del pipe en blanco/negro y
        // texto del fitting en gris claro/oscuro según corresponda).
        private bool _isDarkTheme = false;

        /// <summary>
        /// [Fase 1] Pobla el mapa Row ↔ ElementId del servicio de sync con
        /// las filas recién calculadas. Sólo pipes (los fittings no tienen
        /// elemento de Revit propio en este modelo). Se llama después de
        /// PopulateGrid / BuildSchematic para que el primer evento de
        /// selección que llegue ya encuentre el mapa relleno.
        /// </summary>
        private void RegisterRowsForSync(IList<ResultRow> rows)
        {
            if (_sync == null || rows == null) return;
            _sync.ClearRows();
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (r.IsFitting) continue;
                if (r.RevitId <= 0) continue;
                _sync.RegisterRow(r, new ElementId((long)r.RevitId));
            }
        }

        private void OnColumnVisibilityMenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu menu)) return;
            menu.Items.Clear();

            // [Fase 1] Si la sync con Revit está activa, ofrecer "Ver en Revit"
            // al principio del menú contextual.  La acción opera sobre la fila
            // seleccionada en ese momento.
            if (_sync != null)
            {
                var showItem = new MenuItem { Header = "Ver en Revit (zoom)" };
                showItem.Click += (s, args) =>
                {
                    if (DgResults.SelectedItem is ResultRow row)
                        _sync.SelectInRevit(row, zoom: true);
                };
                menu.Items.Add(showItem);
                var selectItem = new MenuItem { Header = "Seleccionar en Revit" };
                selectItem.Click += (s, args) =>
                {
                    if (DgResults.SelectedItem is ResultRow row)
                        _sync.SelectInRevit(row, zoom: false);
                };
                menu.Items.Add(selectItem);
                menu.Items.Add(new Separator());
            }

            foreach (var col in DgResults.Columns)
            {
                string header = col.Header?.ToString();
                if (string.IsNullOrEmpty(header)) header = "(sin nombre)";
                var item = new MenuItem
                {
                    Header = header,
                    IsCheckable = true,
                    IsChecked = col.Visibility == System.Windows.Visibility.Visible,
                    StaysOpenOnClick = true,
                };
                var capturedCol = col;
                item.Click += (s, args) =>
                {
                    capturedCol.Visibility = ((MenuItem)s).IsChecked
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
                };
                menu.Items.Add(item);
            }

            // Atajos al final del menú para activar/desactivar todas las
            // columnas de un golpe.
            menu.Items.Add(new Separator());
            var showAll = new MenuItem { Header = "Mostrar todas" };
            showAll.Click += (s, args) =>
            {
                foreach (var col in DgResults.Columns)
                    col.Visibility = System.Windows.Visibility.Visible;
            };
            menu.Items.Add(showAll);
            var hideAll = new MenuItem { Header = "Ocultar todas excepto #" };
            hideAll.Click += (s, args) =>
            {
                foreach (var col in DgResults.Columns)
                    col.Visibility = (col.Header?.ToString() == "#")
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
            };
            menu.Items.Add(hideAll);
        }

        // -------------------------------------------------------------------
        //  BFS — orden por sentido del flujo
        // -------------------------------------------------------------------

        /// <summary>
        /// Recorre la red de tuberias en BFS desde el SourceNode y asigna a
        /// cada segmento un BfsIndex (1..N), Depth, FromNodeId, ToNodeId
        /// (renumeracion 1..M de los nudos por orden de descubrimiento).
        /// Devuelve la lista de segmentos en ese orden, listos para el grid.
        /// </summary>
        private static List<PipeSegment> ComputePipeBfsOrder(Core.Hydraulics.PipeNetwork network)
        {
            var ordered = new List<PipeSegment>();
            if (network == null || network.SourceNode == null) return ordered;

            var nodeRank = new Dictionary<PipeNode, int>();
            int nextNodeRank = 1;
            nodeRank[network.SourceNode] = nextNodeRank++;
            // Asignar Label legible al nudo fuente si no lo tiene.
            if (string.IsNullOrEmpty(network.SourceNode.Label))
                network.SourceNode.Label = "Acometida";

            var visited = new HashSet<PipeNode> { network.SourceNode };
            var queue = new Queue<PipeNode>();
            queue.Enqueue(network.SourceNode);
            int seqIndex = 0;
            var depthOf = new Dictionary<PipeNode, int> { [network.SourceNode] = 0 };

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                int curDepth = depthOf[node];
                // Hijos directos: segmentos en los que `node` es NodeStart
                // (la red ya esta orientada desde la fuente).
                int branchCount = node.ConnectedSegments.Count(s => ReferenceEquals(s.NodeStart, node)
                                                                && s.NodeEnd != null
                                                                && !visited.Contains(s.NodeEnd));
                foreach (var seg in node.ConnectedSegments)
                {
                    if (!ReferenceEquals(seg.NodeStart, node)) continue; // solo aguas-abajo
                    var child = seg.NodeEnd;
                    if (child == null || visited.Contains(child)) continue;
                    if (!nodeRank.ContainsKey(child)) nodeRank[child] = nextNodeRank++;
                    seqIndex++;
                    seg.BfsIndex = seqIndex;
                    seg.FromNodeId = nodeRank[node];
                    seg.ToNodeId = nodeRank[child];
                    seg.Depth = curDepth + 1;
                    seg.IsSourceSegment = (curDepth == 0);
                    seg.IsTerminalSegment = child.IsTerminal;
                    seg.DownstreamBranchCount = branchCount;
                    depthOf[child] = curDepth + 1;
                    visited.Add(child);
                    queue.Enqueue(child);
                    ordered.Add(seg);
                }
            }
            // Persistir la renumeracion en el nodo (para tooltips/diag).
            foreach (var kv in nodeRank) kv.Key.Id = kv.Value;
            return ordered;
        }

        /// <summary>BFS analogo para conductos.</summary>
        private static List<DuctSegment> ComputeDuctBfsOrder(Core.Aeraulics.DuctNetwork network)
        {
            var ordered = new List<DuctSegment>();
            if (network == null || network.SourceNode == null) return ordered;

            var nodeRank = new Dictionary<DuctNode, int>();
            int nextNodeRank = 1;
            nodeRank[network.SourceNode] = nextNodeRank++;
            if (string.IsNullOrEmpty(network.SourceNode.Label))
                network.SourceNode.Label = "Ventilador";

            var visited = new HashSet<DuctNode> { network.SourceNode };
            var queue = new Queue<DuctNode>();
            queue.Enqueue(network.SourceNode);
            int seqIndex = 0;
            var depthOf = new Dictionary<DuctNode, int> { [network.SourceNode] = 0 };

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                int curDepth = depthOf[node];
                int branchCount = node.ConnectedSegments.Count(s => ReferenceEquals(s.NodeStart, node)
                                                                && s.NodeEnd != null
                                                                && !visited.Contains(s.NodeEnd));
                foreach (var seg in node.ConnectedSegments)
                {
                    if (!ReferenceEquals(seg.NodeStart, node)) continue;
                    var child = seg.NodeEnd;
                    if (child == null || visited.Contains(child)) continue;
                    if (!nodeRank.ContainsKey(child)) nodeRank[child] = nextNodeRank++;
                    seqIndex++;
                    seg.BfsIndex = seqIndex;
                    seg.FromNodeId = nodeRank[node];
                    seg.ToNodeId = nodeRank[child];
                    seg.Depth = curDepth + 1;
                    seg.IsSourceSegment = (curDepth == 0);
                    seg.IsTerminalSegment = child.IsTerminal;
                    seg.DownstreamBranchCount = branchCount;
                    depthOf[child] = curDepth + 1;
                    visited.Add(child);
                    queue.Enqueue(child);
                    ordered.Add(seg);
                }
            }
            foreach (var kv in nodeRank) kv.Key.Id = kv.Value;
            return ordered;
        }

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

        // -------------------------------------------------------------------
        //  Clasificacion topologica del nudo final (sin lectura de la familia
        //  Revit: solo grado del nudo + diametro/talla de las tuberias/ductos
        //  conectados).  Suficiente para distinguir T (tee), cross, codo,
        //  reductor, terminal y final-abierto.
        // -------------------------------------------------------------------

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
                    // Cambio de talla: comparamos el "tamaño caracteristico"
                    // (D para circulares, max(W,H) para rectangulares).
                    double a = SegSize(seg);
                    double b = SegSize(other);
                    if (a > 0 && b > 0 && Math.Abs(a - b) > 1e-6) return "REDUCER";
                }
                return "ELBOW";
            }
            if (degree == 3) return "TEE";
            if (degree == 4) return "CROSS";
            return "JUNCTION";
        }

        private static double SegSize(DuctSegment s)
        {
            if (s == null) return 0.0;
            if (s.Shape == DuctShape.Circular) return s.DiameterM;
            return Math.Max(s.WidthM, s.HeightM);
        }

        // -------------------------------------------------------------------
        //  Filas de accesorio (insertadas detras de cada tramo en BFS).
        //  No tienen calculo hidraulico — solo localizan visualmente el
        //  accesorio o el aparato terminal aguas-abajo del tramo.
        // -------------------------------------------------------------------

        /// <summary>Crea la fila accesorio que sigue a un tramo de tuberia.</summary>
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
                NodePath    = "@N" + seg.ToNodeId,   // localizacion del accesorio
                BfsIndex    = 0,                      // sin numero hidraulico
                IsTerminal  = (kind == "TERMINAL"),
                BranchCount = pipeRow.BranchCount,
                Status      = "",
                StatusKind  = "",
            };
        }

        /// <summary>Idem para conductos.</summary>
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

        private static double ParseDouble(string text, double defaultVal)
        {
            if (string.IsNullOrWhiteSpace(text)) return defaultVal;
            if (double.TryParse(text.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return defaultVal;
        }

        // -------------------------------------------------------------------
        //  DTOs internos
        // -------------------------------------------------------------------

        public class ResultRow
        {
            // ElementKind:  "PIPE" | "TEE" | "ELBOW" | "REDUCER" | "TERMINAL" |
            //              "CROSS" | "FIN" | "JUNCTION".  Para "PIPE" la fila
            //              lleva todos los datos hidraulicos; para los accesorios
            //              solo la geometria/topologia.
            public string ElementKind  { get; set; } = "PIPE";
            public bool   IsFitting => ElementKind != "PIPE";

            public long   RevitId      { get; set; }
            public int    BfsIndex     { get; set; }   // # de tramo en orden de flujo
            public string FromNode     { get; set; }   // Etiqueta del nudo aguas-arriba
            public string ToNode       { get; set; }   // Etiqueta del nudo aguas-abajo
            public string NodePath     { get; set; }   // "N3 → N4"
            public int    Depth        { get; set; }   // profundidad BFS
            public string LevelName    { get; set; }   // Reference Level del Revit
            public bool   IsSource     { get; set; }   // tramo de acometida
            public bool   IsTerminal   { get; set; }   // tramo que termina en aparato
            public int    BranchCount  { get; set; }   // hijos directos del nudo final
            public string EndNodeKind  { get; set; }   // TERMINAL/TEE/CROSS/ELBOW/REDUCER/FIN/JUNCTION
            public string Size         { get; set; }
            public int    FixtureCount { get; set; }   // N = aparatos aguas-abajo
            public double KFactor      { get; set; } = 1.0; // K simultaneidad
            public double RawFlowLps   { get; set; }   // Q sin simultaneidad
            public double FlowLps      { get; set; }   // Q de diseno (con K)
            public double Velocity     { get; set; }
            public double FrictionPaPm { get; set; }
            public double TotalLoss    { get; set; }
            public double Reynolds     { get; set; }
            public string RecommendedSize { get; set; }
            public string Status       { get; set; }
            public string StatusKind   { get; set; }   // OK / Warning / Error

            // ---- Display properties (vacias en filas de accesorio) ----
            public string ElementLabel => ElementKind == "PIPE" ? "Tubería" : (ElementKind ?? "");
            public string BfsIndexDisplay => (BfsIndex > 0 && !IsFitting) ? BfsIndex.ToString() : "";
            public string RevitIdDisplay  => RevitId > 0 ? RevitId.ToString() : "";
            public string SizeDisplay     => IsFitting ? "" : (Size ?? "");
            public string RecommendedSizeDisplay => IsFitting ? "" : (RecommendedSize ?? "");
            public string FixtureCountDisplay => (!IsFitting && FixtureCount > 0) ? FixtureCount.ToString() : "";
            public string KFactorDisplay  => IsFitting ? "" : KFactor.ToString("F2");
            public string RawFlowLpsDisplay => IsFitting ? "" : RawFlowLps.ToString("F2");
            public string FlowLpsDisplay  => IsFitting ? "" : FlowLps.ToString("F2");
            public string VelocityDisplay => IsFitting ? "" : Velocity.ToString("F2");
            public string FrictionPaPmDisplay => IsFitting ? "" : FrictionPaPm.ToString("F0");
            public string TotalLossDisplay => IsFitting ? "" : TotalLoss.ToString("F0");

            public void SetStatusFrom(CalcResult r)
            {
                if (r == null)
                {
                    Status = "Sin calcular";
                    StatusKind = "Warning";
                    return;
                }
                if (r.Warnings.Any(w => w.Severity == CalcSeverity.Error))
                {
                    Status = string.Join(" · ",
                        r.Warnings.Where(w => w.Severity == CalcSeverity.Error)
                                  .Select(w => w.Message));
                    StatusKind = "Error";
                    return;
                }
                if (r.VelocityExceeded || r.FrictionExceeded
                    || r.Warnings.Any(w => w.Severity == CalcSeverity.Warning))
                {
                    var msgs = new List<string>();
                    if (r.VelocityExceeded) msgs.Add("v excede");
                    if (r.FrictionExceeded) msgs.Add("dP/m excede");
                    msgs.AddRange(r.Warnings
                        .Where(w => w.Severity == CalcSeverity.Warning)
                        .Select(w => w.Code));
                    Status = string.Join(" - ", msgs);
                    StatusKind = "Warning";
                    return;
                }
                Status = "OK";
                StatusKind = "OK";
            }
        }

        public class PresetItem
        {
            public string Key   { get; }
            public string Label { get; }
            public PresetItem(string key, string label) { Key = key; Label = label; }
            public override string ToString() => Label;
        }

        public class BuildingTypeItem
        {
            public SimultaneityProfile Profile { get; }
            public string Label { get; }
            public BuildingTypeItem(SimultaneityProfile profile, string label)
            { Profile = profile; Label = label; }
            public override string ToString() => Label;
        }

        // ===================================================================
        //  FASE 2 — Camino crítico + Heatmap
        // ===================================================================

        // -------------------------------------------------------------------
        //  Algoritmo del camino crítico — diagrama de flujo (comentario)
        //
        //  ┌─────────────────────────────────────────────────────────────────┐
        //  │  ENTRADA: IList<ResultRow> rows  (orden DFS preorder)           │
        //  ├─────────────────────────────────────────────────────────────────┤
        //  │  1. Indexar pipes por nodo de salida → rowByOutgoing            │
        //  │  2. Para cada pipe (en orden):                                  │
        //  │       cumDp[pipe] = cumDp[parent] + max(pipe.TotalLoss, 0)      │
        //  │  3. Terminales = pipes sin hijos en el grafo                    │
        //  │  4. worst = argmax(cumDp) entre terminales                      │
        //  │  5. Trace-back: worst → fuente, reconstruir lista de tramos     │
        //  └─────────────────────────────────────────────────────────────────┘
        //
        //  Complejidad: O(N) tiempo y espacio.
        //  Robustez: acepta grafos desconectados y TotalLoss negativos.
        // -------------------------------------------------------------------

        // -------------------------------------------------------------------
        //  Handlers de UI
        // -------------------------------------------------------------------

        /// <summary>
        /// [Fase 2] Toggle del botón "Camino crítico".
        /// Al activarse: analiza la red actual y superpone el overlay naranja.
        /// Al desactivarse: limpia el overlay y devuelve al color del sistema.
        /// </summary>
        private void OnCriticalPathToggled(object sender, RoutedEventArgs e)
        {
            bool active = BtnCriticalPath?.IsChecked == true;
            if (!active)
            {
                _activeCriticalPath = null;
                ClearAnalyticsOverlay();
                // Si el heatmap sigue activo, reaplicarlo.
                if (_activeHeatmap != null && !_activeHeatmap.IsEmpty)
                    ApplyHeatmap(_activeHeatmap);
                return;
            }

            if (_lastSchemaRows == null || _lastSchemaRows.Count == 0)
            {
                LblStatus.Text = "Sin datos. Ejecuta Calcular primero.";
                BtnCriticalPath.IsChecked = false;
                return;
            }

            _activeCriticalPath = _criticalPathSvc.Analyze(_lastSchemaRows);
            if (_activeCriticalPath.IsEmpty)
            {
                LblStatus.Text = "No se pudo calcular el camino crítico — red sin terminales.";
                BtnCriticalPath.IsChecked = false;
                return;
            }

            // Si hay heatmap activo, aplicar primero el heatmap y después
            // superponer el overlay del camino crítico encima.
            if (_activeHeatmap != null && !_activeHeatmap.IsEmpty)
                ApplyHeatmap(_activeHeatmap);

            ApplyCriticalPathOverlay(_activeCriticalPath);
            LblStatus.Text = $"Camino crítico: {_activeCriticalPath.Path.Count} tramos · "
                           + $"ΔP total = {_activeCriticalPath.TotalDeltaP:F0} Pa";
        }

        /// <summary>
        /// [Fase 2] Cambio en el ComboBox de heatmap.
        /// </summary>
        private void OnHeatmapChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = CboHeatmap?.SelectedIndex ?? 0;
            HeatmapMetric metric = idx switch
            {
                1 => HeatmapMetric.FlowLps,
                2 => HeatmapMetric.Velocity,
                3 => HeatmapMetric.FrictionPaPm,
                4 => HeatmapMetric.HoldingMargin,
                _ => HeatmapMetric.None,
            };

            if (metric == HeatmapMetric.None)
            {
                _activeHeatmap = null;
                ClearAnalyticsOverlay();
                if (LblHeatmapRange != null) LblHeatmapRange.Text = "";
                // Reaplicar camino crítico si está activo.
                if (BtnCriticalPath?.IsChecked == true && _activeCriticalPath != null)
                    ApplyCriticalPathOverlay(_activeCriticalPath);
                return;
            }

            if (_lastSchemaRows == null || _lastSchemaRows.Count == 0) return;

            _activeHeatmap = _heatmapSvc.Compute(_lastSchemaRows, metric);
            if (_activeHeatmap.IsEmpty) return;

            ApplyHeatmap(_activeHeatmap);

            // Actualizar etiqueta de rango.
            string unit = metric switch
            {
                HeatmapMetric.FlowLps      => "L/s",
                HeatmapMetric.Velocity     => "m/s",
                HeatmapMetric.FrictionPaPm => "Pa/m",
                HeatmapMetric.HoldingMargin=> "%",
                _                          => "",
            };
            double displayMin = metric == HeatmapMetric.HoldingMargin
                ? _activeHeatmap.Min * 100 : _activeHeatmap.Min;
            double displayMax = metric == HeatmapMetric.HoldingMargin
                ? _activeHeatmap.Max * 100 : _activeHeatmap.Max;
            if (LblHeatmapRange != null)
                LblHeatmapRange.Text =
                    $"■ {displayMin:F1}  →  ■ {displayMax:F1} {unit}";

            // Superponer camino crítico encima si está activo.
            if (BtnCriticalPath?.IsChecked == true && _activeCriticalPath != null)
                ApplyCriticalPathOverlay(_activeCriticalPath);
        }

        // -------------------------------------------------------------------
        //  Rendering — overlay de análisis
        // -------------------------------------------------------------------

        /// <summary>
        /// Colorea cada línea de pipe según el brush del heatmap.
        /// Las líneas de fitting quedan sin cambio (no tienen datos hidráulicos).
        /// </summary>
        private void ApplyHeatmap(HeatmapResult heatmap)
        {
            if (heatmap == null || heatmap.IsEmpty) return;
            foreach (var kv in _schemaLines)
            {
                ResultRow row = kv.Key;
                WpfLine line   = kv.Value;
                if (heatmap.Brushes.TryGetValue(row, out Brush brush))
                {
                    line.Stroke          = brush;
                    line.StrokeThickness = _lineOriginalStrokes.TryGetValue(line, out var orig)
                                          ? orig.thickness + 0.5 : 3.0;
                    line.StrokeDashArray = null;   // quitar dashes del ramal
                }
            }
        }

        /// <summary>
        /// Superpone el overlay del camino crítico sobre el esquema ya pintado
        /// (puede estar en color de sistema o con heatmap).
        /// Reglas visuales:
        ///   • Tramos del camino crítico → naranja vivo (#FF6D00), grosor ×1.8,
        ///     sin dash, añade glow effect.
        ///   • Tramos fuera del camino  → se atenúan al 30 % de opacidad.
        /// </summary>
        private void ApplyCriticalPathOverlay(CriticalPathResult cpr)
        {
            if (cpr == null || cpr.IsEmpty) return;

            var criticalSet = new HashSet<ResultRow>(cpr.Path);
            var criticalBrush = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x6D, 0x00));
            criticalBrush.Freeze();
            var dimBrush = new SolidColorBrush(WpfColor.FromArgb(0x44, 0x99, 0x99, 0x99));
            dimBrush.Freeze();

            foreach (var kv in _schemaLines)
            {
                ResultRow row = kv.Key;
                WpfLine line   = kv.Value;
                bool isCritical = criticalSet.Contains(row);

                if (isCritical)
                {
                    line.Stroke          = criticalBrush;
                    line.StrokeThickness = (_lineOriginalStrokes.TryGetValue(line, out var orig)
                                           ? orig.thickness : 3.0) * 1.8;
                    line.StrokeDashArray = null;
                    line.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color        = WpfColor.FromRgb(0xFF, 0x6D, 0x00),
                        BlurRadius   = 10,
                        ShadowDepth  = 0,
                        Opacity      = 0.65,
                    };
                }
                else
                {
                    // Atenuar sin destruir la información de heatmap si estaba activo.
                    line.Opacity = 0.28;
                }
            }
        }

        /// <summary>
        /// Restaura todas las líneas del esquema a su color y grosor originales.
        /// Elimina también cualquier efecto de glow y opacidad reducida.
        /// </summary>
        private void ClearAnalyticsOverlay()
        {
            foreach (var kv in _schemaLines)
            {
                WpfLine line = kv.Value;
                if (_lineOriginalStrokes.TryGetValue(line, out var orig))
                {
                    line.Stroke          = orig.stroke;
                    line.StrokeThickness = orig.thickness;
                }
                line.Effect  = null;
                line.Opacity = 1.0;
                // Restaurar dash si era ramal (X1 != X2 → horizontal).
                if (Math.Abs(line.X1 - line.X2) > 0.1)
                    line.StrokeDashArray = new DoubleCollection { 3, 3 };
                else
                    line.StrokeDashArray = null;
            }
            if (LblHeatmapRange != null) LblHeatmapRange.Text = "";
        }

        // -------------------------------------------------------------------
        //  Tooltips justificativos enriquecidos (Fase 2)
        // -------------------------------------------------------------------

        /// <summary>
        /// Construye un tooltip completo para un pipe con toda la información
        /// hidráulica relevante: datos del tramo + posición en el camino crítico
        /// + ΔP acumulado.
        /// </summary>
        private string BuildPipeTooltip(ResultRow row, CriticalPathResult cpr)
        {
            var sb = new System.Text.StringBuilder();

            // Cabecera: número de tramo y ruta
            sb.Append("Tramo #").Append(row.BfsIndex)
              .Append("  ").Append(row.NodePath ?? "");

            // Indicador de camino crítico
            if (cpr != null && !cpr.IsEmpty)
            {
                bool isCrit = false;
                foreach (var p in cpr.Path)
                    if (ReferenceEquals(p, row)) { isCrit = true; break; }
                sb.Append(isCrit ? "\n⚠ CAMINO CRÍTICO" : "");
            }

            // Geometría
            if (!string.IsNullOrEmpty(row.Size))
                sb.Append("\n").Append(row.Size);
            if (!string.IsNullOrEmpty(row.RecommendedSize)
                && row.RecommendedSize != "—" && row.RecommendedSize != row.Size)
                sb.Append("  (rec ").Append(row.RecommendedSize).Append(")");

            // Hidráulica
            if (row.FlowLps > 0)
                sb.Append("\nQ = ").Append(row.FlowLps.ToString("F2")).Append(" L/s");
            if (row.Velocity > 0)
                sb.Append("  v = ").Append(row.Velocity.ToString("F2")).Append(" m/s");
            if (row.FrictionPaPm > 0)
                sb.Append("\nΔP/m = ").Append(row.FrictionPaPm.ToString("F0")).Append(" Pa/m");
            if (row.TotalLoss > 0)
                sb.Append("  ΔP tramo = ").Append(row.TotalLoss.ToString("F0")).Append(" Pa");

            // ΔP acumulado (desde la fuente)
            if (cpr != null && !cpr.IsEmpty
                && cpr.AccumulatedAt.TryGetValue(row, out double accDp))
                sb.Append("\nΔP acumulado = ").Append(accDp.ToString("F0")).Append(" Pa");

            // N aparatos + K
            if (row.FixtureCount > 0)
                sb.Append("\nN = ").Append(row.FixtureCount).Append(" aparatos")
                  .Append("  K = ").Append(row.KFactor.ToString("F2"));

            // Nivel y estado
            if (!string.IsNullOrEmpty(row.LevelName))
                sb.Append("\nNivel: ").Append(row.LevelName);
            if (!string.IsNullOrEmpty(row.Status))
                sb.Append("\n").Append(row.Status);

            return sb.ToString();
        }

        /// <summary>
        /// Construye un tooltip justificativo para un fitting (accesorio).
        /// Incluye el tipo, descripción funcional y datos de contexto del nudo.
        /// </summary>
        private static string BuildFittingTooltip(ResultRow row)
        {
            var sb = new System.Text.StringBuilder();
            string kind = row.EndNodeKind ?? row.ElementKind ?? "?";
            string node = row.NodePath ?? "";

            // Tipo y descripción funcional
            switch (kind)
            {
                case "TEE":
                    sb.Append("TEE — Derivación en T");
                    sb.Append("\nEl flujo principal continúa por el tronco;");
                    sb.Append("\nun ramal sale lateralmente.");
                    break;
                case "CROSS":
                    sb.Append("CROSS — Cruce de cuatro vías");
                    sb.Append("\nEl flujo principal y dos ramales se bifurcan.");
                    break;
                case "ELBOW":
                    sb.Append("CODO — Cambio de dirección");
                    sb.Append("\nPérdida singular ≈ K × v² / 2g");
                    sb.Append("\n  K estimado: 0.5 – 1.5 (radio del codo)");
                    break;
                case "REDUCER":
                    sb.Append("REDUCTOR — Cambio de diámetro");
                    sb.Append("\nPérdida singular ≈ K × Δv² / 2g");
                    sb.Append("\n  K estimado: 0.1 – 0.5 (reducción cónica)");
                    break;
                case "TERMINAL":
                    sb.Append("TERMINAL — Aparato de consumo");
                    sb.Append("\nPunto final de la red.");
                    sb.Append("\nPresión mínima garantizada aquí.");
                    break;
                case "FIN":
                    sb.Append("FIN — Extremo abierto");
                    sb.Append("\nTramo sin continuación en el modelo.");
                    break;
                default:
                    sb.Append(kind).Append(" ").Append(node);
                    break;
            }

            if (!string.IsNullOrEmpty(node))
                sb.Append("\nNudo: ").Append(node);
            if (row.BranchCount > 0)
                sb.Append("\nRamales aguas-abajo: ").Append(row.BranchCount);
            if (!string.IsNullOrEmpty(row.LevelName))
                sb.Append("\nNivel: ").Append(row.LevelName);

            return sb.ToString();
        }

        // -------------------------------------------------------------------
        //  Helper: actualizar tooltips del esquema con datos analíticos.
        //  Llamado tras ApplyCriticalPathOverlay / ApplyHeatmap para que
        //  los ToolTip reflejen el ΔP acumulado y la posición en el camino.
        // -------------------------------------------------------------------

        /// <summary>
        /// Actualiza los ToolTip de todos los glyphs registrados usando la
        /// información del camino crítico activo (si lo hay).  Así al hacer
        /// hover sobre un tramo se ve el ΔP acumulado y si es crítico.
        /// </summary>
        private void RefreshSchematicTooltips()
        {
            foreach (var kv in _schemaGlyphs)
            {
                ResultRow row = kv.Key;
                FrameworkElement el = kv.Value;
                if (el == null) continue;

                string tip = row.IsFitting
                    ? BuildFittingTooltip(row)
                    : BuildPipeTooltip(row, _activeCriticalPath);

                el.ToolTip = tip;
            }
        }
    }
}
