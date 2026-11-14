using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Filter;
using E3G.BimTools.DiTools.Core.Models.OneParameter;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Motor OneParameter (Sprint 10).  Permite buscar todos los elementos del
/// modelo que poseen un parametro dado, ver y editar sus valores de forma
/// masiva o individual, ejecutar Find &amp; Replace y aplicar transformaciones
/// tipo prefix/suffix/regex/concat con preview Before/After.
///
/// Convencion: Search, GetParameterNames y PreviewTransform son de solo lectura
/// (sin tx).  ApplyEdits, FindReplace y ApplyTransform abren una unica
/// transaccion para que un Ctrl-Z deshaga el cambio completo.
/// </summary>
public interface IOneParameterService
{
    /// <summary>
    /// Devuelve la lista de nombres de parametros disponibles en el modelo,
    /// muestreando hasta N elementos por categoria para que sea rapido incluso
    /// en proyectos grandes.  Resultado ordenado alfabeticamente; se usa para
    /// el autocompletado del ComboBox en la ventana.
    /// </summary>
    IReadOnlyList<string> GetParameterNames(Document doc);

    /// <summary>
    /// Escanea el modelo segun el scope definido en <paramref name="request"/> y
    /// devuelve todas las filas de elementos que tienen el parametro especificado.
    /// Si se indica <see cref="OneParameterSearchRequest.CategoryFilter"/>,
    /// solo se escanean esas categorias.
    /// </summary>
    OneParameterSearchResult Search(Document doc, OneParameterSearchRequest request);

    /// <summary>
    /// Aplica los cambios pendientes (filas con <c>IsModified=true</c>) en una
    /// unica transaccion.  Actualiza <c>CurrentValue</c> e <c>IsModified</c>
    /// de cada fila editada con exito.
    /// </summary>
    OneParameterEditResult ApplyEdits(Document doc, IReadOnlyList<OneParameterRow> rows);

    /// <summary>
    /// Encuentra todos los elementos cuyo valor del parametro <paramref name="parameterName"/>
    /// contiene <paramref name="findText"/> y lo sustituye por <paramref name="replaceText"/>.
    /// Solo funciona con parametros de tipo String.  Toda la operacion va en una
    /// unica transaccion.
    /// </summary>
    OneParameterEditResult FindReplace(
        Document                        doc,
        string                          parameterName,
        string                          findText,
        string                          replaceText,
        bool                            matchCase,
        IReadOnlyList<BuiltInCategory>? categoryFilter);

    // ------------------------------------------------------------------
    // Transformaciones (Sprint 10 — epica P10-3)
    // ------------------------------------------------------------------

    /// <summary>
    /// Calcula el resultado Before/After de una transformacion sobre las filas
    /// actualmente cargadas, sin escribir nada en Revit.  Sirve para mostrar
    /// el preview al usuario antes de confirmar.
    /// </summary>
    TransformPreviewResult PreviewTransform(
        IReadOnlyList<OneParameterRow> rows,
        TransformRequest               request);

    /// <summary>
    /// Aplica la transformacion sobre los elementos indicados en una unica
    /// transaccion.  Solo funciona con parametros de tipo String.
    /// </summary>
    OneParameterEditResult ApplyTransform(
        Document                       doc,
        IReadOnlyList<OneParameterRow> rows,
        TransformRequest               request);
}
