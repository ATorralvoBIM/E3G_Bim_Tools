using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Edicion en lote sobre los resultados de OneFilter.  Aplica un valor nuevo a
/// un parametro en todos los elementos indicados, agrupado en una unica
/// transaccion para que un solo Ctrl-Z deshaga todo.
/// </summary>
public interface IFilterBatchEditService
{
    BatchEditResult ApplyBatch(Document doc, IReadOnlyList<ElementId> ids, BatchEditOperation op);
}
