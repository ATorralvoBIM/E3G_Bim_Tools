using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Models.DialuxImport;

// ═════════════════════════════ Lectura del DWG ═════════════════════════════

/// <summary>
/// Una insercion de bloque del DWG de DIALux (candidata a luminaria), ya en
/// coordenadas de espacio modelo (transformaciones de bloques anidados aplicadas).
/// </summary>
public sealed class DialuxInsert
{
    /// <summary>Handle de la entidad INSERT (hex).  Identifica la luminaria entre ejecuciones sobre el mismo DWG.</summary>
    public string Handle { get; init; } = string.Empty;
    public string BlockName { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    /// <summary>Punto de insercion en unidades de dibujo.</summary>
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    /// <summary>Giro en planta, radianes (antihorario).</summary>
    public double Rotation { get; init; }
    /// <summary>Insercion con simetria (escala negativa o extrusion invertida).</summary>
    public bool Mirrored { get; init; }
    /// <summary>Profundidad de anidamiento (0 = en espacio modelo).</summary>
    public int Depth { get; init; }
    /// <summary>Luminaria de DIALux (bloque anonimo de la capa DLXLUM agrupado por su numero).</summary>
    public bool IsDialuxLuminaire { get; init; }
    /// <summary>Atributos visibles u ocultos del INSERT (etiqueta → valor).</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}

/// <summary>Resumen de un nombre de bloque del DWG (una fila de la tabla de mapeo).</summary>
public sealed class DialuxBlockInfo
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public IReadOnlyList<string> Layers { get; init; } = Array.Empty<string>();
    /// <summary>Atributos de la primera insercion, para reconocer la luminaria.</summary>
    public string AttributesSample { get; init; } = string.Empty;
    public bool AnyNested { get; init; }
    public bool IsDialuxLuminaire { get; init; }
    /// <summary>Trazos del simbolo (primera insercion, ejes del bloque) para la miniatura.</summary>
    public IReadOnlyList<DialuxSegment> Symbol { get; init; } = Array.Empty<DialuxSegment>();
}

/// <summary>Segmento del simbolo de un bloque, en unidades de dibujo.</summary>
public readonly record struct DialuxSegment(double X1, double Y1, double X2, double Y2);

public sealed class DialuxDrawing
{
    public string FilePath { get; init; } = string.Empty;
    public List<DialuxInsert> Inserts { get; } = new();
    public List<DialuxBlockInfo> Blocks { get; } = new();
    public List<string> Layers { get; } = new();
    /// <summary>El DWG trae luminarias exportadas por DIALux (capa DLXLUM).</summary>
    public bool HasDialuxLuminaires => Inserts.Any(i => i.IsDialuxLuminaire);
    public LegendUnits? DeclaredUnits { get; init; }
    public LegendUnits SuggestedUnits { get; set; } = LegendUnits.Meters;
    public List<string> Warnings { get; } = new();

    /// <summary>Clave del origen guardada en las luminarias creadas (nombre del archivo sin extension).</summary>
    public string SourceKey => System.IO.Path.GetFileNameWithoutExtension(FilePath);
}

// ═════════════════════════════ Configuracion ═════════════════════════════

/// <summary>Sistema de coordenadas en el que esta el origen del DWG.</summary>
public enum DialuxOrigin
{
    /// <summary>El 0,0 del DWG es el origen interno de Revit (importar "origen a origen").</summary>
    InternalOrigin,
    /// <summary>El 0,0 del DWG es el punto base del proyecto.</summary>
    ProjectBasePoint,
    /// <summary>El DWG esta en coordenadas compartidas (punto de reconocimiento + norte real).</summary>
    SharedCoordinates,
}

/// <summary>De donde sale la altura de montaje de cada luminaria.</summary>
public enum DialuxHeightSource
{
    /// <summary>Altura fija de la fila de mapeo, sobre el nivel.</summary>
    Fixed,
    /// <summary>Coordenada Z de la insercion en el DWG (DIALux exporta en 3D), sobre el nivel.</summary>
    DwgZ,
}

/// <summary>Mapeo de un bloque de DIALux a un tipo de familia de Revit.</summary>
public sealed class DialuxMapping
{
    public string BlockName { get; set; } = string.Empty;
    /// <summary>Familia y tipo destino; vacio = bloque ignorado.</summary>
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public DialuxHeightSource HeightSource { get; set; } = DialuxHeightSource.Fixed;
    /// <summary>Altura sobre el nivel en mm (si <see cref="HeightSource"/> es Fixed).</summary>
    public double HeightMm { get; set; } = 2700;
    /// <summary>Giro adicional en grados (familias dibujadas con otro eje que el bloque).</summary>
    public double ExtraRotationDeg { get; set; }

    public bool IsMapped => !string.IsNullOrWhiteSpace(FamilyName) && !string.IsNullOrWhiteSpace(TypeName);
    public string TargetLabel => IsMapped ? $"{FamilyName} : {TypeName}" : string.Empty;
}

/// <summary>
/// Configuracion completa de la importacion.  Es lo que se guarda/carga como
/// .json (la entrada "Config .json (opcional)" de la rutina Dynamo original).
/// </summary>
public sealed class DialuxConfig
{
    public int Version { get; set; } = 1;
    public LegendUnits? Units { get; set; }
    public DialuxOrigin Origin { get; set; } = DialuxOrigin.InternalOrigin;
    /// <summary>Desplazamiento aplicado al DWG antes de llevarlo a Revit, en mm.</summary>
    public double OffsetXMm { get; set; }
    public double OffsetYMm { get; set; }
    /// <summary>Giro del DWG alrededor de su origen, en grados.</summary>
    public double RotationDeg { get; set; }
    public string LevelName { get; set; } = string.Empty;
    /// <summary>Solo se consideran los bloques de estas capas (vacio = todas).</summary>
    public List<string> Layers { get; set; } = new();
    /// <summary>Distancia por debajo de la cual una luminaria existente se da por bien colocada (mm).</summary>
    public double ToleranceMm { get; set; } = 20;
    public List<DialuxMapping> Mappings { get; set; } = new();

    public DialuxMapping? FindMapping(string blockName) =>
        Mappings.FirstOrDefault(m => string.Equals(m.BlockName, blockName, StringComparison.OrdinalIgnoreCase));
}

// ═════════════════════════════ Plan / resultado ═════════════════════════════

public enum DialuxRowStatus
{
    /// <summary>Esta en el DWG y no en el modelo: CREAR la colocara.</summary>
    New,
    /// <summary>Ya colocada, en su sitio y con el tipo correcto.</summary>
    Ok,
    /// <summary>Colocada, pero desplazada o girada respecto al DWG.</summary>
    Moved,
    /// <summary>Colocada con un tipo distinto al del mapeo.</summary>
    WrongType,
    /// <summary>Esta en el modelo (importada de este DWG) y ya no en el DWG, o su bloque ya no se mapea.</summary>
    Extra,
    /// <summary>No se puede colocar (tipo inexistente, nivel inexistente…).</summary>
    Error,
}

public static class DialuxRowStatusExtensions
{
    public static string Label(this DialuxRowStatus s) => s switch
    {
        DialuxRowStatus.New       => "Nueva",
        DialuxRowStatus.Ok        => "OK",
        DialuxRowStatus.Moved     => "Movida",
        DialuxRowStatus.WrongType => "Tipo distinto",
        DialuxRowStatus.Extra     => "Sobra",
        DialuxRowStatus.Error     => "Error",
        _ => s.ToString(),
    };
}

/// <summary>Una luminaria del plan: DWG ↔ modelo.</summary>
public sealed class DialuxPlanRow
{
    public DialuxRowStatus Status { get; set; }
    public DialuxInsert? Source { get; init; }
    public DialuxMapping? Mapping { get; init; }
    /// <summary>Posicion destino en coordenadas internas de Revit (pies), Z = cota sobre el nivel.</summary>
    public XYZ? Target { get; init; }
    /// <summary>Giro destino en radianes (coordenadas internas).</summary>
    public double TargetRotation { get; init; }
    /// <summary>Instancia existente casada con la fila (si la hay).</summary>
    public ElementId? ExistingId { get; set; }
    public string ExistingType { get; set; } = string.Empty;
    /// <summary>Distancia en planta entre la existente y el destino (mm).</summary>
    public double DeltaMm { get; set; }
    public string Detail { get; set; } = string.Empty;

    public string BlockName => Source?.BlockName ?? Mapping?.BlockName ?? string.Empty;
}

public sealed class DialuxPlan
{
    public List<DialuxPlanRow> Rows { get; } = new();
    public List<string> Warnings { get; } = new();

    public int Count(DialuxRowStatus s) => Rows.Count(r => r.Status == s);

    public string ToSummary() =>
        string.Join(" · ", Enum.GetValues(typeof(DialuxRowStatus)).Cast<DialuxRowStatus>()
            .Select(s => (s, n: Count(s))).Where(x => x.n > 0)
            .Select(x => $"{x.s.Label()}: {x.n}"));
}

public enum DialuxAction
{
    /// <summary>Coloca las luminarias nuevas; no toca las existentes.</summary>
    Create,
    /// <summary>Borra las importadas de este DWG y vuelve a colocarlas todas.</summary>
    Redo,
    /// <summary>Recoloca las movidas, cambia el tipo de las de tipo distinto y coloca las nuevas.</summary>
    Sync,
    /// <summary>Borra solo las sobrantes (importadas de este DWG que ya no estan en el).</summary>
    DeleteExtra,
}

public sealed class DialuxResult
{
    public bool Success { get; init; } = true;
    public string ErrorMessage { get; init; } = string.Empty;
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public List<string> Warnings { get; } = new();

    public string ToSummary() =>
        $"Creadas: {Created} · Actualizadas: {Updated} · Borradas: {Deleted}" +
        (Warnings.Count > 0 ? $" · Avisos: {Warnings.Count}" : string.Empty);
}

/// <summary>Resultado del alineado automatico DWG ↔ arquitectura de Revit (pilares).</summary>
public sealed class DialuxAlignment
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    /// <summary>Desfase (mm) y giro (°) a usar con origen interno.</summary>
    public double OffsetXMm { get; init; }
    public double OffsetYMm { get; init; }
    public double RotationDeg { get; init; }
    public int Matched { get; init; }
    public int DwgPoints { get; init; }
    public int RevitPoints { get; init; }
    /// <summary>Error medio de los pilares casados (mm).</summary>
    public double MeanErrorMm { get; init; }
}

public sealed class DialuxLevel
{
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string Name { get; init; } = string.Empty;
    /// <summary>Cota del nivel en metros (respecto al punto base del proyecto).</summary>
    public double ElevationM { get; init; }
    public string Label => $"{Name}  ({ElevationM:+0.00;-0.00;0.00} m)";
}

/// <summary>Tipo de familia candidato para el mapeo.</summary>
public sealed class DialuxFamilyType
{
    public ElementId Id { get; init; } = ElementId.InvalidElementId;
    public string FamilyName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    /// <summary>Basada en plano de trabajo o en cara: se aloja en el plano del nivel.</summary>
    public bool IsWorkPlaneBased { get; init; }
    public string Label => $"{FamilyName} : {TypeName}";
}
