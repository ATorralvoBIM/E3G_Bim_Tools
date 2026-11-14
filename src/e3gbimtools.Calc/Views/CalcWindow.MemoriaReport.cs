// ============================================================================
//  CalcWindow.MemoriaReport.cs
//  Partial de CalcWindow — exporta la MEMORIA DE CÁLCULO en DOCX.
//
//  Construye un ReportData (CalcReportBuilder) a partir del último cálculo y lo
//  escribe con DocxReportExporter (OpenXML). Incluye la justificación paso a
//  paso del camino crítico para que el cálculo sea verificable.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Windows;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using e3gbimtools.Calc.Reports;

namespace e3gbimtools.Calc.Views
{
    public partial class CalcWindow
    {
        private void OnHydExportReportClick(object sender, RoutedEventArgs e) => ExportCalcMemoria(true);
        private void OnAerExportReportClick(object sender, RoutedEventArgs e) => ExportCalcMemoria(false);

        private void ExportCalcMemoria(bool hyd)
        {
            var report = hyd ? _lastHydReport   : _lastAerReport;
            var rows   = hyd ? _lastHydRows     : _lastAerRows;
            var fluid  = hyd ? _lastHydFluid    : _lastAerFluid;
            var crit   = hyd ? _lastHydCriteria : _lastAerCriteria;
            if (report == null || rows == null) { ShowError("Ejecuta el cálculo primero."); return; }

            var inputsOf = BuildSegmentInputsResolver(hyd);

            string ver = "";
            try { ver = typeof(CalcWindow).Assembly.GetName().Version?.ToString(); } catch { }

            var meta = new CalcReportMeta
            {
                Discipline    = hyd ? "Hidráulica" : "Aeráulica",
                IsHydraulic   = hyd,
                EngineVersion = ver,
            };

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title      = "Guardar memoria de cálculo",
                Filter     = "Documento Word (*.docx)|*.docx",
                FileName   = "memoria_calculo_" + (hyd ? "hidraulica" : "aeraulica")
                             + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".docx",
                DefaultExt = ".docx",
            };
            if (dlg.ShowDialog() != true) return;
            string path = dlg.FileName;

            try
            {
                var data = CalcReportBuilder.Build(meta, report, rows, fluid, crit, inputsOf);
                new DocxReportExporter().Export(data, path);
                SetStatus("Memoria de cálculo exportada: " + path);
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch { /* abrir es best-effort */ }
            }
            catch (Exception ex)
            {
                ShowError("Error al generar la memoria de cálculo: " + ex.Message);
            }
        }

        // Mapa RevitId -> datos geométricos del tramo para la justificación.
        private Func<long, SegmentCalcInputs> BuildSegmentInputsResolver(bool hyd)
        {
            var map = new Dictionary<long, SegmentCalcInputs>();

            if (hyd && _lastHydBfsOrder != null)
            {
                foreach (var s in _lastHydBfsOrder)
                {
                    var fittings = new List<KeyValuePair<string, double>>();
                    if (s.Fittings != null)
                        foreach (var f in s.Fittings)
                        {
                            string label = !string.IsNullOrEmpty(f.Label)
                                ? f.Label
                                : FittingLossDatabase.GetDisplayName(f.FittingType);
                            fittings.Add(new KeyValuePair<string, double>(label, f.KCoefficient));
                        }

                    map[s.RevitId] = new SegmentCalcInputs
                    {
                        DiameterM          = s.DiameterM,
                        LengthM            = s.LengthM,
                        RoughnessM         = s.Roughness,
                        RawFlowM3s         = s.RawFlowM3s,
                        DesignFlowM3s      = s.FlowM3s,
                        SimultaneityK      = s.SimultaneityFactor,
                        DownstreamFixtures = s.DownstreamFixtureCount,
                        MaterialKey        = s.MaterialKey ?? "",
                        Fittings           = fittings,
                    };
                }
            }
            else if (!hyd && _lastAerBfsOrder != null)
            {
                foreach (var s in _lastAerBfsOrder)
                {
                    map[s.RevitId] = new SegmentCalcInputs
                    {
                        DiameterM     = s.HydraulicDiameterM,   // equiv. para rectangular/oval
                        LengthM       = s.LengthM,
                        RoughnessM    = s.Roughness,
                        DesignFlowM3s = s.FlowM3s,
                        SimultaneityK = 1.0,
                    };
                }
            }

            return id => map.TryGetValue(id, out var v) ? v : null;
        }
    }
}
