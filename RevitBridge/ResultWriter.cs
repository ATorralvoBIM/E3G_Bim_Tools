// ============================================================================
//  ResultWriter.cs
//  RevitBridge — Escribe los resultados de un CalcReport al modelo Revit.
//
//  Briefing tecnico v1.0 — Mayo 2026, seccion 6.
//
//  Estrategia:
//    - Una sola Transaction para todo el batch (Regla 1 del briefing).
//    - Para cada CalcResult, busca por nombre los siguientes parametros
//      (project parameters o shared parameters previamente cargados):
//        MEP_CALC_VELOCITY        (m/s)
//        MEP_CALC_FRICTION_PA_M   (Pa/m)
//        MEP_CALC_TOTAL_LOSS_PA   (Pa)
//        MEP_CALC_REYNOLDS        (numero)
//        MEP_CALC_FRICTION_FACTOR (numero)
//        MEP_CALC_OK              (0/1)
//        MEP_CALC_RECOMMENDED_DN  (texto)
//    - Si un parametro no existe, registra un CalcWarning sin lanzar.
//    - Los valores se escriben tal cual en SI; el usuario configura los
//      parametros con tipo "Number" o "Text" en el catalogo de shared params.
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.Saneamiento.Core.Common;

namespace E3G.Saneamiento.RevitBridge
{
    /// <summary>
    /// Escribe los resultados de cálculo de vuelta al modelo Revit como
    /// parámetros de instancia.
    /// </summary>
    public class ResultWriter
    {
        // -- Nombres de los shared/project parameters esperados -------------
        public const string ParamVelocity        = "MEP_CALC_VELOCITY";
        public const string ParamFrictionPaPm    = "MEP_CALC_FRICTION_PA_M";
        public const string ParamTotalLossPa     = "MEP_CALC_TOTAL_LOSS_PA";
        public const string ParamReynolds        = "MEP_CALC_REYNOLDS";
        public const string ParamFrictionFactor  = "MEP_CALC_FRICTION_FACTOR";
        public const string ParamOk              = "MEP_CALC_OK";
        public const string ParamRecommendedDn   = "MEP_CALC_RECOMMENDED_DN";

        /// <summary>Avisos generados durante la escritura.</summary>
        public List<CalcWarning> Messages { get; } = new List<CalcWarning>();

        /// <summary>Numero de elementos actualizados con exito.</summary>
        public int Updated { get; private set; }

        /// <summary>Numero de elementos sin parametros encontrados.</summary>
        public int Skipped { get; private set; }

        /// <summary>
        /// Escribe los CalcResult al documento Revit dentro de una sola
        /// Transaction.  Lanza si hay un fallo critico de Revit; los fallos
        /// blandos (parametro inexistente, elemento borrado) se registran
        /// como Messages y siguen.
        /// </summary>
        public void WriteToRevit(Document doc, CalcReport report, string transactionName = null)
        {
            if (doc == null)    throw new ArgumentNullException(nameof(doc));
            if (report == null) throw new ArgumentNullException(nameof(report));

            Updated = 0;
            Skipped = 0;
            Messages.Clear();

            var tName = string.IsNullOrEmpty(transactionName)
                ? "MEP Calc - Escribir resultados"
                : transactionName;

            using (var t = new Transaction(doc, tName))
            {
                t.Start();
                try
                {
                    foreach (var kv in report.Segments)
                    {
                        var r = kv.Value;
                        if (r.RevitElementId <= 0L)
                        {
                            // tramo sintetico (id negativo o cero) - no se escribe
                            Skipped++;
                            continue;
                        }
                        var elem = doc.GetElement(new ElementId(r.RevitElementId));
                        if (elem == null)
                        {
                            Skipped++;
                            Messages.Add(CalcWarning.Warn("MISSING_ELEMENT",
                                "Element id " + r.RevitElementId + " no encontrado.",
                                r.RevitElementId));
                            continue;
                        }
                        bool any = false;
                        any |= TrySetDouble(elem, ParamVelocity,       r.VelocityMs);
                        any |= TrySetDouble(elem, ParamFrictionPaPm,   r.FrictionPaPm);
                        any |= TrySetDouble(elem, ParamTotalLossPa,    r.TotalLossPa);
                        any |= TrySetDouble(elem, ParamReynolds,       r.ReynoldsNumber);
                        any |= TrySetDouble(elem, ParamFrictionFactor, r.FrictionFactor);
                        any |= TrySetDouble(elem, ParamOk, r.IsOk ? 1.0 : 0.0);
                        if (!string.IsNullOrEmpty(r.RecommendedSize))
                            any |= TrySetString(elem, ParamRecommendedDn, r.RecommendedSize);
                        if (any) Updated++;
                        else
                        {
                            Skipped++;
                            Messages.Add(CalcWarning.Info("NO_PARAMS_FOUND",
                                "Ningun parametro MEP_CALC_* encontrado en elemento "
                                + r.RevitElementId + ". Carga el shared parameter file.",
                                r.RevitElementId));
                        }
                    }
                    t.Commit();
                }
                catch (Exception)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    throw;
                }
            }
        }

        // -- Helpers --------------------------------------------------------

        /// <summary>Pone un double si el parametro existe y es double.  Devuelve true si actualizo.</summary>
        private static bool TrySetDouble(Element elem, string paramName, double value)
        {
            var p = elem.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    return p.Set(value);
                }
                if (p.StorageType == StorageType.Integer)
                {
                    return p.Set((int)Math.Round(value));
                }
                if (p.StorageType == StorageType.String)
                {
                    return p.Set(value.ToString("G6"));
                }
            }
            catch { /* parametro con tipo inesperado */ }
            return false;
        }

        /// <summary>Pone un string si el parametro existe y es texto.</summary>
        private static bool TrySetString(Element elem, string paramName, string value)
        {
            var p = elem.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            try
            {
                if (p.StorageType == StorageType.String)
                    return p.Set(value);
            }
            catch { }
            return false;
        }
    }
}
