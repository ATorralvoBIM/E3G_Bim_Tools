using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.TableGen;

/// <summary>
/// Peticion de generacion en lote: genera una Drafting View por cada hoja
/// del archivo .xlsx indicado (Sprint 9 — TableGen P2).
/// </summary>
public sealed class BatchTableGenRequest
{
    /// <summary>Ruta completa al archivo .xlsx.</summary>
    public string FilePath { get; init; } = "";

    /// <summary>
    /// Patron de nombre para cada vista generada.
    /// Se sustituye {sheet} por el nombre de la hoja de calculo.
    /// Null = "TableGen_{fichero}_{sheet}" (igual que la generacion individual).
    /// </summary>
    public string? NamingPattern { get; init; }

    /// <summary>
    /// Si existe una Drafting View con el mismo nombre, la elimina antes
    /// de regenerar.  False = añade sufijo numerico (comportamiento P1).
    /// </summary>
    public bool OverwriteExisting { get; init; } = false;

    /// <summary>
    /// Factor de escala mm→ft aplicado a todas las hojas del lote.
    /// Por defecto 1/304.8 (1 mm = 1/304.8 ft).
    /// </summary>
    public double MmToFeet { get; init; } = 1.0 / 304.8;
}
