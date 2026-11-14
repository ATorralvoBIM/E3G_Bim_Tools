using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Localization;
using E3G.Saneamiento;
using System;
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
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThickness = System.Windows.Thickness;

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
            E3G.Saneamiento.UI.E3gChrome.ApplyTheme(this);   // tema E3G
            _uiApp = uiApp;

            // [Idioma] Inicialitzar LanguageService i restaurar la selecció
            // guardada al ComboBox abans de mostrar la pàgina.
            LanguageService.Init();
            Loaded += (s, e) =>
            {
                NavegarA(SecIdioma, BtnNavIdioma);
                RestaurarIdiomaComboBox();
                // Ara sí: subscriure SelectionChanged perquè no dispari durant Init.
                CbxIdiomaPlugin.SelectionChanged += OnIdiomaPluginChanged;
            };
        }

        // ── Handlers de canvi d'idioma ────────────────────────────────────

        private void OnIdiomaPluginChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = CbxIdiomaPlugin.SelectedItem as ComboBoxItem;
            if (item == null) return;
            // Mappejar el text visible → codi normalitzat per LanguageService.
            var text = item.Content?.ToString() ?? "";
            LanguageService.Set(text);   // LanguageService.Normalize() s'encarrega del mapping
        }

        /// <summary>Selecciona al ComboBox l'idioma que LanguageService té actiu.</summary>
        private void RestaurarIdiomaComboBox()
        {
            string codi = LanguageService.Current;
            // Triar el ComboBoxItem que correspon al codi actiu.
            foreach (ComboBoxItem item in CbxIdiomaPlugin.Items)
            {
                var text = item.Content?.ToString()?.ToLowerInvariant() ?? "";
                bool match = codi switch
                {
                    "ca" => text.Contains("catal"),
                    "es" => text.Contains("espa"),
                    "en" => text.Contains("engl"),
                    _    => false,
                };
                if (match)
                {
                    // Suspendre SelectionChanged mentre assignem la selecció inicial.
                    CbxIdiomaPlugin.SelectionChanged -= OnIdiomaPluginChanged;
                    CbxIdiomaPlugin.SelectedItem = item;
                    CbxIdiomaPlugin.SelectionChanged += OnIdiomaPluginChanged;
                    break;
                }
            }
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
                LblClassifAviso.Text = $"⚠ Error al cargar la clasificación: {ex.Message}";
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
                LblClassifAviso.Text = "[Diag 1/4] Aún no se ha capturado UIApplication. Activa cualquier vista del proyecto y vuelve a abrir Ajustes.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }
            var doc = ui.ActiveUIDocument?.Document;
            if (doc == null)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = "[Diag 2/4] UIApplication capturada pero ActiveUIDocument es null. Abre un proyecto de Revit.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }
            if (!_clasificacioService.IsAvailable(doc))
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = $"[Diag 3/4] Documento abierto ({doc.Title}) pero IsAvailable() devuelve false. ¿Es un documento de familia?";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "—";
                return;
            }

            var files = FilaClasificacionBuilder.Construir(doc, _clasificacioService);
            if (files == null || files.Count == 0)
            {
                PnlClassifAviso.Visibility = WpfVisibility.Visible;
                LblClassifAviso.Text = $"[Diag 4/4] Documento leído ({doc.Title}) pero no se ha encontrado ningún sistema. Comprueba que el proyecto tenga PipingSystemType definidos.";
                DgClassificacioSistemes.ItemsSource = null;
                LblContadorClassificacio.Text = "0 sistemas";
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
            LblContadorClassificacio.Text = $"{classificats} de {total} clasificados";
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
                    LblContadorClassificacio.Text = $"{classificats} de {total} clasificados";
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
                    "Aún no se ha capturado el contexto de Revit.\n\n" +
                    "Activa cualquier vista del proyecto (haz clic en una vista del Navegador de proyectos) " +
                    "y vuelve a pulsar 'Partidas BC3'.",
                    "Contexto no disponible", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var doc = ui.ActiveUIDocument?.Document;
            if (doc == null)
            {
                MessageBox.Show(
                    "No hay ningún documento activo en Revit. Abre un proyecto y vuelve a intentarlo.",
                    "Sin documento activo", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    $"Error al inicializar las partidas:\n\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error de inicialización", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Filter = "Archivo BC3 (*.bc3)|*.bc3|Todos (*.*)|*.*",
                Title = "Importar catálogo BC3"
            };
            if (ofd.ShowDialog() != true) return;

            try
            {
                _vmPartides.ImportarBc3(ofd.FileName);
                MessageBox.Show(
                    $"Catálogo importado correctamente.\n\n" +
                    $"Archivo: {System.IO.Path.GetFileName(ofd.FileName)}\n" +
                    $"Partidas cargadas: {_vmPartides.TotalPartidasCargadas}\n" +
                    $"Catálogos en total: {_vmPartides.CatalogosUi.Count}",
                    "Importación completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"No se ha podido leer el BC3.\n\n" +
                    $"Archivo: {ofd.FileName}\n\n" +
                    $"Detalle:\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error al importar el BC3", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Las partidas no están inicializadas. Importa antes un BC3.",
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
                        $"No se ha podido leer el identificador de la regla.\n\nTag recibido: {tag?.GetType().Name ?? "null"} = '{tag}'",
                        "Editar regla", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var regla = _vmPartides.ObtenerPlantillaActual().Reglas.FirstOrDefault(r => r.Id == id);
                if (regla == null)
                {
                    MessageBox.Show($"No se encuentra la regla con id {id} en el modelo actual.",
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
                    $"Error al abrir el editor de reglas:\n\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEliminarRegla_Click(object sender, RoutedEventArgs e)
        {
            if (_vmPartides == null) return;
            if (!((sender as Button)?.Tag is System.Guid id)) return;
            if (MessageBox.Show("¿Eliminar esta regla?", "Confirmar",
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
            SecProtocol.Visibility = WpfVisibility.Collapsed;
            SecSaneamiento.Visibility = WpfVisibility.Collapsed;
            SecFontaneria.Visibility = WpfVisibility.Collapsed;
        }

        private void ActualitzarEstatBoto(Button boto, bool actiu)
        {
            boto.Background = actiu
                ? new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#E0DCD6"))
                : new MediaBrush(MediaColors.Transparent);

            boto.FontWeight = actiu ? FontWeights.SemiBold : FontWeights.Normal;

            boto.Foreground = actiu
                ? new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#201F1E"))
                : new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#605E5C"));
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
                IdiomaReporte = (CbxIdiomaReporte.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Igual que la interfaz",
                NombresTecnics = ChkNombresTecnicos.IsChecked == true,

                // Unitats
                UnitatLongitud = (CbxUnidadLongitud.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Metros (m)",
                Decimals = int.TryParse(TxtDecimales.Text, out int dec) ? dec : 2,
                UnitatDiametre = (CbxUnidadDiametro.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Milímetros (mm)",
                FormatPendent = (CbxFormatoPendiente.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Porcentaje (%)",
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
                SeparadorEdifici = (CbxSeparadorEdificio.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Punto ( . )",
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
        // ════════════════════════════════════════════════
        // PROTOCOL DE CLASSIFICACIÓ
        // ════════════════════════════════════════════════

        private void BtnNavProtocol_Click(object sender, RoutedEventArgs e)
        {
            NavegarA(SecProtocol, BtnNavProtocol);
            CarregarProtocol();
        }

        private void CarregarProtocol()
        {
            var protocol = Protocol.ProtocolSession.ObtenerOBuit();
            RenderitzarSeccions(protocol);

            if (double.TryParse(protocol.BajantLongitudMinM.ToString("G"), out _))
                TxtBajantLongMin.Text = protocol.BajantLongitudMinM.ToString("G");
            if (double.TryParse(protocol.ToleranciaElevacioM.ToString("G"), out _))
                TxtToleranciaElev.Text = protocol.ToleranciaElevacioM.ToString("G");

            ActualitzarDiagnosticProtocol(protocol);
        }

        private void RenderitzarSeccions(Protocol.ProtocolInstalacio protocol)
        {
            var panel = new System.Windows.Controls.StackPanel();

            foreach (var seccio in protocol.Seccions)
            {
                var capcalera = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 4) };
                capcalera.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                capcalera.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = System.Windows.GridLength.Auto });

                // Nombre de la seccion editable en linea (estilo E3G: subrayado suave).
                var txtNom = new WpfTextBox
                {
                    Text = seccio.Etiqueta,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new WpfThickness(0, 0, 0, 1),
                    Padding = new WpfThickness(0, 2, 0, 3),
                    Margin = new WpfThickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Nombre de la sección (editable)"
                };
                txtNom.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "E3g.Brush.Text");
                txtNom.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "E3g.Brush.BorderSubtle");
                txtNom.TextChanged += (s, _) => seccio.Etiqueta = txtNom.Text;
                WpfGrid.SetColumn(txtNom, 0);
                capcalera.Children.Add(txtNom);

                var btnEliminar = new Button
                {
                    Content = "Eliminar sección",
                    Tag = seccio,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Eliminar la sección y sus reglas"
                };
                btnEliminar.SetResourceReference(FrameworkElement.StyleProperty, "BtnLink");
                btnEliminar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "E3g.Brush.ErrorText");
                btnEliminar.Click += (s, _) =>
                {
                    protocol.Seccions.Remove(seccio);
                    RenderitzarSeccions(protocol);
                };
                WpfGrid.SetColumn(btnEliminar, 1);
                capcalera.Children.Add(btnEliminar);

                var ctrl = new ProtocolSeccioControl();
                ctrl.CarregarRegles(seccio.Regles);
                ctrl.OnReglesCanviades = regles => seccio.Regles = regles;

                var contenidor = new System.Windows.Controls.StackPanel
                { Margin = new WpfThickness(0, 0, 0, 12) };
                contenidor.Children.Add(capcalera);
                contenidor.Children.Add(ctrl);
                panel.Children.Add(contenidor);
            }

            LstSeccions.ItemsSource = null;
            LstSeccions.Items.Clear();
            LstSeccions.Items.Add(panel);

            Protocol.ProtocolSession.ProtocolActual = protocol;
        }

        private void BtnAfegirSeccio_Click(object sender, RoutedEventArgs e)
        {
            var protocol = Protocol.ProtocolSession.ObtenerOBuit();
            protocol.Seccions.Add(new Protocol.ProtocolSeccio { Etiqueta = "Nueva sección" });
            RenderitzarSeccions(protocol);
        }

        private void BtnAutoDetectar_Click(object sender, RoutedEventArgs e)
        {
            var doc = (_uiApp ?? SaneamientoSession.UiApp)?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                MessageBox.Show("Abre antes un proyecto de Revit.", "Sin documento",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var protocol = Protocol.ProtocolAutoDetect.Suggerir(doc);
            Protocol.ProtocolSession.ProtocolActual = protocol;
            CarregarProtocol();
            MessageBox.Show("Protocolo autodetectado. Revisa las secciones y guárdalo si estás de acuerdo.",
                "Autodetección completada", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDesarProtocol_Click(object sender, RoutedEventArgs e)
        {
            var doc = (_uiApp ?? SaneamientoSession.UiApp)?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                MessageBox.Show("Abre antes un proyecto de Revit.", "Sin documento",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(TxtBajantLongMin.Text, out var longMin)) longMin = 1.0;
            if (!double.TryParse(TxtToleranciaElev.Text, out var tolElev)) tolElev = 0.5;

            var protocol = Protocol.ProtocolSession.ObtenerOBuit();
            protocol.BajantLongitudMinM = longMin;
            protocol.ToleranciaElevacioM = tolElev;

            var storage = new Protocol.ProtocolStorage();
            bool ok = storage.Guardar(doc, protocol);
            MessageBox.Show(
                ok ? "Protocolo guardado en el proyecto (.rvt)."
                   : "Error al guardar. Comprueba que el proyecto no sea de solo lectura.",
                "Guardar protocolo", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        private void BtnExportarProtocol_Click(object sender, RoutedEventArgs e)
        {
            var protocol = Protocol.ProtocolSession.ProtocolActual;
            if (protocol == null)
            {
                MessageBox.Show("No hay ningún protocolo cargado.", "Exportar protocolo",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Protocolo E3G (*.e3gprot)|*.e3gprot",
                FileName = "Protocol_Sanejament.e3gprot"
            };
            if (sfd.ShowDialog() != true) return;
            var json = System.Text.Json.JsonSerializer.Serialize(protocol,
                       new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(sfd.FileName, json);
            MessageBox.Show("Protocolo exportado correctamente.", "Exportar protocolo",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnImportarProtocol_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Protocolo E3G (*.e3gprot)|*.e3gprot|Todos (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true) return;
            try
            {
                var json = System.IO.File.ReadAllText(ofd.FileName);
                var protocol = System.Text.Json.JsonSerializer
                               .Deserialize<Protocol.ProtocolInstalacio>(json);
                if (protocol == null) throw new Exception("Formato no válido.");
                Protocol.ProtocolSession.ProtocolActual = protocol;
                CarregarProtocol();
                MessageBox.Show("Protocolo importado. Revisa las secciones y guárdalo si quieres aplicarlas.",
                    "Importar protocolo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se ha podido importar:\n{ex.Message}",
                    "Error al importar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ActualitzarDiagnosticProtocol(Protocol.ProtocolInstalacio protocol)
        {
            int totalRegles = protocol.Seccions.Sum(s => s.Regles.Count);
            int totalSeccions = protocol.Seccions.Count;
            bool ok = totalRegles > 0;
            TxtDiagnostic.Text = ok
                ? $"Protocolo con {totalSeccions} secciones y {totalRegles} reglas. Guárdalo para conservarlo en el .rvt."
                : "Ninguna regla configurada. Pulsa «Autodetectar» para generar sugerencias.";
            // Colores del tema E3G segun el estado (exito / aviso).
            PnlDiagnostic.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, ok ? "E3g.Brush.SuccessBg" : "E3g.Brush.WarningBg");
            TxtDiagnostic.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, ok ? "E3g.Brush.SuccessText" : "E3g.Brush.WarningText");
        }

        // ════════════════════════════════════════════════
        // MODEL DE DADES
        // ════════════════════════════════════════════════
        public class AjustesConfig
        {
            // Idioma
            public string IdiomaPlugin { get; set; } = "Català";
            public string IdiomaReporte { get; set; } = "Igual que la interfaz";
            public bool NombresTecnics { get; set; } = true;

            // Unitats
            public string UnitatLongitud { get; set; } = "Metros (m)";
            public int Decimals { get; set; } = 2;
            public string UnitatDiametre { get; set; } = "Milímetros (mm)";
            public string FormatPendent { get; set; } = "Porcentaje (%)";
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
            public string SeparadorEdifici { get; set; } = "Punto ( . )";
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
}