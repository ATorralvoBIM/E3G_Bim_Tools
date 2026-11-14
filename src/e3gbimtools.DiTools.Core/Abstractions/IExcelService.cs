using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Lectura y escritura basica de hojas Excel (.xlsx).  Implementacion
/// concreta usa <c>ClosedXML</c>.  Necesario para SheetLink, TableGen,
/// ParaManager y FamilyReviser en sprints posteriores.
/// </summary>
public interface IExcelService
{
    /// <summary>
    /// Escribe <paramref name="rows"/> en una hoja nueva (o existente)
    /// del libro indicado.  Crea el archivo si no existe.
    /// </summary>
    void WriteSheet<T>(string filePath, string sheetName, IEnumerable<T> rows);

    /// <summary>
    /// Lee filas de la hoja indicada deserializandolas a <typeparamref name="T"/>.
    /// La primera fila se usa como cabecera y las propiedades de
    /// <typeparamref name="T"/> deben coincidir por nombre con las columnas.
    /// </summary>
    IEnumerable<T> ReadSheet<T>(string filePath, string sheetName) where T : new();
}
