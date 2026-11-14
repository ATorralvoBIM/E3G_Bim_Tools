// ============================================================================
//  E3gChrome.cs
//  Aspecto E3G (como las apps de DiTools: DxfLegendWindow, ScheduleExcelWindow)
//  para las ventanas y paginas del modulo Saneamiento.
//
//   - ApplyTheme(FrameworkElement): fusiona E3gTheme.xaml (colores E3g.Brush.*,
//     tipografia, estilos) en una pagina/control del panel acoplable.
//   - Apply(Window, subtitulo): convierte una ventana normal en ventana sin
//     bordes E3G: WindowChrome + Border exterior con esquinas redondeadas y
//     sombra + Border de recorte + E3gTitleBar arriba + el contenido original.
//     Misma estructura que espera E3gTitleBar (Content = Border > Border).
// ============================================================================
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.Saneamiento.UI
{
    public static class E3gChrome
    {
        private const string ThemeUri =
            "pack://application:,,,/e3gbimtools.DiTools.UI;component/Themes/E3gTheme.xaml";

        private const double ShadowMargin = 12;   // margen de la sombra (como DxfLegendWindow)
        private const double TitleBarExtra = 13;  // 44 (E3gTitleBar) - ~31 (caption nativo)

        /// <summary>Carga el tema E3G en una pagina o control (panel acoplable).</summary>
        public static void ApplyTheme(FrameworkElement element)
        {
            if (element == null) return;
            try
            {
                var uri = new Uri(ThemeUri, UriKind.Absolute);
                if (!element.Resources.MergedDictionaries.Any(d => d.Source != null && d.Source.Equals(uri)))
                    element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
            catch { /* sin tema antes que romper el panel */ }

            if (element is Control c && element.TryFindResource("E3g.Font.Family") is FontFamily ff)
                c.FontFamily = ff;
            else if (element is Page p && element.TryFindResource("E3g.Font.Family") is FontFamily pf)
                TextElement_SetFont(p, pf);
        }

        private static void TextElement_SetFont(Page p, FontFamily ff)
            => System.Windows.Documents.TextElement.SetFontFamily(p, ff);

        /// <summary>
        /// Aplica el chrome E3G a <paramref name="window"/>.  Llamar DESPUES de
        /// asignar el contenido (InitializeComponent o Content = ...) y ANTES de
        /// Show/ShowDialog.  <paramref name="subtitle"/> null = usa window.Title.
        /// </summary>
        public static void Apply(Window window, string subtitle = null)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (window.Content is Border b0 && b0.Name == "ChromeRoot") return; // ya aplicado

            bool resizable = window.ResizeMode == ResizeMode.CanResize
                          || window.ResizeMode == ResizeMode.CanResizeWithGrip;

            // Tamaño: margen de sombra + barra de titulo mas alta que la nativa.
            if (window.SizeToContent == SizeToContent.Manual)
            {
                if (!double.IsNaN(window.Width)) window.Width += 2 * ShadowMargin;
                if (!double.IsNaN(window.Height)) window.Height += 2 * ShadowMargin + TitleBarExtra;
            }
            if (window.MinWidth > 0) window.MinWidth += 2 * ShadowMargin;
            if (window.MinHeight > 0) window.MinHeight += 2 * ShadowMargin + TitleBarExtra;

            var content = window.Content as UIElement;
            window.Content = null;

            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            if (window.ResizeMode == ResizeMode.CanResizeWithGrip) window.ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 44,
                ResizeBorderThickness = resizable ? new Thickness(8) : new Thickness(0),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });

            E3gWindowBase.ApplyTheme(window);

            var root = new Border
            {
                Name = "ChromeRoot",
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(ShadowMargin),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.20 },
            };
            root.SetResourceReference(Border.BackgroundProperty, "E3g.Brush.Background");
            root.SetResourceReference(Border.BorderBrushProperty, "E3g.Brush.Border");

            var clip = new Border { Name = "ChromeClip", CornerRadius = new CornerRadius(9) };
            var mask = new Border { Background = Brushes.White };
            mask.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(Border.CornerRadius)) { Source = clip });
            mask.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = clip });
            mask.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(FrameworkElement.ActualHeight)) { Source = clip });
            clip.OpacityMask = new VisualBrush(mask);

            var titleBar = new E3gTitleBar
            {
                Title = "e3gbimtools",
                ShowStatus = false,
                ShowMinimize = false,
                ShowMaximize = resizable,
            };
            if (subtitle != null)
                titleBar.Subtitle = subtitle;
            else
                titleBar.SetBinding(E3gTitleBar.SubtitleProperty,
                    new Binding(nameof(Window.Title)) { Source = window });
            DockPanel.SetDock(titleBar, Dock.Top);

            // El cuerpo toma el fondo del tema (E3g.Brush.Background del Border raiz).
            var body = new Border { Child = content };

            var dock = new DockPanel { LastChildFill = true };
            dock.Children.Add(titleBar);
            dock.Children.Add(body);
            clip.Child = dock;
            root.Child = clip;
            window.Content = root;
        }
    }
}
