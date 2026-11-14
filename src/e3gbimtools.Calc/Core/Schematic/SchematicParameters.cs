// ============================================================================
//  SchematicParameters.cs
//  Parámetros de generación del esquema — escala, dimensiones, qué se etiqueta,
//  colores.  Inmutable después de construirse (cambia la instancia, no el
//  contenido) para que el servicio sea referencialmente transparente.
// ============================================================================
namespace e3gbimtools.Calc.Core.Schematic
{
    /// <summary>Escala de vista de Revit para el esquema generado.</summary>
    public enum SchematicScale
    {
        /// <summary>1:50 — recomendable hasta ~200 tramos.</summary>
        OneToFifty = 50,
        /// <summary>1:100 — para redes grandes que no caben a 1:50.</summary>
        OneToHundred = 100,
    }

    public sealed class SchematicParameters
    {
        // ── geometría en milímetros (espacio modelo, antes de aplicar escala) ──
        /// <summary>Separación vertical entre filas (mm).</summary>
        public double MmPerRow { get; set; } = 50.0;
        /// <summary>Indentación horizontal por nivel de columna (mm).</summary>
        public double MmPerColumn { get; set; } = 200.0;
        /// <summary>Margen superior del canvas (mm).</summary>
        public double MmTopMargin { get; set; } = 50.0;
        /// <summary>X del tronco col 0 desde el origen (mm).</summary>
        public double MmTrunkX { get; set; } = 50.0;

        /// <summary>Escala de la vista resultante.</summary>
        public SchematicScale Scale { get; set; } = SchematicScale.OneToFifty;

        // ── tipografía (en mm — la vista hereda la escala) ──
        public double TextHeightMm { get; set; } = 2.5;
        public double LegendTextHeightMm { get; set; } = 3.5;

        /// <summary>Tamaño base de los símbolos esquemáticos (mm de papel).</summary>
        public double SymbolSizeMm { get; set; } = 3.5;

        // ── qué se dibuja ──
        public bool ShowPipeLabels { get; set; } = true;
        public bool ShowFittingLabels { get; set; } = true;
        public bool ShowLegend { get; set; } = true;
        public bool HighlightCriticalPath { get; set; } = true;

        // Filtros por categoría de accesorio de tubería.
        // NOTA: el motor actual (SchematicLayoutEngine) no detecta valves /
        // meters / sensors como nodos del esquema — solo PIPE, TEE, ELBOW,
        // CROSS, REDUCER, TERMINAL, FIN.  Estos flags se propagan desde la UI
        // y se respetan en el bucle de fittings del generador para cuando el
        // motor empiece a producirlos (lectura de PipeAccessory por subtipo).
        public bool ShowValves  { get; set; } = true;
        public bool ShowMeters  { get; set; } = true;
        public bool ShowSensors { get; set; } = true;

        // ── colores (RGB) — usados tanto en preview WPF como en Revit ──
        public (byte R, byte G, byte B) SystemRgb { get; set; } = (0x1A, 0x6F, 0xA8);
        public (byte R, byte G, byte B) CriticalPathRgb { get; set; } = (0xC6, 0x28, 0x28);

        /// <summary>Nombre que se le da a la ViewDrafting creada.</summary>
        public string ViewName { get; set; }

        public SchematicParameters() { }

        /// <summary>Cambia la escala recomendada según el tamaño esperado.</summary>
        public static SchematicScale RecommendScale(int rowCount)
            => rowCount > 200 ? SchematicScale.OneToHundred : SchematicScale.OneToFifty;
    }
}
