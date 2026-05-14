// ============================================================================
//  CalcResult.cs
//  Core.Common — Resultado de cálculo por tramo + informe agregado + avisos.
//
//  Briefing técnico v1.0 — Mayo 2026:
//    - Sección 5: clases CalcResult, CalcReport, CalcWarning.
//    - Sección 6: shared parameters MEP_CALC_*.
//
//  Independencia: NO usa Autodesk.Revit.DB.  El identificador de Revit se
//  conserva como long (Revit 2024+ usa long internamente, IntegerValue ahora
//  está deprecado).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Core.Common
{
    /// <summary>Severidad de un mensaje de cálculo.</summary>
    public enum CalcSeverity
    {
        /// <summary>Información (no afecta a la validez del cálculo).</summary>
        Info,
        /// <summary>Aviso: cálculo ejecutado pero con precaución.</summary>
        Warning,
        /// <summary>Error: el resultado puede no ser fiable.</summary>
        Error
    }

    /// <summary>
    /// Aviso o error generado durante el cálculo, opcionalmente vinculado a un
    /// elemento de Revit.
    /// </summary>
    public class CalcWarning
    {
        public CalcSeverity Severity { get; init; } = CalcSeverity.Warning;
        public string Code            { get; init; }   // p.ej. 'VEL_EXCEEDED'
        public string Message         { get; init; }
        public long?  RevitElementId  { get; init; }   // null = mensaje global

        public override string ToString()
            => $"[{Severity}] {Code}: {Message}"
             + (RevitElementId.HasValue ? $" (Id={RevitElementId.Value})" : string.Empty);

        public static CalcWarning Info(string code, string message, long? elementId = null)
            => new() { Severity = CalcSeverity.Info,    Code = code, Message = message, RevitElementId = elementId };

        public static CalcWarning Warn(string code, string message, long? elementId = null)
            => new() { Severity = CalcSeverity.Warning, Code = code, Message = message, RevitElementId = elementId };

        public static CalcWarning Error(string code, string message, long? elementId = null)
            => new() { Severity = CalcSeverity.Error,   Code = code, Message = message, RevitElementId = elementId };
    }

    /// <summary>
    /// Resultado de cálculo de un tramo (tubería o conducto), expresado en SI.
    /// Firma derivada del briefing § 5; el id se almacena como long para
    /// compatibilidad con Revit 2024+.
    /// </summary>
    public class CalcResult
    {
        /// <summary>Id del elemento Revit asociado (Element.Id.Value).</summary>
        public long RevitElementId { get; set; }

        /// <summary>Caudal volumétrico de DISEÑO [m³/s] (con simultaneidad aplicada si procede).</summary>
        public double FlowM3s { get; set; }

        /// <summary>Caudal volumétrico bruto sin simultaneidad [m³/s].</summary>
        public double RawFlowM3s { get; set; }

        /// <summary>Numero de aparatos terminales con demanda aguas-abajo del tramo.</summary>
        public int DownstreamFixtureCount { get; set; }

        /// <summary>Factor de simultaneidad K aplicado al tramo (1.0 sin simultaneidad).</summary>
        public double SimultaneityFactor { get; set; } = 1.0;

        /// <summary>Indice BFS desde la fuente (orden de aparicion en flujo, 1 = mas cercano a fuente).</summary>
        public int BfsIndex { get; set; }

        /// <summary>Id del nudo aguas-arriba del tramo.</summary>
        public int FromNodeId { get; set; }

        /// <summary>Id del nudo aguas-abajo del tramo.</summary>
        public int ToNodeId { get; set; }

        /// <summary>Nombre del nivel Revit asociado al tramo.</summary>
        public string LevelName { get; set; }

        /// <summary>Velocidad media [m/s].</summary>
        public double VelocityMs { get; set; }

        /// <summary>Pérdida de carga lineal [Pa/m].</summary>
        public double FrictionPaPm { get; set; }

        /// <summary>Pérdida lineal total del tramo (FrictionPaPm × LengthM) [Pa].</summary>
        public double LinearLossPa { get; set; }

        /// <summary>Pérdida singular total del tramo (Σ K·½ρv²) [Pa].</summary>
        public double SingularLossPa { get; set; }

        /// <summary>Pérdida total del tramo = Linear + Singular [Pa].</summary>
        public double TotalLossPa { get; set; }

        /// <summary>Número de Reynolds.</summary>
        public double ReynoldsNumber { get; set; }

        /// <summary>Factor de fricción de Darcy f.</summary>
        public double FrictionFactor { get; set; }

        /// <summary>True si la velocidad calculada excede el límite del criterio.</summary>
        public bool VelocityExceeded { get; set; }

        /// <summary>True si la pérdida unitaria excede el límite del criterio.</summary>
        public bool FrictionExceeded { get; set; }

        /// <summary>Talla recomendada por el cálculo: 'DN50', '300x200', '250Ø', ...</summary>
        public string RecommendedSize { get; set; }

        /// <summary>
        /// Avisos específicos del tramo (p.ej. ε > diámetro, caudal nulo, etc.).
        /// Agregados al CalcReport global cuando aplique.
        /// </summary>
        public List<CalcWarning> Warnings { get; } = new();

        /// <summary>True si no hay errores y se respetan los criterios.</summary>
        public bool IsOk
            => !VelocityExceeded
             && !FrictionExceeded
             && Warnings.All(w => w.Severity != CalcSeverity.Error);

        public override string ToString()
            => $"Id={RevitElementId} v={VelocityMs:0.00} m/s, ΔP/m={FrictionPaPm:0.0} Pa/m, "
             + $"ΔP={TotalLossPa:0} Pa, Re={ReynoldsNumber:0}, OK={IsOk}, DN={RecommendedSize}";
    }

    /// <summary>
    /// Informe agregado de un cálculo: colección de resultados por tramo,
    /// mensajes globales y métricas de la red.
    /// </summary>
    public class CalcReport
    {
        public string Title    { get; set; } = "Cálculo MEP";
        public string SystemName { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>Resultados por tramo (clave = RevitElementId).</summary>
        public Dictionary<long, CalcResult> Segments { get; } = new();

        /// <summary>Avisos y errores globales (no asociados a un solo tramo).</summary>
        public List<CalcWarning> Messages { get; } = new();

        /// <summary>Presión disponible en la fuente [Pa].</summary>
        public double SourcePressurePa { get; set; }

        /// <summary>Presión mínima alcanzada en algún terminal [Pa].</summary>
        public double MinTerminalPressurePa { get; set; }

        /// <summary>Pérdida de carga total del recorrido más desfavorable [Pa].</summary>
        public double WorstCasePressureDropPa { get; set; }

        /// <summary>True si todos los tramos cumplen los criterios y no hay errores.</summary>
        public bool Success
            => Segments.Values.All(r => r.IsOk)
             && Messages.All(m => m.Severity != CalcSeverity.Error);

        /// <summary>Devuelve los avisos del informe + de cada tramo, en orden de severidad.</summary>
        public IEnumerable<CalcWarning> AllMessages
            => Messages
                .Concat(Segments.Values.SelectMany(s => s.Warnings))
                .OrderByDescending(m => (int)m.Severity);

        /// <summary>Añade un aviso global al informe (helper).</summary>
        public void AddMessage(CalcWarning warning)
        {
            if (warning != null) Messages.Add(warning);
        }

        /// <summary>Añade o sustituye el resultado de un tramo.</summary>
        public void AddSegment(CalcResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            Segments[result.RevitElementId] = result;
        }

        /// <summary>Mezcla otro informe en éste (útil para combinar sub-redes).</summary>
        public void Merge(CalcReport other)
        {
            if (other == null) return;
            foreach (var kv in other.Segments) Segments[kv.Key] = kv.Value;
            Messages.AddRange(other.Messages);
            WorstCasePressureDropPa = Math.Max(WorstCasePressureDropPa, other.WorstCasePressureDropPa);
        }

        public int CountErrors   => AllMessages.Count(m => m.Severity == CalcSeverity.Error);
        public int CountWarnings => AllMessages.Count(m => m.Severity == CalcSeverity.Warning);
    }
}
