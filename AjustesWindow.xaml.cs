using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaColors = System.Windows.Media.Colors;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfVisibility = System.Windows.Visibility;

namespace E3G.Saneamiento
{
    public partial class AjustesWindow : Page
    {
        private Button _btnActiu;
        private readonly UIApplication _uiApp;
        private readonly ClasificacionSistemasService _clasificacioService = new ClasificacionSistemasService();
        private E3G.Saneamiento.Presupuesto.Ui.PartidasViewModel _vmPartides;


        public AjustesWindow(UIApplication uiApp = null)
        {
            InitializeComponent();
            _uiApp = uiApp;
            Loaded += (s, e) => NavegarA(SecIdioma, BtnNavIdioma);
        }

        // ════════════════════════════════════════════════
        // NAVEGACIÓ SETTINGS SIDEBAR
        // ════════════════════════════════════════════════

        private void BtnNavIdioma_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecIdioma, BtnNavIdioma);

        private void BtnNavUnidades_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecUnidades, BtnNavUnidades);

        private void BtnNavConfirmaciones_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecConfirmaciones, BtnNavConfirmaciones);

        private void BtnNavNomenclatura_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecNomenclatura, BtnNavNomenclatura);
        private void BtnNavClassificacio_Click(object sender, RoutedEventArgs e)
        {
            NavegarA(SecClassificacio, BtnNavClassificacio);
            CarregarClassificacio();
        }

        private void BtnRecalcularSugerencias_Click(object sender, RoutedEventArgs e)
            => CarregarClassificacio();

        private void CarregarClassificacio()
        {
            try { CarregarClassificacioInternal(); }
            catch (System.Exception ex)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = $"⚠ Error en carregar classificació: {ex.Message}";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "Error";
            }
        }

        private void CarregarClassificacioInternal()
        {
            var ui = _uiApp ?? SaneamientoSession.UiApp;
            if (ui == null)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = "[Diag 1/4] UIApplication no capturada encara. Activa qualsevol vista del projecte i torna a obrir Ajustes.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }
            var doc = ui.ActiveUIDocument?.Document;
            if (doc == null)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = "[Diag 2/4] UIApplication capturada però ActiveUIDocument és null. Obre un projecte de Revit.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }
            if (!_clasificacioService.IsAvailable(doc))
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = $"[Diag 3/4] Document obert ({doc.Title}) però IsAvailable() retorna false. És un document de família?";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }

            var files = FilaClasificacionBuilder.Construir(doc, _clasificacioService);
            if (files == null || files.Count == 0)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = $"[Diag 4/4] Document llegit ({doc.Title}) però FilaClasificacionBuilder.Construir() ha retornat 0 sistemes. Verifica que el projecte tingui PipingSystemType definits.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "0 sistemes";
                return;
            }

            PnlClassifAviso.Visibility = WpfVisibility.Collapsed;

            // Poblar el ComboBox de categoria a cada fila
            var categories = CategoriasCatalogo.Todas.ToList();
            DgClassificacioSistemes.ItemsSource = files;

            // Configurar la columna ComboBox amb les categories disponibles
            var colCombo = DgClassificacioSistemes.Columns
                .OfType<DataGridComboBoxColumn>()
                .FirstOrDefault();
            if (colCombo != null)
            {
                colCombo.ItemsSource = categories;
                colCombo.DisplayMemberPath = "Etiqueta";
                colCombo.SelectedValuePath = "Categoria";
            }

            // Inicialitzar caché en memòria amb l'estat actual (inclou suggeriments).
            // El motor d'exportació BC3 llegeix d'aquesta caché.
            ActualitzarCacheClasificacio(files);

            // Quan l'usuari canviï un dropdown, refresquem la caché immediatament.
            DgClassificacioSistemes.CellEditEnding -= DgClassificacioSistemes_CellEditEnding;
            DgClassificacioSistemes.CellEditEnding += DgClassificacioSistemes_CellEditEnding;

            // Actualitzar comptador
            int total = files.Count;
            int classificats = files.Count(f => f.CategoriaActual != CategoriaSistema.SinAsignar
                                             && f.CategoriaActual != CategoriaSistema.Ignorar);
            LblContadorClassificacio.Text = $"{classificats} de {total} classificats";
        }

        /// <summary>
        /// Refresca la caché ClasificacionSession.AsignacionesActuales amb els
        /// CategoriaActual de totes les files. La crida després de cada canvi
        /// del DataGrid garanteix que el motor d'exportació BC3 vegi els canvis
        /// en temps real (sense necessitar una Transaction al .rvt).
        /// </summary>
        private void ActualitzarCacheClasificacio(System.Collections.Generic.IEnumerable<FilaClasificacion> files)
        {
            var dic = new System.Collections.Generic.Dictionary<ElementId, CategoriaSistema>();
            foreach (var f in files)
            {
                if (f?.TipoSistemaId != null && f.TipoSistemaId != ElementId.InvalidElementId)
                    dic[f.TipoSistemaId] = f.CategoriaActual;
            }
            E3G.Saneamiento.Presupuesto.Storage.ClasificacionSession.AsignacionesActuales = dic;
        }

        private void DgClassificacioSistemes_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Després que el commit del cell faci efecte, sincronitzem la caché.
            // Dispatcher.BeginInvoke per esperar que el binding actualitzi CategoriaActual.
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (DgClassificacioSistemes.ItemsSource is System.Collections.Generic.IEnumerable<FilaClasificacion> files)
                {
                    ActualitzarCacheClasificacio(files);
                    int total = files.Count();
                    int classificats = files.Count(f => f.CategoriaActual != CategoriaSistema.SinAsignar
                                                     && f.CategoriaActual != CategoriaSistema.Ignorar);
                    LblContadorClassificacio.Text = $"{classificats} de {total} classificats";
                }
            }));
        }

        private void BtnNavSaneamiento_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecSaneamiento, BtnNavSaneamiento);

        private void BtnNavFontaneria_Click(object sender, RoutedEventArgs e)
            => NavegarA(SecFontaneria, BtnNavFontaneria);

        private void BtnNavPartides_Click(object sender, RoutedEventArgs e)
        {
            NavegarA(SecPartides, BtnNavPartides);
            InicializarPartidesSiCal();
        }

        // ════════════════════════════════════════════════
        // PARTIDES BC3 — handlers de la sección
        // ════════════════════════════════════════════════
        private void InicializarPartidesSiCal()
        {
            if (_vmPartides != null) return;
            var ui = _uiApp ?? SaneamientoSession.UiApp;
            if (ui == null)
            {
                MessageBox.Show(
                    "Encara no s'ha capturat el context de Revit.\n\n" +
                    "Activa qualsevol vista del projecte (clicka una vista al Project Browser) " +
                    "i torna a clicar 'Partides BC3'.",
                    "Context no disponible", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var doc = ui.ActiveUIDocument?.Document;
            if (doc == null)
            {
                MessageBox.Show(
                    "No hi ha cap document actiu a Revit. Obre un projecte i torna a provar.",
                    "Cap document actiu", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                _vmPartides = new E3G.Saneamiento.Presupuesto.Ui.PartidasViewModel(doc);
                SecPartides.DataContext = _vmPartides;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Error en inicialitzar el ViewModel de partides:\n\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error d'inicialització", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImportarBc3_Click(object sender, RoutedEventArgs e)
        {
            if (_vmPartides == null)
            {
                InicializarPartidesSiCal();
                if (_vmPartides == null) return; // ya mostró diálogo dentro
            }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Arxiu BC3 (*.bc3)|*.bc3|Tots (*.*)|*.*",
                Title = "Importar catàleg BC3"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                _vmPartides.ImportarBc3(ofd.FileName);
                MessageBox.Show(
                    $"Catàleg importat correctament.\n\n" +
                    $"Arxiu: {System.IO.Path.GetFileName(ofd.FileName)}\n" +
                    $"Partides carregades: {_vmPartides.TotalPartidasCargadas}\n" +
                    $"Catàlegs en total: {_vmPartides.CatalogosUi.Count}",
                    "Importació completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"No s'ha pogut llegir el BC3.\n\n" +
                    $"Arxiu: {ofd.FileName}\n\n" +
                    $"Detall:\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error en importar BC3", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMarcarPrimario_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is System.Guid id && _vmPartides != null)
                _vmPartides.MarcarPrimario(id);
        }

        private void BtnEliminarCatalogo_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is System.Guid id && _vmPartides != null)
                _vmPartides.EliminarCatalogo(id);
        }

        private void BtnAnadirRegla_Click(object sender, RoutedEventArgs e)
        {
            if (_vmPartides == null) return;
            if (!((sender as Button)?.Tag is CategoriaSistema cat)) return;
            var dlg = new E3G.Saneamiento.Presupuesto.Ui.ReglaEditorDialog(
                _vmPartides.ObtenerPlantillaActual(), null, cat);
            dlg.Owner = Window.GetWindow(this);
            if (dlg.ShowDialog() == true) _vmPartides.AnadirRegla(dlg.ReglaEditada);
        }

        private void BtnEditarRegla_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_vmPartides == null)
                {
                    MessageBox.Show("ViewModel de partides no inicialitzat. Importa un BC3 primer.",
                        "Editar regla", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Resolución defensiva del Tag: puede venir como Guid (ideal) o como string.
                var tag = (sender as Button)?.Tag;
                System.Guid id;
                if (tag is System.Guid g) id = g;
                else if (tag is string s && System.Guid.TryParse(s, out var parsed)) id = parsed;
                else
                {
                    MessageBox.Show(
                        $"No s'ha pogut llegir l'identificador de la regla.\n\nTag rebut: {tag?.GetType().Name ?? "null"} = '{tag}'",
                        "Editar regla", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var regla = _vmPartides.ObtenerPlantillaActual().Reglas.FirstOrDefault(r => r.Id == id);
                if (regla == null)
                {
                    MessageBox.Show($"Regla amb id {id} no trobada al model actual.",
                        "Editar regla", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dlg = new E3G.Saneamiento.Presupuesto.Ui.ReglaEditorDialog(
                    _vmPartides.ObtenerPlantillaActual(), regla, regla.Categoria);
                dlg.Owner = Window.GetWindow(this);

                if (dlg.ShowDialog() == true)
                    _vmPartides.ActualizarRegla(dlg.ReglaEditada);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Error en obrir l'editor de regla:\n\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error inesperat", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminarRegla_Click(object sender, RoutedEventArgs e)
        {
            if (_vmPartides == null) return;
            if (!((sender as Button)?.Tag is System.Guid id)) return;
            if (MessageBox.Show("Eliminar aquesta regla?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _vmPartides.EliminarRegla(id);
        }

        private void BtnAplicarSugeridas_Click(object sender, RoutedEventArgs e)
        {
            if (_vmPartides == null) { InicializarPartidesSiCal(); if (_vmPartides == null) return; }
            _vmPartides.AplicarReglasSugeridasSaneamiento();
        }

        private void NavegarA(Border seccio, Button boto)
        {
            if (_btnActiu != null)
                ActualitzarEstatBoto(_btnActiu, false);

            OcultarTotesSeccions();
            seccio.Visibility = WpfVisibility.Visible;

            _btnActiu = boto;
            ActualitzarEstatBoto(boto, true);
        }

        private void OcultarTotesSeccions()
        {
            SecIdioma.Visibility = WpfVisibility.Collapsed;
            SecUnidades.Visibility = WpfVisibility.Collapsed;
            SecConfirmaciones.Visibility = WpfVisibility.Collapsed;
            SecNomenclatura.Visibility = WpfVisibility.Collapsed;
            SecClassificacio.Visibility = WpfVisibility.Collapsed;
            SecPartides.Visibility = WpfVisibility.Collapsed;   
            SecSaneamiento.Visibility = WpfVisibility.Collapsed;
            SecFontaneria.Visibility = WpfVisibility.Collapsed;
        }

        private void ActualitzarEstatBoto(Button boto, bool actiu)
        {
            boto.Background = actiu
                ? new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#DDE1E5"))
                : new MediaBrush(MediaColors.Transparent);

            boto.FontWeight = actiu ? FontWeights.SemiBold : FontWeights.Normal;

            boto.Foreground = actiu
                ? new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#1E293B"))
                : new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#475569"));
        }

        // ════════════════════════════════════════════════
        // LLEGIR / GUARDAR AJUSTOS
        // ════════════════════════════════════════════════

        public AjustesConfig LlegirAjustos()
        {
            return new AjustesConfig
            {
                // Idioma
                IdiomaPlugin = (CbxIdiomaPlugin.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Català",
                IdiomaReporte = (CbxIdiomaReporte.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Igual que interfície",
                NombresTecnics = ChkNombresTecnicos.IsChecked == true,

                // Unitats
                UnitatLongitud = (CbxUnidadLongitud.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Metres (m)",
                Decimals = int.TryParse(TxtDecimales.Text, out int dec) ? dec : 2,
                UnitatDiametre = (CbxUnidadDiametro.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Mil·límetres (mm)",
                FormatPendent = (CbxFormatoPendiente.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Percentatge (%)",
                CabeceraExportacio = ChkCabeceraExportacion.IsChecked == true,

                // Confirmacions
                ConfirmarEliminar = ChkConfirmarEliminar.IsChecked == true,
                ConfirmarVistes = ChkConfirmarVistas.IsChecked == true,
                ConfirmarCopia = ChkConfirmarCopia.IsChecked == true,
                UmbralConfirmacio = int.TryParse(TxtUmbralConfirmacion.Text, out int umb) ? umb : 200,
                Verbose = ChkVerbose.IsChecked == true,

                // Nomenclatura
                FormatPlanta = (CbxFormatoPlanta.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "P##.EDIF",
                FormatSoterrani = (CbxFormatoSotano.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "S##.EDIF",
                SeparadorEdifici = (CbxSeparadorEdificio.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Punt ( . )",
                IdsEdifici = TxtIdsEdificio.Text,
                PrefijoE3G = TxtPrefijoE3G.Text,
                AvisarFamilies = ChkAvisarFamilias.IsChecked == true,

                // Saneament
                MedirBr = ChkMedirBr.IsChecked == true,
                MedirBp = ChkMedirBp.IsChecked == true,
                SistemaBr = TxtSistemaBr.Text,
                SistemaBp = TxtSistemaBp.Text,
                SanAgruparNivell = ChkSanAgruparNivel.IsChecked == true,
                MedirColRes = ChkMedirColRes.IsChecked == true,
                MedirColPlu = ChkMedirColPlu.IsChecked == true,
                SistemaColRes = TxtSistemaColRes.Text,
                SistemaColPlu = TxtSistemaColPlu.Text,
                PendentMinima = double.TryParse(TxtPendienteMinima.Text, out double pend) ? pend : 2.0,
                SanValidarPendent = ChkSanValidarPendiente.IsChecked == true,
                SanAgruparDiametre = ChkSanAgruparDiametro.IsChecked == true,
                SanCatTuberias = ChkSanCatTuberias.IsChecked == true,
                SanCatUnions = ChkSanCatUniones.IsChecked == true,
                SanCatArquetes = ChkSanCatArquetas.IsChecked == true,

                // Fontaneria
                MedirAfs = ChkMedirAfs.IsChecked == true,
                MedirAcs = ChkMedirAcs.IsChecked == true,
                MedirAr = ChkMedirAr.IsChecked == true,
                ParamLongitudFont = TxtParamLongitudFont.Text,
                ParamSistemaFont = TxtParamSistemaFont.Text,
                ParamDiametreFont = TxtParamDiametroFont.Text,
                ParamMaterial = TxtParamMaterial.Text,
                FamiliaAfs = TxtFamiliaAfs.Text,
                FamiliaAcs = TxtFamiliaAcs.Text,
                FontAgruparDiametre = ChkFontAgruparDiametro.IsChecked == true,
                FontMontants = ChkFontMontantes.IsChecked == true,
                FontAccessoris = ChkFontAccesorios.IsChecked == true,
                FontAgruparNivell = ChkFontAgruparNivel.IsChecked == true,
                FontFiltrarVista = ChkFontFiltrarVista.IsChecked == true,
                FontAvisarCategoria = ChkFontAvisarCategoria.IsChecked == true,
            };
        }

        public void AplicarAjustos(AjustesConfig cfg)
        {
            if (cfg == null) return;

            SeleccionarComboBoxPerContingut(CbxIdiomaPlugin, cfg.IdiomaPlugin);
            SeleccionarComboBoxPerContingut(CbxIdiomaReporte, cfg.IdiomaReporte);
            ChkNombresTecnicos.IsChecked = cfg.NombresTecnics;

            SeleccionarComboBoxPerContingut(CbxUnidadLongitud, cfg.UnitatLongitud);
            TxtDecimales.Text = cfg.Decimals.ToString();
            SeleccionarComboBoxPerContingut(CbxUnidadDiametro, cfg.UnitatDiametre);
            SeleccionarComboBoxPerContingut(CbxFormatoPendiente, cfg.FormatPendent);
            ChkCabeceraExportacion.IsChecked = cfg.CabeceraExportacio;

            ChkConfirmarEliminar.IsChecked = cfg.ConfirmarEliminar;
            ChkConfirmarVistas.IsChecked = cfg.ConfirmarVistes;
            ChkConfirmarCopia.IsChecked = cfg.ConfirmarCopia;
            TxtUmbralConfirmacion.Text = cfg.UmbralConfirmacio.ToString();
            ChkVerbose.IsChecked = cfg.Verbose;

            SeleccionarComboBoxPerContingut(CbxFormatoPlanta, cfg.FormatPlanta);
            SeleccionarComboBoxPerContingut(CbxFormatoSotano, cfg.FormatSoterrani);
            SeleccionarComboBoxPerContingut(CbxSeparadorEdificio, cfg.SeparadorEdifici);
            TxtIdsEdificio.Text = cfg.IdsEdifici;
            TxtPrefijoE3G.Text = cfg.PrefijoE3G;
            ChkAvisarFamilias.IsChecked = cfg.AvisarFamilies;

            ChkMedirBr.IsChecked = cfg.MedirBr;
            ChkMedirBp.IsChecked = cfg.MedirBp;
            TxtSistemaBr.Text = cfg.SistemaBr;
            TxtSistemaBp.Text = cfg.SistemaBp;
            ChkSanAgruparNivel.IsChecked = cfg.SanAgruparNivell;
            ChkMedirColRes.IsChecked = cfg.MedirColRes;
            ChkMedirColPlu.IsChecked = cfg.MedirColPlu;
            TxtSistemaColRes.Text = cfg.SistemaColRes;
            TxtSistemaColPlu.Text = cfg.SistemaColPlu;
            TxtPendienteMinima.Text = cfg.PendentMinima.ToString();
            ChkSanValidarPendiente.IsChecked = cfg.SanValidarPendent;
            ChkSanAgruparDiametro.IsChecked = cfg.SanAgruparDiametre;
            ChkSanCatTuberias.IsChecked = cfg.SanCatTuberias;
            ChkSanCatUniones.IsChecked = cfg.SanCatUnions;
            ChkSanCatArquetas.IsChecked = cfg.SanCatArquetes;

            ChkMedirAfs.IsChecked = cfg.MedirAfs;
            ChkMedirAcs.IsChecked = cfg.MedirAcs;
            ChkMedirAr.IsChecked = cfg.MedirAr;
            TxtParamLongitudFont.Text = cfg.ParamLongitudFont;
            TxtParamSistemaFont.Text = cfg.ParamSistemaFont;
            TxtParamDiametroFont.Text = cfg.ParamDiametreFont;
            TxtParamMaterial.Text = cfg.ParamMaterial;
            TxtFamiliaAfs.Text = cfg.FamiliaAfs;
            TxtFamiliaAcs.Text = cfg.FamiliaAcs;
            ChkFontAgruparDiametro.IsChecked = cfg.FontAgruparDiametre;
            ChkFontMontantes.IsChecked = cfg.FontMontants;
            ChkFontAccesorios.IsChecked = cfg.FontAccessoris;
            ChkFontAgruparNivel.IsChecked = cfg.FontAgruparNivell;
            ChkFontFiltrarVista.IsChecked = cfg.FontFiltrarVista;
            ChkFontAvisarCategoria.IsChecked = cfg.FontAvisarCategoria;
        }

        private static void SeleccionarComboBoxPerContingut(WpfComboBox cbx, string valor)
        {
            foreach (ComboBoxItem item in cbx.Items)
                if (item.Content?.ToString() == valor)
                { cbx.SelectedItem = item; return; }
        }
    }

    // ════════════════════════════════════════════════
    // MODEL DE DADES
    // ════════════════════════════════════════════════
    public class AjustesConfig
    {
        // Idioma
        public string IdiomaPlugin { get; set; } = "Català";
        public string IdiomaReporte { get; set; } = "Igual que interfície";
        public bool NombresTecnics { get; set; } = true;

        // Unitats
        public string UnitatLongitud { get; set; } = "Metres (m)";
        public int Decimals { get; set; } = 2;
        public string UnitatDiametre { get; set; } = "Mil·límetres (mm)";
        public string FormatPendent { get; set; } = "Percentatge (%)";
        public bool CabeceraExportacio { get; set; } = true;

        // Confirmacions
        public bool ConfirmarEliminar { get; set; } = true;
        public bool ConfirmarVistes { get; set; } = true;
        public bool ConfirmarCopia { get; set; } = true;
        public int UmbralConfirmacio { get; set; } = 200;
        public bool Verbose { get; set; } = false;

        // Nomenclatura
        public string FormatPlanta { get; set; } = "P##.EDIF";
        public string FormatSoterrani { get; set; } = "S##.EDIF";
        public string SeparadorEdifici { get; set; } = "Punt ( . )";
        public string IdsEdifici { get; set; } = "A, B1, B2";
        public string PrefijoE3G { get; set; } = "MC_E3G_";
        public bool AvisarFamilies { get; set; } = true;

        // Saneament
        public bool MedirBr { get; set; } = true;
        public bool MedirBp { get; set; } = true;
        public string SistemaBr { get; set; } = "Bajante residual";
        public string SistemaBp { get; set; } = "Bajante pluvial";
        public bool SanAgruparNivell { get; set; } = true;
        public bool MedirColRes { get; set; } = true;
        public bool MedirColPlu { get; set; } = true;
        public string SistemaColRes { get; set; } = "Colector residual";
        public string SistemaColPlu { get; set; } = "Colector pluvial";
        public double PendentMinima { get; set; } = 2.0;
        public bool SanValidarPendent { get; set; } = true;
        public bool SanAgruparDiametre { get; set; } = true;
        public bool SanCatTuberias { get; set; } = true;
        public bool SanCatUnions { get; set; } = true;
        public bool SanCatArquetes { get; set; } = true;

        // Fontaneria
        public bool MedirAfs { get; set; } = true;
        public bool MedirAcs { get; set; } = true;
        public bool MedirAr { get; set; } = true;
        public string ParamLongitudFont { get; set; } = "Longitud";
        public string ParamSistemaFont { get; set; } = "Nombre del sistema";
        public string ParamDiametreFont { get; set; } = "Diámetro";
        public string ParamMaterial { get; set; } = "Material";
        public string FamiliaAfs { get; set; } = "MC_E3G_PEX";
        public string FamiliaAcs { get; set; } = "MC_E3G_PPR_FIBER BLUE";
        public bool FontAgruparDiametre { get; set; } = true;
        public bool FontMontants { get; set; } = true;
        public bool FontAccessoris { get; set; } = true;
        public bool FontAgruparNivell { get; set; } = false;
        public bool FontFiltrarVista { get; set; } = false;
        public bool FontAvisarCategoria { get; set; } = true;
    }
}