using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.Core.Models.Parameters;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Edicion masiva de un parametro sobre TODOS los elementos de una categoria
/// (sin filtros).  Lo usa la pestaña "Editar valores en lote" de ParaManager.
///
/// La operacion va en una unica transaccion para que un solo Ctrl-Z deshaga
/// todo.  Si <see cref="ParameterScope"/> es Type, el cambio se aplica al
/// ElementType correspondiente (deduplicado).
/// </summary>
public interface IParameterValueBulkEditService
{
    BatchEditResult ApplyToCategory(
        Document doc,
        BuiltInCategory category,
        string parameterName,
        string newValue,
        ParameterScope scope);
}
