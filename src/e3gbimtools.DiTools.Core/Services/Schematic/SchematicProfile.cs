using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.DiTools.Core.Models.Schematic;

namespace E3G.BimTools.DiTools.Core.Services.Schematic;

/// <summary>
/// Como leer los esquemas de un software de fabricante: que capas son equipos
/// (y de que tipo), cuales son tuberias y cuales rotulos.  Hoy: LG LATS.
/// Daikin y Mitsubishi se añadiran como perfiles nuevos cuando haya un DXF de
/// muestra de cada uno.
/// </summary>
public sealed class SchematicProfile
{
    public string Name { get; init; } = string.Empty;
    /// <summary>El perfil se reconoce si alguna capa empieza por este prefijo.</summary>
    public string LayerPrefix { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, SchematicEquipmentKind> EquipmentLayers { get; init; } =
        new Dictionary<string, SchematicEquipmentKind>(StringComparer.OrdinalIgnoreCase);
    public ISet<string> PipeLayers { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Corrige el tipo segun el nombre del bloque (p. ej. el conector de exteriores va en la capa ODU).</summary>
    public Func<SchematicEquipmentKind, string, SchematicEquipmentKind> RefineKind { get; init; } = (k, _) => k;

    public bool Matches(IEnumerable<string> layerNames) =>
        LayerPrefix.Length > 0 && layerNames.Any(l => l.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>LG LATS (Multi V): capas LATS_*_DIA del diagrama de tuberias.</summary>
    public static SchematicProfile LgLats { get; } = new()
    {
        Name = "LG LATS",
        LayerPrefix = "LATS_",
        EquipmentLayers = new Dictionary<string, SchematicEquipmentKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["LATS_EQ_ODU_DIA"] = SchematicEquipmentKind.OutdoorUnit,
            ["LATS_EQ_HRUNIT_DIA"] = SchematicEquipmentKind.HeatRecovery,
            ["LATS_EQ_BRANCH_DIA"] = SchematicEquipmentKind.Branch,
            ["LATS_EQ_RISERHOLE_DIA"] = SchematicEquipmentKind.RiserHole,
            ["LATS_EQ_IDU_DIA"] = SchematicEquipmentKind.IndoorUnit,
            // Variantes sin _DIA (planta de LATS) por si se exporta el otro dibujo.
            ["LATS_EQ_ODU"] = SchematicEquipmentKind.OutdoorUnit,
            ["LATS_EQ_HRUNIT"] = SchematicEquipmentKind.HeatRecovery,
            ["LATS_EQ_BRANCH"] = SchematicEquipmentKind.Branch,
            ["LATS_EQ_IDU"] = SchematicEquipmentKind.IndoorUnit,
        },
        PipeLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LATS_PIPE_REFRIGERANT_DIA", "LATS_PIPE_REFRIGERANT",
        },
        RefineKind = (kind, block) =>
            kind == SchematicEquipmentKind.OutdoorUnit && block.IndexOf("COUPLER", StringComparison.OrdinalIgnoreCase) >= 0
                ? SchematicEquipmentKind.OutdoorConnector
                : kind,
    };

    public static IReadOnlyList<SchematicProfile> All { get; } = new[] { LgLats };
}
