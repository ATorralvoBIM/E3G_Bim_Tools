using System.Collections.Generic;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Lee un archivo BC3 (FIEBDC-3) y devuelve una lista plana de entradas
/// del catálogo, lista para mostrar en el <c>CatalogPickerDialog</c>.
/// </summary>
public interface IBC3CatalogImporterService
{
    /// <summary>
    /// Carga el archivo BC3 en <paramref name="filePath"/> y devuelve todas
    /// sus entradas (~C) con la relación padre-hijo deducida de los ~D.
    /// </summary>
    /// <param name="filePath">Ruta absoluta al archivo .bc3.</param>
    /// <returns>
    /// Lista ordenada por código.  Incluye capítulos y partidas.
    /// </returns>
    List<CatalogEntry> Import(string filePath);
}
