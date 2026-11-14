namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// Relacion padre-hijo en un BC3 (registro ~D).
///
/// Un concepto puede tener N descomposiciones (cada una apunta a otro
/// concepto por su codigo, con un factor y rendimiento).
/// </summary>
public sealed class BC3Decomposition
{
    /// <summary>Codigo del concepto hijo.</summary>
    public string ChildCode { get; set; } = string.Empty;

    /// <summary>Factor multiplicador (default = 1).</summary>
    public double Factor { get; set; } = 1.0;

    /// <summary>Rendimiento o cantidad (cuanto del hijo entra por unidad de padre).</summary>
    public double Performance { get; set; } = 1.0;
}
