// ============================================================================
//  SchematicGeneratorService.cs
//  Orquestador de la generación del esquema en Revit.
//
//  Flujo de una invocación:
//    1) Abrir TransactionGroup "Generar esquema MEP".
//    2) Crear ViewDrafting nueva.
//    3) Construir el SymbolFactory (registra estilos de línea).
//    4) Sub-Transacción "lineas":      conexiones (vertical + dashed).
//    5) Sub-Transacción "fittings":    glifos TEE/ELBOW/CROSS/REDUCER/TERMINAL.
//    6) Sub-Transacción "etiquetas":   TextNote por cada nodo con label.
//    7) Sub-Transacción "leyenda":     bloque de texto con metadatos.
//    8) Asimilar TransactionGroup (un solo paso en el undo del usuario).
//
//  Si cualquier sub-Tx lanza una excepción se propaga y la TransactionGroup
//  se descarta (RollBack), dejando el documento intacto.
//
//  Este servicio NO se ejecuta desde el thread de UI directamente — se
//  invoca dentro de un ExternalEventBus.Execute (ya tenemos uno desde Fase 1).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Core.Schematic;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public sealed class SchematicGeneratorService
    {
        public sealed class Result
        {
            public ElementId ViewId { get; set; }
            public string ViewName { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            /// <summary>Mensajes informativos / avisos de carga de símbolos.</summary>
            public string SymbolReport { get; set; }
        }

        /// <summary>Genera la vista a partir del layout ya construido.</summary>
        /// <param name="kindToSymbolRfa">Diccionario opcional Kind→ruta .rfa
        /// para usar familias en lugar de los glifos primitivos.  Las familias
        /// que no sean Detail Item / Generic Annotation se descartan.</param>
        public Result Generate(UIDocument uidoc, SchematicLayout layout,
            IReadOnlyDictionary<string, string> kindToSymbolRfa = null)
        {
            if (uidoc == null) return Fail("UIDocument nulo");
            if (layout == null) return Fail("Layout nulo");
            var doc = uidoc.Document;

            var tg = new TransactionGroup(doc, "Generar esquema MEP");
            try
            {
                tg.Start();
                ViewDrafting view;

                using (var tx = new Transaction(doc, "Crear vista"))
                {
                    tx.Start();
                    string name = layout.Params.ViewName ??
                        ViewFactory.BuildDefaultName(layout.Legend?.SystemName ?? "Sistema");
                    view = ViewFactory.Create(doc, name, layout.Params.Scale);
                    tx.Commit();
                }

                // ── Cargar familias .rfa del mapping ─────────────────────────
                var symbols = new Dictionary<string, FamilySymbol>(
                    StringComparer.OrdinalIgnoreCase);
                string symbolReport = null;
                if (kindToSymbolRfa != null && kindToSymbolRfa.Count > 0)
                {
                    using (var tx = new Transaction(doc, "Cargar familias de símbolos"))
                    {
                        tx.Start();
                        var loader = new SymbolFamilyLoader();
                        var rep = loader.LoadAll(doc, kindToSymbolRfa);
                        foreach (var kv in rep.Resolved) symbols[kv.Key] = kv.Value;
                        if (rep.LoadedCount + rep.ReusedCount + rep.RejectedCount > 0)
                            symbolReport = rep.Summary();
                        tx.Commit();
                    }
                }

                var factory = new RevitSymbolFactory(doc, view,
                    layout.Params.SystemRgb, layout.Params.CriticalPathRgb);

                // Líneas
                using (var tx = new Transaction(doc, "Trazar líneas del esquema"))
                {
                    tx.Start();
                    foreach (var c in layout.Connections)
                    {
                        if (c.Kind == SchematicConnectionKind.Continuation)
                            factory.DrawLine(c.From, c.To, c.OnCriticalPath);
                        else
                            factory.DrawDashed(c.From, c.To, c.OnCriticalPath);
                    }
                    tx.Commit();
                }

                // Fuente
                using (var tx = new Transaction(doc, "Marcador de fuente"))
                {
                    tx.Start();
                    if (symbols.TryGetValue("FUENTE", out var fSym))
                        factory.PlaceFamilyInstance(layout.SourcePos, fSym, false);
                    else
                        factory.DrawSource(layout.SourcePos);
                    tx.Commit();
                }

                // Glifos de fittings + círculos de bifurcación
                using (var tx = new Transaction(doc, "Glifos de accesorios"))
                {
                    tx.Start();
                    foreach (var n in layout.Nodes)
                    {
                        if (!n.IsFitting) continue;
                        if (n.IsBifurcation) continue;  // los TEE/CROSS van en juncs
                        var kind = n.Row.EndNodeKind;
                        // Filtros por categoría — listos para cuando el motor
                        // emita VALVE/METER/SENSOR como EndNodeKind.
                        if (kind == "VALVE"  && !layout.Params.ShowValves)  continue;
                        if (kind == "METER"  && !layout.Params.ShowMeters)  continue;
                        if (kind == "SENSOR" && !layout.Params.ShowSensors) continue;
                        if (kind != null && symbols.TryGetValue(kind, out var nSym))
                        {
                            factory.PlaceFamilyInstance(n.Pos, nSym, n.OnCriticalPath);
                            continue;
                        }
                        switch (kind)
                        {
                            case "ELBOW":    factory.DrawElbow(n.Pos, n.OnCriticalPath); break;
                            case "TERMINAL": factory.DrawTerminal(n.Pos, n.OnCriticalPath); break;
                            case "REDUCER":  factory.DrawReducer(n.Pos, n.OnCriticalPath); break;
                            case "FIN":      factory.DrawTerminal(n.Pos, n.OnCriticalPath); break;
                        }
                    }
                    foreach (var j in layout.Junctions)
                    {
                        var pk = j.ParentFitting?.EndNodeKind;
                        if (pk != null && symbols.TryGetValue(pk, out var jSym))
                        {
                            factory.PlaceFamilyInstance(j.Pos, jSym, j.OnCriticalPath);
                            continue;
                        }
                        if (pk == "TEE")
                            factory.DrawTee(j.Pos, j.OnCriticalPath);
                        else if (pk == "CROSS")
                            factory.DrawCross(j.Pos, j.OnCriticalPath);
                        else
                            factory.DrawJunctionDot(j.Pos, j.OnCriticalPath);
                    }
                    tx.Commit();
                }

                // Etiquetas
                using (var tx = new Transaction(doc, "Etiquetas"))
                {
                    tx.Start();
                    foreach (var n in layout.Nodes)
                    {
                        if (string.IsNullOrEmpty(n.Label)) continue;
                        // Empezar a la derecha del glifo
                        var labelPos = new SchematicPoint(
                            n.Pos.XMm + (n.IsFitting ? 6.0 : 4.0),
                            n.Pos.YMm - 1.5);
                        factory.DrawText(labelPos, n.Label,
                            layout.Params.TextHeightMm, n.IsFitting);
                    }
                    tx.Commit();
                }

                // Leyenda
                if (layout.Params.ShowLegend && layout.Legend != null)
                {
                    using (var tx = new Transaction(doc, "Leyenda"))
                    {
                        tx.Start();
                        var legendPos = new SchematicPoint(
                            layout.Params.MmTrunkX,
                            layout.HeightMm - 40);
                        factory.DrawText(legendPos, BuildLegendText(layout.Legend),
                            layout.Params.LegendTextHeightMm, false);
                        tx.Commit();
                    }
                }

                tg.Assimilate();
                return new Result
                {
                    Success = true,
                    ViewId = view.Id,
                    ViewName = view.Name,
                    SymbolReport = symbolReport,
                };
            }
            catch (Exception ex)
            {
                try { if (tg.HasStarted()) tg.RollBack(); } catch { }
                return Fail(ex.Message);
            }
        }

        /// <summary>Activa la vista generada en Revit (zoom-to-fit).</summary>
        public void OpenView(UIDocument uidoc, ElementId viewId)
        {
            if (uidoc == null || viewId == ElementId.InvalidElementId) return;
            try
            {
                var v = uidoc.Document.GetElement(viewId) as View;
                if (v != null) uidoc.ActiveView = v;
            }
            catch { }
        }

        private static string BuildLegendText(SchematicLegend l)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Sistema: {l.SystemName}");
            if (!string.IsNullOrEmpty(l.CriteriaName))
                sb.AppendLine($"Criterio: {l.CriteriaName}");
            sb.AppendLine($"Tramos: {l.PipeCount}  ·  ΔP peor caso: {l.WorstCaseDpPa:F0} Pa");
            sb.AppendLine($"Avisos: {l.WarningCount}  ·  Errores: {l.ErrorCount}");
            sb.Append($"Fecha: {l.GeneratedAt:yyyy-MM-dd HH:mm}");
            return sb.ToString();
        }

        private static Result Fail(string msg)
            => new Result { Success = false, Error = msg };
    }
}
