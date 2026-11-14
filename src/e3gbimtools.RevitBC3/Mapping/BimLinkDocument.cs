using System;
using System.Collections.Generic;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Documento raiz del archivo de vinculacion BIM (.bimlink.json).
///
/// El archivo se genera automaticamente junto al BC3 exportado y permite:
///   - Trazabilidad elemento a elemento (Revit ElementId + IfcGUID → partida BC3).
///   - Insumo para generar PSETs en el IFC (Sprint A4).
///   - Auditoria del proceso de extraccion (cobertura de GUIDs).
///
/// Formato del nombre del archivo:
///   &lt;nombre-bc3-sin-extension&gt;.bimlink.json
///
/// Ejemplo:
///   presupuesto-e3gbimtools.bc3 → presupuesto-e3gbimtools.bimlink.json
/// </summary>
public sealed class BimLinkDocument
{
    /// <summary>Version del esquema del archivo. Actualmente "1.0".</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Nombre del generador.</summary>
    public string Generator { get; set; } = "e3gbimtools.RevitBC3";

    /// <summary>Fecha y hora de generacion (UTC, ISO 8601).</summary>
    public string Generated { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>Nombre del archivo BC3 asociado (solo el nombre, sin ruta).</summary>
    public string Bc3File { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del archivo IFC de referencia, si se conoce.
    /// Vacio si el modelo no se ha exportado a IFC todavia.
    /// </summary>
    public string IfcModel { get; set; } = string.Empty;

    /// <summary>Lista de partidas con sus elementos contribuyentes.</summary>
    public List<BimLinkPartida> Partidas { get; set; } = new();

    /// <summary>Estadisticas de cobertura de IfcGUID del proceso de extraccion.</summary>
    public BimLinkGuidStats EstadisticasGuid { get; set; } = new();
}

/// <summary>
/// Partida del presupuesto con la lista de elementos Revit que la componen.
/// </summary>
public sealed class BimLinkPartida
{
    /// <summary>Codigo final en el BC3 exportado (puede tener sufijo .001 si hay colision).</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Codigo original del catalogo BC3 importado.</summary>
    public string CodigoCatalogo { get; set; } = string.Empty;

    /// <summary>Resumen / descripcion corta de la partida.</summary>
    public string Resumen { get; set; } = string.Empty;

    /// <summary>Unidad de medicion de la partida (m, m2, m3, ud, etc.).</summary>
    public string Unidad { get; set; } = string.Empty;

    /// <summary>Cantidad total de la partida (suma de elementos o override manual).</summary>
    public double Cantidad { get; set; }

    /// <summary>
    /// True si la cantidad es un override manual del usuario, no la suma automatica.
    /// En este caso la lista de Elementos puede estar vacia o ser ilustrativa.
    /// </summary>
    public bool EsOverrideManual { get; set; }

    /// <summary>Codigo del capitulo al que pertenece la partida (ej. "02SAN").</summary>
    public string Capitulo { get; set; } = string.Empty;

    /// <summary>Codigo del subcapitulo, si tiene (ej. "02SAN.01"). Vacio si no tiene.</summary>
    public string Subcapitulo { get; set; } = string.Empty;

    /// <summary>
    /// Elementos Revit que contribuyen a esta partida.
    /// Vacio si hay override manual o si la partida no tiene contribuciones.
    /// </summary>
    public List<BimLinkElement> Elementos { get; set; } = new();
}

/// <summary>
/// Aporte de un elemento Revit concreto a una partida del presupuesto.
/// </summary>
public sealed class BimLinkElement
{
    /// <summary>
    /// IfcGUID de 22 caracteres (alfabeto IFC Base64: 0-9 A-Z a-z _ $).
    /// Calculado deterministicamente desde UniqueId si el modelo no fue exportado a IFC.
    /// </summary>
    public string IfcGuid { get; set; } = string.Empty;

    /// <summary>
    /// Origen del IfcGUID: "Parameter" (leido del param IFC_GUID),
    /// "Calculated" (calculado desde UniqueId) o "Fallback" (emergencia, no fiable).
    /// </summary>
    public string OrigenGuid { get; set; } = string.Empty;

    /// <summary>ElementId interno de Revit (para diagnostico y trazabilidad).</summary>
    public long ElementId { get; set; }

    /// <summary>Cantidad aportada por este elemento a la partida.</summary>
    public double Cantidad { get; set; }

    /// <summary>Nombre del sistema MEP al que pertenece el elemento (vacio si no aplica).</summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>Nombre del nivel (planta) al que esta asociado el elemento (vacio si no aplica).</summary>
    public string LevelName { get; set; } = string.Empty;
}

/// <summary>
/// Estadisticas de cobertura de IfcGUID del proceso de extraccion.
/// </summary>
public sealed class BimLinkGuidStats
{
    /// <summary>Total de elementos que contribuyen a alguna partida.</summary>
    public int TotalElementos { get; set; }

    /// <summary>Elementos cuyo GUID se leyo del parametro IFC_GUID / IfcGUID.</summary>
    public int DesdeParametro { get; set; }

    /// <summary>Elementos cuyo GUID se calculo desde UniqueId (equivalente al export IFC de Revit).</summary>
    public int DesdeCalculo { get; set; }

    /// <summary>Elementos con GUID de emergencia (no fiable para vinculacion IFC).</summary>
    public int SinGuid { get; set; }

    /// <summary>Porcentaje de elementos con GUID fiable (DesdeParametro + DesdeCalculo). Ideal: 100.0.</summary>
    public double CoberturaPorc { get; set; }
}
