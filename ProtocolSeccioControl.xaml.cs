using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Protocol;

namespace E3G.Saneamiento
{
    public partial class ProtocolSeccioControl : UserControl
    {
        private List<ProtocolRegla> _regles = new();

        public string Titol
        {
            get => TxtTitol.Text;
            set => TxtTitol.Text = value;
        }

        public ProtocolSeccioControl() => InitializeComponent();

        public void CarregarRegles(List<ProtocolRegla> regles)
        {
            _regles = new List<ProtocolRegla>(regles);
            Refrescar();
        }

        public List<ProtocolRegla> ObtenirRegles() => new(_regles);

        private void Refrescar()
        {
            LstRegles.ItemsSource = _regles.Select(r => new
            {
                r.Id,
                r.Nom,
                Resum = r.Condicions.Count == 0
                    ? "(cap condició)"
                    : string.Join(r.Logica == LogicaCondicions.Totes ? " I " : " O ",
                                  r.Condicions.Select(c => $"{c.ParamNom} {c.Operador.Etiqueta()} \"{c.Valor}\""))
            }).ToList();
            TxtBuit.Visibility = _regles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAfegir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ProtocolReglaDialog(null);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true)
            {
                _regles.Add(dlg.ReglaEditada!);
                Refrescar();
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Guid id) return;
            var regla = _regles.FirstOrDefault(r => r.Id == id);
            if (regla == null) return;
            var dlg = new ProtocolReglaDialog(regla);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true)
            {
                var idx = _regles.IndexOf(regla);
                _regles[idx] = dlg.ReglaEditada!;
                Refrescar();
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not Guid id) return;
            if (MessageBox.Show("Eliminar aquesta regla?", "Confirmar",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _regles.RemoveAll(r => r.Id == id);
            Refrescar();
        }
    }
}