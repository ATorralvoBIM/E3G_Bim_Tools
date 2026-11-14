namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// Linea de medicion (proveniente de un registro ~M de FIEBDC-3).
///
/// Una partida puede tener varias mediciones (cada una con su descripcion
/// y dimensiones).  Para Sprint A1 capturamos la cantidad final;
/// el desglose detallado (largo x ancho x alto) se anadira en sprints
/// posteriores si hace falta.
/// </summary>
public sealed class BC3Measurement
{
    /// <summary>Codigo de la partida a la que pertenece la medicion.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Texto descriptivo de la medicion (linea, posicion, comentario).</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Cantidad medida total.</summary>
    public double Quantity { get; set; }

    /// <summary>
    /// Si la medicion viene desglosada en dimensiones (L x A x H), se guardan
    /// para futuras herramientas de revision.  En Sprint A1 quedan a 0.
    /// </summary>
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
