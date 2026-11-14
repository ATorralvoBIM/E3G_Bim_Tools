using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace E3G.Saneamiento
{
    public partial class FontaneriaWindow : Page
    {
        private ExternalEvent _eventoMedicion;
        private EventoMedicionFontaneria _handlerMedicion;
        private bool _todoExpandido = false;
        private string _filtroActivo = "todos";
        private ResumenFontaneria _ultimoResumen;

        // Colores corporativos fontanería
        private static readonly Color ColVerde = (Color)ColorConverter.ConvertFromString("#1D9E75");
        private static readonly Color ColVerdeClaro = (Color)ColorConverter.ConvertFromString("#D1FAE5");
        private static readonly Color ColRojo = (Color)ColorConverter.ConvertFromString("#FEE2E2");
        private static readonly Color ColVioleta = (Color)ColorConverter.ConvertFromString("#EDE9FE");

        public FontaneriaWindow()
        {
            InitializeComponent();
            E3G.Saneamiento.UI.E3gChrome.ApplyTheme(this);   // tema E3G
            _handlerMedicion = new EventoMedicionFontaneria { Pagina = this };
            _eventoMedicion = ExternalEvent.Create(_handlerMedicion);
            FontaneriaSession.HandlerMedicion = _handlerMedicion;
            FontaneriaSession.EventoMedicion = _eventoMedicion;
            FontaneriaSession.HandlerSeleccion = SaneamientoSession.HandlerSeleccion;
            FontaneriaSession.EventoSel = SaneamientoSession.EventoSel;
        }

        // ── Selección en Revit ──
        private void SeleccionarEnRevit(List<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            if (SaneamientoSession.HandlerSeleccion == null) return;
            SaneamientoSession.HandlerSeleccion.IdsASeleccionar = ids;
            SaneamientoSession.EventoSel.Raise();
        }

        // ── Filtros ──
        private void AplicarFiltro(string filtro)
        {
            _filtroActivo = filtro;
            var btns = new[] { FiltroTodos, FiltroP14, FiltroP56, FiltroP7, Filtro1D, Filtro2D, Filtro3D };
            foreach (var b in btns)
                b.Opacity = 1.0;

            switch (filtro)
            {
                case "todos": FiltroTodos.Opacity = 1; break;
                case "p14": FiltroP14.Opacity = 1; break;
                case "p56": FiltroP56.Opacity = 1; break;
                case "p7": FiltroP7.Opacity = 1; break;
                case "1d": Filtro1D.Opacity = 1; break;
                case "2d": Filtro2D.Opacity = 1; break;
                case "3d": Filtro3D.Opacity = 1; break;
            }

            if (_ultimoResumen != null) PoblarGrupos(_ultimoResumen);
        }

        private bool FiltroMatchGrupo(FilaGrupoFon grp)
        {
            switch (_filtroActivo)
            {
                case "p14":
                    return grp.Plantas.Any(p => p.StartsWith("P01") || p.StartsWith("P02") ||
                                                p.StartsWith("P03") || p.StartsWith("P04"));
                case "p56":
                    return grp.Plantas.Any(p => p.StartsWith("P05") || p.StartsWith("P06"));
                case "p7":
                    return grp.Plantas.Any(p => p.StartsWith("P07"));
                case "1d":
                    return grp.TipoNombre.Contains(".1D.");
                case "2d":
                    return grp.TipoNombre.Contains(".2D.");
                case "3d":
                    return grp.TipoNombre.Contains(".3D.");
                default:
                    return true;
            }
        }

        private void FiltroTodos_Click(object s, RoutedEventArgs e) => AplicarFiltro("todos");
        private void FiltroP14_Click(object s, RoutedEventArgs e) => AplicarFiltro("p14");
        private void FiltroP56_Click(object s, RoutedEventArgs e) => AplicarFiltro("p56");
        private void FiltroP7_Click(object s, RoutedEventArgs e) => AplicarFiltro("p7");
        private void Filtro1D_Click(object s, RoutedEventArgs e) => AplicarFiltro("1d");
        private void Filtro2D_Click(object s, RoutedEventArgs e) => AplicarFiltro("2d");
        private void Filtro3D_Click(object s, RoutedEventArgs e) => AplicarFiltro("3d");

        private void BtnExpandir_Click(object s, RoutedEventArgs e)
        {
            _todoExpandido = !_todoExpandido;
            TxtExpandir.Text = _todoExpandido ? "Colapsar todo" : "Expandir todo";
            if (_ultimoResumen != null) PoblarGrupos(_ultimoResumen);
        }

        // ── Datos ──
        public void ActualizarDatos(ResumenFontaneria res)
        {
            if (res == null) return;
            _ultimoResumen = res;
            FontaneriaSession.UltimoResumen = res;
            PoblarGrupos(res);
            PoblarTotales(res);
        }

        private void PoblarGrupos(ResumenFontaneria res)
        {
            PanelGrupos.Children.Clear();
            var grupos = res.Grupos.Where(FiltroMatchGrupo).ToList();
            foreach (var grp in grupos)
                PanelGrupos.Children.Add(CrearFilaGrupo(grp));
        }

        private void PoblarTotales(ResumenFontaneria res)
        {
            PanelTotalesBody.Children.Clear();
            var dns = new[] { "ø16", "ø20", "ø25", "ø32" };
            var sistemas = new[]
            {
                ("AFS", "#1D9E75"),
                ("ACS", "#F87171"),
                ("AR",  "#A78BFA"),
            };

            foreach (var (sis, hex) in sistemas)
            {
                if (!res.TotalesDN.ContainsKey(sis)) continue;
                var fila = new DockPanel { Margin = new Thickness(12, 5, 12, 5) };
                var lblSis = new TextBlock
                {
                    Text = sis,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Width = 40,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(lblSis, Dock.Left);
                fila.Children.Add(lblSis);

                var dnPanel = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var dn in dns)
                {
                    double val = res.TotalesDN[sis].ContainsKey(dn) ? res.TotalesDN[sis][dn] : 0;
                    var cell = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                    cell.Children.Add(new TextBlock
                    {
                        Text = dn,
                        FontSize = 9,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")),
                    });
                    cell.Children.Add(new TextBlock
                    {
                        Text = val > 0 ? $"{val:F1} m" : "—",
                        FontSize = 11,
                        FontWeight = FontWeights.Medium,
                        Foreground = val > 0
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDB8B2")),
                    });
                    dnPanel.Children.Add(cell);
                }
                fila.Children.Add(dnPanel);
                PanelTotalesBody.Children.Add(fila);
            }

            PanelTotales.Visibility = Visibility.Visible;
        }

        // ── Creación UI grupo ──
        private UIElement CrearFilaGrupo(FilaGrupoFon grp)
        {
            bool esMedianera = grp.TipoNombre.Contains("_M");
            string dorm = grp.TipoNombre.Contains(".1D.") ? "1D"
                        : grp.TipoNombre.Contains(".2D.") ? "2D"
                        : grp.TipoNombre.Contains(".3D.") ? "3D" : "—";

            var outer = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDD9D4")),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                ClipToBounds = true,
            };

            var stack = new StackPanel();

            // Header fila
            var header = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Cursor = Cursors.Hand,
            };

            var hPanel = new DockPanel();

            // Flecha
            var arrow = new TextBlock
            {
                Text = _todoExpandido ? "▼" : "▶",
                FontSize = 9,
                Foreground = new SolidColorBrush(_todoExpandido
                    ? (Color)ColorConverter.ConvertFromString("#1D9E75")
                    : (Color)ColorConverter.ConvertFromString("#A19F9D")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            DockPanel.SetDock(arrow, Dock.Left);

            // N instancias
            var nBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1FAE5")),
                CornerRadius = new CornerRadius(99),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"×{grp.NumInstancias}",
                    FontSize = 9,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46")),
                }
            };
            DockPanel.SetDock(nBadge, Dock.Right);

            // Btn seleccionar grupo
            var btnSel = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAF6F1")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7")),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "⊹ Sel. grupo",
                    FontSize = 9,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46")),
                }
            };
            DockPanel.SetDock(btnSel, Dock.Right);
            var instIds = grp.InstanciaIds;
            btnSel.MouseLeftButtonUp += (s, ev) =>
            {
                ev.Handled = true;
                SeleccionarEnRevit(instIds);
            };

            // Pill dormitorios
            var (pillBg, pillFg) = dorm == "1D" ? ("#FEF3C7", "#92400E")
                                  : dorm == "2D" ? ("#E6F1FB", "#0F6CBD")
                                                 : ("#D1FAE5", "#065F46");
            var pill = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pillBg)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = dorm,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pillFg)),
                }
            };

            // Nombre tipo
            var nombrePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            nombrePanel.Children.Add(pill);
            nombrePanel.Children.Add(new TextBlock
            {
                Text = grp.TipoNombre.Replace("_M", ""),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#605E5C")),
                FontFamily = new FontFamily("Consolas, Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (esMedianera)
                nombrePanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE9FE")),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "_M",
                        FontSize = 9,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5B21B6")),
                    }
                });

            // Métricas hidráulicas
            var metricPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            void AddMetric(string lbl, string val, string fg)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                sp.Children.Add(new TextBlock { Text = lbl, FontSize = 8, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")) });
                sp.Children.Add(new TextBlock { Text = val, FontSize = 10, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)) });
                metricPanel.Children.Add(sp);
            }
            if (grp.FlowTotal > 0)
            {
                AddMetric("Qt (L/s)", grp.FlowTotal.ToString("F4"), "#1D9E75");
                AddMetric("Qd (L/s)", grp.FlowDim.ToString("F4"), "#1D9E75");
                AddMetric("Δp/m", grp.Dpm.ToString("F3"), "#605E5C");
            }
            else
            {
                AddMetric("Qt", "—", "#A19F9D");
            }

            // Tags plantas
            var plantasPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var planta in grp.Plantas.OrderBy(p => p))
            {
                string nivel = planta.Replace(".A", "");
                var (tagBg, tagFg) = nivel.StartsWith("P07") ? ("#D1FAE5", "#065F46")
                                    : nivel.StartsWith("P05") || nivel.StartsWith("P06") ? ("#E6F1FB", "#0F6CBD")
                                    : ("#EDE9FE", "#5B21B6");
                plantasPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagBg)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 0, 2, 2),
                    Child = new TextBlock
                    {
                        Text = nivel,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagFg)),
                    }
                });
            }

            var leftStack = new StackPanel();
            var row1 = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            row1.Children.Add(nombrePanel);
            var row2 = new DockPanel();
            row2.Children.Add(metricPanel);
            row2.Children.Add(plantasPanel);
            leftStack.Children.Add(row1);
            leftStack.Children.Add(row2);

            hPanel.Children.Add(arrow);
            hPanel.Children.Add(nBadge);
            hPanel.Children.Add(btnSel);
            hPanel.Children.Add(leftStack);
            header.Child = hPanel;

            // Panel de detalle (pestañas internas)
            var detalle = new StackPanel { Visibility = _todoExpandido ? Visibility.Visible : Visibility.Collapsed };
            var detalleBorder = new Border
            {
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1EFEC")),
                BorderThickness = new Thickness(0, 0.5, 0, 0),
                Child = detalle,
            };

            // Pestañas internas
            var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 8) };
            var tabNames = new[] { "Instancias", "Medición tuberías", "Puntos consumo", "Llaves", "Derivaciones" };
            var tabPanels = new Dictionary<string, Border>();
            var tabBtns = new List<Border>();

            void SelectTab(int idx)
            {
                for (int i = 0; i < tabBtns.Count; i++)
                {
                    var tb = tabBtns[i];
                    var txt = tb.Child as TextBlock;
                    if (i == idx)
                    {
                        tb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D9E75"));
                        if (txt != null) txt.Foreground = Brushes.White;
                    }
                    else
                    {
                        tb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDD9D4"));
                        if (txt != null) txt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#605E5C"));
                    }
                }
                foreach (var kv in tabPanels)
                    kv.Value.Visibility = Visibility.Collapsed;
                tabPanels[tabNames[idx]].Visibility = Visibility.Visible;
            }

            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var name = tabNames[i];
                var tabBtn = new Border
                {
                    Background = i == 0
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D9E75"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDD9D4")),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = name,
                        FontSize = 10,
                        Foreground = i == 0 ? Brushes.White
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#605E5C")),
                    }
                };
                tabBtn.MouseLeftButtonUp += (s, ev) => SelectTab(idx);
                tabBtns.Add(tabBtn);
                tabBar.Children.Add(tabBtn);

                var panel = new Border { Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
                tabPanels[name] = panel;
            }

            // Contenido Instancias
            tabPanels["Instancias"].Child = CrearTabInstancias(grp);
            // Contenido Medición
            tabPanels["Medición tuberías"].Child = CrearTabMedicion(grp);
            // Contenido Puntos consumo
            tabPanels["Puntos consumo"].Child = CrearTabConsumo(grp);
            // Contenido Llaves
            tabPanels["Llaves"].Child = CrearTabLlaves(grp);
            // Contenido Derivaciones
            tabPanels["Derivaciones"].Child = CrearTabDerivaciones(grp);

            detalle.Children.Add(tabBar);
            foreach (var kv in tabPanels)
                detalle.Children.Add(kv.Value);

            stack.Children.Add(header);
            stack.Children.Add(detalleBorder);
            outer.Child = stack;

            // Toggle desplegable
            header.MouseLeftButtonUp += (s, ev) =>
            {
                if (ev.Handled) return;
                bool open = detalle.Visibility == Visibility.Visible;
                detalle.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
                arrow.Text = open ? "▶" : "▼";
                arrow.Foreground = open
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D9E75"));
            };

            return outer;
        }

        // ── Pestañas internas ──
        private UIElement CrearTabInstancias(FilaGrupoFon grp)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            stack.Children.Add(new TextBlock
            {
                Text = $"{grp.NumInstancias} instancias en: {string.Join(" · ", grp.Plantas.OrderBy(p => p))}",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")),
                Margin = new Thickness(0, 0, 0, 8),
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            // Header
            grid.RowDefinitions.Add(new RowDefinition());
            AddGridCell(grid, "Planta", 0, 0, "#A19F9D", false, true);
            AddGridCell(grid, "ID grupo", 0, 1, "#A19F9D", false, true);
            AddGridCell(grid, "Acción", 0, 2, "#A19F9D", false, true);

            int row = 1;
            foreach (var kv in grp.InstanciaIdPorPlanta.OrderBy(x => x.Key))
            {
                grid.RowDefinitions.Add(new RowDefinition());
                string planta = grp.Plantas.OrderBy(p => p).ElementAtOrDefault(row - 1) ?? "—";
                string nivel = planta.Replace(".A", "");
                var (tagBg, tagFg) = nivel.StartsWith("P07") ? ("#D1FAE5", "#065F46")
                                    : nivel.StartsWith("P05") || nivel.StartsWith("P06") ? ("#E6F1FB", "#0F6CBD")
                                    : ("#EDE9FE", "#5B21B6");

                var tagBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagBg)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 3, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = nivel + ".A",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagFg)),
                    }
                };
                Grid.SetColumn(tagBorder, 0); Grid.SetRow(tagBorder, row);
                grid.Children.Add(tagBorder);

                AddGridCell(grid, kv.Value.ToString(), row, 1, "#605E5C");

                int grupoId = kv.Value;
                var btnSelInst = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAF6F1")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7")),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(2, 3, 2, 3),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = "⊹ Sel.", FontSize = 9, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46")) }
                };
                btnSelInst.MouseLeftButtonUp += (s, ev) => SeleccionarEnRevit(new List<int> { grupoId });
                Grid.SetColumn(btnSelInst, 2); Grid.SetRow(btnSelInst, row);
                grid.Children.Add(btnSelInst);
                row++;
            }
            stack.Children.Add(grid);
            return stack;
        }

        private UIElement CrearTabMedicion(FilaGrupoFon grp)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            var dns = new[] { "ø16", "ø20", "ø25", "ø32" };
            var sistemas = new[] { ("AFS", "#1D9E75"), ("ACS", "#F87171"), ("AR", "#A78BFA") };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            foreach (var _ in dns)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.RowDefinitions.Add(new RowDefinition());
            AddGridCell(grid, "Sis.", 0, 0, "#A19F9D", false, true);
            for (int ci = 0; ci < dns.Length; ci++)
                AddGridCell(grid, dns[ci], 0, ci + 1, "#A19F9D", false, true);

            int row = 1;
            double[] totalesDN = new double[dns.Length];

            foreach (var (sis, hex) in sistemas)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                AddGridCell(grid, sis, row, 0, hex, true);
                for (int ci = 0; ci < dns.Length; ci++)
                {
                    double val = grp.MedicionSisDN.ContainsKey(sis) && grp.MedicionSisDN[sis].ContainsKey(dns[ci])
                                 ? grp.MedicionSisDN[sis][dns[ci]] : 0;
                    totalesDN[ci] += val;
                    AddGridCell(grid, val > 0 ? $"{val:F3}" : "—", row, ci + 1,
                                val > 0 ? hex : "#BDB8B2");
                }
                row++;
            }

            // Total
            grid.RowDefinitions.Add(new RowDefinition());
            AddGridCell(grid, "Total", row, 0, "#201F1E", true);
            for (int ci = 0; ci < dns.Length; ci++)
                AddGridCell(grid, totalesDN[ci] > 0 ? $"{totalesDN[ci]:F3}" : "—", row, ci + 1,
                            totalesDN[ci] > 0 ? "#201F1E" : "#BDB8B2", true);

            stack.Children.Add(grid);
            return stack;
        }

        private UIElement CrearTabConsumo(FilaGrupoFon grp)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            if (!grp.Aparatos.Any())
            {
                stack.Children.Add(new TextBlock { Text = "Sin datos de aparatos", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")) });
                return stack;
            }
            foreach (var (sis, hex) in new[] { ("AFS", "#1D9E75"), ("ACS", "#F87171"), ("AR", "#A78BFA") })
            {
                if (!grp.Aparatos.ContainsKey(sis)) continue;
                stack.Children.Add(new TextBlock { Text = sis, FontSize = 10, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), Margin = new Thickness(0, 4, 0, 2) });
                foreach (var kv in grp.Aparatos[sis].OrderBy(x => x.Key))
                {
                    var row = new DockPanel { Margin = new Thickness(8, 1, 0, 1) };
                    row.Children.Add(new TextBlock { Text = $"{kv.Value} ud.", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#605E5C")), Width = 40 });
                    DockPanel.SetDock(row.Children[0], Dock.Right);
                    row.Children.Add(new TextBlock { Text = kv.Key, FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#605E5C")) });
                    stack.Children.Add(row);
                }
            }
            return stack;
        }

        private UIElement CrearTabLlaves(FilaGrupoFon grp)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            stack.Children.Add(new TextBlock
            {
                Text = "Pendiente de implementar — requiere familia de llaves en modelo",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")),
            });
            return stack;
        }

        private UIElement CrearTabDerivaciones(FilaGrupoFon grp)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 10) };
            if (!grp.Derivaciones.Any())
            {
                stack.Children.Add(new TextBlock { Text = "Sin derivaciones individuales encontradas", FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")) });
                return stack;
            }

            stack.Children.Add(new TextBlock
            {
                Text = "Derivación individual AFS · metros por DN por planta",
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A19F9D")),
                Margin = new Thickness(0, 0, 0, 6),
            });

            var dns = grp.Derivaciones.Values.SelectMany(d => d.Keys).Distinct().OrderBy(d => d).ToList();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (var _ in dns)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            grid.RowDefinitions.Add(new RowDefinition());
            AddGridCell(grid, "Planta", 0, 0, "#A19F9D", false, true);
            for (int ci = 0; ci < dns.Count; ci++)
                AddGridCell(grid, dns[ci], 0, ci + 1, "#A19F9D", false, true);
            AddGridCell(grid, "Total", 0, dns.Count + 1, "#A19F9D", false, true);
            AddGridCell(grid, "Sel.", 0, dns.Count + 2, "#A19F9D", false, true);

            int row = 1;
            foreach (var planta in grp.Plantas.OrderBy(p => p))
            {
                if (!grp.Derivaciones.ContainsKey(planta)) continue;
                grid.RowDefinitions.Add(new RowDefinition());
                var dnData = grp.Derivaciones[planta];
                double total = dnData.Values.Sum();
                string nivel = planta.Replace(".A", "");
                var (tagBg, tagFg) = nivel.StartsWith("P07") ? ("#D1FAE5", "#065F46")
                                    : nivel.StartsWith("P05") || nivel.StartsWith("P06") ? ("#E6F1FB", "#0F6CBD")
                                    : ("#EDE9FE", "#5B21B6");
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagBg)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = nivel + ".A", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tagFg)) }
                };
                Grid.SetColumn(tagBorder, 0); Grid.SetRow(tagBorder, row);
                grid.Children.Add(tagBorder);

                for (int ci = 0; ci < dns.Count; ci++)
                {
                    double val = dnData.ContainsKey(dns[ci]) ? dnData[dns[ci]] : 0;
                    AddGridCell(grid, val > 0 ? $"{val:F3}" : "—", row, ci + 1, val > 0 ? "#7C3AED" : "#BDB8B2");
                }
                AddGridCell(grid, $"{total:F3}", row, dns.Count + 1, "#201F1E", true);

                // Botón seleccionar derivación
                var derIds = grp.IdsDerivacionPorPlanta.ContainsKey(planta)
                             ? grp.IdsDerivacionPorPlanta[planta] : new List<int>();
                var btnDer = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0EDFF")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4B5FD")),
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(2, 2, 2, 2),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "⊹ Sel.", FontSize = 9, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4C1D95")) }
                };
                var capturedIds = derIds;
                btnDer.MouseLeftButtonUp += (s, ev) =>
                {
                    ev.Handled = true;
                    SeleccionarEnRevit(capturedIds);
                };
                Grid.SetColumn(btnDer, dns.Count + 2); Grid.SetRow(btnDer, row);
                grid.Children.Add(btnDer);
                row++;
            }
            stack.Children.Add(grid);
            return stack;
        }

        // ── Helper grid ──
        private void AddGridCell(Grid grid, string text, int row, int col,
            string hex, bool bold = false, bool isHeader = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = isHeader ? 10 : 11,
                FontWeight = bold || isHeader ? FontWeights.Medium : FontWeights.Normal,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Padding = new Thickness(4, 4, 4, 4),
                TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right,
            };
            Grid.SetColumn(tb, col);
            Grid.SetRow(tb, row);
            grid.Children.Add(tb);
        }

        // ── Botones ──
        private void BtnMedir_Click(object sender, RoutedEventArgs e)
        {
            _eventoMedicion.Raise();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (_ultimoResumen == null)
            { MessageBox.Show("Ejecuta primero 'Medir'.", "E3G Fontanería"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "E3G_Mediciones_Fontaneria",
                DefaultExt = ".csv",
                Filter = "CSV|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();

            // ── BLOC 1: Medició tubs de grup ──
            sb.AppendLine("MEDICIÓN TUBERÍAS DE GRUPO");
            sb.AppendLine("Tipo;Instancias;Plantas;Qt (L/s);Qd (L/s);Δp/m (kPa);AFS_DN16;AFS_DN20;AFS_DN25;AFS_DN32;ACS_DN16;ACS_DN20;ACS_DN25;ACS_DN32;AR_DN16;AR_DN20;AR_DN25;AR_DN32");
            foreach (var grp in _ultimoResumen.Grupos)
            {
                double Get(string sis, string dn) =>
                    grp.MedicionSisDN.ContainsKey(sis) && grp.MedicionSisDN[sis].ContainsKey(dn)
                    ? grp.MedicionSisDN[sis][dn] : 0;
                sb.AppendLine($"{grp.TipoNombre};{grp.NumInstancias};{string.Join("|", grp.Plantas)};{grp.FlowTotal};{grp.FlowDim};{grp.Dpm};" +
                    $"{Get("AFS", "ø16")};{Get("AFS", "ø20")};{Get("AFS", "ø25")};{Get("AFS", "ø32")};" +
                    $"{Get("ACS", "ø16")};{Get("ACS", "ø20")};{Get("ACS", "ø25")};{Get("ACS", "ø32")};" +
                    $"{Get("AR", "ø16")};{Get("AR", "ø20")};{Get("AR", "ø25")};{Get("AR", "ø32")}");
            }

            // ── BLOC 2: Derivacions individuals per planta ──
            var grpsAmbDer = _ultimoResumen.Grupos.Where(g => g.Derivaciones.Any()).ToList();
            if (grpsAmbDer.Any())
            {
                sb.AppendLine();
                sb.AppendLine("DERIVACIONES INDIVIDUALES AFS");

                // Recollir tots els DN presents a les derivacions
                var dnsDer = grpsAmbDer
                    .SelectMany(g => g.Derivaciones.Values)
                    .SelectMany(d => d.Keys)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                sb.AppendLine("Tipo;" + "Planta;" + string.Join(";", dnsDer) + ";Total (m)");

                foreach (var grp in grpsAmbDer)
                {
                    foreach (var planta in grp.Plantas.OrderBy(p => p))
                    {
                        if (!grp.Derivaciones.ContainsKey(planta)) continue;
                        var dnData = grp.Derivaciones[planta];
                        double total = dnData.Values.Sum();
                        var valsDN = dnsDer.Select(dn =>
                            dnData.ContainsKey(dn) ? dnData[dn].ToString("F3") : "0");
                        sb.AppendLine($"{grp.TipoNombre};{planta};{string.Join(";", valsDN)};{total:F3}");
                    }
                }
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"✓ Exportado en:\n{dlg.FileName}", "E3G Fontanería");
        }
    }
}