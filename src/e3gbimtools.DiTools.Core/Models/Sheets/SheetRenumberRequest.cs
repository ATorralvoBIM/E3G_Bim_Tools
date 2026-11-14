using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Petición para renumerar un conjunto de sheets siguiendo un patrón
/// (Sprint 6 — SheetGen).
/// Nuevo número = Prefix + (StartIndex + i).ToString("D{PadDigits}") + Suffix
/// </summary>
public sealed class SheetRenumberRequest
{
    /// <summary>Ids de las sheets a renumerar, en el orden deseado.</summary>
    public IList<ElementId> SheetIds { get; set; } = new List<ElementId>();

    /// <summary>Prefijo a anteponer al número. Puede ser vacío.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Sufijo a añadir tras el número. Puede ser vacío.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>Índice inicial de la secuencia (normalmente 1).</summary>
    public int StartIndex { get; set; } = 1;

    /// <summary>Número de dígitos para el pad (ej. 3 → "001", "002"…).</summary>
    public int PadDigits { get; set; } = 2;

    /// <summary>Calcula el número que correspondería a la posición <paramref name="index"/> (0-based).</summary>
    public string ComputeNumber(int index)
    {
        var num = (StartIndex + index).ToString($"D{PadDigits}");
        return $"{Prefix}{num}{Suffix}";
    }

    public bool IsValid(out string? error)
    {
        if (SheetIds.Count == 0)
        {
            error = "Selecciona al menos una sheet.";
            return false;
        }
        if (PadDigits < 1)
        {
            error = "PadDigits debe ser al menos 1.";
            return false;
        }
        if (StartIndex < 0)
        {
            error = "StartIndex no puede ser negativo.";
            return false;
        }
        error = null;
        return true;
    }
}
