// ============================================================================
//  ReportSamples.cs
//  Ejemplos de uso de la API de informes — ÚTIL como referencia desde la UI
//  o desde el self-test.  No se ejecuta en producción; solo documenta cómo
//  enlazar el pipeline completo.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using E3G.Saneamiento.RevitBridge;

namespace E3G.Saneamiento.Reports
{
    public static class ReportSamples
    {
        /// <summary>
        /// Genera un informe PDF + DOCX a la carpeta dada, a partir de los
        /// datos típicos producidos por CalcWindow.  Se asume que el caller
        /// ya tiene a mano el PNG del esquema (ver CanvasSnapshot.RenderToPng).
        /// </summary>
        public static (string pdfPath, string docxPath) GenerateReports(
            string outputFolder,
            ReportMetadata meta,
            IList<ReportRow> rows,
            byte[] schemaPng,
            IList<ReportCriticalPathStep> criticalSteps,
            string criticalSummary,
            ReportBranding branding = null,
            ReportCover cover = null)
        {
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

            var data = CteHs4Template.Build(new CteHs4Template.Inputs
            {
                Meta = meta,
                Rows = rows,
                SchemaPng = schemaPng,
                CriticalSteps = criticalSteps,
                CriticalSummary = criticalSummary,
                Branding = branding,
                Cover = cover,
            });

            string baseName = SafeFileName($"Informe_{meta?.SystemName ?? "MEP"}_{DateTime.Now:yyyyMMdd_HHmm}");
            string pdfPath  = Path.Combine(outputFolder, baseName + ".pdf");
            string docxPath = Path.Combine(outputFolder, baseName + ".docx");

            new PdfReportExporter().Export(data, pdfPath);
            new DocxReportExporter().Export(data, docxPath);
            return (pdfPath, docxPath);
        }

        /// <summary>
        /// Construye un informe sintético sin Revit para verificar el motor
        /// de exportación (smoke test rápido).
        /// </summary>
        public static (string pdfPath, string docxPath) GenerateDemoReport(string outputFolder)
        {
            var meta = new ReportMetadata
            {
                Domain = "Tuberías", SystemName = "AFS — Edif. piloto",
                Criterion = "CTE DB-HS4 (vmax 2 m/s, ΔP/m ≤ 400 Pa/m)",
                BuildingType = "Vivienda colectiva",
                FluidLabel = "Agua fría sanitaria", TemperatureC = "10",
                SourcePressure = "350", ProjectName = "Edif. Demo E3G",
                TotalSegments = "142", WorstDpPa = "38214",
                WarningCount = "2", ErrorCount = "0",
            };
            var rows = new List<ReportRow>
            {
                new ReportRow { BfsIndex="1", ElementKind="PIPE",  NodePath="N1 → N2", Size="DN 50",
                                RecommendedSize="DN 50", FlowLps="2.10", Velocity="1.07",
                                FrictionPaPm="240", TotalLoss="1820", Status="OK", StatusKind="OK" },
                new ReportRow { BfsIndex="2", ElementKind="PIPE",  NodePath="N2 → N3", Size="DN 40",
                                RecommendedSize="DN 40", FlowLps="1.62", Velocity="1.29",
                                FrictionPaPm="362", TotalLoss="3120", Status="OK", StatusKind="OK" },
                new ReportRow { BfsIndex="",  ElementKind="TEE",   NodePath="@N3", IsFitting = true,
                                Status="Bifurcación 3 vías", StatusKind="" },
                new ReportRow { BfsIndex="3", ElementKind="PIPE",  NodePath="N3 → N4", Size="DN 32",
                                RecommendedSize="DN 40", FlowLps="1.18", Velocity="1.46",
                                FrictionPaPm="421", TotalLoss="4220", Status="ΔP/m al límite",
                                StatusKind="Warning" },
            };
            var crit = new List<ReportCriticalPathStep>
            {
                new ReportCriticalPathStep { Order=1, Element="N1 → N2", Size="DN 50",
                    FlowLps="2.10", DpPa="1820",  AccPa="1820"  },
                new ReportCriticalPathStep { Order=2, Element="N2 → N3", Size="DN 40",
                    FlowLps="1.62", DpPa="3120",  AccPa="4940"  },
                new ReportCriticalPathStep { Order=3, Element="N3 → N4", Size="DN 32",
                    FlowLps="1.18", DpPa="4220",  AccPa="9160", Note="Aviso vmax próximo a 2 m/s" },
            };

            return GenerateReports(outputFolder, meta, rows, schemaPng: null,
                criticalSteps: crit, criticalSummary: "ΔP peor caso: 38.2 kPa",
                branding: new ReportBranding { CompanyName = "E3G Ingeniería",
                    CompanyWeb = "www.e3g.es", PrimaryColor = "#1A3A5E",
                    AccentColor = "#C62828", DocReference = "DOC-MEP-DEMO" },
                cover: new ReportCover { ProjectName = "Edif. Demo E3G",
                    AuthorName = "Ángel T.", AuthorRole = "Ingeniero",
                    Revision = "00", Norm = "CTE DB-HS4" });
        }

        private static string SafeFileName(string raw)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                raw = raw.Replace(ch, '_');
            return raw;
        }
    }
}
