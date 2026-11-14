using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.UI;

namespace E3G.Saneamiento
{
    /// <summary>
    /// Pagina «Climatizacion» del panel MEP, con pestañas como Saneamiento:
    ///   • Trazado DXF: tuberias de refrigerante desde el DXF del fabricante (App.PaginaTrazadoClima).
    ///   • Esquema de principio: esquema desde DXF (App.PaginaEsquemaDxf).
    ///   • Ajustes: opciones del trazado (App.PaginaAjustesClima).
    /// Las tres son paginas externas de DiTools; cada una se crea la primera vez
    /// que se abre y se conserva (no se pierde lo cargado al cambiar de pestaña).
    /// </summary>
    public partial class ClimatizacionPage : Page
    {
        private readonly Dictionary<string, object> _creadas = new Dictionary<string, object>();

        public ClimatizacionPage()
        {
            E3gChrome.ApplyTheme(this);
            InitializeComponent();
            Mostrar(App.PaginaTrazadoClima);
        }

        private void OnTabChecked(object sender, RoutedEventArgs e)
        {
            if (Host == null) return; // durante InitializeComponent
            if (sender == TabTrazado) Mostrar(App.PaginaTrazadoClima);
            else if (sender == TabEsquema) Mostrar(App.PaginaEsquemaDxf);
            else if (sender == TabAjustes) Mostrar(App.PaginaAjustesClima);
        }

        private void Mostrar(string clave)
        {
            if (!_creadas.TryGetValue(clave, out var vista))
            {
                vista = App.TienePaginaExterna(clave) ? App.CrearPaginaExterna(clave) : null;
                if (vista != null) _creadas[clave] = vista;
            }
            Host.Content = vista;
            SinModulo.Visibility = vista == null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
