using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.ViewSheets;

/// <summary>
/// Configuración persistente de una instalación MEP para el módulo
/// "View &amp; Sheet Manager".  Es un POCO puro serializable con
/// <c>System.Text.Json</c> (mismo patrón que <c>SheetLayoutDefinition</c>).
///
/// Decisión de diseño: los ids de Revit NO se serializan como
/// <see cref="ElementId"/> (no serializa limpiamente).  Se guardan como
/// <see cref="long"/> — el valor de <c>ElementId.Value</c> en Revit 2025 — y se
/// resuelven contra el documento activo al cargar con
/// <see cref="ToElementId(long)"/>.  Un valor &lt;= 0 significa "no asignado /
/// usar el valor global del panel".
/// </summary>
public sealed class InstalacionConfig
{
    /// <summary>Nombre visible de la instalación. Ej. "Fontanería".</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Prefijo para el número de hoja. Ej. "FON".</summary>
    public string PrefijoHoja { get; set; } = string.Empty;

    /// <summary>
    /// <c>ElementId.Value</c> de la View Template a aplicar (vista con
    /// <c>IsTemplate == true</c>).  &lt;= 0 = sin plantilla.
    /// </summary>
    public long PlantillaVistaId { get; set; } = -1;

    /// <summary>
    /// <c>ElementId.Value</c> del <see cref="Autodesk.Revit.DB.ViewFamilyType"/>
    /// (familia <c>FloorPlan</c>).  &lt;= 0 = usar el ViewFamilyType global del panel.
    /// </summary>
    public long ViewFamilyTypeId { get; set; } = -1;

    /// <summary>Escala de la vista (1:N). Ej. 50.  &lt;= 0 = usar la global del panel.</summary>
    public int Escala { get; set; } = 50;

    /// <summary>
    /// Patrón de tokens para el nombre de la vista.  Tokens soportados:
    /// <c>{prefijo}</c>, <c>{nivel}</c>, <c>{instalacion}</c>, <c>{contador}</c>.
    /// </summary>
    public string PatronNombre { get; set; } = "{prefijo}_{nivel}_{instalacion}";

    /// <summary>Color hex para el badge de la instalación en la UI. Ej. "#2D7A5F".</summary>
    public string ColorHex { get; set; } = "#2D7A5F";

    /// <summary>Resuelve un valor long persistido a un <see cref="ElementId"/> de Revit 2025.</summary>
    public static ElementId ToElementId(long value) =>
        value > 0 ? new ElementId(value) : ElementId.InvalidElementId;

    /// <summary>Conjunto por defecto de instalaciones (primera ejecución, sin JSON guardado).</summary>
    public static List<InstalacionConfig> Defaults() => new()
    {
        new InstalacionConfig { Nombre = "Fontanería",   PrefijoHoja = "FON", ColorHex = "#378ADD" },
        new InstalacionConfig { Nombre = "Saneamiento",  PrefijoHoja = "SAN", ColorHex = "#A0522D" },
        new InstalacionConfig { Nombre = "Pluviales",    PrefijoHoja = "PLU", ColorHex = "#1AB3C4" },
        new InstalacionConfig { Nombre = "Climatización", PrefijoHoja = "CLI", ColorHex = "#EF9F27" },
    };
}
