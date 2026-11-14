using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.OneFilter;

/// <summary>
/// Diálogo simple para elegir un color (RGB).  Evita la dependencia de
/// System.Windows.Forms.ColorDialog para no obligar a UseWindowsForms en el csproj.
/// </summary>
public partial class SimpleColorDialog : Window
{
    public byte R { get; private set; } = 255;
    public byte G { get; private set; } = 69;
    public byte B { get; private set; } = 0;

    private static readonly (byte R, byte G, byte B, string Name)[] Swatches =
    {
        (255,  69,   0, "Rojo"),
        (255, 165,   0, "Naranja"),
        (255, 215,   0, "Amarillo"),
        ( 46, 204, 113, "Verde"),
        ( 52, 152, 219, "Azul"),
        ( 46, 117, 182, "Azul DiTools"),
        (155,  89, 182, "Morado"),
        (149, 165, 166, "Gris"),
        (231,  76,  60, "Rojo intenso"),
        ( 29, 158, 117, "Verde Fontanería"),
        ( 74, 122, 150, "Azul Saneamiento"),
        (  0,   0,   0, "Negro"),
    };

    public SimpleColorDialog()
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        BuildSwatches();
        RBox.TextChanged += (_, __) => RefreshPreviewFromBoxes();
        GBox.TextChanged += (_, __) => RefreshPreviewFromBoxes();
        BBox.TextChanged += (_, __) => RefreshPreviewFromBoxes();
        RefreshPreviewFromBoxes();
    }

    private void BuildSwatches()
    {
        foreach (var s in Swatches)
        {
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 4),
                Background = new SolidColorBrush(Color.FromRgb(s.R, s.G, s.B)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                ToolTip = s.Name,
                Tag = s,
            };
            btn.Click += OnSwatchClick;
            SwatchesPanel.Children.Add(btn);
        }
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is ValueTuple<byte, byte, byte, string> s)
        {
            RBox.Text = s.Item1.ToString();
            GBox.Text = s.Item2.ToString();
            BBox.Text = s.Item3.ToString();
        }
    }

    private void RefreshPreviewFromBoxes()
    {
        var r = ParseByte(RBox.Text);
        var g = ParseByte(GBox.Text);
        var b = ParseByte(BBox.Text);
        PreviewBorder.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private static byte ParseByte(string s)
    {
        if (byte.TryParse(s, out var v)) return v;
        return 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        R = ParseByte(RBox.Text);
        G = ParseByte(GBox.Text);
        B = ParseByte(BBox.Text);
        DialogResult = true;
        Close();
    }
}
