using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Hosting;

namespace E3G.BimTools.DiTools.UI.Views.RefrigerantRouting;

/// <summary>Pestaña «Ajustes» de Climatizacion: opciones del trazado de refrigerante (se guardan solas).</summary>
public partial class RefrigerantSettingsView : UserControl
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly IRefrigerantRoutingService _service;
    private readonly IRevitInvoker _revit;
    private bool _loading = true;

    private RefrigerantRoutingSettings S => RefrigerantRoutingSettings.Current;

    public RefrigerantSettingsView(IRefrigerantRoutingService service, IRevitInvoker revit)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _revit = revit ?? throw new ArgumentNullException(nameof(revit));
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        Fill();
        IsVisibleChanged += async (_, e) => { if (e.NewValue is true) await LoadProjectAsync(); };
        Loaded += async (_, _) => await LoadProjectAsync();
    }

    private void Fill()
    {
        _loading = true;
        PipeTypeCombo.Text = S.PipeType;
        LiquidCombo.Text = S.LiquidSystem;
        GasCombo.Text = S.GasSystem;
        CreateSystemsCheck.IsChecked = S.CreateMissingSystems;
        AddSizesCheck.IsChecked = S.AddMissingSizes;
        SpacingBox.Text = S.SpacingMm.ToString("0.#", Es);
        UnitHeightBox.Text = S.UnitHeightMm.ToString("0.#", Es);
        TrimCheck.IsChecked = S.TrimAtRoom;
        InsideBox.Text = S.InsideRoomMm.ToString("0.#", Es);
        ReplaceCheck.IsChecked = S.ReplacePrevious;
        PipeLayerBox.Text = S.PipeLayer;
        RoomLayerBox.Text = S.RoomLayer;
        _loading = false;
    }

    private async System.Threading.Tasks.Task LoadProjectAsync()
    {
        try
        {
            var info = await _revit.InvokeAsync(app =>
            {
                var uidoc = app.ActiveUIDocument;
                return uidoc?.Document is null || uidoc.Document.IsFamilyDocument ? null : _service.GetProjectInfo(uidoc.Document, null);
            });
            if (info is null) return;
            _loading = true;
            PipeTypeCombo.ItemsSource = info.PipeTypes.Select(t => t.Name).ToList();
            LiquidCombo.ItemsSource = info.PipingSystems.Select(t => t.Name).ToList();
            GasCombo.ItemsSource = info.PipingSystems.Select(t => t.Name).ToList();
            PipeTypeCombo.Text = S.PipeType;
            LiquidCombo.Text = S.LiquidSystem;
            GasCombo.Text = S.GasSystem;
        }
        catch { /* sin proyecto: se pueden escribir los nombres */ }
        finally { _loading = false; }
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // El texto de un ComboBox editable cambia despues de SelectionChanged.
        Dispatcher.BeginInvoke(new Action(Save), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Save()
    {
        if (_loading) return;
        S.PipeType = Text(PipeTypeCombo, S.PipeType);
        S.LiquidSystem = Text(LiquidCombo, S.LiquidSystem);
        S.GasSystem = Text(GasCombo, S.GasSystem);
        S.CreateMissingSystems = CreateSystemsCheck.IsChecked == true;
        S.AddMissingSizes = AddSizesCheck.IsChecked == true;
        S.TrimAtRoom = TrimCheck.IsChecked == true;
        S.ReplacePrevious = ReplaceCheck.IsChecked == true;
        if (Num(SpacingBox.Text) is double sp && sp > 0) S.SpacingMm = sp;
        if (Num(UnitHeightBox.Text) is double uh && uh > 0) S.UnitHeightMm = uh;
        if (Num(InsideBox.Text) is double ins && ins >= 0) S.InsideRoomMm = ins;
        if (!string.IsNullOrWhiteSpace(PipeLayerBox.Text)) S.PipeLayer = PipeLayerBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(RoomLayerBox.Text)) S.RoomLayer = RoomLayerBox.Text.Trim();
        S.Save(this);
        SavedText.Text = "Guardado " + DateTime.Now.ToString("HH:mm:ss", Es) + ".";
    }

    private static string Text(ComboBox c, string fallback)
    {
        string t = (c.SelectedItem as string) ?? c.Text;
        return string.IsNullOrWhiteSpace(t) ? fallback : t.Trim();
    }

    private static double? Num(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, Es, out var v) || double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v : null;
}
