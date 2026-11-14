namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Mediciones built-in que se pueden calcular sin necesidad de un
/// parametro custom: longitud, area, volumen o cantidad de unidades.
/// </summary>
public enum BuiltInMeasurement
{
    /// <summary>Ninguna built-in: se usa un parametro custom (ver <see cref="MeasurementParameter.CustomParameterName"/>).</summary>
    None,

    /// <summary>Longitud del elemento (para conductos, tuberias, cables, lineas).</summary>
    Length,

    /// <summary>Area (para conductos, suelos, paredes).</summary>
    Area,

    /// <summary>Volumen (para hormigon, conductos volumetricos).</summary>
    Volume,

    /// <summary>Cantidad de unidades (cuenta elementos).</summary>
    Count,
}
