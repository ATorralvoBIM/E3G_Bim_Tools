namespace E3G.BimTools.DiTools.Core.Models.Levels;

/// <summary>
/// Opciones de la operacion CopyLevels.  Conjunto reducido en Sprint 1:
/// prefijo/sufijo en el nombre + opcion de crear vistas de planta automaticas.
/// </summary>
public sealed class LevelCopyOptions
{
    /// <summary>
    /// Texto que se antepone al nombre original al crear el nivel en el anfitrion.
    /// Vacio = sin prefijo.
    /// </summary>
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Texto que se anade al final del nombre original al crear el nivel en el anfitrion.
    /// Vacio = sin sufijo.
    /// </summary>
    public string NameSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Si es true, por cada nivel copiado se genera una vista de planta arquitectonica
    /// (ViewPlan) usando el ViewFamilyType "FloorPlan" por defecto del documento anfitrion.
    /// </summary>
    public bool CreateFloorPlans { get; set; } = false;
}
