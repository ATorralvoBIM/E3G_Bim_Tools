using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Servicio de importacion de archivos BC3 (FIEBDC-3).
/// </summary>
public interface IBC3ImporterService
{
    /// <summary>
    /// Carga el archivo BC3 indicado y devuelve su representacion en memoria.
    /// Lanza <see cref="System.IO.FileNotFoundException"/> si la ruta no existe.
    /// </summary>
    BC3Document Load(string filePath);
}
