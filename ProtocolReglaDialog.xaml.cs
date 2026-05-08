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
        public bool Desat { get; private set; } = false;

        private readonly ObservableCollection<CondicioViewModel> _condicions = new();
        private readonly Document? _doc;

        public ProtocolReglaDialog(ProtocolRegla? regla, Document? doc = null)
        {
            InitializeComponent();
            _doc = doc;
            LstCondicions.ItemsSource = _condicions;

            // ── Inicialitzar CmbAplicaA amb categories del model ─────────────
            var categories = new List<CategoriaRevitItem>();
            if (doc != null)
            {
                try
                {
                    var catsMEP = new (string Nom, int Valor)[]
                    {
                        ("Canonades (OST_PipeCurves)",            (int)BuiltInCategory.OST_PipeCurves),
                        ("Accessoris canonada (OST_PipeFitting)", (int)BuiltInCategory.OST_PipeFitting),
                        ("Equips mecànics",                       (int)BuiltInCategory.OST_MechanicalEquipment),
                        ("Aparells sanitaris",                    (int)BuiltInCategory.OST_PlumbingFixtures),
                        ("Conductes (OST_DuctCurves)",            (int)BuiltInCategory.OST_DuctCurves),
                    };
                    foreach (var c in catsMEP)
                        categories.Add(new CategoriaRevitItem { Nom = c.Nom, Valor = c.Valor });

                    categories.Add(new CategoriaRevitItem { Nom = "─── Altres categories ───", Valor = -99 });

                    var cats = doc.Settings.Categories
                        .Cast<Category>()
                        .Where(cat => cat != null && cat.CategoryType == CategoryType.Model)
                        .Where(cat => !categories.Any(c => c.Valor == (int)cat.Id.IntegerValue))
                        .OrderBy(cat => cat.Name);
                    foreach (var cat in cats)
                    {
                        categories.Add(new CategoriaRevitItem
                        {
                            Nom = cat.Name,
                            Valor = (int)cat.Id.IntegerValue
                        });
                    }
                }
                catch { }
            }

            if (categories.Count == 0)
            {
                categories.Add(new CategoriaRevitItem { Nom = "Canonades (OST_PipeCurves)", Valor = (int)BuiltInCategory.OST_PipeCurves });
                categories.Add(new CategoriaRevitItem { Nom = "Grup tipus (GroupType)", Valor = -1 });
                categories.Add(new CategoriaRevitItem { Nom = "Nivell (Level)", Valor = -2 });
                categories.Add(new CategoriaRevitItem { Nom = "Workset", Valor = -3 });
            }

            CmbAplicaA.ItemsSource = categories;
            CmbAplicaA.DisplayMemberPath = "Nom";
            CmbAplicaA.SelectedValuePath = "Valor";
            CmbAplicaA.SelectedIndex = 0;

            // ── Inicialitzar CmbLogica ───────────────────────────────────────
            CmbLogica.Items.Add(new ComboBoxItem { Content = "TOTES (AND)", Tag = 0 });
            CmbLogica.Items.Add(new ComboBoxItem { Content = "QUALSEVOL (OR)", Tag = 1 });
            CmbLogica.SelectedIndex = 0;

            // ── Carregar dades de la regla existent ──────────────────────────
            if (regla != null)
            {
                TxtNom.Text = regla.Nom;

                var item = categories.FirstOrDefault(c => c.Valor == (int)regla.Aplica);
                if (item != null) CmbAplicaA.SelectedItem = item;

                foreach (ComboBoxItem ci in CmbLogica.Items)
                    if ((int)(ci.Tag ?? 0) == (int)regla.Logica)
                    { ci.IsSelected = true; break; }

                foreach (var c in regla.Condicions)
                    _condicions.Add(new CondicioViewModel(c, doc));
            }

            if (_condicions.Count == 0)
                _condicions.Add(new CondicioViewModel(null, doc));

            // ── Quan canvia la categoria, actualitzem els paràmetres ─────────
            CmbAplicaA.SelectionChanged += (s, _) => ActualitzarParamsPerCategoria();
            ActualitzarParamsPerCategoria();
        }

        // ── Actualitza els paràmetres de totes les condicions ────────────────
        private void ActualitzarParamsPerCategoria()
        {
            if (CmbAplicaA.SelectedItem is not CategoriaRevitItem cat) return;
            if (cat.Valor == -99) return; // separador
            foreach (var vm in _condicions)
                vm.ActualitzarParamsPerCategoria(_doc, cat.Valor);
        }

        private void BtnAfegirCondicio_Click(object sender, RoutedEventArgs e)
        {
            var nova = new CondicioViewModel(null, _doc);
            if (CmbAplicaA.SelectedItem is CategoriaRevitItem cat && cat.Valor != -99)
                nova.ActualitzarParamsPerCategoria(_doc, cat.Valor);
            _condicions.Add(nova);
        }

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

            // Determinar ElementTipus a partir del BuiltInCategory seleccionat
            var catSeleccionada = CmbAplicaA.SelectedItem as CategoriaRevitItem;
            var aplica = catSeleccionada?.Valor switch
            {
                (int)BuiltInCategory.OST_PipeCurves => ElementTipus.Canonada,
                (int)BuiltInCategory.OST_PipeFitting => ElementTipus.Canonada,
                -1 => ElementTipus.GrupTipus,
                -2 => ElementTipus.Nivell,
                -3 => ElementTipus.Workset,
                _ => ElementTipus.Canonada
            };

            var logica = (CmbLogica.SelectedItem as ComboBoxItem)?.Tag is int t
                ? (LogicaCondicions)t
                : LogicaCondicions.Totes;

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

    // ── ViewModel per a cada condició ────────────────────────────────────────
    public class CondicioViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string p) => PropertyChanged?.Invoke(this, new(p));

        private string _param = "Type Name";
        private FilterOperator _operador = FilterOperator.Conte;
        private string _valor = "";
        private readonly Document? _doc;

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

        public ObservableCollection<string> ParamsDisponibles { get; } = new();
        public ObservableCollection<string> ValorsSuggerits { get; } = new();

        public CondicioViewModel(FilterCondicio? c, Document? doc)
        {
            _doc = doc;
            if (c != null) { _param = c.ParamNom; _operador = c.Operador; _valor = c.Valor; }
        }

        /// <summary>
        /// Recarrega els paràmetres filtrats per la categoria BuiltIn seleccionada.
        /// </summary>
        public void ActualitzarParamsPerCategoria(Document? doc, int categoriaBuiltIn)
        {
            ParamsDisponibles.Clear();

            // Paràmetres virtuals geomètrics per canonades
            if (categoriaBuiltIn == (int)BuiltInCategory.OST_PipeCurves ||
                categoriaBuiltIn == (int)BuiltInCategory.OST_PipeFitting)
            {
                ParamsDisponibles.Add(ParamVirtual.Vertical);
                ParamsDisponibles.Add(ParamVirtual.Horitzontal);
                ParamsDisponibles.Add(ParamVirtual.LongitudM);
                ParamsDisponibles.Add(ParamVirtual.ElevacioNivell_M);
            }

            if (doc == null)
            {
                Notify(nameof(ParamsDisponibles));
                return;
            }

            try
            {
                var element = new FilteredElementCollector(doc)
                    .OfCategory((BuiltInCategory)categoriaBuiltIn)
                    .WhereElementIsNotElementType()
                    .FirstElement();

                if (element != null)
                {
                    foreach (Parameter p in element.Parameters)
                    {
                        var nom = p.Definition?.Name;
                        if (!string.IsNullOrEmpty(nom) && !ParamsDisponibles.Contains(nom))
                            ParamsDisponibles.Add(nom);
                    }
                }
                else
                {
                    // Cap element d'aquesta categoria al model — params genèrics
                    foreach (var p in new[] { "Type Name", "Name", "Comments", "Mark", "Workset" })
                        if (!ParamsDisponibles.Contains(p)) ParamsDisponibles.Add(p);
                }
            }
            catch { }

            Notify(nameof(ParamsDisponibles));
        }

        private void ActualitzarSuggeriments()
        {
            ValorsSuggerits.Clear();
            if (_doc == null) return;
            try
            {
                if (_param == "Workset")
                {
                    var worksets = new FilteredWorksetCollector(_doc)
                        .OfKind(WorksetKind.UserWorkset).ToList();
                    foreach (var ws in worksets)
                        ValorsSuggerits.Add(ws.Name);
                }
                else if (_param == "Reference Level")
                {
                    var nivells = new FilteredElementCollector(_doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation).Select(l => l.Name);
                    foreach (var n in nivells)
                        ValorsSuggerits.Add(n);
                }
                else if (_param == ParamVirtual.Vertical ||
                         _param == ParamVirtual.Horitzontal)
                {
                    ValorsSuggerits.Add("true");
                    ValorsSuggerits.Add("false");
                }
            }
            catch { }
            Notify(nameof(ValorsSuggerits));
        }
    }

    public class CategoriaRevitItem
    {
        public string Nom { get; set; } = "";
        public int Valor { get; set; }
    }

    public record OperadorItem(FilterOperator Op)
    {
        public string Display => Op.Etiqueta();
    }
}