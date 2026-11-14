using System;

namespace E3G.BimTools.DiTools.Core.Models.Sheets;

/// <summary>
/// Describe una Drawing Sheet a crear (Sprint 6 — SheetGen).
/// Puede construirse manualmente desde la UI o deserializarse desde Excel.
/// </summary>
public sealed class SheetDescriptor
{
    /// <summary>Número de plano (parámetro built-in "Sheet Number").</summary>
    public string SheetNumber { get; set; } = string.Empty;

    /// <summary>Nombre del plano (parámetro built-in "Sheet Name").</summary>
    public string SheetName { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del tipo de familia del title block tal como aparece en el proyecto.
    /// Ejemplo: "A1 metric : A1 metric".
    /// </summary>
    public string TitleBlockFamilyTypeName { get; set; } = string.Empty;

    /// <summary>Disciplina (parámetro de proyecto, opcional).</summary>
    public string? Discipline { get; set; }

    /// <summary>Id del layout a aplicar al crear la sheet (null = sin layout).</summary>
    public Guid? LayoutId { get; set; }

    /// <summary>
    /// Validación básica sin tocar el documento Revit.
    /// </summary>
    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(SheetNumber))
        {
            error = "SheetNumber no puede estar vacío.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(SheetName))
        {
            error = "SheetName no puede estar vacío.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(TitleBlockFamilyTypeName))
        {
            error = "TitleBlockFamilyTypeName no puede estar vacío.";
            return false;
        }
        error = null;
        return true;
    }
}
