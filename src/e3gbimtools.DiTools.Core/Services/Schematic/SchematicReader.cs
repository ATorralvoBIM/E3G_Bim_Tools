using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ACadSharp.Entities;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;
using E3G.BimTools.DiTools.Core.Models.Schematic;
using E3G.BimTools.DiTools.Core.Services.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.Schematic;

/// <summary>
/// Interpreta un esquema de principio: lo aplana con el lector de «Leyenda desde
/// DXF» (que ya resuelve bloques, MTEXT, capas y colores) y, si reconoce el
/// perfil del fabricante, construye el grafo equipos–tramos–rotulos.
///
///   • Equipo = INSERT en una capa de equipos; su caja es la extension real de
///     lo que dibuja el bloque.
///   • Tramo = polilinea en una capa de tuberias; cada extremo se une al equipo
///     cuya caja lo contiene.
///   • Rotulo = MTEXT/TEXT; va al tramo si esta pegado a uno de sus segmentos
///     (diametros encima, longitud debajo) y si no, al equipo mas cercano.
/// </summary>
public sealed class SchematicReader
{
    private static readonly Regex DiameterRx = new(@"^\s*\d[\d\-/]*(\s*:\s*\d[\d\-/]*)+\s*$", RegexOptions.Compiled);
    private static readonly Regex LengthRx = new(@"^\s*\d+([.,]\d+)?\s*(/\s*\d+([.,]\d+)?\s*)?m\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILegendTextMeasurer _measurer;

    public SchematicReader(ILegendTextMeasurer measurer)
    {
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
    }

    public SchematicDiagram Read(string path)
    {
        var src = new DxfLegendReader(_measurer).Read(path);
        var profile = SchematicProfile.All.FirstOrDefault(p => p.Matches(src.Layers.Select(l => l.Name)));
        var d = new SchematicDiagram { FilePath = path, Source = src, ProfileName = profile?.Name };
        d.Warnings.AddRange(src.Warnings);
        if (profile is null) return d;

        var labels = new List<SchematicLabel>();
        foreach (var range in src.Sources)
        {
            var items = src.Items.Skip(range.Start).Take(range.Count).ToList();
            switch (range.Entity)
            {
                case Insert ins when profile.EquipmentLayers.TryGetValue(LayerOf(ins), out var kind):
                {
                    var b = LegendGeometry.Bounds(items);
                    var box = b is { } bb
                        ? new SchematicBox(bb.MinX, bb.MinY, bb.MaxX, bb.MaxY)
                        : new SchematicBox(ins.InsertPoint.X - 0.05, ins.InsertPoint.Y - 0.05, ins.InsertPoint.X + 0.05, ins.InsertPoint.Y + 0.05);
                    string block = ins.Block?.Name ?? string.Empty;
                    var eq = new SchematicEquipment
                    {
                        Index = d.Equipment.Count,
                        Kind = profile.RefineKind(kind, block),
                        BlockName = block,
                        Layer = LayerOf(ins),
                        InsertX = ins.InsertPoint.X,
                        InsertY = ins.InsertPoint.Y,
                        Box = box,
                    };
                    foreach (var a in ins.Attributes)
                        if (!a.IsInvisible && !string.IsNullOrWhiteSpace(a.Value))
                            eq.Attributes.Add(MTextLayout.ReplaceSpecials(a.Value).Trim());
                    d.Equipment.Add(eq);
                    break;
                }
                case LwPolyline pl when profile.PipeLayers.Contains(LayerOf(pl)):
                {
                    var pts = pl.Vertices.Select(v => (v.Location.X, v.Location.Y)).ToList();
                    if (pl.IsClosed && pts.Count > 1) pts.Add(pts[0]);
                    AddPipe(d, LayerOf(pl), pts);
                    break;
                }
                case Line ln when profile.PipeLayers.Contains(LayerOf(ln)):
                    AddPipe(d, LayerOf(ln), new() { (ln.StartPoint.X, ln.StartPoint.Y), (ln.EndPoint.X, ln.EndPoint.Y) });
                    break;
                case MText or TextEntity:
                {
                    var texts = items.OfType<LegendText>().ToList();
                    var b = LegendGeometry.Bounds(items);
                    if (texts.Count == 0 || b is null) break;
                    string raw = range.Entity is MText mt ? mt.Value ?? string.Empty : ((TextEntity)range.Entity).Value ?? string.Empty;
                    // LATS separa las lineas con saltos reales (no \P): se respetan.
                    var lines = raw.Replace("\r\n", "\n").Split('\n')
                        .SelectMany(part => MTextLayout.Parse(part).Select(p => p.Text))
                        .Select(t => Glyphs(t).Trim()).Where(t => t.Length > 0).ToList();
                    if (lines.Count == 0) lines = texts.Select(t => t.Text).ToList();
                    labels.Add(new SchematicLabel
                    {
                        Lines = lines,
                        Box = new SchematicBox(b.Value.MinX, b.Value.MinY, b.Value.MaxX, b.Value.MaxY),
                        Height = texts.Max(t => t.Height),
                        Color = texts[0].Color,
                        Layer = texts[0].Layer,
                    });
                    break;
                }
            }
        }

        d.TextHeight = labels.Count > 0 ? Median(labels.Select(l => l.Height)) : 1;
        ConnectPipes(d);
        AttachLabels(d, labels);

        int sueltos = d.Pipes.Count(p => p.From is null || p.To is null);
        if (sueltos > 0) d.Warnings.Add($"{sueltos} tramo(s) con algún extremo sin equipo: se recolocan por proporción.");
        return d;
    }

    private static string LayerOf(Entity e) => e.Layer?.Name ?? "0";

    /// <summary>Letras en circulo (Ⓐ, ⓐ…; Calibri no las tiene) → «(A)».</summary>
    private static string Glyphs(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (ch >= 'Ⓐ' && ch <= 'Ⓩ') sb.Append('(').Append((char)('A' + ch - 'Ⓐ')).Append(')');
            else if (ch >= 'ⓐ' && ch <= 'ⓩ') sb.Append('(').Append((char)('a' + ch - 'ⓐ')).Append(')');
            else if (ch >= '①' && ch <= '⑳') sb.Append('(').Append(ch - '①' + 1).Append(')');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void AddPipe(SchematicDiagram d, string layer, List<(double X, double Y)> pts)
    {
        // Quita vertices repetidos.
        var clean = new List<(double X, double Y)>();
        foreach (var p in pts)
            if (clean.Count == 0 || Math.Abs(clean[^1].X - p.X) + Math.Abs(clean[^1].Y - p.Y) > 1e-9) clean.Add(p);
        if (clean.Count < 2) return;
        d.Pipes.Add(new SchematicPipe { Index = d.Pipes.Count, Layer = layer, Points = clean });
    }

    /// <summary>Cada extremo de tramo se une al equipo mas pequeño cuya caja lo contiene.</summary>
    private static void ConnectPipes(SchematicDiagram d)
    {
        double tol = d.TextHeight * 0.5;
        int? Find(double x, double y) => d.Equipment
            .Where(e => e.Box.Contains(x, y, tol))
            .OrderBy(e => e.Box.Width * e.Box.Height)
            .Select(e => (int?)e.Index)
            .FirstOrDefault();

        foreach (var p in d.Pipes)
        {
            p.From = Find(p.Points[0].X, p.Points[0].Y);
            p.To = Find(p.Points[^1].X, p.Points[^1].Y);
        }
    }

    private static void AttachLabels(SchematicDiagram d, List<SchematicLabel> labels)
    {
        double h = d.TextHeight;
        foreach (var l in labels)
        {
            // Tramo: el rotulo esta encima o debajo de un segmento, dentro de su recorrido.
            SchematicPipe? bestPipe = null;
            double bestPipeDist = double.MaxValue;
            foreach (var p in d.Pipes)
                for (int i = 0; i + 1 < p.Points.Count; i++)
                {
                    var (x1, y1) = p.Points[i];
                    var (x2, y2) = p.Points[i + 1];
                    double dist;
                    if (Math.Abs(y1 - y2) < 1e-9)                       // horizontal
                    {
                        if (l.Box.Cx < Math.Min(x1, x2) || l.Box.Cx > Math.Max(x1, x2)) continue;
                        dist = Math.Max(0, Math.Max(l.Box.MinY - y1, y1 - l.Box.MaxY));
                    }
                    else if (Math.Abs(x1 - x2) < 1e-9)                  // vertical
                    {
                        if (l.Box.Cy < Math.Min(y1, y2) || l.Box.Cy > Math.Max(y1, y2)) continue;
                        dist = Math.Max(0, Math.Max(l.Box.MinX - x1, x1 - l.Box.MaxX));
                    }
                    else continue;
                    if (dist < bestPipeDist) { bestPipeDist = dist; bestPipe = p; }
                }

            var bestEq = d.Equipment.OrderBy(e => e.Box.Distance(l.Box)).FirstOrDefault();
            double eqDist = bestEq?.Box.Distance(l.Box) ?? double.MaxValue;

            // Diametros y longitudes van al tramo; cualquier otro texto solo si el
            // tramo esta claramente mas cerca que el equipo (si no, es del equipo).
            string first = l.Lines.FirstOrDefault() ?? string.Empty;
            bool pipeText = DiameterRx.IsMatch(first) || LengthRx.IsMatch(first);
            if (bestPipe is not null && bestPipeDist <= 1.5 * h && (pipeText || eqDist > 3 * h))
            {
                if (bestPipe.Diameter is null && DiameterRx.IsMatch(first)) bestPipe.Diameter = l;
                else if (bestPipe.Length is null && LengthRx.IsMatch(first)) bestPipe.Length = l;
                else bestPipe.OtherLabels.Add(l);
            }
            else if (bestEq is not null)
                bestEq.Labels.Add(l);
            else
                d.FreeLabels.Add(l);
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count == 0 ? 0 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
