// ============================================================================
//  SystemPickerDialog.xaml.cs
//  Cuadro de seleccion WPF para PipingSystem / MechanicalSystem que reemplaza
//  el TaskDialog limitado a 4 entradas.  Soporta filtro por texto, navegacion
//  por teclado y doble clic.  El payload puede ser cualquier objeto.
//
//  Sprint 6 - Gap 3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace E3G.Saneamiento
{
    /// <summary>Dialogo escalable para escoger un sistema MEP del modelo.</summary>
    public partial class SystemPickerDialog : Window
    {
        /// <summary>Item de la lista.  Payload guarda la referencia al sistema.</summary>
        public class Item
        {
            public string Label    { get; set; }
            public string Sublabel { get; set; }
            public object Payload  { get; set; }

            public override string ToString() => Label ?? "";
        }

        private List<Item> _all;

        /// <summary>Item seleccionado (null si el usuario cancelo).</summary>
        public Item Selected { get; private set; }

        /// <summary>
        /// Crea el dialogo con un encabezado, un subtitulo y la lista de items.
        /// </summary>
        public SystemPickerDialog(string header, string subheader, IEnumerable<Item> items)
        {
            InitializeComponent();
            E3G.Saneamiento.UI.E3gChrome.Apply(this);
            LblHeader.Text    = string.IsNullOrEmpty(header)    ? "Selecciona un sistema MEP" : header;
            LblSubheader.Text = subheader ?? "";
            _all = (items ?? Enumerable.Empty<Item>()).ToList();
            ApplyFilter(null);
            if (LstSystems.Items.Count > 0) LstSystems.SelectedIndex = 0;
            Loaded += (_, __) => TxtFilter.Focus();
        }

        private void ApplyFilter(string query)
        {
            IEnumerable<Item> filtered = _all;
            if (!string.IsNullOrEmpty(query))
            {
                var q = query.Trim();
                filtered = _all.Where(it =>
                       (it.Label    ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || (it.Sublabel ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var list = filtered.ToList();
            LstSystems.ItemsSource = list;
            LblCount.Text = list.Count + " de " + _all.Count + " sistemas";
            if (list.Count > 0) LstSystems.SelectedIndex = 0;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(TxtFilter.Text);

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Selected = LstSystems.SelectedItem as Item;
            DialogResult = Selected != null;
            Close();
        }

        private void LstSystems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstSystems.SelectedItem is Item)
                BtnOk_Click(sender, null);
        }

        private void LstSystems_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && LstSystems.SelectedItem is Item)
            {
                BtnOk_Click(sender, null);
                e.Handled = true;
            }
        }
    }
}
