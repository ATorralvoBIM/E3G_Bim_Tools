using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace E3G.Saneamiento
{
    public partial class MainWindow : Page
    {
        private readonly SaneamientoWindow _saneamiento;
        private readonly FontaneriaWindow _fontaneria;
        private readonly AjustesWindow _ajustes;          
        private Button _btnActiu;

        public MainWindow(SaneamientoWindow saneamiento, FontaneriaWindow fontaneria,
                          AjustesWindow ajustes)          
        {
            InitializeComponent();
            _saneamiento = saneamiento;
            _fontaneria = fontaneria;
            _ajustes = ajustes;                          

            Loaded += (s, e) => NavegarA(_saneamiento, BtnNavSaneamiento, "#4a7a96");
        }

        private void BtnNavSaneamiento_Click(object sender, RoutedEventArgs e)
            => NavegarA(_saneamiento, BtnNavSaneamiento, "#4a7a96");

        private void BtnNavFontaneria_Click(object sender, RoutedEventArgs e)
            => NavegarA(_fontaneria, BtnNavFontaneria, "#1D9E75");

        private void BtnNavAjustes_Click(object sender, RoutedEventArgs e)  
            => NavegarA(_ajustes, BtnNavAjustes, "#6B7280");

        private void NavegarA(Page pagina, Button boto, string colorAccent)
        {
            if (_btnActiu != null)
                ActualitzarEstatBoto(_btnActiu, false, colorAccent);

            _btnActiu = boto;
            ActualitzarEstatBoto(boto, true, colorAccent);
            ContentArea.Navigate(pagina);
        }

        private void ActualitzarEstatBoto(Button boto, bool actiu, string colorAccent)
        {
            boto.Background = actiu
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDE1E5"))
                : new SolidColorBrush(Colors.Transparent);

            if (boto.Template.FindName("NavBorder", boto) is Border border)
            {
                border.BorderBrush = actiu
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorAccent))
                    : new SolidColorBrush(Colors.Transparent);
            }
        }

        private void BtnNavProximament_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Aquesta funcionalitat estarà disponible pròximament.",
                "E3G BIM Tools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}