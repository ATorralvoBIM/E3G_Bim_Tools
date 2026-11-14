// ============================================================================
//  ResultRow.cs
//  Core.Common - Fila de resultados compartida entre Filtering, Analytics,
//  Schematic y la UI (CalcWindow).
//
//  Extraida del monolito CalcWindow.xaml.cs durante la migracion al proyecto
//  e3gbimtools.Calc (FASE 3). En el monolito era una clase anidada dentro
//  de CalcWindow; aqui pasa a ser un POCO independiente para que las capas
//  no-UI (FilterService, CriticalPathService, SchematicLayoutEngine, etc.)
//  puedan referenciarlo sin depender de la View.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace e3gbimtools.Calc.Core.Common
{
    public class ResultRow
    {
        // ElementKind:  "PIPE" | "TEE" | "ELBOW" | "REDUCER" | "TERMINAL" |
        //              "CROSS" | "FIN" | "JUNCTION".  Para "PIPE" la fila
        //              lleva todos los datos hidraulicos; para los accesorios
        //              solo la geometria/topologia.
        public string ElementKind  { get; set; } = "PIPE";
        public bool   IsFitting => ElementKind != "PIPE";

        public long   RevitId      { get; set; }
        public int    BfsIndex     { get; set; }   // # de tramo en orden de flujo
        public string FromNode     { get; set; }   // Etiqueta del nudo aguas-arriba
        public string ToNode       { get; set; }   // Etiqueta del nudo aguas-abajo
        public string NodePath     { get; set; }   // "N3 → N4"
        public int    Depth        { get; set; }   // profundidad BFS
        public string LevelName    { get; set; }   // Reference Level del Revit
        public bool   IsSource     { get; set; }   // tramo de acometida
        public bool   IsTerminal   { get; set; }   // tramo que termina en aparato
        public int    BranchCount  { get; set; }   // hijos directos del nudo final
        public string EndNodeKind  { get; set; }   // TERMINAL/TEE/CROSS/ELBOW/REDUCER/FIN/JUNCTION
        public string Size         { get; set; }
        public int    FixtureCount { get; set; }   // N = aparatos aguas-abajo
        public double KFactor      { get; set; } = 1.0; // K simultaneidad
        public double RawFlowLps   { get; set; }   // Q sin simultaneidad
        public double FlowLps      { get; set; }   // Q de diseno (con K)
        public double Velocity     { get; set; }
        public double FrictionPaPm { get; set; }
        public double TotalLoss    { get; set; }
        public double Reynolds     { get; set; }
        public string RecommendedSize { get; set; }
        public string Status       { get; set; }
        public string StatusKind   { get; set; }   // OK / Warning / Error / Disconnected
        public bool   Disconnected { get; set; }    // tramo cortado de la fuente

        // ---- Display properties (vacias en filas de accesorio) ----
        public string ElementLabel => ElementKind == "PIPE" ? "Tubería" : (ElementKind ?? "");
        public string BfsIndexDisplay => (BfsIndex > 0 && !IsFitting) ? BfsIndex.ToString() : "";
        public string RevitIdDisplay  => RevitId > 0 ? RevitId.ToString() : "";
        public string SizeDisplay     => IsFitting ? "" : (Size ?? "");
        public string RecommendedSizeDisplay => IsFitting ? "" : (RecommendedSize ?? "");
        public string FixtureCountDisplay => (!IsFitting && FixtureCount > 0) ? FixtureCount.ToString() : "";
        public string KFactorDisplay  => IsFitting ? "" : KFactor.ToString("F2");
        public string RawFlowLpsDisplay => IsFitting ? "" : RawFlowLps.ToString("F2");
        public string FlowLpsDisplay  => IsFitting ? "" : FlowLps.ToString("F2");
        public string VelocityDisplay => IsFitting ? "" : Velocity.ToString("F2");
        public string FrictionPaPmDisplay => IsFitting ? "" : FrictionPaPm.ToString("F0");
        public string TotalLossDisplay => IsFitting ? "" : TotalLoss.ToString("F0");

        public void SetStatusFrom(CalcResult r)
        {
            if (r == null)
            {
                Status = "Sin calcular";
                StatusKind = "Warning";
                return;
            }
            // Prioridad máxima: ESTE es el tramo exacto del corte. Estado propio
            // (no "Error" genérico) para que destaque y sea fácil de localizar.
            if (r.Disconnected)
            {
                Disconnected = true;
                Status = "⚠ CORTE EN ESTE TRAMO";
                StatusKind = "Disconnected";
                return;
            }
            // Tramo aguas abajo de un corte: aislado, resultado no fiable.
            if (r.Warnings.Any(w => w.Code == "DOWNSTREAM_OF_BREAK"))
            {
                Status = "Aguas abajo de un corte (no fiable)";
                StatusKind = "Warning";
                return;
            }
            if (r.Warnings.Any(w => w.Severity == CalcSeverity.Error))
            {
                Status = string.Join(" · ",
                    r.Warnings.Where(w => w.Severity == CalcSeverity.Error)
                              .Select(w => w.Message));
                StatusKind = "Error";
                return;
            }
            if (r.VelocityExceeded || r.FrictionExceeded || r.VelocityBelowMin
                || r.Warnings.Any(w => w.Severity == CalcSeverity.Warning))
            {
                var msgs = new List<string>();
                if (r.VelocityExceeded) msgs.Add("v excede");
                if (r.VelocityBelowMin) msgs.Add("v baja");
                if (r.FrictionExceeded) msgs.Add("dP/m excede");
                msgs.AddRange(r.Warnings
                    .Where(w => w.Severity == CalcSeverity.Warning)
                    .Select(w => w.Code));
                Status = string.Join(" - ", msgs);
                StatusKind = "Warning";
                return;
            }
            Status = "OK";
            StatusKind = "OK";
        }
    }
}
