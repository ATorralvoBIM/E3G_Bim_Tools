namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Configuracion del parametro de medicion de una regla.
/// Puede ser una medicion built-in (longitud/area/volumen/cantidad) o un
/// parametro custom de Revit (instancia o tipo, numerico).
/// </summary>
public sealed class MeasurementParameter
{
    /// <summary>Tipo de medicion.  None = se usa <see cref="CustomParameterName"/>.</summary>
    public BuiltInMeasurement BuiltIn { get; set; } = BuiltInMeasurement.None;

    /// <summary>Nombre del parametro custom (si BuiltIn == None).</summary>
    public string CustomParameterName { get; set; } = string.Empty;

    /// <summary>True si BuiltIn != None.</summary>
    public bool IsBuiltIn => BuiltIn != BuiltInMeasurement.None;

    public override string ToString()
    {
        if (BuiltIn != BuiltInMeasurement.None)
            return BuiltIn switch
            {
                BuiltInMeasurement.Length => "Longitud",
                BuiltInMeasurement.Area   => "Area",
                BuiltInMeasurement.Volume => "Volumen",
                BuiltInMeasurement.Count  => "Cantidad",
                _                         => "—",
            };
        return string.IsNullOrEmpty(CustomParameterName) ? "(sin definir)" : CustomParameterName;
    }
}
