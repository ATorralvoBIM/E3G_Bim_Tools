using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

// ============================================================================
// «Trazado de refrigerante desde DXF» (Climatizacion, 2026-11-12).
// Geometria en planta sin dependencias de Revit (se prueba en los tests): el
// DXF se lee en sus unidades y una PlanTransform lo lleva a metros en el
// sistema interno del proyecto.
// ============================================================================

/// <summary>Punto o vector en planta.</summary>
public readonly record struct P2(double X, double Y)
{
    public static P2 operator +(P2 a, P2 b) => new(a.X + b.X, a.Y + b.Y);
    public static P2 operator -(P2 a, P2 b) => new(a.X - b.X, a.Y - b.Y);
    public static P2 operator *(P2 a, double k) => new(a.X * k, a.Y * k);
    public static P2 operator *(double k, P2 a) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public P2 Normalized() { double l = Length; return l < 1e-12 ? new P2(0, 0) : new P2(X / l, Y / l); }
    /// <summary>Girado +90° (a la izquierda).</summary>
    public P2 Left => new(-Y, X);
    public static double Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
    public double DistanceTo(P2 o) => (this - o).Length;
}

/// <summary>Segmento en planta.</summary>
public readonly record struct Seg2(P2 A, P2 B)
{
    public double Length => (B - A).Length;
}

/// <summary>
/// Lleva el DXF al modelo: q = R(Rotation)·(Scale·p) + (Tx, Ty), en metros y
/// coordenadas internas del proyecto.
/// </summary>
public sealed record PlanTransform(double Scale, double Rotation, double Tx, double Ty)
{
    public static PlanTransform Identity { get; } = new(1, 0, 0, 0);

    /// <summary>Como se ha obtenido (para el usuario).</summary>
    public string Method { get; init; } = "Manual";
    /// <summary>Fraccion de puntos de la arquitectura del DXF que caen sobre muros de Revit (0..1).</summary>
    public double Score { get; init; } = double.NaN;
    public int Matched { get; init; }
    public int Sampled { get; init; }

    public P2 Apply(P2 p)
    {
        double c = Math.Cos(Rotation), s = Math.Sin(Rotation);
        double x = p.X * Scale, y = p.Y * Scale;
        return new P2(c * x - s * y + Tx, s * x + c * y + Ty);
    }

    /// <summary>Solo giro y escala (direcciones).</summary>
    public P2 ApplyVector(P2 v)
    {
        double c = Math.Cos(Rotation), s = Math.Sin(Rotation);
        return new P2((c * v.X - s * v.Y) * Scale, (s * v.X + c * v.Y) * Scale);
    }

    public string Describe() =>
        $"Escala {Scale:0.####} · giro {Rotation * 180 / Math.PI:0.##}° · desplazamiento ({Tx:0.###}; {Ty:0.###}) m";
}

// ----------------------------------------------------------------------------
// Lo que se lee del DXF (unidades del DXF)
// ----------------------------------------------------------------------------

/// <summary>Boca de un bloque: donde llega una tuberia (ejes del bloque).</summary>
public sealed class LatsPort
{
    public int Index { get; init; }
    public P2 Local { get; init; }
    /// <summary>Direccion de salida (unitaria, ejes del bloque).</summary>
    public P2 Out { get; init; }
}

/// <summary>Definicion de bloque LATS (derivador, caja, unidad interior…).</summary>
public sealed class LatsBlockDef
{
    public string Name { get; init; } = string.Empty;
    public List<LatsPort> Ports { get; } = new();
    /// <summary>Contorno del cuerpo (ejes del bloque), sin textos ni espacios de servicio.</summary>
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public bool HasBody => MaxX > MinX && MaxY > MinY;
    public int Count { get; set; }
}

/// <summary>Insercion de un bloque LATS en el plano.</summary>
public sealed class LatsDevice
{
    public string Handle { get; init; } = string.Empty;
    public string BlockName { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    /// <summary>Parte lineal (giro × escala, con simetria) de la insercion: x' = M·x + Origin.</summary>
    public double A { get; init; } = 1;
    public double B { get; init; }
    public double C { get; init; }
    public double D { get; init; } = 1;
    public P2 Origin { get; init; }

    public P2 ToWorld(P2 local) => new(A * local.X + B * local.Y + Origin.X, C * local.X + D * local.Y + Origin.Y);
    public P2 VectorToWorld(P2 v) => new P2(A * v.X + B * v.Y, C * v.X + D * v.Y).Normalized();
    public bool Mirrored => A * D - B * C < 0;
}

/// <summary>Tramo de tuberia del DXF: una polilinea que lleva 1..n tubos (liquido:gas…).</summary>
public sealed class LatsPipe
{
    public string Handle { get; init; } = string.Empty;
    public List<P2> Points { get; } = new();
    /// <summary>Diametros tal cual vienen en LATS ("3/8", "7/8"…), uno por tubo; el primero es el liquido.</summary>
    public List<string> Sizes { get; } = new();
}

/// <summary>DXF de tuberias leido.</summary>
public sealed class LatsPlan
{
    public string FilePath { get; init; } = string.Empty;
    public List<LatsPipe> Pipes { get; } = new();
    public List<LatsDevice> Devices { get; } = new();
    public Dictionary<string, LatsBlockDef> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Recintos (polilineas cerradas de la capa de habitaciones).</summary>
    public List<List<P2>> Rooms { get; } = new();
    /// <summary>Lineas de arquitectura (todo lo que no es de LATS), para casar con Revit.</summary>
    public List<Seg2> Architecture { get; } = new();
    /// <summary>
    /// Transformaciones candidatas: inversa de cada insercion de un bloque de
    /// arquitectura (p. ej. la planta exportada de Revit que LATS mete como bloque:
    /// dentro del bloque las coordenadas son las del modelo).
    /// </summary>
    public List<(string Block, PlanTransform Inverse)> ArchitectureInserts { get; } = new();
    /// <summary>Metros por unidad segun $INSUNITS (null si no lo declara).</summary>
    public double? DeclaredMetersPerUnit { get; init; }
    public List<string> Warnings { get; } = new();

    public IEnumerable<string> BlockNames => Devices.Select(d => d.BlockName).Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class LatsReadOptions
{
    public string PipeLayer { get; init; } = "LATS_PIPE_REFRIGERANT";
    public string RoomLayer { get; init; } = "LATS_BOUND_ROOM";
    /// <summary>Prefijo de las capas del programa del fabricante (no son arquitectura).</summary>
    public string VendorLayerPrefix { get; init; } = "LATS_";
}

// ----------------------------------------------------------------------------
// Mapeo de bloques y opciones
// ----------------------------------------------------------------------------

public enum BlockRole
{
    /// <summary>No se modela.</summary>
    Ignore,
    /// <summary>Derivador: un accesorio por tubo (liquido y gas por separado), conectado.</summary>
    Branch,
    /// <summary>Caja / equipo: un accesorio sin conectar (no une liquido y gas en un sistema).</summary>
    Unit,
    /// <summary>Unidad interior: no se modela; el trazado se corta a la entrada del recinto.</summary>
    IndoorUnit,
}

public static class BlockRoleExtensions
{
    public static string Label(this BlockRole r) => r switch
    {
        BlockRole.Branch => "Derivador (uno por tubo, conectado)",
        BlockRole.Unit => "Caja / equipo (sin conectar)",
        BlockRole.IndoorUnit => "Unidad interior (cortar en recinto)",
        _ => "Ignorar",
    };

    /// <summary>Propuesta por nombre de bloque LATS.</summary>
    public static BlockRole Guess(string blockName)
    {
        string n = blockName.ToUpperInvariant();
        if (n.Contains("SPLT") || n.Contains("BRANCH") || n.Contains("HEADER") || n.Contains("YBRANCH")) return BlockRole.Branch;
        if (n.Contains("HPSVUNIT") || n.Contains("HRUNIT") || n.Contains("HR_UNIT") || n.Contains("HBOX")) return BlockRole.Unit;
        if (n.Contains("IDU") || n.Contains("INDOOR")) return BlockRole.IndoorUnit;
        return BlockRole.Ignore;
    }
}

/// <summary>Bloque → uso y familia (null = crear la familia desde el bloque).</summary>
public sealed class BlockMapping
{
    public string BlockName { get; init; } = string.Empty;
    public BlockRole Role { get; init; }
    /// <summary>Id (valor) del FamilySymbol; null = crear la familia desde el bloque.</summary>
    public long? SymbolId { get; init; }
}

public sealed class RefrigerantRoutingOptions
{
    /// <summary>Separacion entre ejes de los tubos de un mismo tramo (m).</summary>
    public double Spacing { get; init; } = 0.05;
    /// <summary>Cortar los tramos que van a una unidad interior a la entrada del recinto.</summary>
    public bool TrimAtRoom { get; init; } = true;
    /// <summary>Lo que entra la tuberia en el recinto (m).</summary>
    public double InsideRoom { get; init; } = 0.30;
    /// <summary>Radio para asociar un extremo libre a una unidad interior (m).</summary>
    public double IndoorUnitRadius { get; init; } = 1.5;
    /// <summary>Tramo minimo tras un cambio de direccion para que quepa el codo (m).</summary>
    public double MinLastSegment { get; init; } = 0.25;
    /// <summary>Tolerancia para casar un extremo de tuberia con una boca (unidades del DXF ya escaladas, m).</summary>
    public double PortTolerance { get; init; } = 0.01;
    public Dictionary<string, BlockMapping> Mappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // Revit
    public long ViewId { get; init; } = -1;
    public long LevelId { get; init; } = -1;
    /// <summary>Cota del eje de las tuberias sobre el nivel (m).</summary>
    public double Height { get; init; } = 2.65;
    public string PipeTypeName { get; init; } = "PIPE_CU";
    public string LiquidSystemName { get; init; } = "50_12_LiquidRefrigerant";
    public string GasSystemName { get; init; } = "50_13_GasRefrigerant";
    public bool CreateMissingSystems { get; init; } = true;
    public bool AddMissingSizes { get; init; } = true;
    /// <summary>Alto de las cajas creadas desde el bloque (m).</summary>
    public double UnitHeight { get; init; } = 0.22;
    /// <summary>Borra antes lo modelado desde este mismo DXF.</summary>
    public bool ReplacePrevious { get; init; } = true;
    public string TagPrefix { get; init; } = "E3G_REF";
}

// ----------------------------------------------------------------------------
// Plan de modelado (metros, coordenadas internas del proyecto)
// ----------------------------------------------------------------------------

public sealed class RoutedPipe
{
    public string Source { get; init; } = string.Empty;
    /// <summary>0 = liquido; 1.. = gas.</summary>
    public int Line { get; init; }
    public bool IsLiquid => Line == 0;
    public string SizeLabel { get; init; } = string.Empty;
    /// <summary>Diametro exterior (mm).</summary>
    public double OuterDiameterMm { get; init; }
    public List<P2> Points { get; } = new();
    /// <summary>Extremo inicial/final en una boca de derivador: (handle, indice de boca).</summary>
    public (string Device, int Port)? StartPort { get; init; }
    public (string Device, int Port)? EndPort { get; init; }
}

public sealed class PlacedDevice
{
    public string Handle { get; init; } = string.Empty;
    public string BlockName { get; init; } = string.Empty;
    public BlockRole Role { get; init; }
    /// <summary>Para derivadores, el tubo (0 = liquido); null en cajas.</summary>
    public int? Line { get; init; }
    public P2 Origin { get; init; }
    public double Rotation { get; init; }
    public bool Mirrored { get; init; }
    /// <summary>Diametro exterior (mm) de la tuberia en cada boca, por indice.</summary>
    public Dictionary<int, double> PortDiametersMm { get; } = new();
}

public sealed class RefrigerantRoutingPlan
{
    public List<RoutedPipe> Pipes { get; } = new();
    public List<PlacedDevice> Devices { get; } = new();
    public List<string> Warnings { get; } = new();
    public int TrimmedAtRoom { get; set; }
    public int Conflicts { get; set; }
    public double TotalLength => Pipes.Sum(p => p.Points.Zip(p.Points.Skip(1), (a, b) => a.DistanceTo(b)).Sum());
}

public sealed class RefrigerantRoutingResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int Pipes { get; init; }
    public int Elbows { get; init; }
    public int Accessories { get; init; }
    public int Connections { get; init; }
    public int Deleted { get; init; }
    public int FamiliesCreated { get; init; }
    public double LengthM { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string ToSummary() =>
        $"{Pipes} tuberías ({LengthM:0.#} m), {Elbows} codos, {Accessories} accesorios, {Connections} conexiones" +
        (FamiliesCreated > 0 ? $", {FamiliesCreated} familias creadas" : string.Empty) +
        (Deleted > 0 ? $"; {Deleted} elementos anteriores borrados" : string.Empty) + ".";
}

// ----------------------------------------------------------------------------
// Datos del proyecto para la interfaz
// ----------------------------------------------------------------------------

public sealed record RefNamedId(ElementId Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record RefPlanView(ElementId Id, string Name, ElementId LevelId, string LevelName)
{
    public string DisplayName => string.IsNullOrEmpty(LevelName) ? Name : $"{Name}  ·  {LevelName}";
}

public sealed record RefLevel(ElementId Id, string Name, double ElevationM)
{
    public string DisplayName => $"{Name}  ({ElevationM * 1000:0} mm)";
}

public sealed class RefrigerantProjectInfo
{
    public IReadOnlyList<RefPlanView> Views { get; init; } = Array.Empty<RefPlanView>();
    public IReadOnlyList<RefLevel> Levels { get; init; } = Array.Empty<RefLevel>();
    public IReadOnlyList<RefNamedId> PipeTypes { get; init; } = Array.Empty<RefNamedId>();
    public IReadOnlyList<RefNamedId> PipingSystems { get; init; } = Array.Empty<RefNamedId>();
    /// <summary>Tipos de accesorio de tuberia ("Familia : Tipo").</summary>
    public IReadOnlyList<RefNamedId> AccessoryTypes { get; init; } = Array.Empty<RefNamedId>();
    public ElementId ActiveViewId { get; init; } = ElementId.InvalidElementId;
}
