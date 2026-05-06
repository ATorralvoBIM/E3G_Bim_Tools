using Autodesk.Revit.DB;
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
        private readonly Document? _doc;

        public ProtocolReglaDialog(ProtocolRegla? regla, Document? doc = null)
        {
            InitializeComponent();
            // Categories disponibles
            var categories = new List<CategoriaRevitItem>
            {
                new() { Nom = "Canonada (OST_PipeCurves)",        Valor = (int)ElementTipus.Canonada },
                new() { Nom = "Grup tipus (GroupType)",            Valor = (int)ElementTipus.GrupTipus },
                new() { Nom = "Nivell (Level)",                    Valor = (int)ElementTipus.Nivell },
                new() { Nom = "Workset",                           Valor = (int)ElementTipus.Workset },
            };

            // Si tenim document, afegim categories MEP del projecte
            if (doc != null)
            {
                try
                {
                    var catsMEP = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.MEPCurve))
                        .Cast<Element>()
                        .Select(e => e.Category?.Name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .OrderBy(n => n);
                    foreach (var cat in catsMEP)
                        if (!categories.Any(c => c.Nom.Contains(cat!)))
                            categories.Add(new() { Nom = cat!, Valor = (int)ElementTipus.Canonada });
                }
                catch { }
            }

            CmbAplicaA.ItemsSource = categories;
            CmbAplicaA.SelectedIndex = 0;

            // Seleccionar la categoria de la regla existent si n'hi ha
            if (regla != null)
            {
                var item = categories.FirstOrDefault(c => c.Valor == (int)regla.Aplica);
                if (item != null) CmbAplicaA.SelectedItem = item;
            }

            _doc = doc;
            LstCondicions.ItemsSource = _condicions;

            if (regla != null)
            {
                TxtNom.Text = regla.Nom;
                foreach (ComboBoxItem item in CmbAplicaA.Items)
                    if ((int)(item.Tag ?? 0) == (int)regla.Aplica)
                    { item.IsSelected = true; break; }
                foreach (ComboBoxItem item in CmbLogica.Items)
                    if ((int)(item.Tag ?? 0) == (int)regla.Logica)
                    { item.IsSelected = true; break; }
                foreach (var c in regla.Condicions)
                    _condicions.Add(new CondicioViewModel(c, doc));
            }

            if (_condicions.Count == 0)
                _condicions.Add(new CondicioViewModel(null, doc));
        }

        private void BtnAfegirCondicio_Click(object sender, RoutedEventArgs e)
            => _condicions.Add(new CondicioViewModel(null, _doc));

        private void BtnEliminarCondicio_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CondicioViewModel vm)
                _condicions.Remove(vm);
        }

        public bool Desat { get; private set; } = false;

        private void BtnDesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text))
            {
                MessageBox.Show("El nom de la regla és obligatori.", "Validació",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var aplica = (ElementTipus)((CmbAplicaA.SelectedItem as CategoriaRevitItem)?.Valor ?? 0);
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

            Desat = true;
            try { DialogResult = true; } catch { }
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            Desat = false;
            try { DialogResult = false; } catch { }
            Close();
        }
    }

    // Afegeix aquesta classe al fitxer (fora de ProtocolReglaDialog)
    public class CategoriaRevitItem
    {
        public string Nom { get; set; } = "";
        public int Valor { get; set; }
    }


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
            set { _param = value; Notify(nameof(Param)); ActualitzarSuggeriments(); }
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

        public IEnumerable<OperadorItem> Operadors { get; } =
            Enum.GetValues(typeof(FilterOperator))
                .Cast<FilterOperator>()
                .Select(op => new OperadorItem(op))
                .ToList();

        // Paràmetres disponibles al projecte
        public ObservableCollection<string> ParamsDisponibles { get; } = new();

        // Suggeriments de valor (worksets, nivells, etc.)
        public ObservableCollection<string> ValorsSuggerits { get; } = new();

        private readonly Document? _doc;

        public CondicioViewModel(FilterCondicio? c, Document? doc)
        {
            _doc = doc;
            if (c != null) { _param = c.ParamNom; _operador = c.Operador; _valor = c.Valor; }
            CarregarParams();
            ActualitzarSuggeriments();
        }

        private void CarregarParams()
        {
            // Paràmetres fixos sempre disponibles
            var fixos = new[]
            {
            "Type Name", "Name", "Comments", "Mark", "Workset", "Reference Level",
            "Nombre de sistema", "Tamaño",
            Protocol.ParamVirtual.Vertical, Protocol.ParamVirtual.Horitzontal,
            Protocol.ParamVirtual.LongitudM, Protocol.ParamVirtual.ElevacioNivell_M
        };
            foreach (var p in fixos)
                ParamsDisponibles.Add(p);

            // Paràmetres compartits del projecte obert
            if (_doc == null) return;
            try
            {
                var it = _doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    var nom = (it.Key as Autodesk.Revit.DB.Definition)?.Name;
                    if (!string.IsNullOrEmpty(nom) && !ParamsDisponibles.Contains(nom))
                        ParamsDisponibles.Add(nom);
                }
            }
            catch { /* si falla, continua amb els fixos */ }
        }

        private void ActualitzarSuggeriments()
        {
            ValorsSuggerits.Clear();
            if (_doc == null) return;

            try
            {
                if (_param == "Workset")
                {
                    // Llegeix els worksets del projecte
                    var table = _doc.GetWorksetTable();
                    var worksets = new Autodesk.Revit.DB.FilteredWorksetCollector(_doc)
                        .OfKind(Autodesk.Revit.DB.WorksetKind.UserWorkset)
                        .ToList();
                    foreach (var ws in worksets)
                        ValorsSuggerits.Add(ws.Name);
                }
                else if (_param == "Reference Level" || _param == "Name" &&
                         _param.Contains("Level"))
                {
                    // Llegeix els nivells del projecte
                    var nivells = new Autodesk.Revit.DB.FilteredElementCollector(_doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Level))
                        .Cast<Autodesk.Revit.DB.Level>()
                        .OrderBy(l => l.Elevation)
                        .Select(l => l.Name);
                    foreach (var n in nivells)
                        ValorsSuggerits.Add(n);
                }
                else if (_param == Protocol.ParamVirtual.Vertical ||
                         _param == Protocol.ParamVirtual.Horitzontal)
                {
                    ValorsSuggerits.Add("true");
                    ValorsSuggerits.Add("false");
                }
            }
            catch { /* si falla, deixa buit */ }

            Notify(nameof(ValorsSuggerits));
        }
    }
    public record OperadorItem(FilterOperator Op)
    {
        public string Display => Op.Etiqueta();
    }
}