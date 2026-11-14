using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ViewSheets;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Generador de planos de instalaciones MEP (módulo "View &amp; Sheet Manager").
/// Recorre el triple bucle <c>instalaciones × niveles × sectores</c> y, por cada
/// combinación, crea una vista de planta (con plantilla, escala y recorte a Scope
/// Box opcional), una hoja con su rótulo y la vista como viewport centrado.
/// El flujo es en dos pasos: <see cref="PrepararPlan"/> (dry-run, sin tocar el
/// modelo) y <see cref="Generar"/> (ejecuta lo marcado, en una sola transacción).
/// </summary>
public interface IViewSheetGenerator
{
    /// <summary>
    /// Calcula el plano de generación SIN crear nada: resuelve nombres de vista,
    /// números/nombres de hoja y la unicidad.  Es lo que alimenta el árbol de
    /// previsualización; el usuario marca/desmarca combinaciones antes de generar.
    /// </summary>
    GeneracionPlan PrepararPlan(Document doc, GeneracionRequest request);

    /// <summary>
    /// Ejecuta la generación de los <see cref="PlanItem"/> con
    /// <see cref="PlanItem.Seleccionado"/> == true.  No lanza por errores de una
    /// combinación: los acumula en el resumen y continúa con el resto del lote.
    /// </summary>
    GeneracionResumen Generar(Document doc, GeneracionPlan plan);

    /// <summary>
    /// Mide la composición REAL de un plano (tamaño de hoja y del viewport con su
    /// rótulo de vista y recorte) creando vista + hoja + viewport en una transacción
    /// que se revierte (no deja rastro ni undo).  Alimenta el preview fidedigno.
    /// </summary>
    ComposicionMedida MedirComposicion(Document doc, GeneracionPlan plan, PlanItem item, PlanViewport viewport);

    /// <summary>
    /// Exporta una hoja ya creada a PNG en <paramref name="carpetaDestino"/> y
    /// devuelve la ruta del archivo generado (o <c>null</c> si falla).  Para el
    /// preview "real" tras generar.  Debe ejecutarse en contexto de la API de Revit.
    /// </summary>
    string? ExportarHojaPng(Document doc, long sheetId, string carpetaDestino);
}
