using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Protocol;

namespace E3G.Saneamiento
{
    public partial class ProtocolSeccioControl : UserControl
    {
        private readonly ObservableCollection<ReglaViewModel> _regles = new();

        public string Titol
        {
            get => TxtTitol.Text;
            set => TxtTitol.Text = value;
        }

        public ProtocolSeccioControl()
        {
            InitializeComponent();
            LstRegles.ItemsSource = _regles;
            ActualitzarBuit();
        }

        public void CarregarRegles(List<ProtocolRegla> regles)
        {
            _regles.Clear();
            foreach (var r in regles)
                _regles.Add(new ReglaViewModel(r));
            ActualitzarBuit();
        }

        public List<ProtocolRegla> ObtenirRegles()
            => _regles.Select(vm => vm.Regla).ToList();

        private void ActualitzarBuit()
        {
            TxtBuit.Visibility = _regles.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAfegir_Click(object sender, RoutedEventArgs e)
        {
            var doc = SaneamientoSession.UiApp?.ActiveUIDocument?.Document;
            var dlg = new ProtocolReglaDialog(null, doc);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            if (dlg.Desat && dlg.ReglaEditada != null)
            {
                _regles.Add(new ReglaViewModel(dlg.ReglaEditada));
                ActualitzarBuit();
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not ReglaViewModel vm) return;
            var doc = SaneamientoSession.UiApp?.ActiveUIDocument?.Document;
            var dlg = new ProtocolReglaDialog(vm.Regla, doc);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            if (dlg.Desat && dlg.ReglaEditada != null)
            {
                var idx = _regles.IndexOf(vm);
                if (idx >= 0) _regles[idx] = new ReglaViewModel(dlg.ReglaEditada);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not ReglaViewModel vm) return;
            if (MessageBox.Show($"Eliminar la regla «{vm.Nom}»?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _regles.Remove(vm);
            ActualitzarBuit();
        }
    }

    public class ReglaViewModel
    {
        public ProtocolRegla Regla { get; }
        public string Nom => Regla.Nom;
        public string Resum => Regla.Condicions.Count == 0
            ? "(cap condició)"
            : string.Join(Regla.Logica == LogicaCondicions.Totes ? " I " : " O ",
                Regla.Condicions.Select(c =>
                    $"{c.ParamNom} {c.Operador.Etiqueta()} \"{c.Valor}\""));

        public ReglaViewModel(ProtocolRegla r) => Regla = r;
    }
}