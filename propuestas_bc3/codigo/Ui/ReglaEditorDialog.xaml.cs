// ============================================================================
//  ReglaEditorDialog.xaml.cs
//  Modal de creación/edición de regla. El buscador de partida cruza todos los
//  catálogos cargados de la Plantilla y muestra badge de procedencia por
//  resultado.
// ============================================================================

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Ui
{
    public partial class ReglaEditorDialog : Window
    {
        private readonly Plantilla _plantilla;
        private readonly Regla _reglaOriginal;
        private readonly CategoriaSistema _categoria;

        private Partida _partidaSeleccionada;
        private CatalogoPartidas _catalogoSeleccionado;

        public Regla ReglaEditada { get; private set; }

        public ReglaEditorDialog(Plantilla plantilla, Regla reglaInicial, CategoriaSistema categoria)
        {
            InitializeComponent();
            _plantilla = plantilla ?? Plantilla.Vacia();
            _reglaOriginal = reglaInicial;
            _categoria = categoria;

            LblSubtitulo.Text = NombrarCategoria(categoria) + " · " +
                (reglaInicial == null ? "Nova regla" : "Editar regla existent");

            CargarValoresIniciales();
        }

        private void CargarValoresIniciales()
        {
            if (_reglaOriginal == null)
            {
                TxtNombre.Text = "";
                TxtDiamMin.Text = "0";
                TxtDiamMax.Text = "999";
                SeleccionarCmbItemPorTag(CmbSubFiltro, "rangoDiametro");
                SeleccionarCmbItemPorTag(CmbCriterio, "longitudTotal");
                MostrarPanelSubFiltro("rangoDiametro");
                _partidaSeleccionada = null;
                _catalogoSeleccionado = null;
                ActualizarPartidaUI();
                return;
            }

            TxtNombre.Text = _reglaOriginal.Nombre ?? "";

            switch (_reglaOriginal.SubFiltro)
            {
                case SubFiltroNinguno:
                    SeleccionarCmbItemPorTag(CmbSubFiltro, "ninguno");
                    MostrarPanelSubFiltro("ninguno");
                    break;
                case SubFiltroRangoDiametro rd:
                    SeleccionarCmbItemPorTag(CmbSubFiltro, "rangoDiametro");
                    TxtDiamMin.Text = rd.MinMm.ToString("0.##", CultureInfo.InvariantCulture);
                    TxtDiamMax.Text = double.IsPositiveInfinity(rd.MaxMm)
                        ? "999"
                        : rd.MaxMm.ToString("0.##", CultureInfo.InvariantCulture);
                    MostrarPanelSubFiltro("rangoDiametro");
                    break;
                case SubFiltroParametro sp:
                    SeleccionarCmbItemPorTag(CmbSubFiltro, "parametro");
                    TxtParamNombre.Text = sp.ParametroNombre ?? "";
                    TxtParamValor.Text  = sp.ValorComparado ?? "";
                    CmbOperador.SelectedIndex = (int)sp.Operador;
                    MostrarPanelSubFiltro("parametro");
                    break;
            }

            SeleccionarCmbItemPorTag(CmbCriterio,
                _reglaOriginal.Criterio is CriterioContar ? "contar" : "longitudTotal");

            // Resolver partida con desambiguación por catálogo.
            _partidaSeleccionada = _plantilla.BuscarPartida(
                _reglaOriginal.PartidaCodigo, _reglaOriginal.PartidaCatalogoId);
            _catalogoSeleccionado = _plantilla.LocalizarCatalogoDePartida(
                _reglaOriginal.PartidaCodigo, _reglaOriginal.PartidaCatalogoId);
            ActualizarPartidaUI();
        }

        private void CmbSubFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSubFiltro.SelectedItem is ComboBoxItem cbi)
                MostrarPanelSubFiltro(cbi.Tag as string);
        }

        private void MostrarPanelSubFiltro(string tipo)
        {
            PnlRangoDiametro.Visibility = tipo == "rangoDiametro" ? Visibility.Visible : Visibility.Collapsed;
            PnlParametro.Visibility    = tipo == "parametro"     ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── Buscador multi-catálogo ───────────────────────────────────────
        private void TxtBuscarPartida_TextChanged(object sender, TextChangedEventArgs e)
        {
            var termino = TxtBuscarPartida.Text;
            if (string.IsNullOrWhiteSpace(termino))
            {
                LstResultadosPartidas.Visibility = Visibility.Collapsed;
                return;
            }

            // Cruza todos los catálogos cargados (primario primero).
            var resultados = _plantilla.BuscarPartidaPorTexto(termino, maxPorCatalogo: 30).ToList();

            LstResultadosPartidas.ItemsSource = resultados;
            LstResultadosPartidas.Visibility = resultados.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LstResultadosPartidas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstResultadosPartidas.SelectedItem is ResultadoBusquedaPartida r)
            {
                _partidaSeleccionada = r.Partida;
                _catalogoSeleccionado = r.Catalogo;
                TxtBuscarPartida.Text = "";
                LstResultadosPartidas.Visibility = Visibility.Collapsed;
                ActualizarPartidaUI();
            }
        }

        private void ActualizarPartidaUI()
        {
            if (_partidaSeleccionada == null)
            {
                LblPartidaCodigo.Text  = "—";
                LblPartidaDesc.Text    = "(cap partida seleccionada)";
                LblPartidaPrecio.Text  = "";
                LblPartidaCatalogo.Text = "";
                LblPartidaCatalogo.Visibility = Visibility.Collapsed;
                return;
            }
            LblPartidaCodigo.Text   = _partidaSeleccionada.Codigo;
            LblPartidaDesc.Text     = _partidaSeleccionada.Descripcion;
            LblPartidaPrecio.Text   = $"{_partidaSeleccionada.Precio:0.00} €/{_partidaSeleccionada.Unidad}";
            LblPartidaCatalogo.Text = _catalogoSeleccionado?.Etiqueta ?? "(catàleg desconegut)";
            LblPartidaCatalogo.Visibility = Visibility.Visible;
        }

        private void BtnDesar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show(this, "Cal posar un nom a la regla.", "Falta informació",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                TxtNombre.Focus();
                return;
            }
            if (_partidaSeleccionada == null)
            {
                MessageBox.Show(this, "Cal seleccionar una partida del catàleg.", "Falta informació",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReglaEditada = new Regla
            {
                Id              = _reglaOriginal?.Id ?? Guid.NewGuid(),
                Nombre          = TxtNombre.Text.Trim(),
                Categoria       = _categoria,
                SubFiltro       = ConstruirSubFiltro(),
                Criterio        = ConstruirCriterio(),
                PartidaCodigo   = _partidaSeleccionada.Codigo,
                PartidaCatalogoId = _catalogoSeleccionado?.Id ?? Guid.Empty,
                CapituloDestino = CapituloPorCategoria(_categoria),
                Activa          = _reglaOriginal?.Activa ?? true,
                Notas           = _reglaOriginal?.Notas
            };
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private ISubFiltro ConstruirSubFiltro()
        {
            var tag = (CmbSubFiltro.SelectedItem as ComboBoxItem)?.Tag as string;
            switch (tag)
            {
                case "ninguno":
                    return new SubFiltroNinguno();
                case "rangoDiametro":
                    double.TryParse(TxtDiamMin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var min);
                    double.TryParse(TxtDiamMax.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var max);
                    if (max <= 0) max = double.PositiveInfinity;
                    return new SubFiltroRangoDiametro { MinMm = min, MaxMm = max };
                case "parametro":
                    return new SubFiltroParametro
                    {
                        ParametroNombre = TxtParamNombre.Text?.Trim(),
                        ValorComparado  = TxtParamValor.Text?.Trim(),
                        Operador        = (OpComparacion)CmbOperador.SelectedIndex
                    };
                default:
                    return new SubFiltroNinguno();
            }
        }

        private ICriterio ConstruirCriterio()
        {
            var tag = (CmbCriterio.SelectedItem as ComboBoxItem)?.Tag as string;
            return tag == "contar" ? (ICriterio)new CriterioContar() : new CriterioLongitudTotal();
        }

        private static void SeleccionarCmbItemPorTag(ComboBox cmb, string tag)
        {
            foreach (var it in cmb.Items)
                if (it is ComboBoxItem cbi && (cbi.Tag as string) == tag)
                {
                    cmb.SelectedItem = cbi;
                    return;
                }
        }

        private static string CapituloPorCategoria(CategoriaSistema c) => c switch
        {
            CategoriaSistema.SaneamientoFecales   => "02SAN",
            CategoriaSistema.SaneamientoPluviales => "02SAN",
            CategoriaSistema.SaneamientoGrises    => "02SAN",
            CategoriaSistema.FontaneriaAFS        => "02FON",
            CategoriaSistema.FontaneriaACS        => "02FON",
            CategoriaSistema.FontaneriaAR         => "02FON",
            CategoriaSistema.PCIRociadores        => "02PCI",
            CategoriaSistema.PCIBIES              => "02PCI",
            _                                     => "02XXX"
        };

        private static string NombrarCategoria(CategoriaSistema c) => c switch
        {
            CategoriaSistema.SaneamientoFecales   => "Sanejament — Fecals",
            CategoriaSistema.SaneamientoPluviales => "Sanejament — Pluvials",
            CategoriaSistema.SaneamientoGrises    => "Sanejament — Grises",
            CategoriaSistema.FontaneriaAFS        => "Fontaneria — AFS",
            CategoriaSistema.FontaneriaACS        => "Fontaneria — ACS",
            CategoriaSistema.FontaneriaAR         => "Fontaneria — AR",
            CategoriaSistema.PCIRociadores        => "PCI — Ruixadors",
            CategoriaSistema.PCIBIES              => "PCI — BIES",
            _                                     => c.ToString()
        };
    }
}
