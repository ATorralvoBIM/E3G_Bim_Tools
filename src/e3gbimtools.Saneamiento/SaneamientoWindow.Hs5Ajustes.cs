using Autodesk.Revit.UI;
using E3G.Saneamiento.Hs5;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfVisibility = System.Windows.Visibility;

namespace E3G.Saneamiento
{
    // Pestaña "Aparatos y ajustes" del panel Saneamiento y acciones HS5 de
    // dimensionado (aplicar DN, exportar informe). Parte de SaneamientoWindow.
    public partial class SaneamientoWindow
    {
        private ExternalEvent _eventoAparatos;
        private ExternalEvent _eventoPluviometria;
        private EventoPluviometriaProyecto _handlerPluviometria;
        // Selección en curso (se guarda en el proyecto cuando están las dos).
        private string _zonaElegida;
        private int _isoyetaElegida;
        private string _errorPluviometria;
        private bool _pintandoPluviometria;
        private EventoLeerAparatos _handlerAparatos;
        private List<Hs5Analisis.TipoAparatoModelo> _tiposAparato = new List<Hs5Analisis.TipoAparatoModelo>();

        /// <summary>(Propiedad de Hs5Config, etiqueta) de los campos de texto de ajustes.</summary>
        private static readonly (string Prop, string Etiqueta)[] CamposParametros =
        {
            (nameof(Hs5Config.ParametroUD), "UD (tubería)"),
            (nameof(Hs5Config.ParametroUDAparato), "UD (aparato)"),
            (nameof(Hs5Config.ParametroSuperficie), "Superficie m² (mixtos)"),
            (nameof(Hs5Config.ParametroLargoArqueta), "Largo arqueta"),
            (nameof(Hs5Config.ParametroAnchoArqueta), "Ancho arqueta"),
        };
        private static readonly (string Prop, string Etiqueta)[] CamposSistemas =
        {
            (nameof(Hs5Config.SistemasResiduales), "Residuales"),
            (nameof(Hs5Config.SistemasPluviales), "Pluviales"),
            (nameof(Hs5Config.SistemasMixtos), "Mixtos"),
            (nameof(Hs5Config.SistemasVentilacion), "Ventilación"),
        };

        private void InicializarAjustes()
        {
            _handlerAparatos = new EventoLeerAparatos { Pagina = this };
            _eventoAparatos = ExternalEvent.Create(_handlerAparatos);
            _handlerPluviometria = new EventoPluviometriaProyecto { Pagina = this };
            _eventoPluviometria = ExternalEvent.Create(_handlerPluviometria);
            _eventoPluviometria.Raise(); // lee la zona e isoyeta guardadas en el proyecto activo

            var cfg = Hs5Config.Actual;
            RellenarCampos(GridParametros, CamposParametros);
            RellenarCampos(GridSistemas, CamposSistemas);

            foreach (int iso in Hs5Tablas.Isoyetas) CmbIsoyeta.Items.Add(iso);
            ActualizarResumenPluviometria();
            ChkNoDisminuir.IsChecked = cfg.NoDisminuirAguasAbajo;
            ChkSuperficieEnUD.IsChecked = cfg.SuperficiePluvialEnParametroUD;
            TxtAparatosEstado.Text = "Pulsa «Leer aparatos del modelo» para revisar el mapeo.";
            RefrescarAjustes();
        }

        private void TabAjustes_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelAjustes == null) return;
            PanelVerificacion.Visibility = WpfVisibility.Collapsed;
            PanelEsquema.Visibility = WpfVisibility.Collapsed;
            PanelAjustes.Visibility = WpfVisibility.Visible;
            SelectorNormativa.Visibility = WpfVisibility.Collapsed;
            CerrarDropdown();
            tabEsquema?.BuidarBrowser();
            if (_tiposAparato.Count == 0) _eventoAparatos.Raise();
        }

        private void RefrescarAjustes()
        {
            var cfg = Hs5Config.Actual;

            OpcionesUso.Children.Clear();
            OpcionesUso.Children.Add(CrearChip("Privado", !cfg.UsoPublico, () => { cfg.UsoPublico = false; cfg.Guardar(); RefrescarAjustes(); }));
            OpcionesUso.Children.Add(CrearChip("Público", cfg.UsoPublico, () => { cfg.UsoPublico = true; cfg.Guardar(); RefrescarAjustes(); }));

            PintarPluviometria();

            PintarAparatos();
            ActualizarCubierta();
        }

        private void RellenarCampos(WpfGrid grid, (string Prop, string Etiqueta)[] campos)
        {
            var cfg = Hs5Config.Actual;
            for (int k = 0; k < campos.Length; k++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = campos[k].Etiqueta, FontSize = 11,
                    Foreground = Pincel("#605E5C"), VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 6, 3),
                };
                var prop = typeof(Hs5Config).GetProperty(campos[k].Prop);
                var txt = new TextBox
                {
                    Text = (string)prop.GetValue(cfg), FontSize = 11, Padding = new Thickness(6, 3, 6, 3),
                    BorderBrush = Pincel("#DDD9D4"), Foreground = Pincel("#201F1E"), Background = Pincel("#FFFFFF"),
                    Margin = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center,
                };
                txt.LostFocus += (s, e) =>
                {
                    string v = txt.Text?.Trim();
                    if (string.IsNullOrEmpty(v) || v == (string)prop.GetValue(cfg)) { txt.Text = (string)prop.GetValue(cfg); return; }
                    prop.SetValue(cfg, v);
                    cfg.Guardar();
                };
                WpfGrid.SetRow(lbl, k); WpfGrid.SetColumn(lbl, 0);
                WpfGrid.SetRow(txt, k); WpfGrid.SetColumn(txt, 1);
                grid.Children.Add(lbl);
                grid.Children.Add(txt);
            }
        }

        // ── Pluviometría del proyecto: zona e isoyeta del Anejo B, guardadas en el .rvt ──

        private void CmbIsoyeta_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pintandoPluviometria || !(CmbIsoyeta.SelectedItem is int iso)) return;
            _isoyetaElegida = iso;
            GuardarPluviometriaSiCompleta();
        }

        private void ElegirZona(string zona)
        {
            _zonaElegida = zona;
            GuardarPluviometriaSiCompleta();
            RefrescarAjustes();
        }

        /// <summary>Guarda en el proyecto en cuanto hay zona e isoyeta.</summary>
        private void GuardarPluviometriaSiCompleta()
        {
            if (_zonaElegida == null || _isoyetaElegida <= 0) { RefrescarAjustes(); return; }
            var cfg = Hs5Config.Actual;
            if (_zonaElegida == cfg.ZonaProyecto && _isoyetaElegida == cfg.IsoyetaProyecto) return;
            _handlerPluviometria.Guardar = (_zonaElegida, _isoyetaElegida);
            _eventoPluviometria.Raise();
        }

        /// <summary>Refleja en el panel la pluviometría guardada en el proyecto (lo llama el evento de Revit).</summary>
        public void MostrarPluviometriaProyecto(string error)
        {
            var cfg = Hs5Config.Actual;
            _errorPluviometria = error;
            // Al leer el proyecto (o cambiar de proyecto) manda lo guardado en él.
            _zonaElegida = cfg.ZonaProyecto;
            _isoyetaElegida = cfg.IsoyetaProyecto;
            RefrescarAjustes();
            ActualizarResumenPluviometria();
        }

        private void PintarPluviometria()
        {
            var cfg = Hs5Config.Actual;
            OpcionesZona.Children.Clear();
            foreach (string z in new[] { "A", "B" })
            {
                string zona = z;
                OpcionesZona.Children.Add(CrearChip($"Zona {z}", _zonaElegida == z, () => ElegirZona(zona)));
            }
            _pintandoPluviometria = true;
            CmbIsoyeta.SelectedItem = _isoyetaElegida > 0 ? (object)_isoyetaElegida : null;
            _pintandoPluviometria = false;

            string proyecto = cfg.NombreProyecto ?? "el proyecto activo";
            if (_errorPluviometria != null)
            {
                TxtIntensidad.Text = $"No se ha podido guardar en {proyecto}: {_errorPluviometria}";
                TxtIntensidad.Foreground = Pincel("#B45309");
            }
            else if (cfg.IntensidadDefinida)
            {
                TxtIntensidad.Text = $"{cfg.DescripcionPluviometria} · guardado en {proyecto}";
                TxtIntensidad.Foreground = Pincel("#0F6CBD");
            }
            else
            {
                TxtIntensidad.Text = _zonaElegida != null || _isoyetaElegida > 0
                    ? "Elige zona e isoyeta para guardarlas en el proyecto."
                    : $"{proyecto} aún no tiene pluviometría: se aplica 100 mm/h (f = 1).";
                TxtIntensidad.Foreground = Pincel("#B45309");
            }
        }

        /// <summary>Línea de pluviometría bajo el selector de la pestaña Verificación.</summary>
        private void ActualizarResumenPluviometria()
        {
            var cfg = Hs5Config.Actual;
            TxtPluviometriaVerif.Text = cfg.IntensidadDefinida
                ? $"Pluviometría: {cfg.DescripcionPluviometria}"
                : "Pluviometría sin definir: se aplica 100 mm/h (f = 1). Defínela en «Aparatos y ajustes».";
            TxtPluviometriaVerif.Foreground = Pincel(cfg.IntensidadDefinida ? "#605E5C" : "#B45309");
        }

        private void ChkSuperficieEnUD_Changed(object sender, RoutedEventArgs e)
        {
            var cfg = Hs5Config.Actual;
            bool v = ChkSuperficieEnUD.IsChecked == true;
            if (cfg.SuperficiePluvialEnParametroUD == v) return;
            cfg.SuperficiePluvialEnParametroUD = v; cfg.Guardar();
        }

        private void ChkNoDisminuir_Changed(object sender, RoutedEventArgs e)
        {
            var cfg = Hs5Config.Actual;
            bool v = ChkNoDisminuir.IsChecked == true;
            if (cfg.NoDisminuirAguasAbajo == v) return;
            cfg.NoDisminuirAguasAbajo = v; cfg.Guardar();
        }

        // ── Mapeo de aparatos ──

        private void BtnLeerAparatos_Click(object sender, RoutedEventArgs e) => _eventoAparatos.Raise();

        public void MostrarAparatos(List<Hs5Analisis.TipoAparatoModelo> tipos)
        {
            _tiposAparato = tipos ?? new List<Hs5Analisis.TipoAparatoModelo>();
            PintarAparatos();
        }

        private void PintarAparatos()
        {
            ListaAparatos.Children.Clear();
            var cfg = Hs5Config.Actual;
            if (_tiposAparato.Count == 0) return;

            int sinMapear = 0;
            var opciones = Hs5Aparatos.Opciones().ToList();
            foreach (var t in _tiposAparato)
            {
                string guardado = Hs5Mapeo.De(t.Clave);
                string auto = Hs5Aparatos.Detectar(t.Familia, t.Tipo);
                var r = Hs5Aparatos.Resolver(0, t.Familia, t.Tipo, guardado, cfg.UsoPublico, t.DNDesague, t.UD);
                if (r.SinMapear) sinMapear++;

                var fila = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                fila.Children.Add(new TextBlock
                {
                    Text = $"{t.Familia} · {t.Tipo}", FontSize = 11, FontWeight = FontWeights.Medium,
                    Foreground = Pincel("#201F1E"), TextWrapping = TextWrapping.Wrap,
                });
                fila.Children.Add(new TextBlock
                {
                    Text = $"×{t.Cantidad}  ·  desagüe ø{(t.DNDesague > 0 ? t.DNDesague.ToString() : "?")}  ·  " +
                           $"UD familia {(double.IsNaN(t.UD) ? "—" : t.UD.ToString("0.#", CultureInfo.GetCultureInfo("es-ES")))}  ·  " +
                           (guardado != null ? "mapeo manual" : auto != null ? "propuesta automática" : "sin reconocer"),
                    FontSize = 10, Foreground = Pincel("#A19F9D"),
                });

                var combo = new ComboBox { FontSize = 11, Margin = new Thickness(0, 4, 0, 2) };
                combo.Items.Add(new ComboBoxItem { Content = "— Sin mapear —", Tag = null });
                foreach (var (valor, texto) in opciones)
                    combo.Items.Add(new ComboBoxItem { Content = texto, Tag = valor });
                string actual = guardado ?? auto;
                combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == actual)
                                     ?? combo.Items[0];
                var clave = t.Clave;
                combo.SelectionChanged += (s, e) =>
                {
                    var sel = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                    Hs5Mapeo.Asignar(clave, sel == null || sel == Hs5Aparatos.Detectar(t.Familia, t.Tipo) ? null : sel);
                    PintarAparatos();
                };
                fila.Children.Add(combo);

                string resultado = r.Ignorado ? "Ignorado: no se comprueba su derivación"
                    : $"UD {r.UD.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))}{(r.ComprobarUD ? "" : " (no se comprueba)")}  ·  " +
                      $"uso {(r.UsoPublico ? "público" : "privado")}  ·  Ø mín. derivación {(r.DN > 0 ? "ø" + r.DN : "?")}" +
                      (r.Aviso != null ? $"  ·  ⚠ {r.Aviso}" : "");
                fila.Children.Add(new TextBlock
                {
                    Text = resultado, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = Pincel(r.SinMapear ? "#B45309" : "#0F6CBD"),
                });
                ListaAparatos.Children.Add(new Border
                {
                    BorderBrush = Pincel("#DDD9D4"), BorderThickness = new Thickness(0, 0, 0, 0.5),
                    Padding = new Thickness(0, 4, 0, 4), Child = fila,
                });
            }
            TxtAparatosEstado.Text = $"{_tiposAparato.Count} tipo(s) de aparato en el modelo · " +
                                     (sinMapear > 0 ? $"⚠ {sinMapear} sin mapear (se aplica la tabla 4.2 por Ø)" : "todos mapeados") +
                                     $" · mapeo guardado en la app ({Hs5Mapeo.Cantidad} manual(es))";
        }

        // ── Cubiertas ──

        private void TxtSupCubierta_TextChanged(object sender, TextChangedEventArgs e) => ActualizarCubierta();

        private void ActualizarCubierta()
        {
            if (TxtCubierta == null) return;
            string txt = (TxtSupCubierta.Text ?? "").Replace(',', '.');
            if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || s <= 0)
            {
                TxtCubierta.Text = "Introduce la superficie de cubierta en proyección horizontal.";
                return;
            }
            var cfg = Hs5Config.Actual;
            double sCorr = s * cfg.FactorPluviometrico;
            var t47 = Hs5Tablas.T4_7;
            var canalon = Enumerable.Range(0, t47.Columnas.Length).Select(c =>
            {
                var q = t47.Consultar(sCorr, c);
                return $"{t47.Columnas[c]} → {(q.FueraDeTabla ? "> ø" + q.DNMinimo : "ø" + q.DNMinimo)}";
            });
            TxtCubierta.Text = $"Sumideros mínimos (4.6): {Hs5Tablas.NumeroSumideros(s)}\n" +
                               $"Canalón semicircular (4.7) para {sCorr:0.#} m² (× f {cfg.FactorPluviometrico:0.00}):\n   " +
                               string.Join("   ·   ", canalon) +
                               "\nSección cuadrangular: un 10 % superior a la semicircular (4.2.2.3).";
        }

        // ── Dimensionado ──

        private void BtnAplicarDN_Click(object sender, RoutedEventArgs e)
        {
            var cambios = (Sesion.Resultados ?? new List<ResultadoTuberia>())
                .Where(r => !r.EsCorrect && r.Tipo != TipoTramo.Arqueta && r.DNCorrect > r.DNActual)
                .ToList();
            if (cambios.Count == 0)
            {
                MessageBox.Show("No hay tuberías con DN inferior al exigido en el último análisis.", "E3G Saneamiento");
                return;
            }
            var resumen = string.Join("\n", cambios.GroupBy(r => (r.DNActual, r.DNCorrect))
                .OrderBy(g => g.Key.DNActual)
                .Select(g => $"   ø{g.Key.DNActual} → ø{g.Key.DNCorrect}: {g.Count()} tubería(s)"));
            if (MessageBox.Show($"Se cambiará el diámetro de {cambios.Count} tubería(s):\n\n{resumen}\n\n" +
                                "Los KO por UD, pendiente o reducción aguas abajo que no se resuelven con el DN no se tocan. ¿Continuar?",
                                "Aplicar DN HS5", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _handler.Accion = AccionEvento.AplicarDN;
            _evento.Raise();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            var res = Sesion.Resultados;
            if (res == null || res.Count == 0)
            { MessageBox.Show("Ejecuta primero 'Iniciar análisis'.", "E3G Saneamiento"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"E3G_HS5_{DateTime.Now:yyyyMMdd_HHmm}",
                DefaultExt = ".csv",
                Filter = "CSV (Excel)|*.csv",
            };
            if (dlg.ShowDialog() != true) return;

            var cfg = Hs5Config.Actual;
            var es = CultureInfo.GetCultureInfo("es-ES");
            string Q(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine("Informe CTE DB-HS5 · E3G BIM Tools");
            sb.AppendLine($"Fecha;{DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine($"Modo;{(cfg.EsAutomatico ? "Automático" : cfg.Tabla.Nombre)}");
            sb.AppendLine($"Uso de aparatos;{(cfg.UsoPublico ? "Público" : "Privado")}");
            sb.AppendLine("Pluviometría;" + cfg.DescripcionPluviometria);
            sb.AppendLine($"Tramos;{res.Count};Correctos;{res.Count(r => r.EsCorrect)};KO;{res.Count(r => !r.EsCorrect)}");
            sb.AppendLine();
            sb.AppendLine("ID;Estado;Tramo;Red;Sistema;UD;Superficie (m²);Pendiente (%);DN modelo;DN / exigencia HS5;Tabla;Columna;Criterio;Avisos");
            foreach (var r in res.OrderBy(x => x.Tipo).ThenBy(x => x.Id.Value))
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    r.Id.Value.ToString(),
                    r.EsCorrect ? "OK" : "KO",
                    NombreTramo(r.Tipo),
                    r.Red.ToString(),
                    Q(r.SysType),
                    r.UD.ToString("0.##", es),
                    r.Superficie.ToString("0.##", es),
                    double.IsNaN(r.PendientePct) ? "" : r.PendientePct.ToString("0.00", es),
                    r.DNActual.ToString(),
                    Q(r.TextoExigido ?? r.DNCorrect.ToString()),
                    r.TablaId,
                    Q(r.Columna),
                    Q(r.Criterio),
                    Q(r.Avisos),
                }));
            }
            try
            {
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                MessageBox.Show($"✓ Informe exportado en:\n{dlg.FileName}", "E3G Saneamiento");
            }
            catch (IOException ex)
            {
                MessageBox.Show($"No se pudo escribir el fichero (¿está abierto en Excel?):\n{ex.Message}", "E3G Saneamiento");
            }
        }
    }
}
