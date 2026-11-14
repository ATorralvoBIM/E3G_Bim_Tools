using System.Windows;
using System.Windows.Controls;
using E3G.Saneamiento.UI;

namespace E3G.Saneamiento
{
    /// <summary>Consulta de las tablas del RITE; los valores salen de <see cref="RiteAislamiento"/>.</summary>
    public partial class AislamientoTablaWindow : Window
    {
        private static readonly FluidoRite[] Fluidos = { FluidoRite.Caliente, FluidoRite.Frio, FluidoRite.Acs };

        public AislamientoTablaWindow()
        {
            InitializeComponent();
            foreach (var fluido in Fluidos)
            {
                ColInterior.Children.Add(CrearTabla(RiteAislamiento.Tabla(fluido, UbicacionRite.Interior)));
                ColExterior.Children.Add(CrearTabla(RiteAislamiento.Tabla(fluido, UbicacionRite.Exterior)));
            }
            var ambiente = AmbienteCondensacion.PorDefecto;
            ColAguaFria.Children.Add(CrearTabla(RiteAislamiento.TablaAguaFria(
                RiteAislamiento.TemperaturaPorDefecto(FluidoRite.Afs), ambiente.Temperatura)));

            int e = RiteAislamiento.EspesorAguaFriaMm;
            TxtNotas.Text =
                "Diámetro exterior de la tubería D en mm; espesores en mm, para λ = 0,040 W/(m·K) a 10 °C. " +
                "Procedimiento simplificado: solo para instalaciones de hasta 70 kW.\n" +
                "Los accesorios llevan el mismo espesor que su tubería y el retorno el mismo que la ida. " +
                "Tuberías de D ≤ 25 mm y menos de 10 m, empotradas o por conductos interiores, desde la red general " +
                "hasta la unidad terminal: 10 mm, evitando en cualquier caso la condensación.\n" +
                "Fuente: RD 1027/2007, IT 1.2.4.2.1.2, texto consolidado del BOE (actualización 02/08/2022).\n\n" +
                "Agua fría sanitaria (fuera de las tablas del RITE): el RITE obliga a evitar las condensaciones " +
                "superficiales e intersticiales en las redes de agua fría sanitaria (IT 1.2.4.2.1.2, ap. 9), y el CTE pide " +
                "una protección con capacidad de barrera antivapor en tuberías vistas, ocultas o empotradas " +
                "(DB HS 4, ap. 5.1.1.3.2, materiales según UNE 100171). Ninguno fija un espesor. " +
                $"E3G parte de {e} mm y la app comprueba, con el método de la UNE-EN ISO 12241 al que remite el RITE, que la " +
                "cara exterior del aislamiento quede por encima del punto de rocío del aire; si no, sube al siguiente " +
                "espesor comercial (tabla anticondensación). Cálculo del lado de la seguridad: tubería a la temperatura del " +
                "agua, aire en calma, acabado no metálico (ε = 0,9) y el peor caso entre tramo horizontal y vertical. " +
                $"Con agua a 10 °C y aire a 25 °C, {e} mm basta en todos los diámetros hasta un 70 % de humedad. " +
                "Contra la condensación intersticial, el material debe ser de célula cerrada o llevar barrera de vapor " +
                "de más de 50 MPa·m²·s/g (IT 1.2.4.2.1.1, ap. 5). En el exterior, acabado contra la intemperie " +
                "(IT 1.2.4.2.1.1, ap. 2) y protección contra heladas (DB HS 4, ap. 5.1.1.3.3).\n\n" +
                "Líneas frigoríficas (split, VRV): se aíslan por llevar fluido más frío que el local (IT 1.2.4.2.1.1, ap. 1 a). " +
                "Si la máquina es reversible, la línea de gas lleva frío o gas caliente según el modo y manda la condición más " +
                $"exigente (IT 1.2.4.2.1.2, ap. 4): la app toma el mayor entre la tabla de frío a la temperatura de evaporación y la " +
                $"de calor a {RiteAislamiento.TemperaturaGasCaliente:0} °C. En las conexiones de equipos de refrigeración doméstica " +
                "se permite reducir 10 mm (IT 1.2.4.2.1.2, ap. 7); la app no lo aplica. Las superficies frías de los propios equipos " +
                "llevan el espesor del fabricante (IT 1.2.4.2.1.1, ap. 3).";
            E3gChrome.Apply(this);
        }

        private static UIElement CrearTabla(TablaRite tabla)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            foreach (var _ in tabla.Columnas)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i <= tabla.Filas.Length; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AgregarCelda(grid, 0, 0, "D (mm)", true);
            for (int c = 0; c < tabla.Columnas.Length; c++)
                AgregarCelda(grid, 0, c + 1, tabla.Columnas[c], true);

            for (int f = 0; f < tabla.Filas.Length; f++)
            {
                AgregarCelda(grid, f + 1, 0, tabla.Filas[f], false);
                for (int c = 0; c < tabla.Columnas.Length; c++)
                {
                    int valor = tabla.Valores[f, c];
                    AgregarCelda(grid, f + 1, c + 1, valor > 0 ? valor.ToString() : "—", false);
                }
            }

            var pila = new StackPanel();
            pila.Children.Add(Texto(tabla.Cabecera, 12, FontWeights.SemiBold, "E3g.Brush.Text"));
            pila.Children.Add(Texto(tabla.Titulo, 11, FontWeights.Normal, "E3g.Brush.Text"));
            var encabezado = Texto(tabla.EncabezadoColumnas, 10, FontWeights.Normal, "E3g.Brush.TextMuted");
            encabezado.Margin = new Thickness(0, 2, 0, 6);
            pila.Children.Add(encabezado);
            pila.Children.Add(grid);

            var tarjeta = new Border
            {
                Child = pila,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 12),
            };
            tarjeta.SetResourceReference(Border.BackgroundProperty, "E3g.Brush.Card");
            tarjeta.SetResourceReference(Border.BorderBrushProperty, "E3g.Brush.Border");
            return tarjeta;
        }

        private static void AgregarCelda(Grid grid, int fila, int columna, string valor, bool cabecera)
        {
            var celda = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 4, 4, 4),
                Child = Texto(valor, 11, cabecera ? FontWeights.SemiBold : FontWeights.Normal,
                              cabecera ? "E3g.Brush.TextMuted" : "E3g.Brush.Text"),
            };
            ((TextBlock)celda.Child).TextAlignment = columna == 0 ? TextAlignment.Left : TextAlignment.Right;
            celda.SetResourceReference(Border.BorderBrushProperty, "E3g.Brush.BorderSubtle");
            Grid.SetRow(celda, fila);
            Grid.SetColumn(celda, columna);
            grid.Children.Add(celda);
        }

        private static TextBlock Texto(string valor, double tamano, FontWeight peso, string pincel)
        {
            var texto = new TextBlock { Text = valor, FontSize = tamano, FontWeight = peso };
            texto.SetResourceReference(TextBlock.ForegroundProperty, pincel);
            return texto;
        }
    }
}
