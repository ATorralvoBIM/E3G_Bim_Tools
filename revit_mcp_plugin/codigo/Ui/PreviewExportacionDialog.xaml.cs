// ============================================================================
//  PreviewExportacionDialog.xaml.cs
//  Modal que mostra el contingut del BC3 abans d'exportar:
//   - Resum (partides, línies, elements, import total)
//   - Taula de partides amb cantitats i imports
//   - Per la partida seleccionada, taula de línies de mesurament (origen, uds, long, etc.)
//   - Diagnòstic detallat per regla (què ha passat, per què)
//   - Botó "Generar BC3..." que confirma l'exportació
//
//  Si l'usuari pulsa "Cancel·lar", DialogResult = false (no s'escriu cap arxiu).
//  Si pulsa "Generar BC3...", DialogResult = true → el caller obre SaveFileDialog.
// ============================================================================

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Presupuesto.Modelo;
using E3G.Saneamiento.Presupuesto.Commands;

namespace E3G.Saneamiento.Presupuesto.Ui
{
    public partial class PreviewExportacionDialog : Window
    {
        public ResultadoCalculo Resultado { get; }

        public PreviewExportacionDialog(ResultadoCalculo resultado)
        {
            InitializeComponent();
            Resultado = resultado ?? new ResultadoCalculo
            {
                Mediciones = new System.Collections.Generic.List<MedicionPorPartida>(),
                Warnings = new System.Collections.Generic.List<string>()
            };

            CargarUI();
        }

        private void CargarUI()
        {
            // Mediciones a la taula superior
            var mediciones = Resultado.Mediciones?.ToList() ?? new System.Collections.Generic.List<MedicionPorPartida>();
            DgPartidas.ItemsSource = mediciones;

            // Resum
            var totalLineas = mediciones.Sum(m => m.Lineas?.Count ?? 0);
            var totalElementos = mediciones.SelectMany(m => m.Lineas ?? new System.Collections.Generic.List<LineaMedicion>())
                                            .Where(l => l.ElementoOrigen != null && l.ElementoOrigen.Value > 0)
                                            .Select(l => l.ElementoOrigen.Value)
                                            .Distinct()
                                            .Count();
            var totalImporte = mediciones.Sum(m => m.Importe);

            LblPartidas.Text  = mediciones.Count.ToString();
            LblLineas.Text    = totalLineas.ToString();
            LblElementos.Text = totalElementos.ToString();
            LblImporte.Text   = $"{totalImporte:0.00} €";

            // Avís si no hi ha mediciones
            if (mediciones.Count == 0)
            {
                PnlSinMediciones.Visibility = Visibility.Visible;
                ExpWarnings.IsExpanded = true; // si no hi ha res, obrim el diagnòstic per defecte
                BtnExportar.IsEnabled = false;
                BtnExportar.Opacity = 0.5;
            }
            else
            {
                PnlSinMediciones.Visibility = Visibility.Collapsed;
                BtnExportar.IsEnabled = true;
            }

            // Warnings / diagnòstic
            LstWarnings.ItemsSource = Resultado.Warnings ?? new System.Collections.Generic.List<string>();

            // Per defecte selecciona la primera partida per omplir la taula de línies
            if (mediciones.Count > 0)
            {
                DgPartidas.SelectedIndex = 0;
            }
        }

        private void DgPartidas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgPartidas.SelectedItem is MedicionPorPartida m)
            {
                DgLineas.ItemsSource = m.Lineas;
                LblTituloLineas.Text = $"Línies de mesurament de {m.Partida?.Codigo} — {m.Lineas?.Count ?? 0} línies";
            }
            else
            {
                DgLineas.ItemsSource = null;
                LblTituloLineas.Text = "Selecciona una partida per veure les seves línies de mesurament";
            }
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
