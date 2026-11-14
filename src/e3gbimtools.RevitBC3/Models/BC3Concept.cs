using System.Collections.Generic;

namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// Un concepto del BC3: capitulo, partida, material, mano de obra, maquinaria.
/// FIEBDC-3 unifica todo en "conceptos" identificados por codigo; la
/// jerarquia se reconstruye a partir de los registros ~D (descomposiciones).
/// </summary>
public sealed class BC3Concept
{
    /// <summary>Codigo unico del concepto (clave primaria en el BC3).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Unidad de medida (m, m2, m3, ud, kg, h, etc.).</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Resumen breve (registro ~C, campo RESUMEN).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Texto descriptivo extenso (registro ~T).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Precio unitario.</summary>
    public double Price { get; set; }

    /// <summary>Fecha del precio (texto tal cual viene del BC3, formato variable).</summary>
    public string PriceDate { get; set; } = string.Empty;

    /// <summary>Tipo de concepto (capitulo, partida, etc.).</summary>
    public ConceptType Type { get; set; } = ConceptType.Unknown;

    /// <summary>Lista de descomposiciones — los conceptos hijos.</summary>
    public List<BC3Decomposition> Children { get; } = new();

    /// <summary>Mediciones asociadas (si las hay) — del registro ~M.</summary>
    public List<BC3Measurement> Measurements { get; } = new();

    public override string ToString() => $"{Code} ({Type}): {Summary}";
}
