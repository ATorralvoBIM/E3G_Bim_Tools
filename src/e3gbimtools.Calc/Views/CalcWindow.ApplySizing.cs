// ============================================================================
//  CalcWindow.ApplySizing.cs
//  Partial de CalcWindow — handler "Aplicar dimensionado" (solo tuberías).
//
//  Upgrade mayo 2026: tras calcular, el usuario puede aplicar al modelo el
//  redimensionado sugerido para TODOS los tramos que incumplen velocidad o
//  rozamiento (lote completo). Se elige la siguiente talla disponible del
//  catálogo del tipo Revit de cada tubería. Tras aplicar, se recalcula.
//
//  Flujo: Preview (en contexto API) → confirmación → Apply (en contexto API)
//         → recálculo (OnHydCalcClick) que refresca grid y 3D.
// ============================================================================

using System;
using System.Linq;
using System.Text;
using System.Windows;
using e3gbimtools.Calc.RevitBridge;

namespace e3gbimtools.Calc.Views
{
    public partial class CalcWindow
    {
        private void OnHydApplySizingClick(object sender, RoutedEventArgs e)
        {
            if (_lastHydReport == null) { ShowError("Ejecuta el cálculo primero."); return; }
            if (_externalBus == null)
            {
                ShowError("La ventana debe abrirse desde el botón de la cinta de Revit para " +
                          "poder modificar el modelo (se requiere contexto de API válido).");
                return;
            }

            var report = _lastHydReport;
            SetStatus("Analizando tramos que incumplen normativa…");

            // 1) Preview dentro del contexto de API de Revit.
            ScheduleRevitAction(app =>
            {
                try
                {
                    var doc      = app.ActiveUIDocument.Document;
                    var applier  = new PipeSizingApplier();
                    var plan     = applier.Preview(doc, report);
                    var toChange = plan.Where(c => c.WillChange).ToList();

                    Dispatcher.BeginInvoke((Action)(() =>
                        ConfirmAndApply(report, plan.Count, toChange)));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke((Action)(() => ShowError("Error al analizar el dimensionado: " + msg)));
                }
            });
        }

        // Confirmación en hilo UI; si el usuario acepta, se encola el Apply.
        private void ConfirmAndApply(
            Core.Common.CalcReport report,
            int totalNonCompliant,
            System.Collections.Generic.List<PipeSizingApplier.Change> toChange)
        {
            if (toChange.Count == 0)
            {
                SetStatus("No hay tramos que redimensionar (los que incumplen ya están en su talla límite o no cambian).");
                MessageBox.Show(this,
                    "No se ha encontrado ningún tramo al que aplicar un cambio de diámetro.\n\n" +
                    "Los tramos que incumplen ya están en la talla límite de su catálogo " +
                    "(mayor para velocidad/rozamiento excesivos, menor para sobredimensionado), " +
                    "o el tipo de tubería no expone tallas comerciales.",
                    "Aplicar dimensionado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Se modificará el diámetro de " + toChange.Count + " tubería(s) en el modelo:");
            sb.AppendLine();

            int preview = Math.Min(toChange.Count, 15);
            for (int i = 0; i < preview; i++)
            {
                var c = toChange[i];
                string arrow = c.IsDownsize ? " ↓ " : " ↑ ";
                sb.AppendLine("  · " + c.TramoLabel + ":  "
                    + c.FromMm.ToString("0.#") + " mm " + arrow
                    + c.ToMm.ToString("0.#") + " mm   (sugerido " + c.RecommendedLabel + ")");
            }
            if (toChange.Count > preview)
                sb.AppendLine("  … y " + (toChange.Count - preview) + " más.");

            sb.AppendLine();
            sb.AppendLine("La operación se puede deshacer con Ctrl+Z en Revit.");
            sb.AppendLine("Tras aplicar, el cálculo se ejecutará de nuevo automáticamente.");
            sb.AppendLine();
            sb.AppendLine("¿Aplicar el redimensionado al modelo?");

            var answer = MessageBox.Show(this, sb.ToString(),
                "Aplicar dimensionado",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) { SetStatus("Aplicación de dimensionado cancelada."); return; }

            SetStatus("Aplicando dimensionado al modelo…");

            // 2) Apply dentro del contexto de API de Revit.
            ScheduleRevitAction(app =>
            {
                try
                {
                    var doc     = app.ActiveUIDocument.Document;
                    var applier = new PipeSizingApplier();
                    applier.Apply(doc, report, "MEP Calc - Aplicar dimensionado");
                    int applied = applier.Applied, skipped = applier.Skipped;
                    int fittings = applier.FittingsAligned;

                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        SetStatus("Dimensionado aplicado: " + applied + " tubería(s) actualizada(s)"
                            + (skipped > 0 ? ", " + skipped + " omitida(s)" : "")
                            + (fittings > 0 ? ", " + fittings + " fitting(s) alineado(s)" : "")
                            + ". Recalculando…");
                        // 3) Recalcular y refrescar grid + 3D.
                        OnHydCalcClick(null, null);
                    }));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke((Action)(() => ShowError("Error al aplicar el dimensionado: " + msg)));
                }
            });
        }
    }
}
