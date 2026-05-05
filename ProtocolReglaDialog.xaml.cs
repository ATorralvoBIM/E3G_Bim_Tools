using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Protocol;

namespace E3G.Saneamiento
{
    public partial class ProtocolReglaDialog : Window
    {
        public ProtocolRegla? ReglaEditada { get; private set; }
        private readonly ObservableCollection<CondicioViewModel> _condicions = new();

        public ProtocolReglaDialog(ProtocolRegla? regla)
        {
            InitializeComponent();
            LstCondicions.ItemsSource = _condicions;

            if (regla != null)
            {
                TxtNom.Text = regla.Nom;
                // Aplica
                foreach (ComboBoxItem item in CmbAplicaA.Items)
                    if ((int)(item.Tag ?? 0) == (int)regla.Aplica)
                    { item.IsSelected = true; break; }
                // Lògica
                foreach (ComboBoxItem item in CmbLogica.Items)
                    if ((int)(item.Tag ?? 0) == (int)regla.Logica)
                    { item.IsSelected = true; break; }
                // Condicions
                foreach (var c in regla.Condicions)
                    _condicions.Add(new CondicioViewModel(c));
            }

            if (_condicions.Count == 0)
                _condicions.Add(new CondicioViewModel(null));
        }

        private void BtnAfegirCondicio_Click(object sender, RoutedEventArgs e)
            => _condicions.Add(new CondicioViewModel(null));

        private void BtnEliminarCondicio_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CondicioViewModel vm)
                _condicions.Remove(vm);
        }

        private void BtnDesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text))
            {
                MessageBox.Show("El nom de la regla és obligatori.", "Validació",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var aplica = (ElementTipus)(int)((CmbAplicaA.SelectedItem as ComboBoxItem)?.Tag ?? 0);
            var logica = (LogicaCondicions)(int)((CmbLogica.SelectedItem as ComboBoxItem)?.Tag ?? 0);

            ReglaEditada = new ProtocolRegla
            {
                Id = Guid.NewGuid(),
                Nom = TxtNom.Text.Trim(),
                Aplica = aplica,
                Logica = logica,
                Condicions = _condicions.Select(vm => new FilterCondicio
                {
                    ParamNom = vm.Param,
                    Operador = vm.Operador,
                    Valor = vm.Valor
                }).ToList()
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // ViewModel per a cada condició — INotifyPropertyChanged perquè el binding funcioni
    public class CondicioViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string p) => PropertyChanged?.Invoke(this, new(p));

        private string _param = "Type Name";
        private FilterOperator _operador = FilterOperator.Conte;
        private string _valor = "";

        public string Param
        {
            get => _param;
            set { _param = value; Notify(nameof(Param)); }
        }
        public FilterOperator Operador
        {
            get => _operador;
            set { _operador = value; Notify(nameof(Operador)); }
        }
        public string Valor
        {
            get => _valor;
            set { _valor = value; Notify(nameof(Valor)); }
        }

        // Per al ComboBox d'operadors
        public IEnumerable<OperadorItem> Operadors { get; } =
            Enum.GetValues(typeof(FilterOperator))
                .Cast<FilterOperator>()
                .Select(op => new OperadorItem(op))
                .ToList();

        public CondicioViewModel(FilterCondicio? c)
        {
            if (c != null) { _param = c.ParamNom; _operador = c.Operador; _valor = c.Valor; }
        }
    }

    public record OperadorItem(FilterOperator Op)
    {
        public string Display => Op.Etiqueta();
    }
}