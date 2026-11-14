using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Evalua si una regla aplica a un elemento Revit concreto.
/// </summary>
public interface IMappingRuleEngine
{
    /// <summary>
    /// Devuelve <c>true</c> si el elemento cumple TODOS los filtros de la regla
    /// (AND logico) y pertenece a la categoria indicada.
    /// </summary>
    bool Evaluate(Element element, MappingRule rule);

    /// <summary>
    /// Genera un texto diagnostico para depurar por que una regla matchea
    /// (o no) los elementos del modelo.  Recorre la categoria de la regla,
    /// cuenta matches y para los no-match muestra que valor real lee el
    /// motor para cada parametro filtrado.
    /// </summary>
    string GenerateDiagnostic(Document doc, MappingRule rule, int sampleCount = 5);

    /// <summary>
    /// Devuelve los ElementId de Revit que matchea la regla en el documento
    /// activo.  Util para mostrar trazabilidad al usuario sin tener que
    /// "Calcular mediciones" previamente (issue #6).
    /// </summary>
    IReadOnlyList<ElementId> MatchElements(Document doc, MappingRule rule);
}
