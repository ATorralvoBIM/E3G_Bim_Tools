using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Views;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Calcula diferencias entre dos View Templates (Sprint 4).
/// Sin transacciones: solo lectura.
/// </summary>
public interface IViewTemplateComparer
{
    ViewTemplateDiff Compare(Document doc, ElementId templateAId, ElementId templateBId);
}
