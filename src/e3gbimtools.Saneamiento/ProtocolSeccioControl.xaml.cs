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
        public Action<List<ProtocolRegla>>? OnReglesCanviades { get; set; }

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
            var dlg = new ProtocolReglaDialog(null);
            dlg.Owner = Window.GetWindow(this);
            // REEMPLAÇA PER:
            dlg.ShowDialog();
            if (dlg.Desat)
            {
                _regles.Add(new ReglaViewModel(dlg.ReglaEditada!));
                ActualitzarBuit();
                OnReglesCanviades?.Invoke(ObtenirRegles());
            }
        }


        // REEMPLAÇA PER:
        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Guid id) return;
            var vm = _regles.FirstOrDefault(r => r.Regla.Id == id);
            if (vm == null) return;
            var dlg = new ProtocolReglaDialog(vm.Regla);
            dlg.Owner = Window.GetWindow(this);
            dlg.ShowDialog();
            if (dlg.Desat)
            {
                var idx = _regles.IndexOf(vm);
                _regles[idx] = new ReglaViewModel(dlg.ReglaEditada!);
                ActualitzarBuit();
                OnReglesCanviades?.Invoke(ObtenirRegles());
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not ReglaViewModel vm) return;
            if (MessageBox.Show($"¿Eliminar la regla «{vm.Nom}»?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _regles.Remove(vm);
            ActualitzarBuit();
            OnReglesCanviades?.Invoke(ObtenirRegles());
        }
    }

    public class ReglaViewModel
    {
        public ProtocolRegla Regla { get; }
        public string Nom => Regla.Nom;
        public string Resum => Regla.Condicions.Count == 0
            ? "(sin condiciones)"
            : string.Join(Regla.Logica == LogicaCondicions.Totes ? " I " : " O ",
                Regla.Condicions.Select(c =>
                    $"{c.ParamNom} {c.Operador.Etiqueta()} \"{c.Valor}\""));

        public ReglaViewModel(ProtocolRegla r) => Regla = r;
    }
}