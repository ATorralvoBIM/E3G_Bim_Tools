// ============================================================================
//  CalcWindow.ManualSize.cs
//  Partial de CalcWindow — editor de diámetro por tramo (override en memoria).
//
//  Permite seleccionar una fila de tubería en el grid hidráulico, cambiarle el
//  diámetro eligiendo una talla del catálogo del tipo Revit, recalcular EN
//  MEMORIA (sin tocar el modelo) para verificar caudal/velocidad/presión, y
//  finalmente aplicar el diámetro elegido al modelo.
//
//  Flujo:
//    1. Tras leer la red (OnHydCalcClick) → CaptureHydCatalogs guarda, por
//       elemento Revit, su catálogo de tallas y el diámetro actual del modelo.
//    2. ApplyHydManualSizes sobreescribe seg.DiameterM con los overrides
//       pendientes ANTES de calcular (recálculo en memoria).
//    3. El usuario elige talla en el combo → se guarda el override pendiente.
//       "Recalcular con cambios" reejecuta OnHydCalcClick (relee modelo +
//       aplica overrides en memoria). "Aplicar al modelo" escribe de verdad.
//       "Restablecer" descarta los overrides y recalcula con el modelo real.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Hydraulics;
using e3gbimtools.Calc.RevitBridge;
using PipeNetwork = e3gbimtools.Calc.Core.Hydraulics.PipeNetwork;

namespace e3gbimtools.Calc.Views
{
    public partial class CalcWindow
    {
        // Overrides de diámetro NOMINAL [m] pendientes, por Id de elemento Revit.
        private readonly Dictionary<long, double> _hydManualSizesM = new Dictionary<long, double>();

        // Catálogo de tallas por elemento (capturado al leer la red).
        private readonly Dictionary<long, IReadOnlyList<NominalDiameter>> _hydCatalogByElement
            = new Dictionary<long, IReadOnlyList<NominalDiameter>>();

        // Diámetro NOMINAL real del modelo [m], por elemento (para detectar cambios).
        private readonly Dictionary<long, double> _hydModelDiamM = new Dictionary<long, double>();

        // Evita reentrancia al poblar el combo programáticamente.
        private bool _suppressSizeEditorEvent;

        // Fila a la que está vinculado el editor en este momento.
        private ResultRow _hydSizeEditorRow;

        /// <summary>Opción del combo: una talla del catálogo del tipo Revit.</summary>
        private sealed class HydSizeOption
        {
            public string Label   { get; set; }
            public double NominalM { get; set; }
            public int    Dn       { get; set; }
            public override string ToString() => Label;
        }

        // ------------------------------------------------------------------ //
        //  Captura de catálogos y diámetros del modelo (al leer la red)        //
        // ------------------------------------------------------------------ //
        private void CaptureHydCatalogs(PipeNetwork network)
        {
            _hydCatalogByElement.Clear();
            _hydModelDiamM.Clear();
            if (network == null) return;

            foreach (var seg in network.Segments)
            {
                if (seg == null || seg.RevitId <= 0) continue;
                _hydModelDiamM[seg.RevitId] = seg.DiameterM;
                if (seg.CatalogSeries != null && seg.CatalogSeries.Count > 0)
                    _hydCatalogByElement[seg.RevitId] = seg.CatalogSeries;
            }
        }

        // ------------------------------------------------------------------ //
        //  Aplica overrides pendientes sobre la red (en memoria, antes calc.)  //
        // ------------------------------------------------------------------ //
        private void ApplyHydManualSizes(PipeNetwork network)
        {
            if (network == null || _hydManualSizesM.Count == 0) return;

            foreach (var seg in network.Segments)
            {
                if (seg == null || seg.RevitId <= 0) continue;
                if (_hydManualSizesM.TryGetValue(seg.RevitId, out var nomM) && nomM > 0.0)
                    seg.DiameterM = nomM;
            }
        }

        /// <summary>Descarta todos los overrides pendientes.</summary>
        private void ClearHydManualSizes()
        {
            _hydManualSizesM.Clear();
        }

        // ------------------------------------------------------------------ //
        //  Actualiza el panel editor según la fila seleccionada                //
        // ------------------------------------------------------------------ //
        private void UpdateHydSizeEditor()
        {
            var row = HydResultsGrid?.SelectedItem as ResultRow;

            // Solo para filas de tubería con catálogo conocido.
            if (row == null || row.IsFitting || row.RevitId <= 0
                || !_hydCatalogByElement.TryGetValue(row.RevitId, out var catalog)
                || catalog == null || catalog.Count == 0)
            {
                _hydSizeEditorRow = null;
                if (HydSizeEditorPanel != null)
                    HydSizeEditorPanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            _suppressSizeEditorEvent = true;
            try
            {
                _hydSizeEditorRow = row;
                HydSizeEditorPanel.Visibility = System.Windows.Visibility.Visible;

                var options = catalog
                    .Select(nd => new HydSizeOption
                    {
                        Label    = "DN " + nd.Dn,
                        NominalM = nd.Dn / 1000.0,
                        Dn       = nd.Dn,
                    })
                    .ToList();
                HydSizeEditorCombo.ItemsSource = options;

                // Diámetro efectivo: override pendiente o, en su defecto, modelo.
                double targetM;
                bool pending = _hydManualSizesM.TryGetValue(row.RevitId, out targetM) && targetM > 0.0;
                if (!pending)
                    _hydModelDiamM.TryGetValue(row.RevitId, out targetM);

                HydSizeEditorCombo.SelectedItem = NearestOption(options, targetM);
                HydSizeEditorLabel.Text = row.Size ?? "";

                HydSizeEditorHint.Text = pending
                    ? "Cambio pendiente. Pulsa \"Recalcular con cambios\" para verificar o \"Aplicar al modelo\"."
                    : "";
            }
            finally
            {
                _suppressSizeEditorEvent = false;
            }
        }

        private static HydSizeOption NearestOption(List<HydSizeOption> options, double targetM)
        {
            if (options == null || options.Count == 0) return null;
            HydSizeOption best = options[0];
            double bestDiff = Math.Abs(best.NominalM - targetM);
            foreach (var o in options)
            {
                double d = Math.Abs(o.NominalM - targetM);
                if (d < bestDiff) { bestDiff = d; best = o; }
            }
            return best;
        }

        // ------------------------------------------------------------------ //
        //  Handlers de UI                                                      //
        // ------------------------------------------------------------------ //
        private void OnHydSizeEditorComboChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressSizeEditorEvent) return;
            var row = _hydSizeEditorRow;
            if (row == null || row.RevitId <= 0) return;
            var opt = HydSizeEditorCombo.SelectedItem as HydSizeOption;
            if (opt == null) return;

            double modelM;
            _hydModelDiamM.TryGetValue(row.RevitId, out modelM);

            if (Math.Abs(opt.NominalM - modelM) < 5e-4)
            {
                // Vuelve al diámetro del modelo → no hay override.
                _hydManualSizesM.Remove(row.RevitId);
                HydSizeEditorHint.Text = "";
            }
            else
            {
                _hydManualSizesM[row.RevitId] = opt.NominalM;
                HydSizeEditorHint.Text =
                    "Cambio pendiente (" + (row.Size ?? "") + " → " + opt.Label +
                    "). Pulsa \"Recalcular con cambios\" para verificar o \"Aplicar al modelo\".";
            }
        }

        private void OnHydRecalcWithChangesClick(object sender, RoutedEventArgs e)
        {
            if (_hydManualSizesM.Count == 0)
            {
                SetStatus("No hay cambios de diámetro pendientes.");
                return;
            }
            SetStatus("Recalculando con " + _hydManualSizesM.Count + " cambio(s) de diámetro…");
            OnHydCalcClick(null, null);
        }

        private void OnHydResetManualSizesClick(object sender, RoutedEventArgs e)
        {
            if (_hydManualSizesM.Count == 0)
            {
                SetStatus("No hay cambios pendientes que restablecer.");
                return;
            }
            ClearHydManualSizes();
            SetStatus("Cambios de diámetro descartados. Recalculando con el modelo real…");
            OnHydCalcClick(null, null);
        }

        private void OnHydApplyManualSizeClick(object sender, RoutedEventArgs e)
        {
            if (_hydManualSizesM.Count == 0)
            {
                MessageBox.Show(this,
                    "No hay ningún cambio de diámetro pendiente.\n\n" +
                    "Selecciona un tramo de tubería y elige una talla en el desplegable.",
                    "Aplicar diámetro", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_externalBus == null)
            {
                ShowError("La ventana debe abrirse desde el botón de la cinta de Revit para " +
                          "poder modificar el modelo (se requiere contexto de API válido).");
                return;
            }

            // Copia inmutable de los overrides a aplicar.
            var targets = new Dictionary<long, double>(_hydManualSizesM);

            var sb = new StringBuilder();
            sb.AppendLine("Se aplicará al modelo el diámetro elegido en " + targets.Count + " tubería(s):");
            sb.AppendLine();
            int preview = 0;
            foreach (var kv in targets)
            {
                if (preview++ >= 15) { sb.AppendLine("  … y " + (targets.Count - 15) + " más."); break; }
                string label = _revitIdToRow.TryGetValue(kv.Key, out var r)
                    ? (r.NodePath ?? ("Id " + kv.Key)) : ("Id " + kv.Key);
                sb.AppendLine("  · " + label + ":  → DN " +
                    Math.Round(kv.Value * 1000.0).ToString("0"));
            }
            sb.AppendLine();
            sb.AppendLine("La operación se puede deshacer con Ctrl+Z en Revit.");
            sb.AppendLine("Tras aplicar, el cálculo se ejecutará de nuevo automáticamente.");
            sb.AppendLine();
            sb.AppendLine("¿Aplicar los diámetros al modelo?");

            var answer = MessageBox.Show(this, sb.ToString(),
                "Aplicar diámetro", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) { SetStatus("Aplicación de diámetro cancelada."); return; }

            SetStatus("Aplicando diámetros al modelo…");

            ScheduleRevitAction(app =>
            {
                try
                {
                    var doc     = app.ActiveUIDocument.Document;
                    var applier = new PipeSizingApplier();
                    int applied = applier.ApplyExplicitSizes(doc, targets, "MEP Calc - Aplicar diámetro");

                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        ClearHydManualSizes();
                        SetStatus("Diámetro aplicado: " + applied + " tubería(s) actualizada(s). Recalculando…");
                        OnHydCalcClick(null, null);
                    }));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke((Action)(() => ShowError("Error al aplicar el diámetro: " + msg)));
                }
            });
        }
    }
}
