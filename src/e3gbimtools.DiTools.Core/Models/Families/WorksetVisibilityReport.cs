using System.Collections.Generic;
using System.Text;

namespace E3G.BimTools.DiTools.Core.Models.Families;

/// <summary>
/// Recuento de estados de visibilidad de un workset a lo largo de las vistas inspeccionadas
/// (FamilyReviser — réplica de "Worksets Management", bloque F).
/// </summary>
public sealed class WorksetVisibilityReportRow
{
    public string WorksetName { get; set; } = string.Empty;

    /// <summary>Vistas en las que el workset está visible.</summary>
    public int VisibleCount { get; set; }

    /// <summary>Vistas en las que el workset está oculto.</summary>
    public int HiddenCount { get; set; }

    /// <summary>Vistas que usan la configuración global del workset.</summary>
    public int GlobalCount { get; set; }
}

/// <summary>
/// Reporte de solo lectura del estado de visibilidad de los worksets seleccionados a través
/// del ámbito de vistas elegido (FamilyReviser — bloque F).
/// </summary>
public sealed class WorksetVisibilityReport
{
    /// <summary>Número de vistas compatibles que se inspeccionaron.</summary>
    public int ViewsInspected { get; set; }

    /// <summary>Una fila por workset, ordenada por nombre.</summary>
    public IList<WorksetVisibilityReportRow> Rows { get; set; }
        = new List<WorksetVisibilityReportRow>();

    public string ToReportText()
    {
        if (ViewsInspected == 0)
            return "No se inspeccionó ninguna vista compatible.";

        var sb = new StringBuilder();
        sb.AppendLine($"Vistas inspeccionadas: {ViewsInspected}");
        sb.AppendLine();
        foreach (var r in Rows)
            sb.AppendLine($"• {r.WorksetName}: visible {r.VisibleCount}, oculto {r.HiddenCount}, global {r.GlobalCount}");

        return sb.ToString().TrimEnd();
    }
}
