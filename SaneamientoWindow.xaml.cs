using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento;
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
        private ExternalEvent _eventoMedicion;
        private EventoMedicion _handlerMedicion;
        private ExternalEvent _eventoEsquema;
        private EventoEsquemaPrincipi _handlerEsquema;
        private bool _normDropdownOpen = false;
        private enum FabAccio { Analitzar, MedirFecales, MedirPluvials, MedirGrises }
        private FabAccio _fabAccio = FabAccio.Analitzar; private ExternalEvent _eventoInserir;
        private EventoInserirEsquema _handlerInserir;

        private static readonly MediaColor ColFecales = (MediaColor)ColorConverter.ConvertFromString("#A8D4BA");

        public EsquemaPrincipiControl GetTabEsquema() => tabEsquema;
        public SaneamientoWindow(ExternalEvent evento, EventoSaneamiento handler)
        {
            InitializeComponent();
            _evento = evento;
            _handler = handler;

            _handlerMedicion = new EventoMedicion { Pagina = this };
            _eventoMedicion = ExternalEvent.Create(_handlerMedicion);

            _handlerEsquema = new EventoEsquemaPrincipi { Pagina = this };
            _eventoEsquema = ExternalEvent.Create(_handlerEsquema);

            _handlerInserir = new EventoInserirEsquema { Pagina = this };
            _eventoInserir = ExternalEvent.Create(_handlerInserir);

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
            PanelMediciones.Visibility = WpfVisibility.Collapsed;
            PanelEsquema.Visibility = WpfVisibility.Collapsed;
            SelectorNormativa.Visibility = WpfVisibility.Visible;
            tabEsquema?.BuidarBrowser();
            ActualitzarFab();
        }

        private void TabMediciones_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelMediciones == null) return;
            PanelVerificacion.Visibility = WpfVisibility.Collapsed;
            PanelMediciones.Visibility = WpfVisibility.Visible;
            PanelEsquema.Visibility = WpfVisibility.Collapsed;
            SelectorNormativa.Visibility = WpfVisibility.Collapsed;
            tabEsquema?.BuidarBrowser();
            ActualitzarFab();
        }

        private void TabEsquema_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelEsquema == null) return;
            PanelVerificacion.Visibility = WpfVisibility.Collapsed;
            PanelMediciones.Visibility = WpfVisibility.Collapsed;
            PanelEsquema.Visibility = WpfVisibility.Visible;
            SelectorNormativa.Visibility = WpfVisibility.Collapsed;
            _eventoEsquema.Raise();
            ActualitzarFab();
        }

        public void IniciarEsquema(Document doc, List<int> pipeIds)
        {
            tabEsquema.EventoInserir = _eventoInserir;
            tabEsquema.IniciarAmbDoc(doc, pipeIds);
        }

        // ── Selector normativa ──
        private void BtnNormativa_Click(object sender, RoutedEventArgs e)
        {
            _normDropdownOpen = !_normDropdownOpen;
            NormDropdown.Visibility = _normDropdownOpen ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            NormChevron.Text = _normDropdownOpen ? "▲" : "▼";
        }

        private void NormOpt1_Click(object sender, MouseButtonEventArgs e)
        {
            NormTitulo.Text = "Tabla 4.5 CTE DB-HS5";
            NormSubtitulo.Text = "Colectores Suspendidos · pendiente 1%";
            _normDropdownOpen = false;
            NormDropdown.Visibility = WpfVisibility.Collapsed;
            NormChevron.Text = "▼";
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

            var filasOk = resultados
                .Where(r => r.EsCorrect)
                .Select(r => new FilaTabla
                {
                    IdStr = r.Id.IntegerValue.ToString(),
                    DNActualStr = $"ø{r.DNActual}",
                    UDStr = r.UD.ToString("F1"),
                    DNHs5Str = $"ø{r.DNCorrect}",
                    ColorHex = CteMotor.GetColorHex(r.IdxCte),
                    NombreColor = CteMotor.NombreColor(r.IdxCte),
                    EsCorrect = true,
                }).ToList();

            var filasKo = resultados
                .Where(r => !r.EsCorrect)
                .Select(r => new FilaTabla
                {
                    IdStr = r.Id.IntegerValue.ToString(),
                    DNActualStr = $"ø{r.DNActual}",
                    UDStr = r.UD.ToString("F1"),
                    DNHs5Str = $"KO → ø{r.DNCorrect}",
                    ColorHex = CteMotor.GetColorHex(r.IdxCte),
                    NombreColor = CteMotor.NombreColor(r.IdxCte),
                    EsCorrect = false,
                }).ToList();

            TablaOk.ItemsSource = filasOk;
            TablaKo.ItemsSource = filasKo;
        }

        // ── Mediciones ──
        public void ActualizarMediciones(ResumenMediciones res)
        {
            if (res == null) return;
            Sesion.UltimasMediciones = res;
            PoblarPanelFecales(res);
        }

        private void PoblarPanelFecales(ResumenMediciones res)
        {
            PanelFecalesContenido.Children.Clear();

            // ── Interior viviendas (sempre fix — ve dels Grups) ──────────────────
            var secInterior = CrearSeccion("Interior viviendas", ColFecales,
                () => SeleccionarEnRevit(res.IdsInterior));
            var bodyInterior = ObtenerBody(secInterior);

            foreach (var kv in res.InteriorPorDN.OrderBy(x => x.Key))
                bodyInterior.Children.Add(CrearFilaDN(kv.Key, kv.Value));

            bodyInterior.Children.Add(CrearSeparador("Detalle por grupo"));

            foreach (var grp in res.Grupos)
                bodyInterior.Children.Add(CrearFilaGrupo(grp));

            if (res.AparatosSinGrupo.Any())
            {
                foreach (var nivel in res.AparatosSinGrupo.Select(a => a.Nivel).Distinct())
                {
                    var aparatos = res.AparatosSinGrupo.Where(a => a.Nivel == nivel).ToList();
                    bodyInterior.Children.Add(CrearAparatosSinGrupo(nivel, aparatos));
                }
            }
            PanelFecalesContenido.Children.Add(secInterior);

            // ── Categories dinàmiques del Protocol ──────────────────────────────
            // Ordre: primer les seccions del Protocol, després les que no hi són
            var seccionsProtocol = Protocol.ProtocolSession.ProtocolActual?.Seccions
                .Select(s => s.Etiqueta).ToList()
                ?? new List<string>();

            // Construïm l'ordre de renderització
            var categoriesOrdenades = new List<string>();

            // 1. Primer les que estan al Protocol (en ordre)
            foreach (var etiqueta in seccionsProtocol)
                if (res.MetresPorCategoria.ContainsKey(etiqueta))
                    categoriesOrdenades.Add(etiqueta);

            // 2. Després les que no estan al Protocol (fallback / hardcoded)
            foreach (var cat in res.MetresPorCategoria.Keys
                .Where(k => !categoriesOrdenades.Contains(k))
                .OrderBy(k => k))
                categoriesOrdenades.Add(cat);

            foreach (var categoria in categoriesOrdenades)
            {
                // Obtenir les IDs per selecció
                res.IdsPorCategoria.TryGetValue(categoria, out var ids);
                var idsSel = ids ?? new List<int>();

                // Obtenir les files de ServiciosComunes per aquesta categoria
                var files = res.ServiciosComunes
                    .Where(s => s.Categoria == categoria)
                    .ToList();

                // Títol amb total
                res.MetresPorCategoria.TryGetValue(categoria, out var totalM);
                var titol = $"{categoria}  ·  {totalM:F2} m";

                var sec = CrearSeccion(titol, ColFecales,
                    idsSel.Count > 0 ? () => SeleccionarEnRevit(idsSel) : (Action)null);
                var body = ObtenerBody(sec);

                if (files.Any())
                    body.Children.Add(CrearTablaGrid(files));
                else
                    body.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = $"{totalM:F2} m (sense detall per nivell/DN)",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(
                            (MediaColor)ColorConverter.ConvertFromString("#475569")),
                        Margin = new Thickness(14, 8, 14, 8)
                    });

                PanelFecalesContenido.Children.Add(sec);
            }
        }

        // ── Helpers UI ──
        private Border CrearSeccion(string titulo, MediaColor borderColor, Action onSelectClick = null)
        {
            var outer = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var innerStack = new StackPanel();
            var header = new Border { Padding = new Thickness(14, 10, 14, 10), Cursor = Cursors.Hand };
            var headerPanel = new DockPanel();

            var arrow = new TextBlock
            {
                Text = "▶",
                FontSize = 10,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(arrow, Dock.Right);

            // Botón de selección (icono cursor)
            if (onSelectClick != null)
            {
                var btnSel = new Border
                {
                    Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#EAF1F5")),
                    BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#C0D8E8")),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "⊹ Seleccionar",
                        FontSize = 10,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#2D5F7A")),
                    }
                };
                DockPanel.SetDock(btnSel, Dock.Right);
                btnSel.MouseLeftButtonUp += (s, ev) =>
                {
                    ev.Handled = true;
                    onSelectClick?.Invoke();
                };
                headerPanel.Children.Add(btnSel);
            }

            headerPanel.Children.Add(arrow);

            var title = new TextBlock
            {
                Text = titulo,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#1E293B")),
            };
            headerPanel.Children.Add(title);
            header.Child = headerPanel;

            var body = new StackPanel { Visibility = WpfVisibility.Collapsed };
            var bodyBorder = new Border
            {
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#F1F5F9")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Child = body,
            };

            header.MouseLeftButtonUp += (s, ev) =>
            {
                if (ev.Handled) return;
                bool open = body.Visibility == WpfVisibility.Visible;
                body.Visibility = open ? WpfVisibility.Collapsed : WpfVisibility.Visible;
                arrow.Text = open ? "▶" : "▼";
            };

            innerStack.Children.Add(header);
            innerStack.Children.Add(bodyBorder);
            outer.Child = innerStack;
            outer.Tag = body;
            return outer;
        }

        private StackPanel ObtenerBody(Border sec) => (StackPanel)sec.Tag;

        private UIElement CrearFilaDN(string dn, double longM)
        {
            var color = CteMotor.GetColorHexByDN(dn);
            var panel = new DockPanel { Margin = new Thickness(14, 6, 14, 6) };

            var swatch = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString(color)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var lblDn = new TextBlock
            {
                Text = dn,
                FontSize = 11,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var lblVal = new TextBlock
            {
                Text = $"{longM:F2} m",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#334155")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(lblVal, Dock.Right);

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(swatch);
            left.Children.Add(lblDn);
            panel.Children.Add(lblVal);
            panel.Children.Add(left);
            return panel;
        }

        private UIElement CrearSeparador(string texto) => new Border
        {
            Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#F8FAFC")),
            BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
            BorderThickness = new Thickness(0, 0.5, 0, 0.5),
            Padding = new Thickness(14, 5, 14, 5),
            Child = new TextBlock
            {
                Text = texto,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
            }
        };

        private UIElement CrearFilaGrupo(FilaGrupo grp)
        {
            var outer = new StackPanel();
            var header = new Border
            {
                Padding = new Thickness(14, 7, 14, 7),
                Cursor = Cursors.Hand,
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#F1F5F9")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
            };

            var hPanel = new DockPanel();
            var arrow = new TextBlock
            {
                Text = "▶",
                FontSize = 9,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(arrow, Dock.Right);

            // Botón seleccionar grupo
            var btnSel = new Border
            {
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#EAF1F5")),
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#C0D8E8")),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "⊹ Sel.",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#2D5F7A")),
                }
            };
            DockPanel.SetDock(btnSel, Dock.Right);

            var instIds = grp.InstanciaIds;
            btnSel.MouseLeftButtonUp += (s, ev) =>
            {
                ev.Handled = true;
                SeleccionarEnRevit(instIds);
            };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(new TextBlock
            {
                Text = grp.GrupoTipo,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#334155")),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            left.Children.Add(new Border
            {
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#D4EDDC")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = $"×{grp.NumInstancias}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#2D6B48")),
                }
            });
            left.Children.Add(new TextBlock
            {
                Text = $"ID {grp.TipoId}",
                FontSize = 10,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                VerticalAlignment = VerticalAlignment.Center,
            });

            hPanel.Children.Add(arrow);
            hPanel.Children.Add(btnSel);
            hPanel.Children.Add(left);
            header.Child = hPanel;

            var detail = new StackPanel { Visibility = WpfVisibility.Collapsed };
            var detailBorder = new Border
            {
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#F1F5F9")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Padding = new Thickness(14, 8, 14, 10),
                Child = detail,
            };

            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var ap in grp.Aparatos.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
            {
                chips.Children.Add(new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 5, 5),
                    Child = new TextBlock
                    {
                        Text = ap,
                        FontSize = 10,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                    }
                });
            }
            detail.Children.Add(chips);

            foreach (var kv in grp.DNS.OrderBy(x => x.Key))
                detail.Children.Add(CrearFilaDN(kv.Key, kv.Value));

            header.MouseLeftButtonUp += (s, ev) =>
            {
                if (ev.Handled) return;
                bool open = detail.Visibility == WpfVisibility.Visible;
                detail.Visibility = open ? WpfVisibility.Collapsed : WpfVisibility.Visible;
                arrow.Text = open ? "▶" : "▼";
            };

            outer.Children.Add(header);
            outer.Children.Add(detailBorder);
            return outer;
        }

        private UIElement CrearAparatosSinGrupo(string nivel, List<AparatoSinGrupo> aparatos)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"Aparatos sin grupo · {nivel}",
                FontSize = 10,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                Margin = new Thickness(0, 0, 0, 6),
            });
            var chips = new WrapPanel();
            foreach (var ap in aparatos)
            {
                chips.Children.Add(new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 5, 5),
                    Child = new TextBlock
                    {
                        Text = ap.Cantidad > 1 ? $"{ap.Nombre} ×{ap.Cantidad}" : ap.Nombre,
                        FontSize = 10,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                    }
                });
            }
            stack.Children.Add(chips);
            return new Border
            {
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#FFFBF0")),
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Padding = new Thickness(14, 8, 14, 10),
                Child = stack,
            };
        }

        private UIElement CrearTablaGrid(List<FilaServicioComun> filas)
        {
            var dns = filas.Select(f => f.DN).Distinct().OrderBy(d => d).ToList();
            var niveles = filas.Select(f => f.Nivel).Distinct().OrderBy(n => n).ToList();

            var grid = new WpfGrid { Margin = new Thickness(14, 8, 14, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            foreach (var _ in dns)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition());
            var hdrNivel = new TextBlock
            {
                Text = "Nivel",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                Padding = new Thickness(0, 0, 0, 4),
            };
            WpfGrid.SetColumn(hdrNivel, 0); WpfGrid.SetRow(hdrNivel, 0);
            grid.Children.Add(hdrNivel);

            for (int ci = 0; ci < dns.Count; ci++)
            {
                var hdr = new TextBlock
                {
                    Text = dns[ci],
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    TextAlignment = TextAlignment.Right,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                    Padding = new Thickness(0, 0, 0, 4),
                };
                WpfGrid.SetColumn(hdr, ci + 1); WpfGrid.SetRow(hdr, 0);
                grid.Children.Add(hdr);
            }

            for (int ri = 0; ri < niveles.Count; ri++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                var nivel = niveles[ri];
                var nivelCell = new TextBlock
                {
                    Text = nivel,
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                    Padding = new Thickness(0, 4, 0, 4),
                };
                WpfGrid.SetColumn(nivelCell, 0); WpfGrid.SetRow(nivelCell, ri + 1);
                grid.Children.Add(nivelCell);

                for (int ci = 0; ci < dns.Count; ci++)
                {
                    var dn = dns[ci];
                    var fila = filas.FirstOrDefault(f => f.Nivel == nivel && f.DN == dn);
                    var cell = new TextBlock
                    {
                        Text = fila != null ? $"{fila.LongitudM:F2} m" : "—",
                        FontSize = 10,
                        TextAlignment = TextAlignment.Right,
                        Foreground = new SolidColorBrush(fila != null
                            ? (MediaColor)ColorConverter.ConvertFromString("#334155")
                            : (MediaColor)ColorConverter.ConvertFromString("#D1D5DB")),
                        Padding = new Thickness(0, 4, 0, 4),
                    };
                    WpfGrid.SetColumn(cell, ci + 1); WpfGrid.SetRow(cell, ri + 1);
                    grid.Children.Add(cell);
                }
            }
            return grid;
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
            var win = new Window
            {
                Title = "CTE DB-HS5 · Tabla 4.5",
                Width = 420,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#F8FAFC")),
            };

            var outer = new StackPanel();

            var header = new Border
            {
                Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#4a7a96")),
                Padding = new Thickness(16, 12, 16, 12),
            };
            var hStack = new StackPanel { Orientation = Orientation.Horizontal };
            var logoStack = new StackPanel();
            logoStack.Children.Add(new TextBlock { Text = "E3G", FontSize = 18, FontWeight = FontWeights.Light, Foreground = Brushes.White });
            logoStack.Children.Add(new TextBlock { Text = "ENGINYERIA I ENERGIA", FontSize = 7, Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255)) });
            hStack.Children.Add(logoStack);
            hStack.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(MediaColor.FromArgb(80, 255, 255, 255)), Margin = new Thickness(12, 0, 12, 0) });
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock { Text = "Tabla de dimensionado", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brushes.White });
            infoStack.Children.Add(new TextBlock { Text = "Colectores Suspendidos · pendiente 1%", FontSize = 10, Foreground = new SolidColorBrush(MediaColor.FromArgb(180, 255, 255, 255)) });
            hStack.Children.Add(infoStack);
            header.Child = hStack;
            outer.Children.Add(header);

            outer.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(0, 0, 0, 0.5),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock
                {
                    Text = "CTE DB-HS5 · Tabla 4.5 al 1% de pendiente",
                    FontSize = 11,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                }
            });

            var grid = new WpfGrid { Margin = new Thickness(16, 12, 16, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] dns = { "ø90", "ø110", "ø125", "ø160", "ø200", "ø250", "ø315", "ø350" };
            string[] uds = { "96", "264", "390", "880", "1.600", "2.900", "5.710", "8.300" };
            string[] hexes = { "#E24B4A", "#EF9F27", "#E8D800", "#A0522D", "#378ADD", "#7F77DD", "#1AB3C4", "#E0E0E0" };
            string[] names = { "Rojo", "Naranja", "Amarillo", "Marrón", "Azul", "Violeta", "Cian", "Blanco" };

            grid.RowDefinitions.Add(new RowDefinition());
            string[] headers = { "DN (mm)", "UD máx.", "Color" };
            for (int ci = 0; ci < headers.Length; ci++)
            {
                var th = new TextBlock
                {
                    Text = headers[ci],
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#94A3B8")),
                    Padding = new Thickness(0, 0, 0, 6),
                };
                WpfGrid.SetColumn(th, ci); WpfGrid.SetRow(th, 0);
                grid.Children.Add(th);
            }

            for (int i = 0; i < dns.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                int row = i + 1;

                var tdDn = new TextBlock { Text = dns[i], FontSize = 12, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#1E293B")), Padding = new Thickness(0, 5, 0, 5) };
                WpfGrid.SetColumn(tdDn, 0); WpfGrid.SetRow(tdDn, row); grid.Children.Add(tdDn);

                var tdUd = new TextBlock { Text = uds[i], FontSize = 12, Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#4a7a96")), Padding = new Thickness(0, 5, 0, 5) };
                WpfGrid.SetColumn(tdUd, 1); WpfGrid.SetRow(tdUd, row); grid.Children.Add(tdUd);

                var colorPanel = new StackPanel { Orientation = Orientation.Horizontal };
                colorPanel.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString(hexes[i])), BorderBrush = new SolidColorBrush(MediaColor.FromArgb(30, 0, 0, 0)), BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                colorPanel.Children.Add(new TextBlock { Text = names[i], FontSize = 11, Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")), VerticalAlignment = VerticalAlignment.Center });

                var tdColor = new Border { Padding = new Thickness(0, 5, 0, 5), Child = colorPanel };
                WpfGrid.SetColumn(tdColor, 2); WpfGrid.SetRow(tdColor, row); grid.Children.Add(tdColor);
            }

            outer.Children.Add(new Border { Background = Brushes.White, Child = grid });
            outer.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
                Child = new TextBlock { Text = "E3G BIM Tools · Enginyeria i Energia", FontSize = 10, Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#CBD5E1")), HorizontalAlignment = HorizontalAlignment.Right }
            });

            win.Content = outer;
            win.ShowDialog();
        }

        private void BtnRestaurar_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.Resultados == null || Sesion.Resultados.Count == 0)
            { MessageBox.Show("Ejecuta primero 'Iniciar análisis'.", "E3G Saneamiento"); return; }
            _handler.Accion = AccionEvento.Restaurar;
            _evento.Raise();
        }

        // ── Botones sistema mediciones ──
        private void BtnSisFecales_Click(object sender, RoutedEventArgs e)
        {
            PanelSisFecales.Visibility = WpfVisibility.Visible;
            PanelSisPluviales.Visibility = WpfVisibility.Collapsed;
            PanelSisGrises.Visibility = WpfVisibility.Collapsed;
            ActualizarColorBotonSistema(BtnSisFecales, "#3d8a5e", true);
            ActualizarColorBotonSistema(BtnSisPluviales, "#2e6da4", false);
            ActualizarColorBotonSistema(BtnSisGrises, "#7e52a0", false);
            ActualitzarFab();
        }

        private void BtnSisPluviales_Click(object sender, RoutedEventArgs e)
        {
            PanelSisFecales.Visibility = WpfVisibility.Collapsed;
            PanelSisPluviales.Visibility = WpfVisibility.Visible;
            PanelSisGrises.Visibility = WpfVisibility.Collapsed;
            ActualizarColorBotonSistema(BtnSisFecales, "#3d8a5e", false);
            ActualizarColorBotonSistema(BtnSisPluviales, "#2e6da4", true);
            ActualizarColorBotonSistema(BtnSisGrises, "#7e52a0", false);
            ActualitzarFab();
        }

        private void BtnSisGrises_Click(object sender, RoutedEventArgs e)
        {
            PanelSisFecales.Visibility = WpfVisibility.Collapsed;
            PanelSisPluviales.Visibility = WpfVisibility.Collapsed;
            PanelSisGrises.Visibility = WpfVisibility.Visible;
            ActualizarColorBotonSistema(BtnSisFecales, "#3d8a5e", false);
            ActualizarColorBotonSistema(BtnSisPluviales, "#2e6da4", false);
            ActualizarColorBotonSistema(BtnSisGrises, "#7e52a0", true);
            ActualitzarFab();
        }

        private void ActualizarColorBotonSistema(Button btn, string hex, bool activo)
        {
            var color = (MediaColor)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            btn.Background = activo ? brush : Brushes.White;
            if (btn.Content is TextBlock tb)
                tb.Foreground = activo ? Brushes.White : brush;
        }
       
        public void ActualizarPluvials(ResumenPluvials res)
        {
            if (res == null) return;
            PanelPluvialsContenido.Children.Clear();
            var colPluvials = (MediaColor)ColorConverter.ConvertFromString("#2e6da4");

            var secTotal = CrearSeccion($"Total pluvials · {res.TotalM:F2} m", colPluvials);
            var bodyTotal = ObtenerBody(secTotal);
            bodyTotal.Children.Add(new TextBlock
            {
                Text = $"Bajants: {res.BajantsM:F2} m   ·   Horitzontals: {res.HoritzontalsM:F2} m   ·   Buneres: {res.NumBuneres}",
                FontSize = 11,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                Margin = new Thickness(14, 8, 14, 4),
            });
            foreach (var kv in res.MedicioPorDN.OrderBy(x => x.Key))
                bodyTotal.Children.Add(CrearFilaDN(kv.Key, kv.Value));
            PanelPluvialsContenido.Children.Add(secTotal);

            if (res.Bajants.Any())
            {
                var secBajants = CrearSeccion("Bajants", colPluvials);
                var bodyBajants = ObtenerBody(secBajants);
                foreach (var b in res.Bajants.OrderBy(x => x.PlantaInici))
                {
                    var fila = new DockPanel { Margin = new Thickness(14, 5, 14, 5) };
                    var lblVal = new TextBlock
                    {
                        Text = $"{b.LongitudM:F2} m",
                        FontSize = 11,
                        FontWeight = FontWeights.Medium,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#334155")),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    DockPanel.SetDock(lblVal, Dock.Right);
                    var lblDesc = new TextBlock
                    {
                        Text = $"{b.DN}  ·  {b.PlantaInici} → {b.PlantaFi}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#475569")),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    fila.Children.Add(lblVal);
                    fila.Children.Add(lblDesc);
                    bodyBajants.Children.Add(fila);
                }
                PanelPluvialsContenido.Children.Add(secBajants);
            }
        }

        public void ActualizarGrises(ResumenGrises res)
        {
            if (res == null) return;
            PanelGrisesContenido.Children.Clear();
            var colGrises = (MediaColor)ColorConverter.ConvertFromString("#7e52a0");

            var secTotal = CrearSeccion($"Total grises · {res.TotalM:F2} m", colGrises);
            var bodyTotal = ObtenerBody(secTotal);
            foreach (var kv in res.MedicioPorDN.OrderBy(x => x.Key))
                bodyTotal.Children.Add(CrearFilaDN(kv.Key, kv.Value));
            if (res.Grupos.Any())
            {
                bodyTotal.Children.Add(CrearSeparador("Detall per grup"));
                foreach (var grp in res.Grupos)
                    bodyTotal.Children.Add(CrearFilaGrupo(grp));
            }
            PanelGrisesContenido.Children.Add(secTotal);
        }

        private void BtnMedirPluvials_Click(object sender, RoutedEventArgs e)
        {
            if (SaneamientoSession.EventoPluvials == null) return;
            SaneamientoSession.HandlerPluvials.Pagina = this;
            SaneamientoSession.EventoPluvials.Raise();
        }

        private void BtnMedirGrises_Click(object sender, RoutedEventArgs e)
        {
            if (SaneamientoSession.EventoGrises == null) return;
            SaneamientoSession.HandlerGrises.Pagina = this;
            SaneamientoSession.EventoGrises.Raise();
        }

        // ── Botones mediciones ──
        private void BtnMedirFecales_Click(object sender, RoutedEventArgs e)
        {
            _eventoMedicion.Raise();
        }

        private void BtnExportarBc3_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ui = SaneamientoSession.UiApp;
                if (ui == null)
                {
                    MessageBox.Show(
                        "Encara no s'ha inicialitzat el context de Revit.\n\n" +
                        "Activa qualsevol vista del projecte i torna a clicar.",
                        "Context no disponible",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (ui.ActiveUIDocument?.Document == null)
                {
                    MessageBox.Show("Cap document actiu a Revit. Obre un projecte.",
                        "Sense document", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var clasifService = new ClasificacionSistemasService();
                var cmd = new E3G.Saneamiento.Presupuesto.Commands.ExportarBc3Command(ui, clasifService);
                var ok = cmd.Ejecutar(Window.GetWindow(this));

                // Si Ejecutar retorna false sense missatge previ, mostrem un avis genèric
                // perquè l'usuari sàpiga que el click ha arribat però alguna validació
                // ha fallat en silenci.
                System.Diagnostics.Debug.WriteLine($"[ExportarBC3] Ejecutar retornó {ok}");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Error en exportar BC3:\n\n{ex.Message}\n\n--- Stack ---\n{ex.StackTrace}",
                    "Error inesperat",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportarFecales_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.UltimasMediciones == null)
            { MessageBox.Show("Ejecuta primero 'Medir'.", "E3G Saneamiento"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "E3G_Mediciones_Fecales",
                DefaultExt = ".csv",
                Filter = "CSV|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            var res = Sesion.UltimasMediciones;

            sb.AppendLine("INTERIOR VIVIENDAS");
            sb.AppendLine("Grupo;Instancias;ID;Aparatos;" +
                string.Join(";", res.InteriorPorDN.Keys.OrderBy(x => x)) + ";Total (m)");
            foreach (var grp in res.Grupos)
            {
                var dnsCsv = res.InteriorPorDN.Keys.OrderBy(x => x)
                    .Select(dn => grp.DNS.ContainsKey(dn) ? grp.DNS[dn].ToString("F2") : "0");
                sb.AppendLine($"{grp.GrupoTipo};{grp.NumInstancias};{grp.TipoId};{grp.Aparatos};" +
                    string.Join(";", dnsCsv) + $";{grp.TotalM}");
            }

            if (res.AparatosSinGrupo.Any())
            {
                sb.AppendLine();
                sb.AppendLine("APARATOS SIN GRUPO");
                sb.AppendLine("Nivel;Aparato;Cantidad");
                foreach (var ap in res.AparatosSinGrupo)
                    sb.AppendLine($"{ap.Nivel};{ap.Nombre};{ap.Cantidad}");
            }

            sb.AppendLine();
            sb.AppendLine("SERVICIOS COMUNES");
            sb.AppendLine("Categoría;Nivel;DN;Long. (m)");
            foreach (var f in res.ServiciosComunes)
                sb.AppendLine($"{f.Categoria};{f.Nivel};{f.DN};{f.LongitudM}");

            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"✓ Exportado en:\n{dlg.FileName}", "E3G Saneamiento");
        }
        private void BtnExportarPluvials_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.UltimesPluvials == null)
            { MessageBox.Show("Ejecuta primero 'Medir'.", "E3G Saneamiento"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "E3G_Medicions_Pluvials",
                DefaultExt = ".csv",
                Filter = "CSV|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var res = Sesion.UltimesPluvials;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("RESUM PLUVIALS");
            sb.AppendLine("Total (m);Bajants (m);Horitzontals (m);Num. Buneres");
            sb.AppendLine($"{res.TotalM};{res.BajantsM};{res.HoritzontalsM};{res.NumBuneres}");

            sb.AppendLine();
            sb.AppendLine("MEDICIÓ PER DN");
            sb.AppendLine("DN;Long. (m)");
            foreach (var kv in res.MedicioPorDN.OrderBy(x => x.Key))
                sb.AppendLine($"{kv.Key};{kv.Value}");

            if (res.Bajants.Any())
            {
                sb.AppendLine();
                sb.AppendLine("BAJANTS");
                sb.AppendLine("DN;Planta Inici;Planta Fi;Long. (m)");
                foreach (var b in res.Bajants.OrderBy(x => x.PlantaInici))
                    sb.AppendLine($"{b.DN};{b.PlantaInici};{b.PlantaFi};{b.LongitudM}");
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"✓ Exportado en:\n{dlg.FileName}", "E3G Saneamiento");
        }

        private void BtnExportarGrises_Click(object sender, RoutedEventArgs e)
        {
            if (Sesion.UltimesGrises == null)
            { MessageBox.Show("Ejecuta primero 'Medir'.", "E3G Saneamiento"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "E3G_Medicions_Grises",
                DefaultExt = ".csv",
                Filter = "CSV|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var res = Sesion.UltimesGrises;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("RESUM GRISES");
            sb.AppendLine("Total (m)");
            sb.AppendLine($"{res.TotalM}");

            sb.AppendLine();
            sb.AppendLine("MEDICIÓ PER DN");
            sb.AppendLine("DN;Long. (m)");
            foreach (var kv in res.MedicioPorDN.OrderBy(x => x.Key))
                sb.AppendLine($"{kv.Key};{kv.Value}");

            if (res.Grupos.Any())
            {
                sb.AppendLine();
                sb.AppendLine("DETALL PER GRUP");
                var dnsCols = res.Grupos.SelectMany(g => g.DNS.Keys).Distinct().OrderBy(d => d).ToList();
                sb.AppendLine("Grup;Instàncies;ID;" + string.Join(";", dnsCols) + ";Total (m)");
                foreach (var grp in res.Grupos)
                {
                    var vals = dnsCols.Select(dn => grp.DNS.ContainsKey(dn) ? grp.DNS[dn].ToString("F2") : "0");
                    sb.AppendLine($"{grp.GrupoTipo};{grp.NumInstancias};{grp.TipoId};{string.Join(";", vals)};{grp.TotalM}");
                }
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"✓ Exportado en:\n{dlg.FileName}", "E3G Saneamiento");
        }

        // ── FAB Principal ──
        private void ActualitzarFab()
        {
            if (FabPrincipal == null) return;

            if (TabVerificacion.IsChecked == true)
            {
                FabPrincipal.Visibility = WpfVisibility.Visible;
                FabPrincipal.AccentColor = "#4a7a96";
                FabPrincipal.ToolTip = "Iniciar análisis";
                _fabAccio = FabAccio.Analitzar;
            }
            else if (TabMediciones.IsChecked == true)
            {
                FabPrincipal.Visibility = WpfVisibility.Visible;

                if (PanelSisFecales.Visibility == WpfVisibility.Visible)
                {
                    FabPrincipal.AccentColor = "#3d8a5e";
                    FabPrincipal.ToolTip = "Medir Fecales";
                    _fabAccio = FabAccio.MedirFecales;
                }
                else if (PanelSisPluviales.Visibility == WpfVisibility.Visible)
                {
                    FabPrincipal.AccentColor = "#2e6da4";
                    FabPrincipal.ToolTip = "Medir Pluviales";
                    _fabAccio = FabAccio.MedirPluvials;
                }
                else
                {
                    FabPrincipal.AccentColor = "#7e52a0";
                    FabPrincipal.ToolTip = "Medir Grises";
                    _fabAccio = FabAccio.MedirGrises;
                }
            }
            else // Esquema
            {
                FabPrincipal.Visibility = WpfVisibility.Collapsed;
            }
        }

        private void FabPrincipal_Clicked(object sender, RoutedEventArgs e)
        {
            switch (_fabAccio)
            {
                case FabAccio.Analitzar:
                    BtnAnalizar_Click(sender, e); break;
                case FabAccio.MedirFecales:
                    BtnMedirFecales_Click(sender, e); break;
                case FabAccio.MedirPluvials:
                    BtnMedirPluvials_Click(sender, e); break;
                case FabAccio.MedirGrises:
                    BtnMedirGrises_Click(sender, e); break;
            }
        }
    }

    public class FilaTabla
    {
        public string IdStr { get; set; }
        public string DNActualStr { get; set; }
        public string UDStr { get; set; }
        public string DNHs5Str { get; set; }
        public string ColorHex { get; set; }
        public string NombreColor { get; set; }
        public bool EsCorrect { get; set; }

    }

}