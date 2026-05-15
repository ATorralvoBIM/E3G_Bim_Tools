// ============================================================================
//  CanvasSnapshot.cs
//  Helper para rasterizar un FrameworkElement WPF (Canvas, Grid…) a PNG.
//  El uso típico es: tras BuildAndRender en el SchematicPreviewWindow, llamar
//  CanvasSnapshot.RenderToPng(PreviewCanvas, dpi:200) para obtener los bytes
//  que se pasan al ReportBuilder.AddImage(...).
//
//  Rendimiento:
//    - RenderTargetBitmap fuerza el layout antes de pintar, así que el caller
//      debe asegurarse de que el árbol visual ya ha ido al menos una vez
//      por LayoutUpdated (con Window.Show() basta).
//    - 200 DPI da ~1500×1000 px para un Canvas de 200×130 mm — calidad de
//      impresión profesional sin disparar el tamaño del PDF/DOCX.
//    - PNG compresión de PngBitmapEncoder por defecto (sin params).
// ============================================================================
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace E3G.Saneamiento.Reports
{
    public static class CanvasSnapshot
    {
        /// <summary>
        /// Rasteriza el elemento dado a PNG.
        /// </summary>
        /// <param name="element">FrameworkElement con tamaño definido (ActualWidth/Height > 0).</param>
        /// <param name="dpi">DPI del bitmap resultante (96 = pantalla, 200 = impresión).</param>
        /// <param name="backgroundBrush">Brush de fondo aplicado (null = mantener actual).</param>
        public static byte[] RenderToPng(FrameworkElement element, double dpi = 200,
            Brush backgroundBrush = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            // Forzar measure+arrange en caso de elemento "headless".
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(new Size(element.DesiredSize.Width, element.DesiredSize.Height)));
            double w = element.ActualWidth  > 0 ? element.ActualWidth  : element.DesiredSize.Width;
            double h = element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height;
            if (w <= 0 || h <= 0)
                throw new InvalidOperationException("El elemento no tiene tamaño renderizable.");

            int pxW = (int)Math.Ceiling(w * dpi / 96.0);
            int pxH = (int)Math.Ceiling(h * dpi / 96.0);

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);

            if (backgroundBrush != null)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, w, h));
                rtb.Render(dv);
            }
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }

        /// <summary>Guarda el PNG directamente a disco.</summary>
        public static void SaveToFile(FrameworkElement element, string path, double dpi = 200)
        {
            var bytes = RenderToPng(element, dpi);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
        }
    }
}
