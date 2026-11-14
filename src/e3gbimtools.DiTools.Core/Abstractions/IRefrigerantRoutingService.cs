using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// «Trazado de refrigerante desde DXF» (Climatizacion): lee el DXF de tuberias
/// del fabricante (hoy LG LATS), casa su planta de arquitectura con los muros
/// del nivel en Revit y modela las tuberias de liquido y gas con sus
/// derivadores y cajas como accesorios de tuberia.
/// </summary>
public interface IRefrigerantRoutingService
{
    /// <summary>Lee el DXF/DWG.  No necesita contexto de Revit.</summary>
    LatsPlan Read(string path, LatsReadOptions options);

    /// <summary>Vistas de planta, niveles, tipos de tuberia, sistemas y accesorios del proyecto.</summary>
    RefrigerantProjectInfo GetProjectInfo(Document doc, View? activeView);

    /// <summary>Caras de los muros (modelo y vinculos) cortados a 1,2 m sobre el nivel, en metros y coordenadas internas.</summary>
    IReadOnlyList<Seg2> GetArchitecture(Document doc, long levelId);

    /// <summary>Encaje automatico DXF → Revit (null si no hay con que casar).</summary>
    PlanTransform? Align(LatsPlan plan, IReadOnlyList<Seg2> revit);

    /// <summary>Puntua un encaje escrito a mano.</summary>
    PlanTransform Evaluate(PlanTransform transform, LatsPlan plan, IReadOnlyList<Seg2> revit);

    /// <summary>Plan de modelado (sin tocar Revit).</summary>
    RefrigerantRoutingPlan BuildPlan(LatsPlan plan, PlanTransform transform, RefrigerantRoutingOptions options);

    /// <summary>Modela el plan.  Hilo de Revit; se deshace con un solo Ctrl+Z.</summary>
    RefrigerantRoutingResult Apply(Document doc, LatsPlan plan, PlanTransform transform, RefrigerantRoutingPlan routing, RefrigerantRoutingOptions options);
}
