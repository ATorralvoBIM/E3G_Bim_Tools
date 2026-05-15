// ============================================================================
//  CteHs4Template.cs
//  Plantilla parametrizable para informes de cálculo MEP que sigue la
//  estructura típica exigida en proyectos españoles:
//
//    1. Objeto del informe
//    2. Normativa de aplicación (CTE DB-HS4 / RITE / UNE-149201…)
//    3. Datos de partida (sistema, fluido, temperatura, presión de suministro…)
//    4. Criterios de cálculo (velocidad máx., pérdida unitaria, simultaneidad…)
//    5. Resultados por tramo (tabla)
//    6. Esquema de principio (imagen del Canvas WPF)
//    7. Camino crítico
//    8. Conclusiones
//    9. Anexos
//
//  La plantilla NO genera el PDF/DOCX por sí misma — construye un ReportData
//  con secciones precargadas que el caller puede modificar antes de pasar al
//  Exporter.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using E3G.Saneamiento.RevitBridge;   // ReportRow / ReportMetadata (existentes)

namespace E3G.Saneamiento.Reports
{
    public static class CteHs4Template
    {
        private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Parámetros de entrada para la plantilla.</summary>
        public sealed class Inputs
        {
            public ReportMetadata Meta { get; set; }
            public IList<ReportRow> Rows { get; set; }
            /// <summary>PNG del esquema (CanvasSnapshot.RenderToPng).</summary>
            public byte[] SchemaPng { get; set; }
            /// <summary>Camino crítico ya formateado (orden de aguas-abajo).</summary>
            public IList<ReportCriticalPathStep> CriticalSteps { get; set; }
            public string CriticalSummary { get; set; } = "";
            /// <summary>Branding inyectado (logo, colores, web…).</summary>
            public ReportBranding Branding { get; set; }
            /// <summary>Datos de portada (autor, proyecto…).</summary>
            public ReportCover Cover { get; set; }
        }

        /// <summary>
        /// Construye un ReportData estilo CTE-HS4 / RITE a partir de los
        /// datos de un cálculo existente.
        /// </summary>
        public static ReportData Build(Inputs inp)
        {
            if (inp == null) throw new ArgumentNullException(nameof(inp));
            var meta = inp.Meta ?? new ReportMetadata();

            var rb = ReportBuilder.Create(inp.Cover?.Title ?? "Informe de cálculo MEP")
                .WithBranding(b =>
                {
                    if (inp.Branding == null) return;
                    b.CompanyName  = inp.Branding.CompanyName  ?? b.CompanyName;
                    b.CompanyWeb   = inp.Branding.CompanyWeb   ?? b.CompanyWeb;
                    b.CompanyCif   = inp.Branding.CompanyCif   ?? b.CompanyCif;
                    b.LogoBytes    = inp.Branding.LogoBytes    ?? b.LogoBytes;
                    b.PrimaryColor = inp.Branding.PrimaryColor ?? b.PrimaryColor;
                    b.AccentColor  = inp.Branding.AccentColor  ?? b.AccentColor;
                    b.FooterText   = inp.Branding.FooterText   ?? b.FooterText;
                    b.DocReference = inp.Branding.DocReference ?? b.DocReference;
                })
                .WithCover(c =>
                {
                    if (inp.Cover != null)
                    {
                        c.Title       = inp.Cover.Title       ?? c.Title;
                        c.Subtitle    = inp.Cover.Subtitle    ?? c.Subtitle;
                        c.ProjectName = inp.Cover.ProjectName ?? c.ProjectName;
                        c.ProjectCode = inp.Cover.ProjectCode ?? c.ProjectCode;
                        c.ClientName  = inp.Cover.ClientName  ?? c.ClientName;
                        c.LocationStr = inp.Cover.LocationStr ?? c.LocationStr;
                        c.AuthorName  = inp.Cover.AuthorName  ?? c.AuthorName;
                        c.AuthorRole  = inp.Cover.AuthorRole  ?? c.AuthorRole;
                        c.Revision    = inp.Cover.Revision    ?? c.Revision;
                        c.IssueDate   = inp.Cover.IssueDate;
                    }
                    if (string.IsNullOrEmpty(c.Subtitle))
                        c.Subtitle = $"{meta.Domain}  ·  Sistema: {meta.SystemName}";
                    if (string.IsNullOrEmpty(c.Norm))
                        c.Norm = meta.Domain.StartsWith("Cond", StringComparison.OrdinalIgnoreCase)
                            ? "CTE DB-HE / RITE"
                            : "CTE DB-HS4";
                });

            // ── 1. Objeto del informe ─────────────────────────────────────
            rb.AddSection("Objeto del informe", s => s
                .WithNumbering("1")
                .AddParagraph(
                    $"El presente documento recoge el dimensionado hidráulico/aeráulico " +
                    $"del sistema «{meta.SystemName}» del proyecto " +
                    $"{(string.IsNullOrEmpty(meta.ProjectName) ? "—" : meta.ProjectName)}, " +
                    $"realizado conforme a la normativa de aplicación citada en el apartado 2.")
                .AddParagraph(
                    "El motor de cálculo recorre la red por BFS desde la acometida " +
                    "hasta cada terminal, aplica las pérdidas continuas y singulares " +
                    "y compara el resultado con los umbrales del criterio elegido."));

            // ── 2. Normativa ──────────────────────────────────────────────
            rb.AddSection("Normativa de aplicación", s => s
                .WithNumbering("2")
                .AddParagraph("Se han considerado las siguientes referencias normativas:")
                .AddCriterion("CTE DB-HS4",      "Suministro de agua",          "", "Documento Básico — Salubridad")
                .AddCriterion("UNE-149201:2017", "Dimensionado por simultaneidad", "", "Coeficientes K")
                .AddCriterion("RITE",            "Reglamento Inst. Térmicas",   "", "Aplicable a redes de climatización"));

            // ── 3. Datos de partida ───────────────────────────────────────
            rb.AddSection("Datos de partida", s =>
            {
                s.WithNumbering("3")
                 .AddCriterion("Dominio",      meta.Domain    ?? "", "");
                AddIf(s, "Sistema MEP",        meta.SystemName);
                AddIf(s, "Tipo de edificio",   meta.BuildingType);
                AddIf(s, "Fluido",             meta.FluidLabel);
                AddIf(s, "Temperatura",        meta.TemperatureC, "°C");
                AddIf(s, "Presión de suministro", meta.SourcePressure, "kPa");
                AddIf(s, "Altitud",            meta.Altitude,     "m");
                AddIf(s, "Proyecto",           meta.ProjectName);
            });

            // ── 4. Criterios ──────────────────────────────────────────────
            rb.AddSection("Criterios de cálculo", s => s
                .WithNumbering("4")
                .AddParagraph($"Criterio aplicado: {meta.Criterion ?? "—"}.")
                .AddParagraph(
                    "Las velocidades, pérdidas unitarias y coeficientes de simultaneidad " +
                    "responden a los valores fijados por la normativa indicada en el apartado 2."));

            // ── 5. Resultados por tramo (tabla principal) ────────────────
            rb.AddSection("Resultados por tramo", s =>
            {
                s.WithNumbering("5");
                s.WithTable(t =>
                {
                    t.AddColumn("#",         0.5f, ReportAlign.Right)
                     .AddColumn("Elemento",  1.2f)
                     .AddColumn("Nodos",     1.3f)
                     .AddColumn("Nivel",     1.0f)
                     .AddColumn("Talla",     0.9f)
                     .AddColumn("DN rec",    0.9f)
                     .AddColumn("Q [L/s]",   0.9f, ReportAlign.Right)
                     .AddColumn("v [m/s]",   0.7f, ReportAlign.Right)
                     .AddColumn("ΔP/m",      0.8f, ReportAlign.Right)
                     .AddColumn("ΔP tot",    0.8f, ReportAlign.Right)
                     .AddColumn("Estado",    1.0f);

                    if (inp.Rows != null)
                    {
                        foreach (var r in inp.Rows)
                            t.AddRow(new[]
                            {
                                r.BfsIndex, r.ElementKind, r.NodePath, r.LevelName,
                                r.Size, r.RecommendedSize, r.FlowLps, r.Velocity,
                                r.FrictionPaPm, r.TotalLoss, r.Status,
                            }, MapSeverity(r.StatusKind), r.IsFitting);
                    }
                });
            });

            // ── 6. Esquema de principio ───────────────────────────────────
            if (inp.SchemaPng != null && inp.SchemaPng.Length > 0)
            {
                rb.AddSection("Esquema de principio", s => s
                    .WithNumbering("6")
                    .AddParagraph("Representación unifilar generada automáticamente desde el modelo BIM. " +
                                  "Los tramos en rojo identifican el camino crítico (apartado 7).")
                    .AddImage(inp.SchemaPng, "Figura 6-1. Esquema de principio del sistema."));
            }

            // ── 7. Camino crítico ─────────────────────────────────────────
            if (inp.CriticalSteps != null && inp.CriticalSteps.Count > 0)
            {
                rb.AddSection("Camino crítico", s => s
                    .WithNumbering("7")
                    .AddParagraph("Secuencia de tramos con mayor pérdida de carga acumulada " +
                                  "desde la acometida hasta el aparato más desfavorable.")
                    .WithCriticalPath(cp =>
                    {
                        cp.WithSummary(inp.CriticalSummary);
                        foreach (var step in inp.CriticalSteps)
                            cp.AddStep(step.Order, step.Element, step.Size,
                                step.FlowLps, step.DpPa, step.AccPa, step.Note);
                    }));
            }

            // ── 8. Conclusiones ───────────────────────────────────────────
            rb.AddSection("Conclusiones", s =>
            {
                s.WithNumbering("8");
                int warns = ParseInt(meta.WarningCount);
                int errs  = ParseInt(meta.ErrorCount);
                if (errs > 0)
                    s.AddParagraph($"El cálculo presenta {errs} error(es) que requieren revisión del diseño antes de su validación.");
                else if (warns > 0)
                    s.AddParagraph($"El cálculo concluye dentro de los umbrales normativos con {warns} aviso(s) no bloqueante(s).");
                else
                    s.AddParagraph("El sistema cumple holgadamente los criterios establecidos por la normativa de aplicación. " +
                                   "No se identifican tramos fuera de rango.");
                s.AddParagraph($"Pérdida de carga total peor caso: {meta.WorstDpPa ?? "—"} Pa.");
            });

            // ── 9. Anexos ─────────────────────────────────────────────────
            rb.AddAnnex("A", "Listado completo de tramos", a => a.AddSection("Tabla extendida", x =>
                x.AddParagraph("La tabla del apartado 5 contiene ya el listado completo; este anexo se reserva " +
                               "para futuras ampliaciones (cálculos auxiliares, hipótesis de partida, etc.).")));

            return rb.Build();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void AddIf(ReportBuilder.SectionBuilder s, string k, string v, string unit = "")
        {
            if (!string.IsNullOrWhiteSpace(v)) s.AddCriterion(k, v, unit);
        }

        private static ReportRowSeverity MapSeverity(string statusKind)
        {
            if (string.IsNullOrEmpty(statusKind)) return ReportRowSeverity.None;
            switch (statusKind.Trim().ToUpperInvariant())
            {
                case "OK":      return ReportRowSeverity.Ok;
                case "WARNING": return ReportRowSeverity.Warning;
                case "ERROR":   return ReportRowSeverity.Error;
                default:        return ReportRowSeverity.None;
            }
        }

        private static int ParseInt(string s)
        {
            if (int.TryParse(s, NumberStyles.Integer, Es, out var v)) return v;
            return 0;
        }
    }
}
