using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Protocol;

namespace E3G.Saneamiento
{
    public partial class ProtocolReglaDialog : Window
    {
        public ProtocolRegla? ReglaEditada { get; private set; }
        private readonly List<CondicioViewModel> _condicions = new();

        public ProtocolReglaDialog(ProtocolRegla? regla)
        {
            InitializeComponent();
            if (regla != null)
            {
                TxtNom.Text = regla.Nom;
                // Aplica
                foreach (ComboBoxItem item in CmbAplicaA.Items)
                    if ((int)item.Tag == (int)regla.Aplica) { item.IsSelected = true; break; }
                // Lògica
                foreach (ComboBoxItem item in CmbLogica.Items)
                    if ((int)item.Tag == (int)regla.Logica) { item.IsSelected = true; break; }
                // Condicions
                foreach (var c in regla.Condicions)
                    _condicions.Add(new CondicioViewModel(c));
            }
            else
            {
                // Nova regla: una condició buida per defecte
                _condicions.Add(new CondicioViewModel(null));
            }
            RefrescarCondicions();
        }

        private void RefrescarCondicions()
        {
            var panel = new StackPanel();
            foreach (var vm in _condicions)
            {
                var fila = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                fila.ColumnDefinitions.Add(new ColumnDefinition { Width = System.Windows.GridLength.Auto });

                // Paràmetre
                var cmbParam = new ComboBox { IsEditable = true, FontSize = 11, Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0) };
                AfegirParamsAlCombo(cmbParam);
                cmbParam.Text = vm.Param;
                cmbParam.SelectionChanged += (s, _) => vm.Param = (cmbParam.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? cmbParam.Text;
                cmbParam.LostFocus += (s, _) => vm.Param = cmbParam.Text;
                Grid.SetColumn(cmbParam, 0);
                fila.Children.Add(cmbParam);

                // Operador
                var cmbOp = new ComboBox { FontSize = 11, Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0) };
                foreach (FilterOperator op in Enum.GetValues(typeof(FilterOperator)))
                    cmbOp.Items.Add(new ComboBoxItem { Content = op.Etiqueta(), Tag = op });
                cmbOp.SelectedIndex = (int)vm.Operador;
                cmbOp.SelectionChanged += (s, _) => {
                    if ((cmbOp.SelectedItem as ComboBoxItem)?.Tag is FilterOperator op)
                        vm.Operador = op;
                };
                Grid.SetColumn(cmbOp, 1);
                fila.Children.Add(cmbOp);

                // Valor
                var txtValor = new TextBox { Text = vm.Valor, FontSize = 11, Padding = new Thickness(4), Margin = new Thickness(0, 0, 4, 0) };
                txtValor.TextChanged += (s, _) => vm.Valor = txtValor.Text;
                Grid.SetColumn(txtValor, 2);
                fila.Children.Add(txtValor);

                // Eliminar
                var btnDel = new Button { Content = "✕", Width = 24, Height = 24, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = System.Windows.Media.Brushes.Crimson, Tag = vm };
                btnDel.Click += (s, _) => { _condicions.Remove(vm); RefrescarCondicions(); };
                Grid.SetColumn(btnDel, 3);
                fila.Children.Add(btnDel);

                panel.Children.Add(fila);
            }
            LstCondicions.ItemsSource = null;
            LstCondicions.Items.Clear();
            LstCondicions.ItemsSource = new[] { panel };
        }

        private void AfegirParamsAlCombo(ComboBox cmb)
        {
            // Paràmetres comuns
            foreach (var (display, tag) in new[]
            {
                ("Nom de Tipus", "Type Name"), ("Nom", "Name"), ("Comentaris", "Comments"),
                ("Mark", "Mark"), ("Workset", "Workset"), ("Nivell Referència", "Reference Level"),
                ("--- Geomètrics ---", ""), ("Vertical", ParamVirtual.Vertical),
                ("Horitzontal", ParamVirtual.Horitzontal), ("Longitud (m)", ParamVirtual.LongitudM),
                ("Elevació nivell (m)", ParamVirtual.ElevacioNivell_M)
            })
            {
                cmb.Items.Add(new ComboBoxItem { Content = display, Tag = tag, IsEnabled = tag != "" });
            }

            // Paràmetres compartits del projecte
            var doc = SaneamientoSession.UiApp?.ActiveUIDocument?.Document;
            if (doc != null)
            {
                cmb.Items.Add(new ComboBoxItem { Content = "--- Paràm. compartits ---", IsEnabled = false });
                try
                {
                    var bindingMap = doc.ParameterBindings;
                    var it = bindingMap.ForwardIterator();
                    while (it.MoveNext())
                    {
                        var def = it.Key;
                        cmb.Items.Add(new ComboBoxItem { Content = def.Name, Tag = def.Name });
                    }
                }
                catch { /* si no té accés, ignora */ }
            }
        }

        private void BtnAfegirCondicio_Click(object sender, RoutedEventArgs e)
        {
            _condicions.Add(new CondicioViewModel(null));
            RefrescarCondicions();
        }

        private void BtnDesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text))
            {
                MessageBox.Show("El nom de la regla és obligatori.");
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
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private class CondicioViewModel
        {
            public string Param { get; set; }
            public FilterOperator Operador { get; set; }
            public string Valor { get; set; }
            public CondicioViewModel(FilterCondicio? c)
            {
                Param = c?.ParamNom ?? "Type Name";
                Operador = c?.Operador ?? FilterOperator.Conte;
                Valor = c?.Valor ?? "";
            }
        }
    }
}