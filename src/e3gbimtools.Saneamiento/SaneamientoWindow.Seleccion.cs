using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using WpfVisibility = System.Windows.Visibility;

namespace E3G.Saneamiento
{
    // Selección sincronizada entre las tablas de resultados y el modelo:
    //  · elegir filas selecciona (y resalta) esos tramos en Revit; doble clic, además, los encuadra;
    //  · seleccionar en Revit marca las filas correspondientes y despliega su grupo.
    // Parte de SaneamientoWindow.
    public partial class SaneamientoWindow
    {
        private static readonly List<WeakReference<SaneamientoWindow>> Abiertas = new List<WeakReference<SaneamientoWindow>>();

        /// <summary>Evita el rebote: al reflejar la selección de Revit no se vuelve a enviar a Revit.</summary>
        private bool _aplicandoSeleccionModelo;

        private void InicializarSeleccion()
        {
            lock (Abiertas) Abiertas.Add(new WeakReference<SaneamientoWindow>(this));
            foreach (var grid in new[] { TablaOk, TablaKo })
            {
                grid.SelectionMode = DataGridSelectionMode.Extended;
                grid.SelectionChanged += Tabla_SelectionChanged;
                grid.MouseDoubleClick += Tabla_MouseDoubleClick;
            }
        }

        private List<int> IdsSeleccionadosEnTablas() =>
            TablaOk.SelectedItems.Cast<object>().Concat(TablaKo.SelectedItems.Cast<object>())
                .OfType<FilaTabla>()
                .Select(f => int.TryParse(f.IdStr, out int id) ? id : 0)
                .Where(id => id != 0)
                .Distinct()
                .ToList();

        private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_aplicandoSeleccionModelo) return;
            // Un clic en una tabla sustituye la selección de la otra (Ctrl/Mayús siguen sumando en la misma).
            if (e.AddedItems.Count > 0 && Keyboard.Modifiers == ModifierKeys.None)
            {
                var otra = sender == TablaOk ? TablaKo : TablaOk;
                _aplicandoSeleccionModelo = true;
                try { otra.SelectedItems.Clear(); } finally { _aplicandoSeleccionModelo = false; }
            }
            var ids = IdsSeleccionadosEnTablas();
            if (ids.Count == 0) return;
            EnviarSeleccionARevit(ids, encuadrar: false);
        }

        private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var ids = IdsSeleccionadosEnTablas();
            if (ids.Count > 0) EnviarSeleccionARevit(ids, encuadrar: true);
        }

        private static void EnviarSeleccionARevit(List<int> ids, bool encuadrar)
        {
            if (SaneamientoSession.HandlerSeleccion == null || SaneamientoSession.EventoSel == null) return;
            SaneamientoSession.HandlerSeleccion.IdsASeleccionar = ids;
            SaneamientoSession.HandlerSeleccion.Encuadrar = encuadrar;
            SaneamientoSession.EventoSel.Raise();
        }

        /// <summary>Lo llama el evento SelectionChanged de Revit (hilo de la API).</summary>
        public static void NotificarSeleccionModelo(ICollection<ElementId> seleccion)
        {
            var ids = new HashSet<string>(seleccion.Select(id => id.Value.ToString()));
            List<SaneamientoWindow> paginas;
            lock (Abiertas)
            {
                Abiertas.RemoveAll(w => !w.TryGetTarget(out _));
                paginas = Abiertas.Select(w => w.TryGetTarget(out var p) ? p : null).Where(p => p != null).ToList();
            }
            foreach (var p in paginas)
                p.Dispatcher.BeginInvoke(new Action(() => p.ReflejarSeleccion(ids)));
        }

        private void ReflejarSeleccion(HashSet<string> ids)
        {
            // Si coincide con lo que ya hay marcado (viene de la propia tabla), no hacer nada.
            var actuales = new HashSet<string>(IdsSeleccionadosEnTablas().Select(i => i.ToString()));
            if (actuales.SetEquals(ids)) return;

            _aplicandoSeleccionModelo = true;
            try
            {
                Marcar(TablaOk, BodyOk, ArrowOk, ids);
                Marcar(TablaKo, BodyKo, ArrowKo, ids);
            }
            finally { _aplicandoSeleccionModelo = false; }
        }

        private static void Marcar(DataGrid grid, System.Windows.Controls.Border cuerpo, TextBlock flecha, HashSet<string> ids)
        {
            grid.SelectedItems.Clear();
            if (!(grid.ItemsSource is IEnumerable<FilaTabla> filas)) return;
            var marcadas = filas.Where(f => ids.Contains(f.IdStr)).ToList();
            if (marcadas.Count == 0) return;
            if (cuerpo.Visibility != WpfVisibility.Visible)
            {
                cuerpo.Visibility = WpfVisibility.Visible;
                flecha.Text = "▼";
            }
            foreach (var f in marcadas) grid.SelectedItems.Add(f);
            grid.ScrollIntoView(marcadas[0]);
        }
    }
}
