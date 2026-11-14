namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// DTO plano que representa una entrada del catálogo BC3 importado desde TCQ
/// (o cualquier software FIEBDC-3).  Se usa exclusivamente en la UI del selector
/// de partidas (CatalogPickerDialog) y como resultado de BC3CatalogImporterService.
///
/// Se mantiene deliberadamente simple: sin hijos, sin mediciones, sin ~T.
/// Todo lo que necesita el picker está aquí.
/// </summary>
public sealed class CatalogEntry
{
    /// <summary>Código único del concepto (campo CODIGO del registro ~C).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Unidad de medida (m, m2, m3, ud, kg, h, etc.).</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Descripción breve (campo RESUMEN del registro ~C).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Precio unitario (campo PRECIO del registro ~C).</summary>
    public double PriceUnit { get; set; }

    /// <summary>
    /// Tipo de concepto derivado del campo TIPO del registro ~C y de la
    /// jerarquía ~D.  Valores: "Capitulo", "Partida", "Material",
    /// "ManoDeObra", "Maquinaria", "Auxiliar", "Desconocido".
    /// </summary>
    public string Type { get; set; } = "Desconocido";

    /// <summary>
    /// Código del concepto padre inmediato según los registros ~D.
    /// Null si el concepto es raíz (no aparece como hijo en ningún ~D).
    /// </summary>
    public string? ParentCode { get; set; }

    // ---- propiedades calculadas para la UI ----

    /// <summary>Cadena de visualización compacta para ComboBox y listas.</summary>
    public string Display =>
        string.IsNullOrWhiteSpace(Description)
            ? Code
            : $"{Code}  —  {Description}";

    /// <summary>Icono de tipo para mostrar en el árbol/lista del picker.</summary>
    public string TypeIcon => Type switch
    {
        "Capitulo"   => "📁",
        "Partida"    => "📄",
        "Material"   => "🧱",
        "ManoDeObra" => "👷",
        "Maquinaria" => "⚙",
        "Auxiliar"   => "🔩",
        _            => "·",
    };

    public override string ToString() => Display;
}
