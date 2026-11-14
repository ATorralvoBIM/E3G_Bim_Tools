using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using E3G.Saneamiento.UI;

namespace E3G.Saneamiento
{
    /// <summary>Pagina «Aislamiento» del panel: tabla RITE, analisis y colocacion del aislamiento.</summary>
    public partial class AislamientoPage : Page
    {
        private readonly EventoAislamiento _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent _evento;
        private ResultadoAnalisis _analisis;
        private AislamientoTablaWindow _ventanaTabla;
        private AmbienteCondensacion _ambiente = AmbienteCondensacion.PorDefecto;

        public AislamientoPage()
        {
            E3gChrome.ApplyTheme(this);
            InitializeComponent();
            _handler = new EventoAislamiento { Pagina = this };
            _evento = Autodesk.Revit.UI.ExternalEvent.Create(_handler);
            TxtAireTemperatura.Text = Num(AmbienteCondensacion.PorDefecto.Temperatura);
            TxtAireHumedad.Text = Num(AmbienteCondensacion.PorDefecto.HumedadRelativa);
        }

        private static string Num(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

        private static bool TryLeer(string texto, out double valor)
            => double.TryParse((texto ?? "").Trim().Replace(',', '.'),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out valor);

        private void Ambiente_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtAireTemperatura == null || TxtAireHumedad == null || TxtRocio == null) return;

            _ambiente = TryLeer(TxtAireTemperatura.Text, out double t) && TryLeer(TxtAireHumedad.Text, out double hr)
                        && AmbienteCondensacion.EsValido(t, hr)
                ? new AmbienteCondensacion(t, hr)
                : null;
            TxtRocio.Text = _ambiente == null
                ? "Valores no válidos: temperatura de 0 a 50 °C y humedad entre 0 y 100 %."
                : $"Punto de rocío: {_ambiente.PuntoRocio.ToString("0.0", CultureInfo.CurrentCulture)} °C. " +
                  $"Se parte de {RiteAislamiento.EspesorAguaFriaMm} mm y se comprueba que la superficie del aislamiento " +
                  "quede por encima (UNE-EN ISO 12241). Por defecto, más exigente que el diseño interior del RITE " +
                  "(verano 23…25 °C y 45…60 %, tabla 1.4.1.1) para cubrir falsos techos y patinillos.";

            if (_analisis != null)
                foreach (var fila in _analisis.Filas) fila.Ambiente = _ambiente;
        }

        private void BtnAnalizar_Click(object sender, RoutedEventArgs e)
        {
            Bloquear(true);
            ReportarProgreso(0, "Analizando la selección");
            _handler.Accion = AccionAislamiento.Analizar;
            _evento.Raise();
        }

        private void BtnTabla_Click(object sender, RoutedEventArgs e)
        {
            if (_ventanaTabla != null)
            {
                _ventanaTabla.Activate();
                return;
            }

            _ventanaTabla = new AislamientoTablaWindow();
            _ventanaTabla.Closed += (s, a) => _ventanaTabla = null;
            var revit = SaneamientoSession.UiApp?.MainWindowHandle;
            if (revit.HasValue) new WindowInteropHelper(_ventanaTabla) { Owner = revit.Value };
            _ventanaTabla.Show();
        }

        private void BtnColocar_Click(object sender, RoutedEventArgs e)
        {
            var filas = _analisis?.Filas.Where(f => f.Incluir).ToList() ?? new List<FilaAislamiento>();
            var noAplicables = filas.Where(f => !f.EsAplicable).Select(f => f.Nombre).ToList();
            filas = filas.Where(f => f.EsAplicable).ToList();

            if (filas.Count == 0)
            {
                MessageBox.Show("No hay ningún sistema marcado que se pueda aislar: revisa el resumen de cada uno.",
                                "E3G Aislamiento");
                return;
            }
            if (!(CmbTipo.SelectedItem is TipoAislamiento tipo))
            {
                MessageBox.Show("Elige un tipo de aislamiento.", "E3G Aislamiento");
                return;
            }

            int tuberias = filas.Sum(f => f.Tuberias.Count);
            int accesorios = filas.Sum(f => f.Accesorios.Count);
            string aviso = noAplicables.Count == 0
                ? ""
                : "\n\nNo se aislarán (revisa su resumen): " + string.Join(", ", noAplicables);
            var respuesta = MessageBox.Show(
                $"Se aislarán {tuberias:N0} tuberías y {accesorios:N0} fittings y accesorios con «{tipo.Nombre}». " +
                $"Si ya tienen aislamiento se actualizará.{aviso}\n\n¿Continuar?",
                "E3G Aislamiento", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (respuesta != MessageBoxResult.Yes) return;

            Bloquear(true);
            ReportarProgreso(0, "Colocando aislamiento");
            _handler.Accion = AccionAislamiento.Colocar;
            _handler.DocKey = _analisis.DocKey;
            _handler.Filas = filas;
            _handler.TipoId = tipo.Id;
            _evento.Raise();
        }

        // ── Llamados desde EventoAislamiento, en el hilo principal de Revit ──

        public void MostrarAnalisis(ResultadoAnalisis analisis)
        {
            _analisis = analisis;
            foreach (var fila in analisis.Filas) fila.Ambiente = _ambiente;
            ListaSistemas.ItemsSource = analisis.Filas;
            CmbTipo.ItemsSource = analisis.Tipos;
            CmbTipo.SelectedIndex = analisis.Tipos.Count > 0 ? 0 : -1;
            var visible = analisis.Filas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelColocar.Visibility = visible;
            PanelAmbiente.Visibility = visible;

            var partes = new List<string>
            {
                analisis.Filas.Count == 0
                    ? "La selección no contiene tuberías que se aíslen."
                    : $"Selección: {analisis.Filas.Count} sistemas, " +
                      $"{analisis.Filas.Sum(f => f.Tuberias.Count):N0} tuberías y " +
                      $"{analisis.Filas.Sum(f => f.Accesorios.Count):N0} fittings y accesorios."
            };
            if (analisis.TuberiasExcluidas > 0)
                partes.Add($"{analisis.TuberiasExcluidas:N0} tuberías seleccionadas son de saneamiento, contraincendios " +
                           "o de sistemas marcados «Ignorar» en Ajustes, y no se incluyen.");
            int porRevisar = analisis.Filas.Count(f => f.PorRevisar);
            if (porRevisar > 0)
                partes.Add($"{porRevisar} sistemas sin saber si son calientes o fríos («Revisar»): salen desmarcados; " +
                           "elige su fluido y temperatura y márcalos.");
            if (analisis.AccesoriosSinTuberia > 0)
                partes.Add($"{analisis.AccesoriosSinTuberia:N0} fittings o accesorios sin tubería conectada no se incluyen.");
            if (analisis.Tipos.Count == 0)
                partes.Add("El proyecto no tiene tipos de aislamiento de tubería: crea o carga uno y vuelve a analizar.");
            FinalizarProgreso(string.Join(" ", partes));
        }

        public void MostrarColocacion(ResultadoColocacion r)
        {
            var partes = new List<string>
            {
                $"Creados: {r.Creados:N0}. Actualizados: {r.Actualizados:N0}. Sin cambios: {r.SinCambios:N0}."
            };
            if (r.Bloqueados > 0)
                partes.Add($"Omitidos por estar en posesión de otro usuario o desactualizados: {r.Bloqueados:N0}.");
            if (r.Fallidos > 0)
                partes.Add($"Rechazados por Revit: {r.Fallidos:N0}.");
            if (r.SistemasRevertidos.Count > 0)
                partes.Add("Revit revirtió por completo: " + string.Join(", ", r.SistemasRevertidos) + ".");
            FinalizarProgreso(string.Join(" ", partes));
        }

        public void MostrarError(string mensaje) => FinalizarProgreso(mensaje);

        /// <summary>
        /// El trabajo corre en el hilo de Revit, el mismo que el del panel: se vacia
        /// la cola del Dispatcher (Background, inferior a Render) para que se repinte.
        /// </summary>
        public void ReportarProgreso(double porcentaje, string texto)
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressOperacion.Value = System.Math.Max(0, System.Math.Min(100, porcentaje));
            ProgressText.Text = porcentaje.ToString("0") + " %";
            StatusText.Text = texto + "…";
            try { Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
            catch { /* sin repintado intermedio */ }
        }

        private void FinalizarProgreso(string estado)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = estado;
            Bloquear(false);
        }

        // El repintado intermedio tambien procesa clics: sin bloquear, editar una
        // fila cambiaria los espesores a mitad de la colocacion.
        private void Bloquear(bool ocupado)
        {
            BtnAnalizar.IsEnabled = !ocupado;
            BtnColocar.IsEnabled = !ocupado && _analisis != null && _analisis.Tipos.Count > 0;
            ListaSistemas.IsEnabled = !ocupado;
            PanelAmbiente.IsEnabled = !ocupado;
            CmbTipo.IsEnabled = !ocupado;
        }
    }
}
