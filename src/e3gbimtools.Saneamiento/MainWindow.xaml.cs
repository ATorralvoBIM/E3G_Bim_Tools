using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using E3G.Saneamiento.UI;

namespace E3G.Saneamiento
{
    public partial class MainWindow : Page
    {
        private readonly SaneamientoWindow _saneamiento;
        private readonly FontaneriaWindow _fontaneria;
        private readonly AjustesWindow _ajustes;
        private readonly AislamientoPage _aislamiento;
        private Button _btnActiu;

        // El boton Aislamiento de la barra abre la pagina de aislamiento RITE; la
        // medicion antigua (FontaneriaWindow) sigue sin acceso desde la barra.
        // Ajustes sigue construyendose, pero se abre desde la cinta.
        public MainWindow(SaneamientoWindow saneamiento, FontaneriaWindow fontaneria,
                          AjustesWindow ajustes, AislamientoPage aislamiento)
        {
            E3gChrome.ApplyTheme(this);
            InitializeComponent();
            _saneamiento = saneamiento;
            _fontaneria = fontaneria;
            _ajustes = ajustes;
            _aislamiento = aislamiento;

            // DiTools registra DIALux despues de crear esta pagina (Revit monta el
            // panel al registrarlo), por eso se comprueba al mostrarse.
            Loaded += (s, e) =>
            {
                BtnNavDialux.Visibility = App.TienePaginaExterna(App.PaginaDialux)
                    ? Visibility.Visible : Visibility.Collapsed;
                if (_btnActiu == null) NavegarA(_saneamiento, BtnNavSaneamiento);
            };
        }

        private void BtnNavSaneamiento_Click(object sender, RoutedEventArgs e)
            => NavegarA(_saneamiento, BtnNavSaneamiento);

        private void BtnNavAislamiento_Click(object sender, RoutedEventArgs e)
            => NavegarA(_aislamiento, BtnNavAislamiento);

        // Se crea al pulsarlo la primera vez y se conserva.
        private ClimatizacionPage _climatizacion;

        private void BtnNavClimatizacion_Click(object sender, RoutedEventArgs e)
            => NavegarA(_climatizacion ??= new ClimatizacionPage(), BtnNavClimatizacion);

        // La vista de DIALux (DiTools) se crea la primera vez y se conserva: asi
        // mantiene el DWG cargado y la asociacion al ir y volver de otras paginas.
        private object _dialux;

        private void BtnNavDialux_Click(object sender, RoutedEventArgs e)
        {
            if (_dialux == null) _dialux = App.CrearPaginaExterna(App.PaginaDialux);
            if (_dialux != null) NavegarA(_dialux, BtnNavDialux);
        }

        private void NavegarA(object pagina, Button boto)
        {
            if (_btnActiu != null)
                ActualitzarEstatBoto(_btnActiu, false);

            _btnActiu = boto;
            ActualitzarEstatBoto(boto, true);
            ContentArea.Navigate(pagina);
        }

        // Activo: tinte azul de marca + texto/glifo Primary (como la cinta).
        private static void ActualitzarEstatBoto(Button boto, bool actiu)
        {
            if (actiu)
            {
                boto.SetResourceReference(Control.BackgroundProperty, "E3g.Brush.BrandTint");
                boto.SetResourceReference(Control.ForegroundProperty, "E3g.Brush.Primary");
                boto.SetResourceReference(Control.BorderBrushProperty, "E3g.Brush.TintPressed");
            }
            else
            {
                boto.ClearValue(Control.BackgroundProperty);
                boto.ClearValue(Control.ForegroundProperty);
                boto.ClearValue(Control.BorderBrushProperty);
            }
        }
    }
}
