using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.OneParameter;

/// <summary>
/// Resultado de <see cref="Core.Abstractions.IOneParameterService.Search"/>.
/// </summary>
public sealed class OneParameterSearchResult
{
    public List<OneParameterRow> Rows      { get; } = new();
    public List<string>          Warnings  { get; } = new();
    public int ElementsScanned   { get; set; }
    public int CategoriesFound   { get; set; }

    public string ToSummary() =>
        $"{Rows.Count} elemento(s) con el parametro en {CategoriesFound} categoria(s); " +
        $"{ElementsScanned} escaneado(s); {Warnings.Count} aviso(s).";
}
