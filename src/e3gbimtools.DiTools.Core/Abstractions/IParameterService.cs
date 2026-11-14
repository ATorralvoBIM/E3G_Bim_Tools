using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Operaciones de lectura/escritura sobre parametros de Revit.
/// Las herramientas <c>ParaManager</c>, <c>OneParameter</c> y <c>SheetLink</c>
/// dependeran de este servicio en sprints posteriores.
/// </summary>
public interface IParameterService
{
    /// <summary>
    /// Busca un parametro por nombre.  Devuelve <c>null</c> si el elemento no lo tiene.
    /// </summary>
    Parameter? Find(Element element, string parameterName);

    /// <summary>
    /// Asigna <paramref name="value"/> al parametro indicado.  Devuelve <c>false</c>
    /// si el parametro no existe o es de solo lectura.  No abre transaccion
    /// (eso es responsabilidad del llamante via <see cref="ITransactionService"/>).
    /// </summary>
    bool TrySet(Element element, string parameterName, object value);
}
