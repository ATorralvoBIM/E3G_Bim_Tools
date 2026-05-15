// ============================================================================
//  SchematicLayout.cs
//  DTO del resultado del SchematicLayoutEngine — contiene posiciones de cada
//  fila, lista de conexiones (tronco vertical / dashed bifurcación) y los
//  metadatos de la leyenda.  Coordenadas en MILÍMETROS sobre el origen.
//
//  Los renderers (preview WPF y generador Revit) consumen este mismo DTO y
//  convierten las coordenadas a su sistema (px para WPF, feet para Revit).
// ============================================================================
using System;
using System.Collections.Generic;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.Core.Schematic
{
    public readonly struct SchematicPoint
    {
        public double XMm { get; }
        public double YMm { get; }
        public SchematicPoint(double x, double y) { XMm = x; YMm = y; }
        public override string ToString() => $"({XMm:F1},{YMm:F1})";
    }

    /// <summary>Una posición rectangular asignada a una fila.</summary>
    public sealed class SchematicNode
    {
        public ResultRow Row { get; }
        public SchematicPoint Pos { get; }
        public int Column { get; }
        public bool IsFitting { get; }
        public bool IsTerminal { get; }
        public bool IsBifurcation { get; }
        public string Label { get; }
        public bool OnCriticalPath { get; set; }

        public SchematicNode(ResultRow row, SchematicPoint pos, int column,
            bool isFitting, bool isTerminal, bool isBifurcation, string label)
        {
            Row = row;
            Pos = pos;
            Column = column;
            IsFitting = isFitting;
            IsTerminal = isTerminal;
            IsBifurcation = isBifurcation;
            Label = label;
        }
    }

    public enum SchematicConnectionKind
    {
        /// <summary>Vertical sólido (continuación en la misma columna).</summary>
        Continuation,
        /// <summary>Horizontal discontinuo (bifurcación a otra columna).</summary>
        Bifurcation,
    }

    public sealed class SchematicConnection
    {
        public SchematicPoint From { get; }
        public SchematicPoint To { get; }
        public SchematicConnectionKind Kind { get; }
        public ResultRow Row { get; }
        public bool OnCriticalPath { get; set; }

        public SchematicConnection(SchematicPoint from, SchematicPoint to,
            SchematicConnectionKind kind, ResultRow row)
        {
            From = from;
            To = to;
            Kind = kind;
            Row = row;
        }
    }

    /// <summary>Punto de bifurcación que se dibuja como círculo o glifo TEE/CROSS.</summary>
    public sealed class SchematicJunction
    {
        public SchematicPoint Pos { get; }
        public ResultRow ParentFitting { get; }    // null → círculo genérico
        public bool OnCriticalPath { get; set; }

        public SchematicJunction(SchematicPoint pos, ResultRow parentFitting)
        {
            Pos = pos;
            ParentFitting = parentFitting;
        }
    }

    public sealed class SchematicLegend
    {
        public string SystemName { get; set; }
        public string CriteriaName { get; set; }
        public int PipeCount { get; set; }
        public double WorstCaseDpPa { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>Resultado completo del layout — listo para renderizar.</summary>
    public sealed class SchematicLayout
    {
        public IReadOnlyList<SchematicNode> Nodes { get; }
        public IReadOnlyList<SchematicConnection> Connections { get; }
        public IReadOnlyList<SchematicJunction> Junctions { get; }
        public SchematicPoint SourcePos { get; }
        public SchematicLegend Legend { get; }
        public double WidthMm { get; }
        public double HeightMm { get; }
        public SchematicParameters Params { get; }

        public SchematicLayout(
            IReadOnlyList<SchematicNode> nodes,
            IReadOnlyList<SchematicConnection> connections,
            IReadOnlyList<SchematicJunction> junctions,
            SchematicPoint sourcePos,
            SchematicLegend legend,
            double widthMm,
            double heightMm,
            SchematicParameters paramsRef)
        {
            Nodes = nodes ?? Array.Empty<SchematicNode>();
            Connections = connections ?? Array.Empty<SchematicConnection>();
            Junctions = junctions ?? Array.Empty<SchematicJunction>();
            SourcePos = sourcePos;
            Legend = legend ?? new SchematicLegend();
            WidthMm = widthMm;
            HeightMm = heightMm;
            Params = paramsRef ?? new SchematicParameters();
        }
    }
}
