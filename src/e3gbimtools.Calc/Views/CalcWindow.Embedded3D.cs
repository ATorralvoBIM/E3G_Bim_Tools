// ============================================================================
//  CalcWindow.Embedded3D.cs
//  Visor 3D embebido (WPF Media3D nativo, sin dependencias externas).
//
//  El botón "Ver 3D" de las pestañas Hidráulica / Aeráulica alterna el panel
//  derecho entre el esquema en árbol y un visor 3D que muestra la GEOMETRÍA
//  REAL del sistema calculado (tubos/conductos + codos, tes y accesorios),
//  extraída de Revit y teselada a una malla WPF.
//
//  La extracción se hace en contexto válido de la API (CalcExternalEventBus);
//  como los eventos externos de Revit se ejecutan en el hilo principal —el
//  mismo del dispatcher WPF— la malla se construye y se monta en el viewport
//  sin marshalling adicional.
//
//  Navegación (igual que Revit):
//      · Rueda central (arrastrar) ............ paneo
//      · Mayús + rueda central (arrastrar) .... orbitar
//      · Rueda (girar) ........................ zoom hacia el cursor
//      · Clic izquierdo ....................... seleccionar tubo → resalta fila
// ============================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using e3gbimtools.Calc.Core.Common;
using WpfVisibility   = System.Windows.Visibility;
using MColor          = System.Windows.Media.Color;
using MBrush          = System.Windows.Media.Brush;
using MSolidBrush     = System.Windows.Media.SolidColorBrush;
using Int32Collection = System.Windows.Media.Int32Collection;
using Viewport3D      = System.Windows.Controls.Viewport3D;
using MMaterial       = System.Windows.Media.Media3D.Material;

namespace e3gbimtools.Calc.Views
{
    public partial class CalcWindow
    {
        // ------------------------------------------------------------------ //
        //  Estado del visor 3D embebido                                        //
        // ------------------------------------------------------------------ //
        private Orbit3DView _hyd3D;
        private Orbit3DView _aer3D;
        private bool _hydShowing3D;
        private bool _aerShowing3D;

        // Caché de geometría por sistema.  Se reconstruye solo cuando cambia la
        // referencia de filas (es decir, tras un nuevo cálculo): así alternar
        // árbol/3D es instantáneo y no se vuelve a teselar la geometría.
        // Guardamos UNA malla POR ELEMENTO (no una fusionada) para poder hacer
        // picking y mapear el clic de vuelta a su ElementId / fila de cálculo.
        private List<ElementMesh3D> _hyd3DParts, _aer3DParts;
        private Point3D _hyd3DCenter, _aer3DCenter;
        private double _hyd3DRadius = 1.0, _aer3DRadius = 1.0;
        private IList<ResultRow> _hyd3DBuiltFor, _aer3DBuiltFor;
        private int _hyd3DFittings, _aer3DFittings;

        // Texto base de ayuda de controles (se le añade el diagnóstico de conteo).
        private const string View3DControlsHelp =
            "Rueda central = paneo · Mayús+central = orbitar · Rueda = zoom · Clic = seleccionar tubo";

        private void Set3DHint(bool hyd, int elements, int fittings, int triangles)
        {
            var tb = hyd ? HydView3DHint : AerView3DHint;
            if (tb == null) return;
            tb.Text = elements + " elem · " + fittings + " accesorios · "
                    + triangles + " tri    |    " + View3DControlsHelp;
        }

        // ------------------------------------------------------------------ //
        //  Toggle de botones                                                   //
        // ------------------------------------------------------------------ //
        private void OnHydView3DClick(object sender, RoutedEventArgs e) => Toggle3D(true);
        private void OnAerView3DClick(object sender, RoutedEventArgs e) => Toggle3D(false);

        private void Toggle3D(bool hyd)
        {
            bool showing = hyd ? _hydShowing3D : _aerShowing3D;
            if (showing)
            {
                // Volver al esquema en árbol.
                Set3DVisible(hyd, false);
                return;
            }

            var rows = hyd ? _lastHydRows : _lastAerRows;
            if (rows == null || rows.Count == 0)
            {
                ShowError(hyd
                    ? "Ejecuta primero el cálculo hidráulico."
                    : "Ejecuta primero el cálculo aeráulico.");
                return;
            }

            var brush = hyd ? _hydSystemBrush : _aerSystemBrush;

            // Ids de los tramos desconectados de la fuente (se pintan en magenta
            // en el visor para localizarlos en el espacio de un vistazo).
            var discIds = new HashSet<long>();
            foreach (var r in rows)
                if (r != null && r.Disconnected && r.RevitId > 0) discIds.Add(r.RevitId);

            // ── Caché: si ya está construida para estas mismas filas, reutilizar.
            List<ElementMesh3D> cached = hyd ? _hyd3DParts : _aer3DParts;
            IList<ResultRow> builtFor  = hyd ? _hyd3DBuiltFor : _aer3DBuiltFor;
            if (cached != null && ReferenceEquals(builtFor, rows))
            {
                Orbit3DView cv = EnsureOrbitView(hyd);
                cv.SetParts(cached, SystemColor(brush),
                            hyd ? _hyd3DCenter : _aer3DCenter,
                            hyd ? _hyd3DRadius : _aer3DRadius, discIds);
                Set3DVisible(hyd, true);
                int cFit = hyd ? _hyd3DFittings : _aer3DFittings;
                Set3DHint(hyd, cached.Count, cFit, CountTriangles(cached));
                SetStatus("Visor 3D (" + cached.Count + " elementos, " + cFit + " accesorios, "
                          + CountTriangles(cached) + " triángulos).");
                return;
            }

            if (_externalBus == null)
            {
                ShowError("La sincronización con Revit no está disponible.");
                return;
            }

            // ElementId únicos del sistema activo (tubos/conductos calculados).
            var seen = new HashSet<long>();
            var ids  = new List<ElementId>();
            foreach (var r in rows)
            {
                if (r == null || r.RevitId <= 0) continue;
                if (seen.Add(r.RevitId))
                    ids.Add(new ElementId((long)r.RevitId));
            }
            if (ids.Count == 0)
            {
                ShowError("El sistema no tiene elementos de Revit asociados.");
                return;
            }

            SetStatus("Generando visor 3D...");

            // Extraer geometría en contexto válido de la API de Revit.
            ScheduleRevitAction(uiApp =>
            {
                Document doc = uiApp.ActiveUIDocument != null
                    ? uiApp.ActiveUIDocument.Document : null;
                if (doc == null) return;

                // Expandir con codos, tes, reducciones y accesorios: las filas del
                // cálculo solo contienen los tramos (MEPCurve).  Recopilamos los
                // accesorios tanto por el sistema MEP como por los conectores.
                int fittingCount;
                List<ElementId> fullIds = ExpandWithFittings(doc, ids, out fittingCount);

                Point3D center;
                double radius;
                List<long> emptyFittingIds;
                List<ElementMesh3D> parts = ExtractSystemMeshes(
                    doc, fullIds, out center, out radius, out emptyFittingIds);

                Dispatcher.Invoke(new Action(delegate
                {
                    if (parts == null || parts.Count == 0)
                    {
                        ShowError("No se pudo extraer geometría 3D del sistema.");
                        SetStatus("Sin geometría 3D.");
                        return;
                    }

                    // Guardar en caché para alternancias posteriores.
                    if (hyd)
                    {
                        _hyd3DParts = parts; _hyd3DCenter = center;
                        _hyd3DRadius = radius; _hyd3DBuiltFor = rows;
                        _hyd3DFittings = fittingCount;
                    }
                    else
                    {
                        _aer3DParts = parts; _aer3DCenter = center;
                        _aer3DRadius = radius; _aer3DBuiltFor = rows;
                        _aer3DFittings = fittingCount;
                    }

                    Orbit3DView view = EnsureOrbitView(hyd);
                    view.SetParts(parts, SystemColor(brush), center, radius, discIds);
                    Set3DVisible(hyd, true);
                    Set3DHint(hyd, parts.Count, fittingCount, CountTriangles(parts));
                    SetStatus("Visor 3D — " + parts.Count + " elementos ("
                              + fittingCount + " accesorios), " + CountTriangles(parts) + " triángulos."
                              + (emptyFittingIds.Count > 0
                                 ? "  ⚠ " + emptyFittingIds.Count + " accesorio(s) sin geometría 3D."
                                 : ""));

                    // Diagnóstico: accesorios recopilados que NO produjeron malla
                    // (codos/tes que aparecerán como huecos en el visor). Se
                    // listan sus Id para poder inspeccionar la familia en Revit.
                    if (emptyFittingIds.Count > 0 && hyd && HydSummaryText != null)
                    {
                        int show = Math.Min(emptyFittingIds.Count, 30);
                        var sample = string.Join(", ",
                            emptyFittingIds.GetRange(0, show).ConvertAll(x => x.ToString()));
                        if (emptyFittingIds.Count > show) sample += ", …";
                        HydSummaryText.Text +=
                            "\n[Diag] Visor 3D: " + emptyFittingIds.Count +
                            " accesorio(s) sin geometría sólida extraíble (Id: " + sample + ").";
                    }
                }));
            });
        }

        private static int CountTriangles(List<ElementMesh3D> parts)
        {
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Mesh != null) n += parts[i].Mesh.TriangleIndices.Count / 3;
            return n;
        }

        // ------------------------------------------------------------------ //
        //  Cuando se hace clic sobre un tubo en el visor 3D                    //
        //  → seleccionar y desplazar a la fila de cálculo correspondiente.     //
        // ------------------------------------------------------------------ //
        private void On3DElementPicked(bool hyd, long elementId)
        {
            if (elementId <= 0) return;
            // Reutiliza la lógica existente de selección/scroll de grids + esquema.
            ApplyRevitSelectionToGrids(new List<ElementId> { new ElementId(elementId) });
            // Panel de datos dinámico sobre el visor.
            ResultRow row;
            if (_revitIdToRow.TryGetValue(elementId, out row) && row != null && !row.IsFitting)
                Show3DInfo(hyd, row);
            else
                Show3DAccessoryInfo(hyd);
        }

        private void Show3DInfo(bool hyd, ResultRow row)
        {
            var title = hyd ? Hyd3DInfoTitle : Aer3DInfoTitle;
            var body  = hyd ? Hyd3DInfoBody  : Aer3DInfoBody;
            var panel = hyd ? Hyd3DInfo      : Aer3DInfo;

            string size = row.SizeDisplay;
            title.Text = (hyd ? "Tubería" : "Conducto")
                       + (string.IsNullOrEmpty(size) ? "" : "  " + size)
                       + (row.BfsIndex > 0 ? "   ·   tramo " + row.BfsIndex : "");

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(row.NodePath)) sb.Append(row.NodePath).Append('\n');
            sb.Append("Caudal simult.:  ").Append(row.FlowLpsDisplay).Append(" L/s\n");
            sb.Append("Caudal bruto:    ").Append(row.RawFlowLpsDisplay).Append(" L/s\n");
            sb.Append("Velocidad:       ").Append(row.VelocityDisplay).Append(" m/s\n");
            sb.Append("Pérdida:         ").Append(row.FrictionPaPmDisplay).Append(" Pa/m");
            if (row.FixtureCount > 0)
                sb.Append("\nAparatos:        ").Append(row.FixtureCount);
            if (!string.IsNullOrEmpty(row.Status))
                sb.Append("\nEstado:          ").Append(row.Status);
            body.Text = sb.ToString();

            panel.Visibility = WpfVisibility.Visible;
        }

        private void Show3DAccessoryInfo(bool hyd)
        {
            var title = hyd ? Hyd3DInfoTitle : Aer3DInfoTitle;
            var body  = hyd ? Hyd3DInfoBody  : Aer3DInfoBody;
            var panel = hyd ? Hyd3DInfo      : Aer3DInfo;

            title.Text = "Accesorio";
            body.Text  = "Codo / te / reducción\n(sin datos hidráulicos)";
            panel.Visibility = WpfVisibility.Visible;
        }

        // ------------------------------------------------------------------ //
        //  Expandir la selección con los accesorios conectados                 //
        //  (codos, tes, reducciones, válvulas).  Dos vías combinadas:          //
        //    1) Elementos del MEPSystem del tramo (robusto, captura todo).     //
        //    2) Recorrido de conectores (respaldo).                            //
        // ------------------------------------------------------------------ //
        private static List<ElementId> ExpandWithFittings(
            Document doc, IList<ElementId> baseIds, out int fittingCount)
        {
            var seen   = new HashSet<long>();
            var result = new List<ElementId>();
            foreach (ElementId id in baseIds)
                if (seen.Add(id.Value)) result.Add(id);

            int baseCount = result.Count;

            // 1) Vía MEPSystem: recoger todos los accesorios del/los sistema(s).
            var systemsSeen = new HashSet<long>();
            foreach (ElementId id in baseIds)
            {
                try
                {
                    Element el = doc.GetElement(id);
                    MEPCurve mc = el as MEPCurve;
                    MEPSystem sys = mc != null ? mc.MEPSystem : null;
                    if (sys == null || !systemsSeen.Add(sys.Id.Value)) continue;

                    ElementSet els = sys.Elements;
                    if (els == null) continue;
                    foreach (Element se in els)
                    {
                        if (se == null) continue;
                        if (!IsFittingOrAccessory(se)) continue;
                        if (seen.Add(se.Id.Value)) result.Add(se.Id);
                    }
                }
                catch { /* sistema inválido: ignorar */ }
            }

            // 2) Vía conectores: respaldo para accesorios que no estén en Elements.
            foreach (ElementId id in baseIds)
            {
                Element el = doc.GetElement(id);
                ConnectorManager cm = GetConnectorManager(el);
                if (cm == null) continue;

                try
                {
                    foreach (Connector c in cm.Connectors)
                    {
                        foreach (Connector other in c.AllRefs)
                        {
                            Element owner = other.Owner;
                            if (owner == null) continue;
                            if (!IsFittingOrAccessory(owner)) continue;
                            if (seen.Add(owner.Id.Value)) result.Add(owner.Id);
                        }
                    }
                }
                catch { /* conector inválido: ignorar */ }
            }

            fittingCount = result.Count - baseCount;
            return result;
        }

        private static ConnectorManager GetConnectorManager(Element el)
        {
            MEPCurve mc = el as MEPCurve;
            if (mc != null) return mc.ConnectorManager;
            FamilyInstance fi = el as FamilyInstance;
            if (fi != null && fi.MEPModel != null) return fi.MEPModel.ConnectorManager;
            return null;
        }

        private static bool IsFittingOrAccessory(Element el)
        {
            if (el == null || el.Category == null) return false;
            long c = el.Category.Id.Value;
            return c == (long)BuiltInCategory.OST_PipeFitting
                || c == (long)BuiltInCategory.OST_PipeAccessory
                || c == (long)BuiltInCategory.OST_DuctFitting
                || c == (long)BuiltInCategory.OST_DuctAccessory
                || c == (long)BuiltInCategory.OST_MechanicalEquipment;
        }

        private Orbit3DView EnsureOrbitView(bool hyd)
        {
            if (hyd)
            {
                if (_hyd3D == null)
                {
                    _hyd3D = new Orbit3DView(HydViewport3D, HydView3DHost);
                    _hyd3D.ElementPicked = id => On3DElementPicked(true, id);
                }
                return _hyd3D;
            }
            if (_aer3D == null)
            {
                _aer3D = new Orbit3DView(AerViewport3D, AerView3DHost);
                _aer3D.ElementPicked = id => On3DElementPicked(false, id);
            }
            return _aer3D;
        }

        private void Set3DVisible(bool hyd, bool show)
        {
            if (hyd)
            {
                _hydShowing3D = show;
                Hyd3DBackdrop.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                HydViewport3D.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                HydView3DHint.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                HydSchematicScroll.Visibility = show ? WpfVisibility.Collapsed : WpfVisibility.Visible;
                HydView3DButton.Content       = show ? "Ver árbol" : "Ver 3D";
                if (!show) Hyd3DInfo.Visibility = WpfVisibility.Collapsed;
                if (_hyd3D != null) _hyd3D.IsActive = show;
            }
            else
            {
                _aerShowing3D = show;
                Aer3DBackdrop.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                AerViewport3D.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                AerView3DHint.Visibility      = show ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                AerSchematicScroll.Visibility = show ? WpfVisibility.Collapsed : WpfVisibility.Visible;
                AerView3DButton.Content       = show ? "Ver árbol" : "Ver 3D";
                if (!show) Aer3DInfo.Visibility = WpfVisibility.Collapsed;
                if (_aer3D != null) _aer3D.IsActive = show;
            }
        }

        private static MColor SystemColor(MBrush b)
        {
            MSolidBrush scb = b as MSolidBrush;
            if (scb != null) return scb.Color;
            return MColor.FromRgb(0x2E, 0x6D, 0xB4);
        }

        // ------------------------------------------------------------------ //
        //  Extracción de malla desde Revit — UNA malla por elemento            //
        // ------------------------------------------------------------------ //
        private static List<ElementMesh3D> ExtractSystemMeshes(
            Document doc, IList<ElementId> ids, out Point3D center, out double radius)
        {
            List<long> empty;
            return ExtractSystemMeshes(doc, ids, out center, out radius, out empty);
        }

        private static List<ElementMesh3D> ExtractSystemMeshes(
            Document doc, IList<ElementId> ids, out Point3D center, out double radius,
            out List<long> emptyFittingIds)
        {
            const double FT = 0.3048; // pies → metros (coords manejables)
            var parts = new List<ElementMesh3D>();
            emptyFittingIds = new List<long>();

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            bool any = false;

            // Tubos/conductos: Medium basta (cilindros simples, menos triángulos).
            var optPipe = new Options();
            optPipe.DetailLevel              = ViewDetailLevel.Medium;
            optPipe.ComputeReferences        = false;
            optPipe.IncludeNonVisibleObjects = false;

            // Accesorios (codos, tes, válvulas): MUCHAS familias MEP solo modelan
            // el sólido 3D en detalle FINO; en Medium/Coarse muestran solo líneas
            // simbólicas (por eso no se veían).  Para ellos forzamos Fine.
            // Además, algunas familias de codo modelan el sólido en una
            // subcategoría marcada como NO visible; sin IncludeNonVisibleObjects
            // esos codos se extraen vacíos y aparecen "huecos" en el visor.
            var optFitting = new Options();
            optFitting.DetailLevel              = ViewDetailLevel.Fine;
            optFitting.ComputeReferences        = false;
            optFitting.IncludeNonVisibleObjects = true;

            foreach (ElementId id in ids)
            {
                try
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    bool isCurve = el is MEPCurve;
                    Options opt = isCurve ? optPipe : optFitting;
                    GeometryElement ge = el.get_Geometry(opt);
                    if (ge == null)
                    {
                        if (!isCurve) emptyFittingIds.Add(id.Value);
                        continue;
                    }

                    var acc = new MeshAccum();
                    AddGeometry(ge, acc, FT, null);
                    if (!acc.Any)
                    {
                        if (!isCurve) emptyFittingIds.Add(id.Value);
                        continue;
                    }

                    var mesh = new MeshGeometry3D();
                    mesh.Positions       = acc.Pos;
                    mesh.TriangleIndices = acc.Idx;
                    if (mesh.CanFreeze) mesh.Freeze(); // inmutable → render más rápido

                    parts.Add(new ElementMesh3D(id.Value, mesh));

                    if (acc.MinX < minX) minX = acc.MinX;
                    if (acc.MinY < minY) minY = acc.MinY;
                    if (acc.MinZ < minZ) minZ = acc.MinZ;
                    if (acc.MaxX > maxX) maxX = acc.MaxX;
                    if (acc.MaxY > maxY) maxY = acc.MaxY;
                    if (acc.MaxZ > maxZ) maxZ = acc.MaxZ;
                    any = true;
                }
                catch { /* ignorar elementos sin geometría válida */ }
            }

            if (!any)
            {
                center = new Point3D();
                radius = 1.0;
                return parts;
            }

            center = new Point3D((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
            double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            radius = 0.5 * Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (radius < 1e-6) radius = 1.0;
            return parts;
        }

        // Devuelve true si añadió al menos un triángulo.  'tf' aplica una
        // transformación adicional a cada vértice (null = identidad); se usa al
        // recurrir por geometría de SÍMBOLO, que viene en coordenadas locales.
        private static bool AddGeometry(GeometryElement ge, MeshAccum acc, double ft, Transform tf)
        {
            bool added = false;
            foreach (GeometryObject go in ge)
            {
                Solid solid = go as Solid;
                if (solid != null && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                        added |= AddMesh(face.Triangulate(), acc, ft, tf);
                    continue;
                }

                Mesh mesh = go as Mesh;
                if (mesh != null)
                {
                    added |= AddMesh(mesh, acc, ft, tf);
                    continue;
                }

                GeometryInstance gi = go as GeometryInstance;
                if (gi != null)
                {
                    // Vía normal: geometría de instancia (ya en coords de modelo
                    // cuando tf == null).  Si no produce nada — algunas familias
                    // de codo devuelven instancia vacía — recurrimos al SÍMBOLO
                    // aplicando la transformación de la instancia a mano.
                    bool sub = false;
                    GeometryElement ig = gi.GetInstanceGeometry();
                    if (ig != null) sub = AddGeometry(ig, acc, ft, tf);
                    if (!sub)
                    {
                        GeometryElement sg = gi.GetSymbolGeometry();
                        if (sg != null)
                        {
                            Transform combined = tf == null
                                ? gi.Transform
                                : tf.Multiply(gi.Transform);
                            sub = AddGeometry(sg, acc, ft, combined);
                        }
                    }
                    added |= sub;
                }
            }
            return added;
        }

        private static bool AddMesh(Mesh m, MeshAccum acc, double ft, Transform tf)
        {
            if (m == null) return false;
            bool added = false;
            int nt = m.NumTriangles;
            for (int i = 0; i < nt; i++)
            {
                MeshTriangle tri = m.get_Triangle(i);
                int i0 = acc.AddVertex(tri.get_Vertex(0), ft, tf);
                int i1 = acc.AddVertex(tri.get_Vertex(1), ft, tf);
                int i2 = acc.AddVertex(tri.get_Vertex(2), ft, tf);
                if (i0 == i1 || i1 == i2 || i0 == i2) continue; // triángulo degenerado
                acc.Idx.Add(i0);
                acc.Idx.Add(i1);
                acc.Idx.Add(i2);
                added = true;
            }
            return added;
        }

        // Par (ElementId, malla) para poder hacer picking por elemento.
        internal sealed class ElementMesh3D
        {
            public readonly long ElementId;
            public readonly MeshGeometry3D Mesh;
            public ElementMesh3D(long id, MeshGeometry3D mesh) { ElementId = id; Mesh = mesh; }
        }

        // Acumulador de malla con deduplicación de vértices por posición:
        // al compartir vértices, WPF genera normales SUAVIZADAS (los cilindros
        // y codos se ven redondeados en vez de facetados) y se reduce el número
        // de vértices.  Tolerancia 0,5 mm.
        private sealed class MeshAccum
        {
            public readonly Point3DCollection Pos = new Point3DCollection();
            public readonly Int32Collection   Idx = new Int32Collection();
            public double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue;
            public double MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
            public bool Any;

            private const double Tol = 0.0005; // 0,5 mm
            private readonly Dictionary<long, int> _map = new Dictionary<long, int>();

            public int AddVertex(XYZ p, double ft, Transform tf)
            {
                if (tf != null) p = tf.OfPoint(p);
                double x = p.X * ft, y = p.Y * ft, z = p.Z * ft;

                long qx = (long)Math.Round(x / Tol);
                long qy = (long)Math.Round(y / Tol);
                long qz = (long)Math.Round(z / Tol);
                long key = (qx * 73856093L) ^ (qy * 19349663L) ^ (qz * 83492791L);

                int idx;
                if (_map.TryGetValue(key, out idx)) return idx;

                idx = Pos.Count;
                Pos.Add(new Point3D(x, y, z));
                _map[key] = idx;

                if (x < MinX) MinX = x; if (y < MinY) MinY = y; if (z < MinZ) MinZ = z;
                if (x > MaxX) MaxX = x; if (y > MaxY) MaxY = y; if (z > MaxZ) MaxZ = z;
                Any = true;
                return idx;
            }
        }

        // ================================================================== //
        //  Orbit3DView — cámara estilo Revit + montaje de malla + picking      //
        // ================================================================== //
        internal sealed class Orbit3DView
        {
            private readonly Viewport3D _vp;
            private readonly FrameworkElement _input;
            private readonly PerspectiveCamera _cam;
            private ModelVisual3D _modelVisual;

            // Mapa modelo→ElementId y modelo de la selección actual (para resaltar).
            private readonly Dictionary<GeometryModel3D, long> _modelToId
                = new Dictionary<GeometryModel3D, long>();
            // Material original de cada modelo (base del sistema o magenta de
            // desconectado): al des-resaltar se restaura este, no siempre el base.
            private readonly Dictionary<GeometryModel3D, MMaterial> _modelToBaseMat
                = new Dictionary<GeometryModel3D, MMaterial>();
            private GeometryModel3D _highlighted;
            private MMaterial _baseMat, _highlightMat;

            private Point3D _target;
            private double _yaw   = Math.PI * 0.30;   // azimut
            private double _pitch = Math.PI * 0.22;   // elevación
            private double _dist  = 10.0;
            private double _radius = 5.0;

            private System.Windows.Point _last;
            private int _mode; // 0=nada 1=orbitar 2=panear

            public bool IsActive;

            // Notifica al CalcWindow el ElementId del tubo seleccionado con clic.
            public Action<long> ElementPicked;

            public Orbit3DView(Viewport3D viewport, FrameworkElement input)
            {
                _vp = viewport;
                _input = input;

                _cam = new PerspectiveCamera();
                _cam.FieldOfView   = 45;
                _cam.UpDirection   = new Vector3D(0, 0, 1);
                _cam.NearPlaneDistance = 0.01;
                _vp.Camera = _cam;

                // Iluminación plana: ambiente alto + una direccional suave para dar
                // algo de volumen al cilindro.  Sin highlights especulares: el
                // usuario no quiere "sombras" en los tubos y aligera el render.
                var lights = new ModelVisual3D();
                var grp = new Model3DGroup();
                grp.Children.Add(new AmbientLight(MColor.FromRgb(150, 150, 155)));
                grp.Children.Add(new DirectionalLight(MColor.FromRgb(170, 170, 178),
                    new Vector3D(-1.0, -1.2, -2.0)));
                if (grp.CanFreeze) grp.Freeze();
                lights.Content = grp;
                _vp.Children.Add(lights);

                // Supersampling: como la ventana es AllowsTransparency (render 3D
                // por software, sin MSAA), suavizamos las aristas renderizando a
                // mayor resolución y reduciendo la escala (anti-aliasing por
                // sobremuestreo).
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    _vp, System.Windows.Media.BitmapScalingMode.HighQuality);
                _input.SizeChanged += delegate { ApplySupersample(); };
                ApplySupersample();

                _input.MouseDown  += OnMouseDown;
                _input.MouseUp    += OnMouseUp;
                _input.MouseMove  += OnMove;
                _input.MouseWheel += OnWheel;
            }

            // Factor de sobremuestreo (1 = sin SS).  1.6 ≈ 2,5× píxeles: buen
            // compromiso nitidez/rendimiento.
            private const double Supersample = 1.6;

            private void ApplySupersample()
            {
                double w = _input.ActualWidth, h = _input.ActualHeight;
                if (w < 1 || h < 1) return;
                _vp.HorizontalAlignment = HorizontalAlignment.Left;
                _vp.VerticalAlignment   = VerticalAlignment.Top;
                _vp.Width  = w * Supersample;
                _vp.Height = h * Supersample;
                var t = new System.Windows.Media.ScaleTransform(1.0 / Supersample, 1.0 / Supersample);
                if (t.CanFreeze) t.Freeze();
                _vp.LayoutTransform = t;
            }

            public void SetParts(List<ElementMesh3D> parts, MColor color, Point3D center, double radius,
                HashSet<long> disconnectedIds = null)
            {
                if (_modelVisual != null)
                {
                    _vp.Children.Remove(_modelVisual);
                    _modelVisual = null;
                }
                _modelToId.Clear();
                _modelToBaseMat.Clear();
                _highlighted = null;

                // Material base del sistema + material de resaltado (naranja vivo).
                _baseMat      = BuildMaterial(color);
                _highlightMat = BuildMaterial(MColor.FromRgb(0xFF, 0x8A, 0x00));
                // Material para tramos desconectados de la fuente (magenta vivo).
                var discMat   = BuildMaterial(MColor.FromRgb(0xE0, 0x40, 0xFB));

                var group = new Model3DGroup();
                for (int i = 0; i < parts.Count; i++)
                {
                    ElementMesh3D part = parts[i];
                    if (part.Mesh == null) continue;

                    bool isDisc = disconnectedIds != null && disconnectedIds.Contains(part.ElementId);
                    MMaterial mat = isDisc ? discMat : _baseMat;

                    var model = new GeometryModel3D(part.Mesh, mat);
                    model.BackMaterial = mat; // visible aunque el winding esté invertido
                    // NO se congela: necesitamos cambiar Material al resaltar.
                    group.Children.Add(model);
                    _modelToId[model] = part.ElementId;
                    _modelToBaseMat[model] = mat;
                }

                _modelVisual = new ModelVisual3D();
                _modelVisual.Content = group;
                _vp.Children.Add(_modelVisual);

                _target = center;
                _radius = radius <= 1e-6 ? 1.0 : radius;
                _dist   = _radius * 2.6;
                UpdateCamera();
            }

            private static MMaterial BuildMaterial(MColor color)
            {
                var fill = new MSolidBrush(color);
                if (fill.CanFreeze) fill.Freeze();
                // Matte plano (sin especular): tubos sin brillos/"sombras".
                var mat = new DiffuseMaterial(fill);
                if (mat.CanFreeze) mat.Freeze();
                return mat;
            }

            private Vector3D DirFromAngles()
            {
                double cp = Math.Cos(_pitch);
                return new Vector3D(cp * Math.Cos(_yaw), cp * Math.Sin(_yaw), Math.Sin(_pitch));
            }

            private void UpdateCamera()
            {
                Vector3D dir = DirFromAngles();
                Point3D eye = _target + dir * _dist;
                _cam.Position      = eye;
                _cam.LookDirection = _target - eye;
                _cam.UpDirection   = new Vector3D(0, 0, 1);
                _cam.FarPlaneDistance = _dist + _radius * 8.0;
            }

            // ── Navegación estilo Revit ──────────────────────────────────────
            //    Rueda central .............. paneo
            //    Mayús + rueda central ...... orbitar
            //    Clic izquierdo ............. seleccionar tubo
            private void OnMouseDown(object s, MouseButtonEventArgs e)
            {
                if (!IsActive) return;
                _last = e.GetPosition(_input);

                if (e.ChangedButton == MouseButton.Middle)
                {
                    bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                    _mode = shift ? 1 : 2;   // 1=orbitar (Mayús), 2=panear
                    _input.CaptureMouse();
                    e.Handled = true;
                }
                else if (e.ChangedButton == MouseButton.Left)
                {
                    PickAt(e.GetPosition(_vp));
                }
            }

            private void OnMouseUp(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    _mode = 0;
                    _input.ReleaseMouseCapture();
                }
            }

            private void OnMove(object s, MouseEventArgs e)
            {
                if (!IsActive || _mode == 0) return;
                System.Windows.Point p = e.GetPosition(_input);
                double dx = p.X - _last.X;
                double dy = p.Y - _last.Y;
                _last = p;

                if (_mode == 1) // orbitar
                {
                    _yaw   -= dx * 0.01;
                    _pitch += dy * 0.01;
                    double lim = Math.PI / 2 - 0.05;
                    if (_pitch > lim)  _pitch = lim;
                    if (_pitch < -lim) _pitch = -lim;
                    UpdateCamera();
                }
                else if (_mode == 2) // panear
                {
                    Vector3D look = _target - _cam.Position;
                    look.Normalize();
                    Vector3D right = Vector3D.CrossProduct(look, new Vector3D(0, 0, 1));
                    if (right.Length < 1e-6) right = new Vector3D(1, 0, 0);
                    right.Normalize();
                    Vector3D up = Vector3D.CrossProduct(right, look);
                    up.Normalize();
                    double scale = _dist * 0.0015;
                    _target += (-dx * scale) * right + (dy * scale) * up;
                    UpdateCamera();
                }
            }

            private void OnWheel(object s, MouseWheelEventArgs e)
            {
                if (!IsActive) return;
                bool zoomIn = e.Delta > 0;

                // Zoom hacia el cursor (como Revit): al acercar, desplaza el punto
                // de interés hacia el elemento bajo el cursor.
                if (zoomIn)
                {
                    GeometryModel3D m; Point3D hit;
                    if (TryPick(e.GetPosition(_vp), out m, out hit))
                        _target = _target + (hit - _target) * 0.18;
                }

                _dist *= zoomIn ? 0.85 : 1.18;
                if (_dist < _radius * 0.05) _dist = _radius * 0.05;
                if (_dist > _radius * 40.0) _dist = _radius * 40.0;
                UpdateCamera();
                e.Handled = true;
            }

            // ── Picking ──────────────────────────────────────────────────────
            private void PickAt(System.Windows.Point posInViewport)
            {
                GeometryModel3D model; Point3D pt;
                if (!TryPick(posInViewport, out model, out pt) || model == null) return;

                Highlight(model);

                long id;
                if (_modelToId.TryGetValue(model, out id) && ElementPicked != null)
                    ElementPicked(id);
            }

            private void Highlight(GeometryModel3D model)
            {
                if (ReferenceEquals(_highlighted, model)) return;
                if (_highlighted != null)
                {
                    MMaterial restore;
                    if (!_modelToBaseMat.TryGetValue(_highlighted, out restore)) restore = _baseMat;
                    _highlighted.Material     = restore;
                    _highlighted.BackMaterial = restore;
                }
                _highlighted = model;
                if (_highlighted != null)
                {
                    _highlighted.Material     = _highlightMat;
                    _highlighted.BackMaterial = _highlightMat;
                }
            }

            private bool TryPick(System.Windows.Point pos, out GeometryModel3D model, out Point3D point)
            {
                GeometryModel3D found = null;
                Point3D pt = new Point3D();
                System.Windows.Media.VisualTreeHelper.HitTest(_vp, null,
                    delegate(System.Windows.Media.HitTestResult r)
                    {
                        RayMeshGeometry3DHitTestResult rm = r as RayMeshGeometry3DHitTestResult;
                        if (rm != null)
                        {
                            found = rm.ModelHit as GeometryModel3D;
                            pt = rm.PointHit;
                            return System.Windows.Media.HitTestResultBehavior.Stop;
                        }
                        return System.Windows.Media.HitTestResultBehavior.Continue;
                    },
                    new System.Windows.Media.PointHitTestParameters(pos));
                model = found;
                point = pt;
                return found != null;
            }
        }
    }
}
