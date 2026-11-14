// ============================================================================
//  DocxReportExporter.cs
//  Implementación DOCX del informe MEP usando OpenXML SDK puro.
//
//  Elección de diseño:
//    - Construimos un solo MainDocumentPart con párrafos / tablas; sin estilos
//      externos (los estilos se aplican inline para evitar el subdocumento
//      styles.xml — más simple y robusto para un primer uso).
//    - Las imágenes se embeben como ImagePart con relación al MainDocument.
//    - La paginación de tablas se delega a Word — basta con RepeatHeaderRow.
//
//  Limitaciones conocidas:
//    - No genera índice automático; podría añadirse con SDT (TOC field) si
//      se necesita en futuro.
//    - Footer simple (texto centrado + paginación) sin tabla.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace E3G.Saneamiento.Reports
{
    public sealed class DocxReportExporter : IReportExporter
    {
        public string FileExtension => "docx";
        public string MimeType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        public void Export(ReportData data, string outputPath)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException(nameof(outputPath));
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            using (var fs = File.Create(outputPath))
                Export(data, fs);
        }

        public void Export(ReportData data, Stream output)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (output == null) throw new ArgumentNullException(nameof(output));

            using (var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new W.Document(new W.Body());
                var body = main.Document.Body;

                BuildCover(main, body, data);
                BuildSectionsBlock(main, body, data);
                BuildAnnexesBlock(main, body, data);
                BuildFooter(main, body, data);

                main.Document.Save();
            }
        }

        // ── Portada ────────────────────────────────────────────────────────

        private static void BuildCover(MainDocumentPart main, W.Body body, ReportData data)
        {
            if (data.Branding.LogoBytes != null && data.Branding.LogoBytes.Length > 0)
                body.AppendChild(MakeImage(main, data.Branding.LogoBytes, 60, 20));

            body.AppendChild(SpacerParagraph(60));
            body.AppendChild(Heading(data.Cover.Title, 36, true, data.Branding.PrimaryColor));
            if (!string.IsNullOrEmpty(data.Cover.Subtitle))
                body.AppendChild(Heading(data.Cover.Subtitle, 20, false, "888888"));

            if (data.Cover.HeroImage != null && data.Cover.HeroImage.Length > 0)
            {
                body.AppendChild(SpacerParagraph(20));
                body.AppendChild(MakeImage(main, data.Cover.HeroImage, 160, 90));
            }

            body.AppendChild(SpacerParagraph(40));
            body.AppendChild(CoverMetaTable(data));
            body.AppendChild(SpacerParagraph(40));
            body.AppendChild(SimpleParagraph(data.Branding.CompanyName, 22, bold: true,
                color: data.Branding.PrimaryColor));
            if (!string.IsNullOrEmpty(data.Branding.CompanyWeb))
                body.AppendChild(SimpleParagraph(data.Branding.CompanyWeb, 18, color: "888888"));
            if (!string.IsNullOrEmpty(data.Cover.Norm))
                body.AppendChild(SimpleParagraph("Norma aplicable: " + data.Cover.Norm, 18, color: "888888"));
            body.AppendChild(PageBreak());
        }

        private static W.Table CoverMetaTable(ReportData data)
        {
            var t = new W.Table(
                new W.TableProperties(
                    new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
                    new W.TableBorders(
                        new W.TopBorder    { Val = W.BorderValues.None },
                        new W.BottomBorder { Val = W.BorderValues.None },
                        new W.LeftBorder   { Val = W.BorderValues.None },
                        new W.RightBorder  { Val = W.BorderValues.None },
                        new W.InsideHorizontalBorder { Val = W.BorderValues.None },
                        new W.InsideVerticalBorder   { Val = W.BorderValues.None })));

            void Row(string k, string v)
            {
                if (string.IsNullOrEmpty(v)) return;
                t.AppendChild(new W.TableRow(
                    Cell(k, weight: 30, color: "555555"),
                    Cell(v, weight: 70, bold: true, color: data.Branding.PrimaryColor)));
            }

            Row("Proyecto",       data.Cover.ProjectName);
            Row("Código",         data.Cover.ProjectCode);
            Row("Cliente",        data.Cover.ClientName);
            Row("Emplazamiento",  data.Cover.LocationStr);
            Row("Autor",          data.Cover.AuthorName);
            Row("Cargo",          data.Cover.AuthorRole);
            Row("Revisión",       data.Cover.Revision);
            Row("Fecha",          data.Cover.IssueDate.ToString("dd/MM/yyyy", Es));
            Row("Doc. ref.",      data.Branding.DocReference);
            return t;
        }

        // ── Secciones ──────────────────────────────────────────────────────

        private static void BuildSectionsBlock(MainDocumentPart main, W.Body body, ReportData data)
        {
            int n = 0;
            foreach (var sec in data.Sections)
            {
                n++;
                var num = string.IsNullOrEmpty(sec.Numbering) ? n.ToString() : sec.Numbering;
                if (sec.PageBreakBefore) body.AppendChild(PageBreak());
                ComposeSection(main, body, sec, num, data.Branding.PrimaryColor, data.Branding.AccentColor);
            }
        }

        private static void BuildAnnexesBlock(MainDocumentPart main, W.Body body, ReportData data)
        {
            foreach (var ax in data.Annexes)
            {
                body.AppendChild(PageBreak());
                body.AppendChild(Heading($"Anexo {ax.Letter}. {ax.Title}", 32, true, data.Branding.PrimaryColor));
                int n = 0;
                foreach (var s in ax.Sections)
                {
                    n++;
                    ComposeSection(main, body, s, $"{ax.Letter}.{n}",
                        data.Branding.PrimaryColor, data.Branding.AccentColor);
                }
            }
        }

        private static void ComposeSection(MainDocumentPart main, W.Body body, ReportSection sec,
            string number, string primary, string accent)
        {
            body.AppendChild(Heading($"{number}.  {sec.Title}", 26, true, primary));

            foreach (var p in sec.Paragraphs)
                body.AppendChild(SimpleParagraph(p, 20, justify: true));

            if (sec.Criteria != null && sec.Criteria.Count > 0)
                body.AppendChild(BuildCriteriaTable(sec.Criteria, primary));

            if (sec.Table != null && sec.Table.Columns.Count > 0)
                body.AppendChild(BuildDataTable(sec.Table, primary));

            foreach (var img in sec.Images)
            {
                body.AppendChild(MakeImage(main, img.Bytes,
                    img.WidthMm > 0 ? img.WidthMm : 160f, 0));
                if (!string.IsNullOrEmpty(img.Caption))
                    body.AppendChild(SimpleParagraph(img.Caption, 16, italic: true, color: "888888", center: true));
            }

            if (sec.CriticalPath != null)
                body.AppendChild(BuildCriticalPathTable(sec.CriticalPath, accent));
        }

        // ── Tablas concretas ───────────────────────────────────────────────

        private static W.Table BuildCriteriaTable(List<ReportCriterion> list, string primary)
        {
            var t = MakeTable(new[] { 2400f, 1200f, 800f, 4600f });
            t.AppendChild(HeaderRow(primary, "Criterio", "Valor", "Ud", "Nota"));
            foreach (var c in list)
                t.AppendChild(DataRow(c.Key, c.Value, c.Unit, c.Note));
            return t;
        }

        private static W.Table BuildDataTable(ReportTable tbl, string primary)
        {
            float total = tbl.Columns.Sum(c => c.Weight <= 0 ? 1f : c.Weight);
            float[] widths = tbl.Columns.Select(c => (c.Weight <= 0 ? 1f : c.Weight) / total * 9000f).ToArray();
            var t = MakeTable(widths);

            // Cabecera repetible
            var headerRow = HeaderRow(primary, tbl.Columns.Select(c => c.Header).ToArray());
            if (tbl.RepeatHeader)
                headerRow.PrependChild(new W.TableRowProperties(new W.TableHeader()));
            t.AppendChild(headerRow);

            foreach (var row in tbl.Rows)
            {
                var cells = new List<string>(row.Cells);
                while (cells.Count < tbl.Columns.Count) cells.Add("");
                t.AppendChild(DataRow(row.Severity, row.IsFitting,
                    Enumerable.Range(0, tbl.Columns.Count)
                              .Select(i => (cell: cells[i], align: tbl.Columns[i].Align))
                              .ToArray()));
            }
            return t;
        }

        private static W.Table BuildCriticalPathTable(ReportCriticalPath cp, string accent)
        {
            var t = MakeTable(new[] { 500f, 3000f, 1000f, 1000f, 1000f, 1000f, 2000f });
            t.AppendChild(HeaderRow(accent, "#", "Elemento", "Talla",
                "Q [L/s]", "ΔP [Pa]", "ΔP acum", "Nota"));
            foreach (var s in cp.Steps)
                t.AppendChild(DataRow(s.Order.ToString(), s.Element, s.Size,
                    s.FlowLps, s.DpPa, s.AccPa, s.Note));
            return t;
        }

        // ── Footer ─────────────────────────────────────────────────────────

        private static void BuildFooter(MainDocumentPart main, W.Body body, ReportData data)
        {
            var footerPart = main.AddNewPart<FooterPart>("rFooter");
            var footer = new W.Footer(
                new W.Paragraph(
                    new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }),
                    new W.Run(new W.Text(
                        $"{data.Branding.CompanyName}  ·  {data.Cover.Title}") {
                            Space = SpaceProcessingModeValues.Preserve }),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
                    new W.Run(new W.FieldCode("PAGE")),
                    new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }))
            );
            footerPart.Footer = footer;

            var sectPr = new W.SectionProperties(
                new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = "rFooter" },
                new W.PageSize { Width = 11906, Height = 16838 },         // A4
                new W.PageMargin { Top = 1134, Right = 1134, Bottom = 1418, Left = 1134, Header = 720, Footer = 720, Gutter = 0 }
            );
            body.AppendChild(sectPr);
        }

        // ── Helpers de construcción ────────────────────────────────────────

        private static W.Paragraph PageBreak() =>
            new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page }));

        private static W.Paragraph SpacerParagraph(int sizeHalfPt) =>
            new W.Paragraph(new W.ParagraphProperties(
                new W.SpacingBetweenLines { After = sizeHalfPt.ToString() }));

        private static W.Paragraph Heading(string text, int sizeHalfPt, bool bold, string colorHex)
        {
            var run = new W.Run();
            var props = new W.RunProperties(
                new W.FontSize { Val = sizeHalfPt.ToString() },
                new W.RunFonts { Ascii = "Calibri" });
            if (bold) props.AppendChild(new W.Bold());
            if (!string.IsNullOrEmpty(colorHex))
                props.AppendChild(new W.Color { Val = colorHex.TrimStart('#') });
            run.AppendChild(props);
            run.AppendChild(new W.Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve });
            return new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "120", After = "80" }),
                run);
        }

        private static W.Paragraph SimpleParagraph(string text, int sizeHalfPt = 20,
            bool bold = false, bool italic = false, string color = null,
            bool justify = false, bool center = false)
        {
            var run = new W.Run();
            var props = new W.RunProperties(
                new W.FontSize { Val = sizeHalfPt.ToString() },
                new W.RunFonts { Ascii = "Calibri" });
            if (bold) props.AppendChild(new W.Bold());
            if (italic) props.AppendChild(new W.Italic());
            if (!string.IsNullOrEmpty(color))
                props.AppendChild(new W.Color { Val = color.TrimStart('#') });
            run.AppendChild(props);
            run.AppendChild(new W.Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve });

            var pPr = new W.ParagraphProperties();
            if (justify) pPr.AppendChild(new W.Justification { Val = W.JustificationValues.Both });
            else if (center) pPr.AppendChild(new W.Justification { Val = W.JustificationValues.Center });
            return new W.Paragraph(pPr, run);
        }

        private static W.Table MakeTable(float[] columnWidthsTwips)
        {
            var t = new W.Table(
                new W.TableProperties(
                    new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
                    new W.TableBorders(
                        new W.TopBorder    { Val = W.BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new W.LeftBorder   { Val = W.BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new W.RightBorder  { Val = W.BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 2, Color = "E0E0E0" },
                        new W.InsideVerticalBorder   { Val = W.BorderValues.Single, Size = 2, Color = "E0E0E0" })));
            var grid = new W.TableGrid();
            foreach (var w in columnWidthsTwips)
                grid.AppendChild(new W.GridColumn { Width = ((int)w).ToString() });
            t.AppendChild(grid);
            return t;
        }

        private static W.TableRow HeaderRow(string bgHex, params string[] cells)
        {
            var row = new W.TableRow();
            foreach (var c in cells)
                row.AppendChild(Cell(c, bold: true, color: "FFFFFF", bgHex: bgHex));
            return row;
        }

        private static W.TableRow DataRow(params string[] cells)
        {
            var row = new W.TableRow();
            foreach (var c in cells) row.AppendChild(Cell(c));
            return row;
        }

        private static W.TableRow DataRow(ReportRowSeverity sev, bool isFitting,
            (string cell, ReportAlign align)[] cells)
        {
            string bg = sev switch
            {
                ReportRowSeverity.Ok      => "E6F4EA",
                ReportRowSeverity.Warning => "FFF7E0",
                ReportRowSeverity.Error   => "FDECEC",
                _ => null,
            };
            var row = new W.TableRow();
            foreach (var pair in cells)
            {
                var align = pair.align == ReportAlign.Right  ? W.JustificationValues.Right :
                            pair.align == ReportAlign.Center ? W.JustificationValues.Center :
                            W.JustificationValues.Left;
                row.AppendChild(Cell(pair.cell ?? "", italic: isFitting,
                    color: isFitting ? "888888" : null, bgHex: bg, align: align));
            }
            return row;
        }

        private static W.TableCell Cell(string text, int weight = 0,
            bool bold = false, bool italic = false, string color = null, string bgHex = null,
            W.JustificationValues? align = null)
        {
            var run = new W.Run();
            var rp = new W.RunProperties(new W.FontSize { Val = "18" }, new W.RunFonts { Ascii = "Calibri" });
            if (bold)   rp.AppendChild(new W.Bold());
            if (italic) rp.AppendChild(new W.Italic());
            if (!string.IsNullOrEmpty(color))
                rp.AppendChild(new W.Color { Val = color.TrimStart('#') });
            run.AppendChild(rp);
            run.AppendChild(new W.Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve });

            var pPr = new W.ParagraphProperties();
            if (align.HasValue) pPr.AppendChild(new W.Justification { Val = align.Value });
            var para = new W.Paragraph(pPr, run);

            var cellProps = new W.TableCellProperties();
            if (weight > 0)
                cellProps.AppendChild(new W.TableCellWidth { Width = weight.ToString(), Type = W.TableWidthUnitValues.Pct });
            if (!string.IsNullOrEmpty(bgHex))
                cellProps.AppendChild(new W.Shading
                {
                    Val = W.ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = bgHex.TrimStart('#'),
                });
            return new W.TableCell(cellProps, para);
        }

        // ── Imágenes embebidas ─────────────────────────────────────────────

        private static W.Paragraph MakeImage(MainDocumentPart main, byte[] imageBytes,
            float widthMm, float heightMm)
        {
            var imgPart = main.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(imageBytes)) imgPart.FeedData(ms);
            var relId = main.GetIdOfPart(imgPart);

            // Si no se da altura, intentar respetar aspecto leyendo PNG header.
            if (heightMm <= 0)
                heightMm = EstimateHeightMm(imageBytes, widthMm);

            long cx = (long)(widthMm * 36000);
            long cy = (long)(heightMm * 36000);

            var element = new W.Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cx, Cy = cy },
                    new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                    new DW.DocProperties { Id = 1U, Name = "img" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "img.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

            return new W.Paragraph(
                new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }),
                new W.Run(element));
        }

        private static float EstimateHeightMm(byte[] pngBytes, float widthMm)
        {
            // PNG signature + IHDR (offset 16: width, offset 20: height — big-endian).
            try
            {
                if (pngBytes.Length < 24 ||
                    pngBytes[0] != 0x89 || pngBytes[1] != 0x50) return widthMm * 0.6f;
                int w = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
                int h = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
                if (w <= 0) return widthMm * 0.6f;
                return widthMm * h / (float)w;
            }
            catch { return widthMm * 0.6f; }
        }
    }
}
