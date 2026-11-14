using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace E3G.BimTools.DiTools.UI.Controls;

/// <summary>
/// Helper estatico para aplicar el tema e3g a cualquier <see cref="Window"/>.
/// Se evita la herencia XAML entre ensamblados (que es fragil cuando el
/// hijo vive en .DiTools y el padre en .UI).  Las ventanas heredan de
/// <see cref="Window"/> normal y llaman a <see cref="ApplyTheme"/> en su
/// constructor.
/// </summary>
public static class E3gWindowBase
{
    private const string ThemeUri =
        "pack://application:,,,/e3gbimtools.DiTools.UI;component/Themes/E3gTheme.xaml";

    private const string ProductName = "e3gbimtools";

    /// <summary>Carga el tema e3g en la ventana y aplica defaults visuales.</summary>
    public static void ApplyTheme(Window window)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));

        // Intentar cargar el diccionario de tema. Si el pack URI falla en el contexto
        // de Revit la ventana se abre igual (sin tema) en lugar de crashear el proceso.
        try
        {
            var uri = new Uri(ThemeUri, UriKind.Absolute);
            var already = window.Resources.MergedDictionaries
                .Any(d => d.Source is not null && d.Source.Equals(uri));

            if (!already)
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }
        catch { /* fail gracefully */ }

        // Defaults visuales (independientes del tema).
        window.SnapsToDevicePixels = true;
        window.UseLayoutRounding = true;

        if (window.Resources["E3g.Font.Family"] is FontFamily family)
            window.FontFamily = family;

        // Las ventanas con chrome custom (AllowsTransparency=True) usan un
        // Background transparente y pintan el cuerpo con un Border interior.
        // No lo sobreescribimos o romperiamos la transparencia/sombra.
        if (!window.AllowsTransparency
            && window.Resources["E3g.Brush.Background"] is Brush bg)
            window.Background = bg;
    }

    /// <summary>
    /// Carga el tema e3g en un control que vive fuera de una ventana propia (p. ej.
    /// una vista dentro de un panel acoplable de otro modulo).
    /// </summary>
    public static void ApplyTheme(FrameworkElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (element is Window window) { ApplyTheme(window); return; }

        try
        {
            var uri = new Uri(ThemeUri, UriKind.Absolute);
            if (!element.Resources.MergedDictionaries.Any(d => d.Source is not null && d.Source.Equals(uri)))
                element.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }
        catch { /* fail gracefully */ }

        element.SnapsToDevicePixels = true;
        element.UseLayoutRounding = true;
        if (element.Resources["E3g.Font.Family"] is FontFamily family)
            System.Windows.Documents.TextElement.SetFontFamily(element, family);
    }

    /// <summary>Fija el titulo de la ventana en formato "<Producto> — <subtitulo>".</summary>
    public static void SetTitle(Window window, string subtitle)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        window.Title = string.IsNullOrWhiteSpace(subtitle) ? ProductName : $"{ProductName} — {subtitle}";
    }
}
