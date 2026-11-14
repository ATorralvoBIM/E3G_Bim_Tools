namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Indica el origen del IfcGUID asignado a un elemento durante la extraccion.
/// </summary>
public enum IfcGuidSource
{
    /// <summary>
    /// GUID leido del parametro built-in <c>IFC_GUID</c> o del parametro
    /// compartido <c>IfcGUID</c> (el elemento ya habia sido exportado a IFC).
    /// </summary>
    Parameter,

    /// <summary>
    /// GUID calculado deterministicamente desde el <c>UniqueId</c> de Revit
    /// mediante <see cref="E3G.BimTools.RevitBC3.Services.RevitIfcGuidCalculator"/>.
    /// Coincidira con el que Revit asignaria al exportar a IFC.
    /// </summary>
    Calculated,

    /// <summary>
    /// No se pudo obtener ni leer ni calcular un GUID valido.  En este caso
    /// <see cref="ElementMeasurementContribution.IfcGuid"/> contiene un
    /// identificador de emergencia basado en el <c>ElementId</c> de Revit.
    /// </summary>
    Fallback,
}

/// <summary>
/// Detalle del aporte de UN elemento Revit a una <see cref="MeasurementResult"/>.
/// Se guarda durante la extraccion para poder emitir en el BC3 exportado una
/// linea de medicion detallada por elemento, llevando el IfcGUID como comentario.
/// Esto permite rastrear desde Presto/Arquimedes cada cantidad hasta el elemento
/// original del modelo Revit/IFC.
/// </summary>
public sealed class ElementMeasurementContribution
{
    /// <summary>Identificador interno del elemento en Revit (Element.Id.Value).</summary>
    public long ElementId { get; set; }

    /// <summary>
    /// IfcGUID del elemento.  Se rellena siempre con uno de estos origenes
    /// (en orden de prioridad):
    /// <list type="number">
    ///   <item>Parametro built-in <c>IFC_GUID</c> o compartido <c>IfcGUID</c>.</item>
    ///   <item>Calculo deterministico desde el <c>UniqueId</c> de Revit
    ///         (mismo resultado que Revit obtendria al exportar a IFC).</item>
    ///   <item>Identificador de emergencia prefijado con "E" si lo anterior falla.</item>
    /// </list>
    /// Consulta <see cref="IfcGuidSource"/> para saber que origen se uso.
    /// </summary>
    public string IfcGuid { get; set; } = string.Empty;

    /// <summary>Cantidad aportada por el elemento, ya en unidades metricas y con UnitFactor aplicado.</summary>
    public double Quantity { get; set; }

    /// <summary>
    /// Nombre del sistema MEP al que pertenece el elemento (ej. "AFS", "PLUVIALS Ø110").
    /// Vacio si el elemento no tiene sistema asignado o no es un elemento MEP.
    /// Se usa para agrupar las lineas de medicion en el Visor BC3 y en el ~M exportado.
    /// </summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del nivel (planta) al que esta asociado el elemento (ej. "P00", "P01").
    /// Vacio si no se puede determinar.
    /// </summary>
    public string LevelName { get; set; } = string.Empty;

    /// <summary>
    /// Indica el origen del <see cref="IfcGuid"/>: leido del parametro,
    /// calculado desde UniqueId, o fallback de emergencia.
    /// Util para el panel de diagnostico de cobertura de GUIDs.
    /// </summary>
    public IfcGuidSource IfcGuidSource { get; set; } = IfcGuidSource.Fallback;

    public override string ToString() => $"{ElementId} [{IfcGuid}/{IfcGuidSource}] = {Quantity:F3}";
}
