// ============================================================================
//  LibraryReportDialog.xaml.cs
//  Dialog modal que muestra el resultado detallado de una carga masiva de
//  familias .rfa: archivo, estado (Ok/Aviso/Error), categoría detectada y
//  mensaje.  Se invoca desde la ventana host cuando el ViewModel emite un
//  WarningRaised con items > 1.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace E3G.Saneamiento.UI.Schematic
{
    public partial class LibraryReportDialog : Window
    {
        public sealed class ReportItem
        {
            public string FileName { get; set; }
            public string StatusText { get; set; }
            public string Category { get; set; }
            public string Message { get; set; }
            public Brush StatusBg { get; set; }
            public Brush StatusFg { get; set; }
        }

        public LibraryReportDialog(string title, string summary, IEnumerable<ReportItem> items)
        {
            InitializeComponent();
            LblTitle.Text = title ?? "Informe";
            LblSummary.Text = summary ?? "";
            DgItems.ItemsSource = (items ?? Array.Empty<ReportItem>()).ToList();
        }

        /// <summary>Helper que convierte una lista de FamilyValidationResult
        /// en items con coloreado de estado.</summary>
        public static List<ReportItem> BuildItems(
            IEnumerable<(string path, FamilyValidationResult result)> entries)
        {
            var list = new List<ReportItem>();
            foreach (var (path, r) in entries)
            {
                var item = new ReportItem
                {
                    FileName = Path.GetFileName(path),
                    Category = r?.DetectedCategory ?? "—",
                    Message  = r?.Message ?? "",
                };
                switch (r?.Kind)
                {
                    case FamilyValidationKind.Ok:
                        item.StatusText = "OK";
                        item.StatusBg = new SolidColorBrush(Color.FromRgb(0xE6, 0xF4, 0xEA));
                        item.StatusFg = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
                        break;
                    case FamilyValidationKind.WrongCategory:
                        item.StatusText = "AVISO";
                        item.StatusBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0));
                        item.StatusFg = new SolidColorBrush(Color.FromRgb(0xE6, 0x5C, 0x00));
                        break;
                    case FamilyValidationKind.CannotRead:
                        item.StatusText = "ERROR";
                        item.StatusBg = new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7));
                        item.StatusFg = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));
                        break;
                    default:
                        item.StatusText = "DIFERIDO";
                        item.StatusBg = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
                        item.StatusFg = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                        break;
                }
                list.Add(item);
            }
            return list;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            var items = DgItems.ItemsSource as IEnumerable<ReportItem>;
            if (items == null) return;
            var sb = new StringBuilder();
            sb.AppendLine(LblTitle.Text);
            sb.AppendLine(LblSummary.Text);
            sb.AppendLine();
            sb.AppendLine("Archivo\tResultado\tCategoría\tMensaje");
            foreach (var it in items)
                sb.AppendLine($"{it.FileName}\t{it.StatusText}\t{it.Category}\t{it.Message}");
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
