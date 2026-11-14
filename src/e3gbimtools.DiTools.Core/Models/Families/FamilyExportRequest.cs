using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Petición de export de familias a disco (Sprint 7 — FamilyReviser).
/// </summary>
public sealed class FamilyExportRequest
{
    /// <summary>Familias a exportar (deben tener IsSelected = true).</summary>
    public IList<FamilyDescriptor> Families { get; set; } = new List<FamilyDescriptor>();

    /// <summary>Carpeta raíz donde se escribirán los archivos .rfa.</summary>
    public string RootFolder { get; set; } = string.Empty;

    /// <summary>Regla de organización de subcarpetas.</summary>
    public FamilyExportRule Rule { get; set; } = new FamilyExportRule();

    /// <summary>Si <c>true</c> sobrescribe .rfa ya existentes en disco.</summary>
    public bool Overwrite { get; set; } = true;

    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(RootFolder))
        {
            error = "La carpeta raíz no puede estar vacía.";
            return false;
        }
        if (Families.Count == 0)
        {
            error = "No hay familias seleccionadas para exportar.";
            return false;
        }
        error = null;
        return true;
    }
}
