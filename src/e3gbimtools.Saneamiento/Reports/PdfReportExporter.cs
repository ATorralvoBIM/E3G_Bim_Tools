// ============================================================================
//  PdfReportExporter.cs
//  Implementación PDF del informe MEP usando QuestPDF.
//
//  Estructura del documento generado:
//    1. Portada — logo + título + metadatos del proyecto + revisión.
//    2. Sumario — un párrafo automático (no índice, demasiado meta para 1 doc).
//    3. Secciones — cada una con cabecera numerada, párrafos, criterios,
//       tabla paginada (cabecera repetida), imágenes con pie y, opcionalmente,
//       bloque de camino crítico.
//    4. Anexos — letra (A, B…) + secciones internas.
//  Footer en cada página: branding + paginación X/Y + fecha.
//
//  Rendimiento:
//    - QuestPDF hace layout en una sola pasada lazy.
//    - Las imágenes se pasan tal cual (PNG/JPG) — no se re-codifica.
//    - Para tablas muy largas la cabecera repite en cada página sin coste.
//    - Llamar QuestPDF.Settings.License = LicenseType.Community al arrancar
//      (lo hace este exporter la primera vez).
// ============================================================================
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace E3G.Saneamiento.Reports
{
    public sealed class PdfReportExporter : IReportExporter
    {
        public string FileExtension => "pdf";
        public string MimeType => "application/pdf";

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
        private static bool _licenseSet;

        public PdfReportExporter()
        {
            // Idempotente — se puede llamar muchas veces sin coste.
            if (!_licenseSet)
            {
                QuestPDF.Settings.License = LicenseType.Community;
                _licenseSet = true;
            }
        }

        public void Export(ReportData data, string outputPath)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException(nameof(outputPath));

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            BuildDocument(data).GeneratePdf(outputPath);
        }

        public void Export(ReportData data, Stream output)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (output == null) throw new ArgumentNullException(nameof(output));
            BuildDocument(data).GeneratePdf(output);
        }

        // ── Construcción del documento ────────────────────────────────────

        private IDocument BuildDocument(ReportData data)
        {
            var primary = ParseColor(data.Branding.PrimaryColor, Colors.Blue.Darken3);
            var accent  = ParseColor(data.Branding.AccentColor,  Colors.Red.Darken1);
            var muted   = ParseColor(data.Branding.MutedColor,   Colors.Grey.Medium);

            return Document.Create(container =>
            {
                // ── Portada (sin header/footer para que respire) ──────────
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20, Unit.Millimetre);
                    page.DefaultTextStyle(t => t.FontFamily(Fonts.Calibri).FontSize(11));
                    ComposeCover(page, data, primary, muted);
                });

                // ── Cuerpo: secciones ─────────────────────────────────────
                int secNumber = 0;
                foreach (var sec in data.Sections)
                {
                    secNumber++;
                    var sNum = string.IsNullOrEmpty(sec.Numbering) ? secNumber.ToString() : sec.Numbering;
                    container.Page(page =>
                    {
                        ConfigurePage(page, data, primary, muted);
                        page.Content().PaddingTop(2, Unit.Millimetre).Column(col =>
                        {
                            ComposeSection(col, sec, sNum, primary, accent, muted);
                        });
                    });
                }

                // ── Anexos ─────────────────────────────────────────────────
                foreach (var ax in data.Annexes)
                {
                    container.Page(page =>
                    {
                        ConfigurePage(page, data, primary, muted, isAnnex: true, annexLetter: ax.Letter);
                        page.Content().Column(col =>
                        {
                            col.Item().PaddingBottom(6).Text($"Anexo {ax.Letter}. {ax.Title}")
                                .FontSize(16).Bold().FontColor(primary);
                            int n = 0;
                            foreach (var s in ax.Sections)
                            {
                                n++;
                                ComposeSection(col, s, $"{ax.Letter}.{n}", primary, accent, muted);
                            }
                        });
                    });
                }
            });
        }

        // ── Portada ────────────────────────────────────────────────────────

        private static void ComposeCover(PageDescriptor page, ReportData data, string primary, string muted)
        {
            page.Content().Column(col =>
            {
                col.Spacing(8);

                // Logo + barra de color a la izquierda
                col.Item().Row(row =>
                {
                    if (data.Branding.LogoBytes != null && data.Branding.LogoBytes.Length > 0)
                        row.AutoItem().Height(40).Image(data.Branding.LogoBytes).FitHeight();
                    else
                        row.AutoItem().Text(data.Branding.CompanyName).FontSize(14).Bold().FontColor(primary);
                    row.RelativeItem();
                    row.AutoItem().AlignRight().Column(c =>
                    {
                        if (!string.IsNullOrEmpty(data.Branding.DocReference))
                            c.Item().Text($"Doc.: {data.Branding.DocReference}").FontSize(10).FontColor(muted);
                        c.Item().Text($"Rev. {data.Cover.Revision}").FontSize(10).FontColor(muted);
                        c.Item().Text(data.Cover.IssueDate.ToString("dd/MM/yyyy", Es))
                            .FontSize(10).FontColor(muted);
                    });
                });

                col.Item().PaddingTop(40).BorderTop(2).BorderColor(primary).PaddingTop(20);

                col.Item().Text(data.Cover.Title).FontSize(28).Bold().FontColor(primary);
                if (!string.IsNullOrEmpty(data.Cover.Subtitle))
                    col.Item().PaddingTop(2).Text(data.Cover.Subtitle).FontSize(14).FontColor(muted);

                // Imagen hero (opcional) — esquema de portada, render del modelo…
                if (data.Cover.HeroImage != null && data.Cover.HeroImage.Length > 0)
                    col.Item().PaddingVertical(18).Image(data.Cover.HeroImage).FitWidth();

                col.Item().PaddingTop(20).Element(e => CoverMeta(e, data, primary, muted));

                col.Item().PaddingTop(40).BorderTop(1).BorderColor(muted).PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text(data.Branding.CompanyName).FontSize(11).Bold().FontColor(primary);
                        if (!string.IsNullOrEmpty(data.Branding.CompanyCif))
                            c.Item().Text(data.Branding.CompanyCif).FontSize(9).FontColor(muted);
                        if (!string.IsNullOrEmpty(data.Branding.CompanyWeb))
                            c.Item().Text(data.Branding.CompanyWeb).FontSize(9).FontColor(muted);
                    });
                    r.AutoItem().AlignBottom().Text(data.Cover.Norm).FontSize(9).FontColor(muted);
                });
            });
        }

        private static void CoverMeta(IContainer container, ReportData data, string primary, string muted)
        {
            container.Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.ConstantColumn(140); cd.RelativeColumn(); });

                void Row(string k, string v)
                {
                    if (string.IsNullOrEmpty(v)) return;
                    t.Cell().PaddingVertical(2).Text(k).FontColor(muted);
                    t.Cell().PaddingVertical(2).Text(v).Bold().FontColor(primary);
                }

                Row("Proyecto", data.Cover.ProjectName);
                Row("Código", data.Cover.ProjectCode);
                Row("Cliente", data.Cover.ClientName);
                Row("Emplazamiento", data.Cover.LocationStr);
                Row("Autor", data.Cover.AuthorName);
                Row("Cargo", data.Cover.AuthorRole);
                Row("Norma aplicable", data.Cover.Norm);
            });
        }

        // ── Cabecera/pie comunes en páginas no-portada ────────────────────

        private static void ConfigurePage(PageDescriptor page, ReportData data,
            string primary, string muted, bool isAnnex = false, string annexLetter = "")
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontFamily(Fonts.Calibri).FontSize(10));

            page.Header().Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text(data.Cover.Title).FontSize(9).Bold().FontColor(primary);
                    if (!string.IsNullOrEmpty(data.Cover.ProjectName))
                        c.Item().Text(data.Cover.ProjectName).FontSize(8).FontColor(muted);
                });
                if (data.Branding.LogoBytes != null && data.Branding.LogoBytes.Length > 0)
                    r.AutoItem().Height(20).Image(data.Branding.LogoBytes).FitHeight();
            });

            page.Footer().BorderTop(0.5f).BorderColor(muted).PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span(data.Branding.CompanyName).FontSize(8).FontColor(muted);
                    if (!string.IsNullOrEmpty(data.Branding.FooterText))
                        text.Span(" · " + data.Branding.FooterText).FontSize(8).FontColor(muted);
                });
                row.AutoItem().Text(t =>
                {
                    t.Span("Página ").FontSize(8).FontColor(muted);
                    t.CurrentPageNumber().FontSize(8).FontColor(muted);
                    t.Span(" / ").FontSize(8).FontColor(muted);
                    t.TotalPages().FontSize(8).FontColor(muted);
                });
            });
        }

        // ── Sección ────────────────────────────────────────────────────────

        private static void ComposeSection(ColumnDescriptor col, ReportSection sec, string number,
            string primary, string accent, string muted)
        {
            col.Item().PaddingTop(4).PaddingBottom(2).BorderBottom(1).BorderColor(primary)
                .Text(string.IsNullOrEmpty(number) ? sec.Title : $"{number}.  {sec.Title}")
                .FontSize(14).Bold().FontColor(primary);

            foreach (var p in sec.Paragraphs)
                col.Item().PaddingTop(4).Text(p).Justify();

            if (sec.Criteria != null && sec.Criteria.Count > 0)
                col.Item().PaddingTop(6).Element(e => RenderCriteria(e, sec.Criteria, primary, muted));

            if (sec.Table != null && sec.Table.Columns.Count > 0)
                col.Item().PaddingTop(6).Element(e => RenderTable(e, sec.Table, primary, accent, muted));

            foreach (var img in sec.Images)
                col.Item().PaddingTop(8).Element(e => RenderImage(e, img, muted));

            if (sec.CriticalPath != null)
                col.Item().PaddingTop(8).Element(e => RenderCriticalPath(e, sec.CriticalPath, primary, accent, muted));
        }

        private static void RenderCriteria(IContainer container, System.Collections.Generic.List<ReportCriterion> list,
            string primary, string muted)
        {
            container.Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2);  // Key
                    cd.RelativeColumn(1);  // Value
                    cd.RelativeColumn(1);  // Unit
                    cd.RelativeColumn(3);  // Note
                });
                t.Cell().Background(primary).Padding(4).Text("Criterio").FontColor(Colors.White).Bold();
                t.Cell().Background(primary).Padding(4).Text("Valor").FontColor(Colors.White).Bold().AlignRight();
                t.Cell().Background(primary).Padding(4).Text("Ud").FontColor(Colors.White).Bold();
                t.Cell().Background(primary).Padding(4).Text("Nota").FontColor(Colors.White).Bold();

                foreach (var c in list)
                {
                    t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(4).Text(c.Key ?? "");
                    t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(4).AlignRight().Text(c.Value ?? "").Bold();
                    t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(4).Text(c.Unit ?? "").FontColor(muted);
                    t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(4).Text(c.Note ?? "").FontColor(muted);
                }
            });
        }

        private static void RenderTable(IContainer container, ReportTable tbl,
            string primary, string accent, string muted)
        {
            container.Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    foreach (var c in tbl.Columns)
                        cd.RelativeColumn(c.Weight <= 0 ? 1f : c.Weight);
                });

                // Cabecera (se repite en cada página si RepeatHeader)
                if (tbl.RepeatHeader)
                {
                    t.Header(h =>
                    {
                        foreach (var c in tbl.Columns)
                            h.Cell().Background(primary).Padding(3).Text(c.Header ?? "")
                                .FontColor(Colors.White).Bold().FontSize(9);
                    });
                }
                else
                {
                    foreach (var c in tbl.Columns)
                        t.Cell().Background(primary).Padding(3).Text(c.Header ?? "")
                            .FontColor(Colors.White).Bold().FontSize(9);
                }

                foreach (var row in tbl.Rows)
                {
                    string bg = SeverityBg(row.Severity);
                    for (int i = 0; i < tbl.Columns.Count; i++)
                    {
                        var cellText = i < row.Cells.Count ? (row.Cells[i] ?? "") : "";
                        var align = tbl.Columns[i].Align;
                        IContainer cell = t.Cell()
                            .BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(2);
                        if (bg != null) cell = cell.Background(bg);
                        cell = align switch
                        {
                            ReportAlign.Right  => cell.AlignRight(),
                            ReportAlign.Center => cell.AlignCenter(),
                            _ => cell,
                        };
                        cell.Text(text =>
                        {
                            var span = text.Span(cellText).FontSize(9);
                            if (row.IsFitting) span.Italic().FontColor(muted);
                        });
                    }
                }
            });
        }

        private static string SeverityBg(ReportRowSeverity sev)
        {
            switch (sev)
            {
                case ReportRowSeverity.Ok:      return Colors.Green.Lighten5;
                case ReportRowSeverity.Warning: return Colors.Amber.Lighten5;
                case ReportRowSeverity.Error:   return Colors.Red.Lighten5;
                default: return null;
            }
        }

        private static void RenderImage(IContainer container, ReportImage img, string muted)
        {
            container.Column(c =>
            {
                if (img.WidthMm > 0)
                    c.Item().Width(img.WidthMm, Unit.Millimetre).Image(img.Bytes).FitWidth();
                else
                    c.Item().Image(img.Bytes).FitWidth();
                if (!string.IsNullOrEmpty(img.Caption))
                    c.Item().PaddingTop(2).Text(img.Caption).FontSize(8).Italic().FontColor(muted).AlignCenter();
            });
        }

        private static void RenderCriticalPath(IContainer container, ReportCriticalPath cp,
            string primary, string accent, string muted)
        {
            container.Column(col =>
            {
                if (!string.IsNullOrEmpty(cp.Summary))
                    col.Item().PaddingBottom(4).Text(cp.Summary).Bold().FontColor(accent);

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(28);  // #
                        cd.RelativeColumn(3);   // Elemento
                        cd.RelativeColumn(1);   // Talla
                        cd.RelativeColumn(1);   // Q [L/s]
                        cd.RelativeColumn(1);   // ΔP [Pa]
                        cd.RelativeColumn(1);   // ΔP acum [Pa]
                        cd.RelativeColumn(2);   // Nota
                    });
                    t.Header(h =>
                    {
                        string[] headers = { "#", "Elemento", "Talla", "Q [L/s]", "ΔP [Pa]", "ΔP acum", "Nota" };
                        foreach (var hdr in headers)
                            h.Cell().Background(accent).Padding(3).Text(hdr)
                                .FontColor(Colors.White).Bold().FontSize(9);
                    });
                    foreach (var s in cp.Steps)
                    {
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.Order.ToString()).FontSize(9);
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.Element ?? "").FontSize(9);
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.Size ?? "").FontSize(9);
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.FlowLps ?? "").FontSize(9);
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.DpPa ?? "").FontSize(9);
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.AccPa ?? "").FontSize(9).Bold();
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(s.Note ?? "").FontSize(8).FontColor(muted);
                    }
                });
            });
        }

        private static string ParseColor(string hex, string fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 6) return "#" + h.ToUpperInvariant();
            return fallback;
        }
    }
}
