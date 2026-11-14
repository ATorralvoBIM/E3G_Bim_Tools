using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento;
using E3G.Saneamiento.Hs5;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using RevitColor = Autodesk.Revit.DB.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfVisibility = System.Windows.Visibility;



namespace E3G.Saneamiento
{
    public partial class SaneamientoWindow : Page
    {
        private ExternalEvent _evento;
        private EventoSaneamiento _handler;
        private ExternalEvent _eventoEsquema;
        private EventoEsquemaPrincipi _handlerEsquema;
        private bool _normDropdownOpen = false;
        private ExternalEvent _eventoInserir;
        private EventoInserirEsquema _handlerInserir;

        public EsquemaPrincipiControl GetTabEsquema() => tabEsquema;
        public SaneamientoWindow(ExternalEvent evento, EventoSaneamiento handler)
        {
            InitializeComponent();
            E3G.Saneamiento.UI.E3gChrome.ApplyTheme(this);   // tema E3G
            _evento = evento;
            _handler = handler;

            _handlerEsquema = new EventoEsquemaPrincipi { Pagina = this };
            _eventoEsquema = ExternalEvent.Create(_handlerEsquema);

            _handlerInserir = new EventoInserirEsquema { Pagina = this };
            _eventoInserir = ExternalEvent.Create(_handlerInserir);

            InicializarAjustes();
            InicializarSeleccion();
            RefrescarSelector();
            ActualizarTabla();
        }

        // ── Selección en Revit ──
        private void SeleccionarEnRevit(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            if (SaneamientoSession.HandlerSeleccion == null || SaneamientoSession.EventoSel == null) return;
            SaneamientoSession.HandlerSeleccion.IdsASeleccionar = ids;
            SaneamientoSession.EventoSel.Raise();
        }

        // ── Pestañas principales ──
        private void TabVerificacion_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelVerificacion == null) return;
            PanelVerificacion.Visibility = WpfVisibility.Visible;
            PanelEsquema.Visibility = WpfVisibility.Collapsed;
            PanelAjustes.Visibility = WpfVisibility.Collapsed;
            SelectorNormativa.Visibility = WpfVisibility.Visible;
            tabEsquema?.BuidarBrowser();
        }

        private void TabEsquema_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelEsquema == null) return;
            PanelVerificacion.Visibility = WpfVisibility.Collapsed;
            PanelAjustes.Visibility = WpfVisibility.Collapsed;
            PanelEsquema.Visibility = WpfVisibility.Visible;
            SelectorNormativa.Visibility = WpfVisibility.Collapsed;
            CerrarDropdown();
            _eventoEsquema.Raise();
        }

        public void IniciarEsquema(Document doc, List<int> pipeIds)
        {
            tabEsquema.EventoInserir = _eventoInserir;
            tabEsquema.IniciarAmbDoc(doc, pipeIds);
        }

        // ── Selector de tabla HS5 ──
        private static SolidColorBrush Pincel(string hex) =>
            new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString(hex));

        private void BtnNormativa_Click(object sender, RoutedEventArgs e)
        {
            _normDropdownOpen = !_normDropdownOpen;
            NormDropdown.Visibility = _normDropdownOpen ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            NormChevron.Text = _normDropdownOpen ? "▲" : "▼";
        }

        private void CerrarDropdown()
        {
            _normDropdownOpen = false;
            NormDropdown.Visibility = WpfVisibility.Collapsed;
            NormChevron.Text = "▼";
        }

        private void SeleccionarTabla(string tablaId)
        {
            var cfg = Hs5Config.Actual;
            if (cfg.TablaId != tablaId)
            {
                cfg.TablaId = tablaId;
                cfg.ColumnaPendiente = Hs5Config.ColumnaDelModelo;
                cfg.Guardar();
            }
            CerrarDropdown();
            RefrescarSelector();
        }

        /// <summary>Repinta cabecera, desplegable, opciones de columna y leyenda según la configuración.</summary>
        private void RefrescarSelector()
        {
            var cfg = Hs5Config.Actual;
            var tabla = cfg.Tabla; // null en automático

            if (cfg.EsAutomatico)
            {
                NormTitulo.Text = "Automático · CTE DB-HS5";
                NormSubtitulo.Text = "Derivaciones 4.1 · Ramales 4.3 · Bajantes 4.4/4.8 · Colectores 4.5/4.9 · Ventilación";
            }
            else
            {
                NormTitulo.Text = tabla.Nombre;
                NormSubtitulo.Text = $"{tabla.Titulo} · {DescripcionColumna(cfg, tabla)}";
            }

            // Desplegable: automático + una fila por tabla de verificación.
            NormDropdownItems.Children.Clear();
            AgregarOpcionDropdown(Hs5Config.TablaAutomatica, "Automático",
                "Clasifica cada tramo por la red y aplica su tabla: derivación 4.1, ramal 4.3, bajante 4.4 / 4.8, colector 4.5 / 4.9, ventilación 4.10–4.12, arquetas 4.13",
                cfg.EsAutomatico);
            foreach (var t in Hs5Tablas.Verificacion)
                AgregarOpcionDropdown(t.Id, $"{t.Nombre} · {t.Titulo}", t.Descripcion, !cfg.EsAutomatico && t.Id == tabla.Id);

            // Pendiente: para ramales y colectores (automático) o tablas por pendiente.
            bool verPendiente = cfg.EsAutomatico || tabla.VaPorPendiente;
            FilaPendiente.Visibility = verPendiente ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            OpcionesPendiente.Children.Clear();
            if (verPendiente)
            {
                var columnas = (tabla ?? Hs5Tablas.T4_5).Columnas;
                OpcionesPendiente.Children.Add(CrearChip("Del modelo", cfg.ColumnaPendiente < 0, () =>
                {
                    cfg.ColumnaPendiente = Hs5Config.ColumnaDelModelo; cfg.Guardar(); RefrescarSelector();
                }));
                for (int i = 0; i < columnas.Length; i++)
                {
                    int col = i;
                    OpcionesPendiente.Children.Add(CrearChip(columnas[i], cfg.ColumnaPendiente == col, () =>
                    {
                        cfg.ColumnaPendiente = col; cfg.Guardar(); RefrescarSelector();
                    }));
                }
            }

            // Altura de bajante: plantas que vierten según la red, o forzado.
            bool verAltura = cfg.EsAutomatico || tabla.Id == Hs5Tablas.T4_4.Id;
            FilaAltura.Visibility = verAltura ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            OpcionesAltura.Children.Clear();
            if (verAltura)
            {
                foreach (var (modo, texto) in new[]
                {
                    (Hs5Config.AlturaDelModelo, "Plantas del modelo"),
                    (Hs5Config.AlturaHasta3, "≤ 3 plantas"),
                    (Hs5Config.AlturaMasDe3, "> 3 plantas"),
                })
                {
                    int m = modo;
                    OpcionesAltura.Children.Add(CrearChip(texto, cfg.ModoAltura == m, () =>
                    {
                        cfg.ModoAltura = m; cfg.Guardar(); RefrescarSelector();
                    }));
                }
            }

            // Leyenda: un color por DN de la tabla activa (o de las tres en automático).
            var dns = cfg.EsAutomatico
                ? Hs5Tablas.Verificacion.SelectMany(t => t.DN).Distinct().OrderBy(d => d).ToArray()
                : tabla.DN;
            LeyendaTitulo.Text = cfg.EsAutomatico
                ? "Leyenda — DN exigido (todas las tablas)"
                : $"Leyenda — DN exigido por la tabla {tabla.Id}";
            LeyendaGrid.Children.Clear();
            foreach (int dn in dns)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                sp.Children.Add(new Border
                {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                    Background = Pincel(Hs5Colores.Hex(dn)), Margin = new Thickness(0, 0, 5, 0),
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"ø{dn}", FontSize = 11, Foreground = Pincel("#605E5C"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                LeyendaGrid.Children.Add(sp);
            }
        }

        private void AgregarOpcionDropdown(string id, string titulo, string descripcion, bool activa)
        {
            var fila = new DockPanel();
            if (activa)
            {
                var tag = new TextBlock
                {
                    Text = "activa", FontSize = 9, Padding = new Thickness(5, 2, 5, 2),
                    Background = Pincel("#E6F1FB"), Foreground = Pincel("#0F6CBD"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                };
                DockPanel.SetDock(tag, Dock.Right);
                fila.Children.Add(tag);
            }
            var check = new TextBlock
            {
                Text = activa ? "✓" : "", FontSize = 10, Width = 16,
                Foreground = Pincel("#0F6CBD"), VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(check, Dock.Left);
            fila.Children.Add(check);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock
            {
                Text = titulo, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Pincel("#201F1E"),
            });
            textos.Children.Add(new TextBlock
            {
                Text = descripcion, FontSize = 10, Foreground = Pincel("#605E5C"), TextWrapping = TextWrapping.Wrap,
            });
            fila.Children.Add(textos);

            var item = new Border
            {
                Padding = new Thickness(14, 10, 14, 10), Cursor = Cursors.Hand, Child = fila,
                Background = activa ? Pincel("#E6F1FB") : Brushes.Transparent,
                BorderBrush = Pincel("#DDD9D4"),
                BorderThickness = new Thickness(0, NormDropdownItems.Children.Count == 0 ? 0 : 0.5, 0, 0),
            };
            item.MouseLeftButtonUp += (s, ev) => SeleccionarTabla(id);
            NormDropdownItems.Children.Add(item);
        }

        private static string DescripcionColumna(Hs5Config cfg, TablaHs5 tabla)
        {
            if (!tabla.VaPorPendiente)
                return cfg.ModoAltura == Hs5Config.AlturaMasDe3 ? "más de 3 plantas"
                     : cfg.ModoAltura == Hs5Config.AlturaHasta3 ? "hasta 3 plantas"
                     : "plantas del modelo";
            return cfg.ColumnaPendiente >= 0 && cfg.ColumnaPendiente < tabla.Columnas.Length
                ? $"pendiente {tabla.Columnas[cfg.ColumnaPendiente]}"
                : "pendiente del modelo";
        }

        private UIElement CrearChip(string texto, bool activo, Action onClick)
        {
            // Segmento E3G: activo = tinte de marca + texto Primary; inactivo = superficie con borde.
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 2, 6, 2), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                Background = activo ? Pincel("#E6F1FB") : Pincel("#F6F4F1"),
                BorderBrush = activo ? Pincel("#BFD8F2") : Pincel("#DDD9D4"),
                Child = new TextBlock
                {
                    Text = texto, FontSize = 11, FontWeight = activo ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = activo ? Pincel("#0F6CBD") : Pincel("#201F1E"),
                },
            };
            if (!activo)
            {
                chip.MouseEnter += (s, ev) => chip.Background = Pincel("#E0DCD6");
                chip.MouseLeave += (s, ev) => chip.Background = Pincel("#F6F4F1");
            }
            chip.MouseLeftButtonUp += (s, ev) => onClick();
            return chip;
        }


        // ── Grupos OK / KO ──
        private void GrupoOk_Click(object sender, MouseButtonEventArgs e)
        {
            bool open = BodyOk.Visibility == WpfVisibility.Visible;
            BodyOk.Visibility = open ? WpfVisibility.Collapsed : WpfVisibility.Visible;
            ArrowOk.Text = open ? "▶" : "▼";
        }

        private void GrupoKo_Click(object sender, MouseButtonEventArgs e)
        {
            bool open = BodyKo.Visibility == WpfVisibility.Visible;
            BodyKo.Visibility = open ? WpfVisibility.Collapsed : WpfVisibility.Visible;
            ArrowKo.Text = open ? "▶" : "▼";
        }

        // ── Verificación ──
        public void ActualizarTabla()
        {
            var resultados = Sesion.Resultados;
            if (resultados == null || resultados.Count == 0)
            {
                CardTotal.Text = "—";
                CardOk.Text = "—";
                CardKo.Text = "—";
                BadgeOk.Text = "0 segmentos";
                BadgeKo.Text = "0 segmentos";
                TablaOk.ItemsSource = null;
                TablaKo.ItemsSource = null;
                TxtAvisos.Visibility = WpfVisibility.Collapsed;
                return;
            }

            int total = resultados.Count;
            int correctas = resultados.Count(r => r.EsCorrect);
            int ko = total - correctas;

            CardTotal.Text = total.ToString();
            CardOk.Text = correctas.ToString();
            CardKo.Text = ko.ToString();
            BadgeOk.Text = $"{correctas} segmento{(correctas != 1 ? "s" : "")}";
            BadgeKo.Text = $"{ko} segmento{(ko != 1 ? "s" : "")}";

            TablaOk.ItemsSource = resultados.Where(r => r.EsCorrect).Select(CrearFila).ToList();
            TablaKo.ItemsSource = resultados.Where(r => !r.EsCorrect).Select(CrearFila).ToList();

            var avisos = new List<string>();
            int fuera = resultados.Count(r => r.FueraDeTabla);
            int sinPend = resultados.Count(r => r.PendienteInsuficiente);
            var red = Sesion.ResumenRed;
            if (red != null && red.RedTruncada)
                avisos.Add("La red conectada es muy grande y se ha recorrido parcialmente: la clasificación puede ser incompleta.");
            if (fuera > 0)
                avisos.Add($"{fuera} tramo(s) superan el máximo de su tabla.");
            int udMal = resultados.Count(r => r.Tipo == TipoTramo.Derivacion && r.Avisos.Contains("UD del modelo"));
            if (udMal > 0)
                avisos.Add($"{udMal} derivación(es) con UD del modelo menor que la del aparato (tabla 4.1).");
            if (red != null && red.AparatosSinMapear > 0)
                avisos.Add($"{red.AparatosSinMapear} aparato(s) sin mapear: revisa la pestaña «Aparatos y ajustes».");
            if (sinPend > 0)
                avisos.Add($"{sinPend} tramo(s) con pendiente inferior a la mínima de la tabla.");
            if (Sesion.OmitidasOrientacion > 0)
                avisos.Add($"{Sesion.OmitidasOrientacion} tubería(s) omitidas por orientación " +
                           "(bajantes → tabla 4.4; ramales y colectores → 4.3 / 4.5).");
            var lineas = avisos.Select(a => "⚠ " + a).ToList();
            if (red != null)
                lineas.Insert(0, $"Red: {red.Aparatos} aparato(s), {red.Derivaciones} derivación(es), {red.Bajantes} bajante(s), " +
                                 $"{red.Desviaciones} desviación(es), {red.Ventilaciones} ventilación(es), {red.Arquetas} arqueta(s).");
            if (resultados.Any(r => r.Red == RedHs5Tipo.Pluvial || r.Red == RedHs5Tipo.Mixta))
            {
                var cfgP = Hs5Config.Actual;
                lineas.Insert(red != null ? 1 : 0, cfgP.IntensidadDefinida
                    ? $"Pluviales aplicadas con {cfgP.DescripcionPluviometria} (Anejo B, {cfgP.NombreProyecto})"
                    : "⚠ Pluviometría sin definir en este proyecto: se aplica 100 mm/h (f = 1). Elige zona e isoyeta en «Aparatos y ajustes».");
                MostrarPluviometriaProyecto(null);
            }
            TxtAvisos.Text = string.Join("\n", lineas);
            TxtAvisos.Visibility = lineas.Count > 0 ? WpfVisibility.Visible : WpfVisibility.Collapsed;
        }

        private static FilaTabla CrearFila(ResultadoTuberia r)
        {
            string exigido = r.TextoExigido ?? $"ø{r.DNCorrect}";
            string dnHs5 = r.FueraDeTabla ? $"> {exigido} (fuera)"
                         : r.PendienteInsuficiente ? $"{exigido} · pend. < mín."
                         : r.EsCorrect ? exigido
                         : $"KO → {exigido}";
            return new FilaTabla
            {
                IdStr = r.Id.Value.ToString(),
                DNActualStr = r.DNActual > 0 ? $"ø{r.DNActual}" : "—",
                UDStr = r.Red == RedHs5Tipo.Pluvial ? $"{r.Superficie:0.#} m²" : r.UD.ToString("0.#"),
                PendStr = double.IsNaN(r.PendientePct) ? "vert." : $"{r.PendientePct:0.0} %",
                DNHs5Str = dnHs5,
                TramoStr = NombreTramo(r.Tipo),
                Criterio = string.IsNullOrEmpty(r.Avisos) ? r.Criterio : $"{r.Criterio}  ⚠ {r.Avisos}",
                ColorHex = Hs5Colores.Hex(r.DNCorrect),
                NombreColor = Hs5Colores.Nombre(r.DNCorrect),
                EsCorrect = r.EsCorrect,
            };
        }

        private static string NombreTramo(TipoTramo t)
        {
            switch (t)
            {
                case TipoTramo.Bajante: return "Bajante";
                case TipoTramo.Desviacion: return "Desviación";
                case TipoTramo.Colector: return "Colector";
                case TipoTramo.Derivacion: return "Derivación";
                case TipoTramo.VentPrimaria: return "Vent. primaria";
                case TipoTramo.VentSecundaria: return "Vent. secundaria";
                case TipoTramo.VentTerciaria: return "Vent. terciaria";
                case TipoTramo.Arqueta: return "Arqueta";
                default: return "Ramal";
            }
        }

        // ── Botones verificación ──
        private void BtnAnalizar_Click(object sender, RoutedEventArgs e)
        {
            _handler.Accion = AccionEvento.Analizar;
            _evento.Raise();
        }

        private void BtnActualizar_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.Resultados == null || Sesion.Resultados.Count == 0)
            { MessageBox.Show("Ejecuta primero 'Iniciar análisis'.", "E3G Saneamiento"); return; }
            _handler.Accion = AccionEvento.Actualizar;
            _evento.Raise();
        }

        private void BtnTabla_Click(object sender, RoutedEventArgs e)
        {
            var cfg = Hs5Config.Actual;
            var win = new Window
            {
                Title = "CTE DB-HS5 · Tablas de dimensionado",
                Width = 760,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };

            var tabs = new TabControl();
            void Agregar(string cabecera, UIElement contenido, bool activa = false)
            {
                var item = new TabItem
                {
                    Header = cabecera,
                    Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = contenido },
                };
                tabs.Items.Add(item);
                if (activa) tabs.SelectedItem = item;
            }

            var es = Hs5Tablas.Es;
            Agregar("4.1", CrearVistaTexto("Tabla 4.1 · UD por aparato sanitario",
                "UD y diámetro mínimo de sifón y derivación individual (mm) · «–» = no aplica",
                new[] { "Aparato", "UD privado", "UD público", "Ø privado", "Ø público" },
                Hs5Tablas.T4_1.Select(a => new[]
                {
                    a.Nombre,
                    double.IsNaN(a.UDPrivado) ? "–" : a.UDPrivado.ToString("0.#", es),
                    double.IsNaN(a.UDPublico) ? "–" : a.UDPublico.ToString("0.#", es),
                    a.DNPrivado > 0 ? a.DNPrivado.ToString() : "–",
                    a.DNPublico > 0 ? a.DNPublico.ToString() : "–",
                })));
            Agregar("4.2", CrearVistaTexto("Tabla 4.2 · UD de otros aparatos", "Según el diámetro del desagüe",
                new[] { "Ø desagüe (mm)", "UD" },
                Hs5Tablas.T4_2.Select(r => new[] { r.DN.ToString(), r.UD.ToString("0", es) })));
            foreach (var t in Hs5Tablas.PorDN)
                Agregar(t.Id, CrearVistaTabla(t), !cfg.EsAutomatico && cfg.Tabla.Id == t.Id);
            Agregar("4.6", CrearVistaTexto("Tabla 4.6 · Número de sumideros", "Según la superficie de cubierta en proyección horizontal",
                new[] { "Superficie (m²)", "Nº de sumideros" },
                new[] { new[] { "S < 100", "2" }, new[] { "100 ≤ S < 200", "3" }, new[] { "200 ≤ S < 500", "4" }, new[] { "S > 500", "1 cada 150 m²" } }));
            Agregar("4.10", CrearVistaTexto("Tabla 4.10 · Ventilación secundaria",
                "Máxima longitud efectiva (m) por Ø de la columna de ventilación",
                new[] { "Ø bajante", "UD" }.Concat(Hs5Tablas.T4_10_DNVentilacion.Select(d => $"ø{d}")).ToArray(),
                Hs5Tablas.T4_10.Select(r => new[] { r.Bajante.ToString(), r.UD.ToString("#,##0", es) }
                    .Concat(Enumerable.Range(0, Hs5Tablas.T4_10_DNVentilacion.Length)
                        .Select(c => c < r.Long.Length && !double.IsNaN(r.Long[c]) ? r.Long[c].ToString("0", es) : "")).ToArray())));
            Agregar("4.11", CrearVistaTexto("Tabla 4.11 · Ventilación secundaria con uniones en cada planta", "",
                new[] { "Ø bajante (mm)", "Ø columna de ventilación (mm)" },
                Hs5Tablas.T4_11.Select(r => new[] { r.Bajante.ToString(), r.Ventilacion.ToString() })));
            Agregar("4.12", CrearVistaTexto("Tabla 4.12 · Ventilación terciaria",
                "Máxima longitud del ramal de ventilación (m) por su Ø",
                new[] { "Ø ramal desagüe", "Pendiente (%)" }.Concat(Hs5Tablas.T4_12_DNVentilacion.Select(d => $"ø{d}")).ToArray(),
                Hs5Tablas.T4_12.Select(r => new[] { r.Ramal.ToString(), r.Pendiente.ToString("0", es) }
                    .Concat(Enumerable.Range(0, Hs5Tablas.T4_12_DNVentilacion.Length)
                        .Select(c => c >= r.Long.Length || double.IsNaN(r.Long[c]) ? ""
                                   : double.IsPositiveInfinity(r.Long[c]) ? ">300" : r.Long[c].ToString("0", es))).ToArray())));
            Agregar("4.13", CrearVistaTexto("Tabla 4.13 · Dimensiones de las arquetas", "Según el Ø del colector de salida",
                new[] { "Ø colector salida (mm)", "L × A (cm)" },
                Hs5Tablas.T4_13.Select(r => new[] { r.DNSalida.ToString(), $"{r.L} × {r.A}" })));
            Agregar("B.1", CrearVistaTexto("Tabla B.1 · Intensidad pluviométrica i (mm/h)", "Según isoyeta y zona (figura B.1)",
                new[] { "Isoyeta", "Zona A", "Zona B" },
                Hs5Tablas.Isoyetas.Select(iso => new[]
                {
                    iso.ToString(),
                    Hs5Tablas.Intensidad('A', iso).ToString("0"),
                    Hs5Tablas.Intensidad('B', iso).ToString("0"),
                })));
            if (tabs.SelectedItem == null && tabs.Items.Count > 0) tabs.SelectedIndex = 0;

            win.Content = tabs;
            E3G.Saneamiento.UI.E3gChrome.Apply(win);   // ventana sin bordes E3G
            win.ShowDialog();
        }

        /// <summary>Tabla de texto genérica (cabecera + filas) con el estilo de la ventana de tablas.</summary>
        private UIElement CrearVistaTexto(string titulo, string descripcion, string[] cabecera, IEnumerable<string[]> filas)
        {
            var outer = new StackPanel();
            var header = new Border { Background = Pincel("#0F6CBD"), Padding = new Thickness(16, 12, 16, 12) };
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = titulo, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brushes.White });
            if (!string.IsNullOrEmpty(descripcion))
                info.Children.Add(new TextBlock { Text = descripcion, FontSize = 10, Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255)) });
            header.Child = info;
            outer.Children.Add(header);

            var grid = new WpfGrid { Margin = new Thickness(16, 12, 16, 12) };
            for (int c = 0; c < cabecera.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = c == 0 ? new GridLength(1.6, GridUnitType.Star) : new GridLength(1, GridUnitType.Star),
                });
            grid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < cabecera.Length; c++)
            {
                var th = new TextBlock
                {
                    Text = cabecera[c], FontSize = 10, FontWeight = FontWeights.Medium, Foreground = Pincel("#A19F9D"),
                    Padding = new Thickness(0, 0, 6, 6), TextWrapping = TextWrapping.Wrap,
                    TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right,
                };
                WpfGrid.SetColumn(th, c); WpfGrid.SetRow(th, 0); grid.Children.Add(th);
            }
            int row = 1;
            foreach (var f in filas)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                for (int c = 0; c < cabecera.Length && c < f.Length; c++)
                {
                    var td = new TextBlock
                    {
                        Text = f[c], FontSize = 11, Padding = new Thickness(0, 4, 6, 4), TextWrapping = TextWrapping.Wrap,
                        Foreground = Pincel(c == 0 ? "#201F1E" : "#0F6CBD"),
                        TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right,
                    };
                    WpfGrid.SetColumn(td, c); WpfGrid.SetRow(td, row); grid.Children.Add(td);
                }
                row++;
            }
            outer.Children.Add(new Border { Background = Brushes.White, Child = grid });
            return outer;
        }

        private UIElement CrearVistaTabla(TablaHs5 tabla)
        {
            var outer = new StackPanel();

            var header = new Border { Background = Pincel("#0F6CBD"), Padding = new Thickness(16, 12, 16, 12) };
            var hStack = new StackPanel { Orientation = Orientation.Horizontal };
            var logoStack = new StackPanel();
            logoStack.Children.Add(new TextBlock { Text = "E3G", FontSize = 18, FontWeight = FontWeights.Light, Foreground = Brushes.White });
            logoStack.Children.Add(new TextBlock { Text = "ENGINYERIA I ENERGIA", FontSize = 7, Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255)) });
            hStack.Children.Add(logoStack);
            hStack.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(MediaColor.FromArgb(80, 255, 255, 255)), Margin = new Thickness(12, 0, 12, 0) });
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock { Text = $"Tabla {tabla.Id} · {tabla.Titulo}", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brushes.White });
            infoStack.Children.Add(new TextBlock { Text = tabla.Descripcion, FontSize = 10, Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255)) });
            hStack.Children.Add(infoStack);
            header.Child = hStack;
            outer.Children.Add(header);

            outer.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Pincel("#DDD9D4"),
                BorderThickness = new Thickness(0, 0, 0, 0.5),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock
                {
                    Text = $"Máximo de {tabla.Magnitud} admisible por diámetro · «–» = no admitido",
                    FontSize = 11, Foreground = Pincel("#A19F9D"),
                }
            });

            // DN | color | una columna por cada columna de la tabla
            var grid = new WpfGrid { Margin = new Thickness(16, 12, 16, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            foreach (var _ in tabla.Columnas)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition());
            var headers = new List<string> { "DN (mm)", "" };
            headers.AddRange(tabla.Columnas);
            for (int ci = 0; ci < headers.Count; ci++)
            {
                var th = new TextBlock
                {
                    Text = headers[ci], FontSize = 10, FontWeight = FontWeights.Medium,
                    Foreground = Pincel("#A19F9D"), Padding = new Thickness(0, 0, 0, 6),
                    TextAlignment = ci >= 2 ? TextAlignment.Right : TextAlignment.Left,
                };
                WpfGrid.SetColumn(th, ci); WpfGrid.SetRow(th, 0);
                grid.Children.Add(th);
            }

            for (int i = 0; i < tabla.DN.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                int row = i + 1;

                var tdDn = new TextBlock { Text = $"ø{tabla.DN[i]}", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Pincel("#201F1E"), Padding = new Thickness(0, 5, 0, 5) };
                WpfGrid.SetColumn(tdDn, 0); WpfGrid.SetRow(tdDn, row); grid.Children.Add(tdDn);

                var sw = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = Pincel(Hs5Colores.Hex(tabla.DN[i])), BorderBrush = new SolidColorBrush(MediaColor.FromArgb(30, 0, 0, 0)), BorderThickness = new Thickness(0.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                WpfGrid.SetColumn(sw, 1); WpfGrid.SetRow(sw, row); grid.Children.Add(sw);

                for (int c = 0; c < tabla.Columnas.Length; c++)
                {
                    string celda = tabla.Celda(i, c);
                    var td = new TextBlock
                    {
                        Text = celda, FontSize = 12, TextAlignment = TextAlignment.Right,
                        Foreground = Pincel(celda == "–" ? "#A19F9D" : "#0F6CBD"),
                        Padding = new Thickness(0, 5, 0, 5),
                    };
                    WpfGrid.SetColumn(td, c + 2); WpfGrid.SetRow(td, row); grid.Children.Add(td);
                }
            }

            outer.Children.Add(new Border { Background = Brushes.White, Child = grid });
            outer.Children.Add(new Border
            {
                BorderBrush = Pincel("#DDD9D4"),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock { Text = "E3G BIM Tools · Enginyeria i Energia", FontSize = 10, Foreground = Pincel("#A19F9D"), HorizontalAlignment = HorizontalAlignment.Right }
            });

            return outer;
        }

        private void BtnRestaurar_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.Resultados == null || Sesion.Resultados.Count == 0)
            { MessageBox.Show("Ejecuta primero 'Iniciar análisis'.", "E3G Saneamiento"); return; }
            _handler.Accion = AccionEvento.Restaurar;
            _evento.Raise();
        }

        // ── Barra de estado / progreso del análisis ──

        /// <summary>Texto de estado de la barra inferior.</summary>
        public void MostrarEstado(string texto)
        {
            if (StatusText != null) StatusText.Text = texto ?? "";
        }

        /// <summary>
        /// Progreso del análisis (0-100).  El análisis corre en el hilo de Revit, que
        /// es el mismo que el del panel: tras actualizar la barra se vacía la cola del
        /// Dispatcher (prioridad Background, inferior a Render) para que se repinte.
        /// </summary>
        public void ReportarProgreso(double porcentaje, string texto)
        {
            if (ProgressPanel == null) return;
            ProgressPanel.Visibility = WpfVisibility.Visible;
            ProgressAnalisis.Value = Math.Max(0, Math.Min(100, porcentaje));
            ProgressText.Text = porcentaje.ToString("0") + " %";
            if (!string.IsNullOrEmpty(texto)) StatusText.Text = texto + "…";
            try { Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); }
            catch { /* sin repintado intermedio */ }
        }

        /// <summary>Oculta la barra de progreso y deja <paramref name="estadoFinal"/> como estado.</summary>
        public void FinalizarProgreso(string estadoFinal)
        {
            if (ProgressPanel != null) ProgressPanel.Visibility = WpfVisibility.Collapsed;
            if (estadoFinal != null) MostrarEstado(estadoFinal);
        }
    }

    public class FilaTabla
    {
        public string IdStr { get; set; }
        public string DNActualStr { get; set; }
        public string UDStr { get; set; }
        public string PendStr { get; set; }
        public string DNHs5Str { get; set; }
        public string TramoStr { get; set; }
        public string Criterio { get; set; }
        public string ColorHex { get; set; }
        public string NombreColor { get; set; }
        public bool EsCorrect { get; set; }

    }

}