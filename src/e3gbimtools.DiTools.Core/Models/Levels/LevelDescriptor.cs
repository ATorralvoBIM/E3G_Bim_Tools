using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Levels;

/// <summary>
/// Snapshot ligero de un nivel encontrado en un documento vinculado.
/// No retiene el Element (los Element de un LinkDocument no pueden cruzar
/// transacciones del doc anfitrion); en su lugar guarda los datos minimos
/// para listarlo en la UI y para recrearlo en el anfitrion.
/// </summary>
public sealed class LevelDescriptor
{
    /// <summary>ElementId.Value del nivel en el documento vinculado de origen.</summary>
    public long LinkedLevelId { get; set; }

    /// <summary>Nombre del nivel tal como aparece en el documento vinculado.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Elevacion del nivel en unidades internas Revit (feet).  Si el vinculo
    /// trae transformacion (offset Z), se aplica antes de pasar al destino.
    /// </summary>
    public double ElevationInternal { get; set; }

    /// <summary>Elevacion en metros para mostrar al usuario.</summary>
    public double ElevationMeters { get; set; }

    /// <summary>True si en el documento anfitrion ya existe un nivel con el mismo nombre.</summary>
    public bool ExistsInHost { get; set; }

    /// <summary>Resumen para tooltip / debug.</summary>
    public override string ToString() => $"{Name} (Z={ElevationMeters:F3} m)";
}
