using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Obtiene la lista de categorias Revit que contienen elementos
/// en el documento activo, ordenadas alfabeticamente por nombre
/// localizado (igual que la ventana Visibilidad/Graficos — VV).
/// </summary>
public interface IRevitCategoryProvider
{
    IReadOnlyList<RevitCategoryInfo> GetCategoriesWithElements(Document doc);
}
