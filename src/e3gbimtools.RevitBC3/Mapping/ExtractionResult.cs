using System;
using System.Collections.Generic;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Resultado completo de una extraccion de mediciones del modelo.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>Fecha/hora en que se ejecuto el calculo.</summary>
    public DateTime CalculatedAt { get; set; } = DateTime.Now;

    /// <summary>Numero total de elementos analizados (sin contar tipos).</summary>
    public int ElementsAnalyzed { get; set; }

    /// <summary>Numero total de reglas evaluadas (activas).</summary>
    public int RulesEvaluated { get; set; }

    /// <summary>Numero total de elementos matcheados por alguna regla.</summary>
    public int ElementsMatched { get; set; }

    // ── Diagnostico de cobertura de IfcGUID ──────────────────────────────────

    /// <summary>
    /// Elementos cuyo IfcGUID se leyo directamente del parametro
    /// <c>IFC_GUID</c> o <c>IfcGUID</c> de Revit.
    /// </summary>
    public int GuidFromParameter { get; set; }

    /// <summary>
    /// Elementos cuyo IfcGUID se calculo deterministicamente desde
    /// el <c>UniqueId</c> de Revit (el modelo no habia sido exportado a IFC).
    /// </summary>
    public int GuidCalculated { get; set; }

    /// <summary>
    /// Elementos sin GUID valido — se emitio un identificador de emergencia.
    /// Idealmente debe ser 0.
    /// </summary>
    public int GuidFallback { get; set; }

    /// <summary>
    /// Porcentaje de elementos matcheados con GUID valido
    /// (parametro + calculado).  100 % = cobertura perfecta.
    /// </summary>
    public double GuidCoveragePercent =>
        ElementsMatched == 0
            ? 0.0
            : (GuidFromParameter + GuidCalculated) * 100.0 / ElementsMatched;

    /// <summary>Mediciones agrupadas por codigo BC3.</summary>
    public List<MeasurementResult> Measurements { get; set; } = new();

    /// <summary>Elementos sin regla (agrupados por categoria + tipo).</summary>
    public List<OrphanGroup> Orphans { get; set; } = new();
}
