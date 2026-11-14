// ============================================================================
//  ReportBuilder.cs
//  Fluent API para construir un ReportData sin lidiar con los DTOs a pelo.
//
//  Uso típico:
//      var rpt = ReportBuilder.Create("Informe MEP — AFS")
//          .WithBranding(b => b.CompanyName = "E3G")
//          .WithCover(c => { c.ProjectName = "Edif. X"; c.Norm = "CTE DB-HS4"; })
//          .AddSection("Criterios", s => s
//              .AddParagraph("Cálculo según CTE DB-HS4 (2022).")
//              .AddCriterion("Velocidad máxima", "2.0", "m/s"))
//          .AddSection("Resultados por tramo", s => s.WithTable(t => …))
//          .AddSection("Esquema", s => s.AddImage(pngBytes, "Figura 1: esquema"))
//          .AddAnnex("A", "Datos de entrada", a => …)
//          .Build();
//
//      new PdfReportExporter().Export(rpt, "C:/temp/informe.pdf");
//
//  Diseño:
//    - Cada método devuelve `this` (o el sub-builder) para encadenar.
//    - Los lambdas evitan tener que crear los objetos sueltos.
//    - El Build() final devuelve un ReportData inmutable funcional (los DTOs
//      siguen siendo mutables pero ya no los toca nadie).
// ============================================================================
using System;
using System.Collections.Generic;

namespace E3G.Saneamiento.Reports
{
    public sealed class ReportBuilder
    {
        private readonly ReportData _data = new ReportData();

        private ReportBuilder() { }

        public static ReportBuilder Create(string title = null)
        {
            var b = new ReportBuilder();
            if (!string.IsNullOrEmpty(title)) b._data.Cover.Title = title;
            return b;
        }

        // ── Branding / Cover ──────────────────────────────────────────────

        public ReportBuilder WithBranding(Action<ReportBranding> configure)
        {
            configure?.Invoke(_data.Branding);
            return this;
        }

        public ReportBuilder WithCover(Action<ReportCover> configure)
        {
            configure?.Invoke(_data.Cover);
            return this;
        }

        // ── Secciones ──────────────────────────────────────────────────────

        public ReportBuilder AddSection(string title, Action<SectionBuilder> build = null)
        {
            var sec = new ReportSection { Title = title ?? "" };
            build?.Invoke(new SectionBuilder(sec));
            _data.Sections.Add(sec);
            return this;
        }

        // ── Anexos ────────────────────────────────────────────────────────

        public ReportBuilder AddAnnex(string letter, string title, Action<AnnexBuilder> build = null)
        {
            var ax = new ReportAnnex { Letter = letter ?? "A", Title = title ?? "" };
            build?.Invoke(new AnnexBuilder(ax));
            _data.Annexes.Add(ax);
            return this;
        }

        public ReportData Build() => _data;

        // ============================================================
        //  Sub-builders
        // ============================================================

        public sealed class SectionBuilder
        {
            private readonly ReportSection _s;
            internal SectionBuilder(ReportSection s) { _s = s; }

            public SectionBuilder WithNumbering(string numbering) { _s.Numbering = numbering ?? ""; return this; }
            public SectionBuilder PageBreakBefore(bool on = true) { _s.PageBreakBefore = on; return this; }

            public SectionBuilder AddParagraph(string text)
            {
                if (!string.IsNullOrEmpty(text)) _s.Paragraphs.Add(text);
                return this;
            }

            public SectionBuilder AddCriterion(string key, string value, string unit = "", string note = "")
            {
                _s.Criteria.Add(new ReportCriterion { Key = key, Value = value, Unit = unit, Note = note });
                return this;
            }

            public SectionBuilder WithTable(Action<TableBuilder> build)
            {
                if (_s.Table == null) _s.Table = new ReportTable();
                build?.Invoke(new TableBuilder(_s.Table));
                return this;
            }

            public SectionBuilder AddImage(byte[] bytes, string caption = "", float widthMm = 0, bool fullPage = false)
            {
                if (bytes != null && bytes.Length > 0)
                    _s.Images.Add(new ReportImage
                    {
                        Bytes = bytes, Caption = caption ?? "", WidthMm = widthMm, FullPage = fullPage
                    });
                return this;
            }

            public SectionBuilder WithCriticalPath(Action<CriticalPathBuilder> build)
            {
                if (_s.CriticalPath == null) _s.CriticalPath = new ReportCriticalPath();
                build?.Invoke(new CriticalPathBuilder(_s.CriticalPath));
                return this;
            }
        }

        public sealed class TableBuilder
        {
            private readonly ReportTable _t;
            internal TableBuilder(ReportTable t) { _t = t; }

            public TableBuilder AddColumn(string header, float weight = 1f, ReportAlign align = ReportAlign.Left)
            {
                _t.Columns.Add(new ReportColumn { Header = header, Weight = weight, Align = align });
                return this;
            }

            public TableBuilder AddRow(IEnumerable<string> cells,
                ReportRowSeverity sev = ReportRowSeverity.None, bool isFitting = false)
            {
                var row = new ReportTableRow { Severity = sev, IsFitting = isFitting };
                if (cells != null) row.Cells.AddRange(cells);
                _t.Rows.Add(row);
                return this;
            }

            public TableBuilder AddRow(params string[] cells) => AddRow(cells, ReportRowSeverity.None, false);

            public TableBuilder RepeatHeader(bool on = true) { _t.RepeatHeader = on; return this; }
        }

        public sealed class CriticalPathBuilder
        {
            private readonly ReportCriticalPath _cp;
            internal CriticalPathBuilder(ReportCriticalPath cp) { _cp = cp; }

            public CriticalPathBuilder WithSummary(string text) { _cp.Summary = text ?? ""; return this; }

            public CriticalPathBuilder AddStep(int order, string element, string size,
                string flowLps, string dpPa, string accPa, string note = "")
            {
                _cp.Steps.Add(new ReportCriticalPathStep
                {
                    Order = order, Element = element, Size = size,
                    FlowLps = flowLps, DpPa = dpPa, AccPa = accPa, Note = note,
                });
                return this;
            }
        }

        public sealed class AnnexBuilder
        {
            private readonly ReportAnnex _ax;
            internal AnnexBuilder(ReportAnnex ax) { _ax = ax; }

            public AnnexBuilder AddSection(string title, Action<SectionBuilder> build = null)
            {
                var sec = new ReportSection { Title = title ?? "" };
                build?.Invoke(new SectionBuilder(sec));
                _ax.Sections.Add(sec);
                return this;
            }
        }
    }
}
