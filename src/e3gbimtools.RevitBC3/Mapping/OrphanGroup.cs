using System.Collections.Generic;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Grupo de elementos del modelo que no fueron asignados a ninguna regla
/// (huerfanos).  Agrupados por categoria + tipo de familia para que el
/// usuario pueda detectar bloques importantes sin mapear.
/// </summary>
public sealed class OrphanGroup
{
    /// <summary>Nombre de la categoria (localizado, ej. "Tuberias").</summary>
    public string CategoryDisplayName { get; set; } = string.Empty;

    /// <summary>Nombre del tipo / familia / sistema (depende de la categoria).</summary>
    public string TypeOrFamilyName { get; set; } = string.Empty;

    /// <summary>Numero de elementos huerfanos en este grupo.</summary>
    public int Count { get; set; }

    /// <summary>IDs de los elementos del grupo.</summary>
    public List<long> ElementIds { get; set; } = new();

    public override string ToString() =>
        $"{CategoryDisplayName} / {TypeOrFamilyName}: {Count}";
}
