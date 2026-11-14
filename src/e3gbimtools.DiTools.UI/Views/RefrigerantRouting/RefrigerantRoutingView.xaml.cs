using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Hosting;
using Mouse = System.Windows.Input.Mouse;
using Color = System.Windows.Media.Color;
using Visibility = System.Windows.Visibility;

namespace E3G.BimTools.DiTools.UI.Views.RefrigerantRouting;

/// <summary>
/// Pestaña «Trazado DXF» de Climatizacion (panel MEP, 2026-11-12): DXF de
/// tuberias del fabricante → planta encajada con los muros de Revit → tuberias
/// de liquido (50_12) y gas (50_13) con derivadores y cajas como accesorios.
/// Pide la vista, el nivel asociado, la altura y el mapeo bloque → familia;
/// el resto de opciones esta en la pestaña «Ajustes» (<see cref="RefrigerantRoutingSettings"/>).
/// Habla con Revit por <see cref="IRevitInvoker"/> porque el panel no es modal.
/// </summary>
public partial class RefrigerantRoutingView : UserControl
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private const string Titulo = "Trazado de refrigerante";
    private const string CreateLabel = "‹Familia del bloque (se crea si no existe)›";

    private readonly IRefrigerantRoutingService _service;
    private readonly IRevitInvoker _revit;
    private RefrigerantRoutingSettings S => RefrigerantRoutingSettings.Current;

    private RefrigerantProjectInfo? _project;
    private LatsPlan? _plan;
    private PlanTransform? _transform;
    private IReadOnlyList<Seg2>? _revitArch;
    private long _revitArchLevel = -1;
    private readonly List<MappingRow> _rows = new();
    private bool _busy;
    private bool _loading = true;

    public RefrigerantRoutingView(IRefrigerantRoutingService service, IRevitInvoker revit)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _revit = revit ?? throw new ArgumentNullException(nameof(revit));
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        HeightBox.Text = S.HeightMm.ToString("0", Es);
        _loading = false;
        RefrigerantRoutingSettings.Changed += (s, _) => { if (!ReferenceEquals(s, this)) Dispatcher.Invoke(UpdatePreview); };
        Loaded += async (_, _) => await LoadProjectAsync();
    }

    // ============================================================
    // Proyecto
    // ============================================================

    private async Task LoadProjectAsync()
    {
        try
        {
            _project = await _revit.InvokeAsync(app =>
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc?.Document is null || uidoc.Document.IsFamilyDocument) return null;
                return _service.GetProjectInfo(uidoc.Document, uidoc.ActiveView);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "No se pudo leer el proyecto activo: " + ex.Message;
            return;
        }
        if (_project is null)
        {
            StatusText.Text = "Abre un proyecto de Revit (no el editor de familias).";
            return;
        }

        _loading = true;
        LevelCombo.ItemsSource = _project.Levels;
        ViewCombo.ItemsSource = _project.Views;
        ViewCombo.SelectedItem = _project.Views.FirstOrDefault(v => v.Id == _project.ActiveViewId) ?? _project.Views.FirstOrDefault();
        if (ViewCombo.SelectedItem is RefPlanView v)
            LevelCombo.SelectedItem = _project.Levels.FirstOrDefault(l => l.Id == v.LevelId);
        _loading = false;
        RefreshFamilies();
        UpdateButtons();
    }

    private void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ViewCombo.SelectedItem is not RefPlanView v || _project is null) return;
        LevelCombo.SelectedItem = _project.Levels.FirstOrDefault(l => l.Id == v.LevelId) ?? LevelCombo.SelectedItem;
    }

    private void OnLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateButtons();
    }

    private void OnOptionChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (TryMm(HeightBox.Text, out var h)) { S.HeightMm = h; S.Save(this); }
    }

    // ============================================================
    // DXF
    // ============================================================

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Plano de tuberías del fabricante",
            Filter = "Dibujos CAD (*.dxf;*.dwg)|*.dxf;*.dwg|Todos los archivos (*.*)|*.*",
            InitialDirectory = Directory.Exists(S.LastFolder) ? S.LastFolder : string.Empty,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        S.LastFolder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
        S.Save(this);
        await LoadFileAsync(dlg.FileName);
    }

    private async Task LoadFileAsync(string path)
    {
        SetBusy(true, "Leyendo el DXF…");
        try
        {
            var opts = new LatsReadOptions { PipeLayer = S.PipeLayer, RoomLayer = S.RoomLayer };
            _plan = await Task.Run(() => _service.Read(path, opts));
        }
        catch (Exception ex)
        {
            _plan = null;
            SetBusy(false, "No se pudo leer el archivo: " + ex.Message);
            return;
        }
        SetBusy(false, string.Empty);

        FileBox.Text = path;
        int branches = _plan.Devices.Count(d => BlockRoleExtensions.Guess(d.BlockName) == BlockRole.Branch);
        FileInfo.Text = $"{_plan.Pipes.Count} tramos · {_plan.Devices.Count} bloques del fabricante · {_plan.Rooms.Count} recintos · " +
                        $"{_plan.Architecture.Count:N0} líneas de arquitectura" +
                        (_plan.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", _plan.Warnings.Take(4)) : string.Empty);
        BuildMappingRows();
        _transform = null;
        ShowAlign();
        UpdatePreview();
        StatusText.Text = "Encajando con la arquitectura de Revit…";
        await AlignAsync();
    }

    // ============================================================
    // Encaje
    // ============================================================

    private async void OnAlignClick(object sender, RoutedEventArgs e) => await AlignAsync();

    private async Task AlignAsync()
    {
        if (_plan is null || LevelCombo.SelectedItem is not RefLevel level) { UpdateButtons(); return; }
        SetBusy(true, "Leyendo los muros del nivel y encajando…");
        try
        {
            await EnsureRevitArchitectureAsync(level);
            var plan = _plan;
            var arch = _revitArch!;
            _transform = await Task.Run(() => _service.Align(plan, arch));
            SetBusy(false, _transform is null
                ? "No hay muros en el nivel o arquitectura en el DXF: ajusta el encaje a mano."
                : "Encajado. Revisa la vista previa y pulsa «Modelar tuberías».");
            if (_transform is not null) FillManual(_transform);
        }
        catch (Exception ex)
        {
            SetBusy(false, "No se pudo encajar: " + ex.Message);
        }
        ShowAlign();
        UpdatePreview();
    }

    private async Task EnsureRevitArchitectureAsync(RefLevel level)
    {
        long id = level.Id.Value;
        if (_revitArch is not null && _revitArchLevel == id) return;
        _revitArch = await _revit.InvokeAsync(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            return doc is null ? Array.Empty<Seg2>() : _service.GetArchitecture(doc, id);
        });
        _revitArchLevel = id;
    }

    private async void OnManualClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null) return;
        if (!TryNum(TxBox.Text, out var tx) || !TryNum(TyBox.Text, out var ty) || !TryNum(RotBox.Text, out var rot)
            || !TryNum(ScaleBox.Text, out var sc) || sc <= 0)
        {
            Avisar("Revisa los valores del ajuste manual.");
            return;
        }
        var t = new PlanTransform(sc, rot * Math.PI / 180, tx, ty) { Method = "Manual" };
        if (LevelCombo.SelectedItem is RefLevel level)
        {
            try
            {
                await EnsureRevitArchitectureAsync(level);
                var plan = _plan; var arch = _revitArch!;
                t = await Task.Run(() => _service.Evaluate(t, plan, arch)) with { Method = "Manual" };
            }
            catch { /* sin puntuacion */ }
        }
        _transform = t;
        ShowAlign();
        UpdatePreview();
        UpdateButtons();
    }

    private void FillManual(PlanTransform t)
    {
        TxBox.Text = t.Tx.ToString("0.####", Es);
        TyBox.Text = t.Ty.ToString("0.####", Es);
        RotBox.Text = (t.Rotation * 180 / Math.PI).ToString("0.####", Es);
        ScaleBox.Text = t.Scale.ToString("0.######", Es);
    }

    private void ShowAlign()
    {
        if (_transform is not { } t)
        {
            AlignInfo.Text = _plan is null ? "Sin encajar. Carga el DXF y pulsa «Encajar con Revit»." : "Sin encajar.";
            AlignBadge.Background = (Brush)FindResource("E3g.Brush.SurfaceSubtle");
            return;
        }
        string score = double.IsNaN(t.Score) ? "sin comprobar" : $"{t.Score:P0} de las líneas del DXF sobre muros de Revit";
        AlignInfo.Text = $"{t.Method} · {score}\n{t.Describe()}";
        AlignBadge.Background = new SolidColorBrush(double.IsNaN(t.Score) ? Color.FromRgb(0xEE, 0xEE, 0xEE)
            : t.Score >= 0.4 ? Color.FromRgb(0xE3, 0xF4, 0xE1)
            : t.Score >= 0.2 ? Color.FromRgb(0xFD, 0xF0, 0xD5)
            : Color.FromRgb(0xFB, 0xE0, 0xDE));
        UpdateButtons();
    }

    // ============================================================
    // Mapeo de bloques
    // ============================================================

    private void BuildMappingRows()
    {
        _rows.Clear();
        if (_plan is not null)
            foreach (var name in _plan.BlockNames.OrderBy(n => n))
            {
                _plan.Blocks.TryGetValue(name, out var def);
                S.Mappings.TryGetValue(name, out var saved);
                var role = saved is not null && Enum.TryParse<BlockRole>(saved.Role, out var r) ? r : BlockRoleExtensions.Guess(name);
                var row = new MappingRow(name, $"×{def?.Count ?? 0} · {def?.Ports.Count ?? 0} bocas", role, saved?.Family ?? string.Empty);
                row.UserChanged += (_, _) => OnMappingChanged(row);
                _rows.Add(row);
            }
        MappingList.ItemsSource = null;
        MappingList.ItemsSource = _rows;
        RefreshFamilies();
    }

    private void RefreshFamilies()
    {
        var fams = new List<FamilyOption> { new(null, CreateLabel) };
        if (_project is not null) fams.AddRange(_project.AccessoryTypes.Select(a => new FamilyOption(a.Id.Value, a.Name)));
        foreach (var r in _rows) r.SetFamilies(fams);
    }

    private void OnMappingChanged(MappingRow row)
    {
        S.Mappings[row.BlockName] = new RefrigerantRoutingSettings.MappingSetting
        {
            Role = row.Role.Role.ToString(),
            Family = row.Family?.Id is null ? string.Empty : row.Family.Name,
        };
        S.Save(this);
        UpdatePreview();
    }

    // ============================================================
    // Vista previa
    // ============================================================

    private RefrigerantRoutingOptions Options(RefLevel? level, RefPlanView? view, double heightMm) => new()
    {
        Spacing = S.SpacingMm / 1000,
        TrimAtRoom = S.TrimAtRoom,
        InsideRoom = S.InsideRoomMm / 1000,
        Height = heightMm / 1000,
        UnitHeight = S.UnitHeightMm / 1000,
        PipeTypeName = S.PipeType,
        LiquidSystemName = S.LiquidSystem,
        GasSystemName = S.GasSystem,
        CreateMissingSystems = S.CreateMissingSystems,
        AddMissingSizes = S.AddMissingSizes,
        ReplacePrevious = S.ReplacePrevious,
        LevelId = level?.Id.Value ?? -1,
        ViewId = view?.Id.Value ?? -1,
        Mappings = _rows.ToDictionary(r => r.BlockName,
            r => new BlockMapping { BlockName = r.BlockName, Role = r.Role.Role, SymbolId = r.Family?.Id },
            StringComparer.OrdinalIgnoreCase),
    };

    private void UpdatePreview()
    {
        if (_plan is null) { Preview.SetDrawing(null, new HashSet<string>()); PreviewHint.Visibility = Visibility.Visible; return; }
        var t = _transform ?? new PlanTransform(_plan.DeclaredMetersPerUnit ?? 1, 0, 0, 0);
        var routing = _service.BuildPlan(_plan, t, Options(null, null, 0));

        var d = new LegendDrawing { FilePath = _plan.FilePath };
        var gray = new LegendColor(170, 170, 170); var orange = new LegendColor(245, 190, 120);
        var liq = new LegendColor(40, 170, 40); var gas = new LegendColor(150, 40, 40); var blue = new LegendColor(30, 80, 230);
        void Line(string layer, LegendColor c, P2 a, P2 b) =>
            d.Items.Add(new LegendCurve(layer, c, null) { Kind = LegendCurveKind.Line, X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y });

        if (_revitArch is not null) foreach (var s in _revitArch) Line("Revit", gray, s.A, s.B);
        foreach (var s in _plan.Architecture.Where(s => s.Length * t.Scale > 0.3).OrderByDescending(s => s.Length).Take(6000))
            Line("DXF", orange, t.Apply(s.A), t.Apply(s.B));
        foreach (var p in routing.Pipes)
            for (int i = 1; i < p.Points.Count; i++) Line("Tuberias", p.IsLiquid ? liq : gas, p.Points[i - 1], p.Points[i]);
        foreach (var dev in routing.Devices)
        {
            double h = dev.Role == BlockRole.Unit ? 0.25 : 0.06;
            var o = dev.Origin;
            Line("Accesorios", blue, o + new P2(-h, -h), o + new P2(h, -h)); Line("Accesorios", blue, o + new P2(h, -h), o + new P2(h, h));
            Line("Accesorios", blue, o + new P2(h, h), o + new P2(-h, h)); Line("Accesorios", blue, o + new P2(-h, h), o + new P2(-h, -h));
        }

        // Encuadre: lo del DXF (tuberias y accesorios) con un margen.
        var pts = routing.Pipes.SelectMany(p => p.Points).ToList();
        if (pts.Count == 0) pts = d.Items.OfType<LegendCurve>().SelectMany(c => new[] { new P2(c.X1, c.Y1), new P2(c.X2, c.Y2) }).ToList();
        if (pts.Count > 0)
        {
            double m = 2;
            d.MinX = pts.Min(p => p.X) - m; d.MaxX = pts.Max(p => p.X) + m;
            d.MinY = pts.Min(p => p.Y) - m; d.MaxY = pts.Max(p => p.Y) + m;
            // Lo que cae fuera del encuadre no se pinta (Revit puede tener todo el edificio).
            d.Items.RemoveAll(i => i is LegendCurve c && Math.Max(c.X1, c.X2) < d.MinX || i is LegendCurve c2 && Math.Min(c2.X1, c2.X2) > d.MaxX
                                   || i is LegendCurve c3 && Math.Max(c3.Y1, c3.Y2) < d.MinY || i is LegendCurve c4 && Math.Min(c4.Y1, c4.Y2) > d.MaxY);
        }
        Preview.SetDrawing(d, new HashSet<string>());
        PreviewHint.Visibility = Visibility.Collapsed;

        if (!_busy && _transform is not null)
            StatusText.Text = $"{routing.Pipes.Count} tuberías ({routing.TotalLength:0} m), {routing.Devices.Count} accesorios, " +
                              $"{routing.TrimmedAtRoom} tramos cortados en la entrada del recinto" +
                              (routing.Conflicts > 0 ? $", ⚠ {routing.Conflicts} conflictos de lado líquido/gas" : string.Empty) + ".";
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Preview.ResetView();

    // ============================================================
    // Modelar
    // ============================================================

    private async void OnModelClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _transform is null) return;
        if (ViewCombo.SelectedItem is not RefPlanView view) { Avisar("Elige la vista donde se modela."); return; }
        if (LevelCombo.SelectedItem is not RefLevel level) { Avisar("Elige el nivel asociado."); return; }
        if (!TryMm(HeightBox.Text, out var heightMm)) { Avisar("La altura no es un número válido."); return; }
        if (!double.IsNaN(_transform.Score) && _transform.Score < 0.2 &&
            MessageBox.Show(Window.GetWindow(this),
                $"El encaje con la arquitectura es bajo ({_transform.Score:P0}). ¿Modelar igualmente?",
                Titulo, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var opts = Options(level, view, heightMm);
        var plan = _plan; var t = _transform;
        var routing = _service.BuildPlan(plan, t, opts);

        SetBusy(true, "Modelando…");
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        RefrigerantRoutingResult result;
        try
        {
            result = await _revit.InvokeAsync(app =>
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc is null || doc.IsFamilyDocument)
                    return new RefrigerantRoutingResult { Success = false, ErrorMessage = "No hay un proyecto de Revit activo." };
                var r = _service.Apply(doc, plan, t, routing, opts);
                if (r.Success && doc.GetElement(view.Id) is View v && uidoc!.ActiveView?.Id != v.Id)
                {
                    try { uidoc.RequestViewChange(v); } catch { /* no critico */ }
                }
                return r;
            });
        }
        catch (Exception ex)
        {
            result = new RefrigerantRoutingResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (!result.Success)
        {
            SetBusy(false, "Error: " + result.ErrorMessage);
            MessageBox.Show(Window.GetWindow(this), result.ErrorMessage, Titulo + " · error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        SetBusy(false, result.ToSummary());
        await LoadProjectAsync(); // familias y sistemas nuevos
        RefreshFamiliesKeepingSelection();
        if (result.Warnings.Count > 0)
        {
            var body = result.ToSummary() + "\n\nAvisos:\n  · " + string.Join("\n  · ", result.Warnings.Take(15)) +
                       (result.Warnings.Count > 15 ? $"\n  … y {result.Warnings.Count - 15} más." : string.Empty);
            MessageBox.Show(Window.GetWindow(this), body, Titulo, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshFamiliesKeepingSelection()
    {
        foreach (var r in _rows) r.Remember();
        RefreshFamilies();
    }

    // ============================================================
    // Utilidades
    // ============================================================

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        if (!string.IsNullOrEmpty(status)) StatusText.Text = status;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        AlignButton.IsEnabled = !_busy && _plan is not null && LevelCombo.SelectedItem is RefLevel;
        ModelButton.IsEnabled = !_busy && _plan is not null && _transform is not null
                                && ViewCombo.SelectedItem is RefPlanView && LevelCombo.SelectedItem is RefLevel;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, Es, out v) || double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryMm(string s, out double v) => TryNum(s, out v) && v >= 0 && v < 100000;

    private void Avisar(string texto) =>
        MessageBox.Show(Window.GetWindow(this), texto, Titulo, MessageBoxButton.OK, MessageBoxImage.Information);

    // ============================================================
    // Filas del mapeo
    // ============================================================

    public sealed record RoleOption(BlockRole Role, string Label);

    public sealed record FamilyOption(long? Id, string Name);

    public sealed class MappingRow : INotifyPropertyChanged
    {
        private static readonly IReadOnlyList<RoleOption> AllRoles =
            Enum.GetValues(typeof(BlockRole)).Cast<BlockRole>().Select(r => new RoleOption(r, r.Label())).ToList();

        private RoleOption _role;
        private FamilyOption? _family;
        private string _wantedFamily;
        private IReadOnlyList<FamilyOption> _families = Array.Empty<FamilyOption>();

        public MappingRow(string blockName, string info, BlockRole role, string wantedFamily)
        {
            BlockName = blockName;
            Info = info;
            _role = AllRoles.First(r => r.Role == role);
            _wantedFamily = wantedFamily;
        }

        public string BlockName { get; }
        public string Info { get; }
        public IReadOnlyList<RoleOption> Roles => AllRoles;
        public IReadOnlyList<FamilyOption> Families => _families;
        public bool UsesFamily => _role.Role is BlockRole.Branch or BlockRole.Unit;

        public RoleOption Role
        {
            get => _role;
            set { if (value is null || value == _role) return; _role = value; Raise(); Raise(nameof(UsesFamily)); UserChanged?.Invoke(this, EventArgs.Empty); }
        }

        public FamilyOption? Family
        {
            get => _family;
            set { if (value == _family) return; _family = value; Raise(); UserChanged?.Invoke(this, EventArgs.Empty); }
        }

        /// <summary>Recuerda la eleccion actual para volver a ponerla al recargar la lista.</summary>
        public void Remember() => _wantedFamily = _family?.Id is null ? string.Empty : _family.Name;

        public void SetFamilies(IReadOnlyList<FamilyOption> fams)
        {
            _families = fams;
            Raise(nameof(Families));
            _family = fams.FirstOrDefault(f => f.Id is not null && f.Name == _wantedFamily) ?? fams[0];
            Raise(nameof(Family));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>Solo cuando el usuario cambia el uso o la familia (no al recargar la lista).</summary>
        public event EventHandler? UserChanged;
        private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
