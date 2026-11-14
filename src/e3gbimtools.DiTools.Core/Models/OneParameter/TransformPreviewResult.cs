using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Resultado del preview de una transformacion: lista de filas Before/After.
/// </summary>
public sealed class TransformPreviewResult
{
    public List<TransformPreviewRow> Rows     { get; } = new();
    public List<string>              Warnings { get; } = new();

    public int WillChangeCount => Rows.FindAll(r => r.WillChange).Count;
    public int ErrorCount      => Rows.FindAll(r => r.Error is not null).Count;

    public string ToSummary() =>
        $"{WillChangeCount} elemento(s) cambiaran; {ErrorCount} error(es); {Warnings.Count} aviso(s).";
}
