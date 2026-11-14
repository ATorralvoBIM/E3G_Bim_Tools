namespace E3G.BimTools.DiTools.Core.Models.SheetLink;

/// <summary>
/// Resultado de la validacion de un archivo .xlsx exportado por SheetLink (Sprint 12).
///
/// Comprueba:
/// <list type="bullet">
///   <item><description>Presencia de la hoja "Planos" (datos exportados).</description></item>
///   <item><description>Presencia de la hoja "_Meta" (metadatos de exportacion).</description></item>
///   <item><description>Que el <c>ColumnHash</c> del archivo coincide con el esquema
///   de columnas de la version actual de SheetLink (detecta archivos de versiones anteriores
///   o con columnas alteradas manualmente).</description></item>
/// </list>
/// </summary>
public sealed class XlsxValidationResult
{
    /// <summary>True si el archivo es valido y compatible con la version actual.</summary>
    public bool IsValid { get; set; }

    /// <summary>Descripcion del error si <c>IsValid = false</c>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Fecha en que se realizo la exportacion (ISO 8601), leida de la hoja _Meta.</summary>
    public string? ExportDate { get; set; }

    /// <summary>Titulo del documento Revit en el momento de la exportacion.</summary>
    public string? DocumentTitle { get; set; }

    /// <summary>Numero de planos que se exportaron.</summary>
    public int SheetsExported { get; set; }

    /// <summary>Hash SHA-256 de 8 caracteres de los nombres de columna (para compatibilidad de esquema).</summary>
    public string? ColumnHash { get; set; }
}
