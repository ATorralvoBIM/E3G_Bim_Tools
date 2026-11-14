using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.Core.Models.ViewSheets;

/// <summary>
/// Datos de entrada para una ejecución del generador de planos
/// (<see cref="Abstractions.IViewSheetGenerator"/>).  Recoge lo que el panel
/// recopila del usuario: niveles, instalaciones a procesar y los valores
/// globales (title block, ViewFamilyType y patrones de numeración).
/// </summary>
public sealed class GeneracionRequest
{
    /// <summary>Niveles seleccionados sobre los que crear vistas de planta.</summary>
    public IList<ElementId> NivelIds { get; set; } = new List<ElementId>();

    /// <summary>Instalaciones a procesar (solo las marcadas por el usuario).</summary>
    public IList<InstalacionConfig> Instalaciones { get; set; } = new List<InstalacionConfig>();

    /// <summary>
    /// <c>ElementId.Value</c> de los Scope Boxes (sectores) para recortar las vistas.
    /// Vacío = una vista por instalación×nivel sin recorte (comportamiento por defecto).
    /// Con sectores el bucle pasa a ser instalaciones × niveles × sectores.
    /// </summary>
    public IList<long> SectorIds { get; set; } = new List<long>();

    /// <summary><c>ElementId.Value</c> del <see cref="FamilySymbol"/> de title block (rótulo).</summary>
    public long TitleBlockTypeId { get; set; } = -1;

    /// <summary>
    /// <c>ElementId.Value</c> del tipo de viewport a aplicar (categoría
    /// <c>OST_Viewports</c>: "con título", "sin título", etc.).  &lt;= 0 = tipo por
    /// defecto.  Cambiarlo afecta al tamaño real del viewport (p.ej. "sin título"
    /// elimina la etiqueta inferior).
    /// </summary>
    public long ViewportTypeId { get; set; } = -1;

    /// <summary>
    /// <c>ElementId.Value</c> del <see cref="ViewFamilyType"/> global (familia FloorPlan).
    /// Se usa cuando la instalación no define el suyo propio.
    /// </summary>
    public long ViewFamilyTypeId { get; set; } = -1;

    /// <summary>Escala global por defecto (1:N) cuando la instalación no define la suya.</summary>
    public int EscalaGlobal { get; set; } = 50;

    /// <summary>
    /// Patrón de tokens para el número de hoja (<c>SHEET_NUMBER</c>).  Tokens:
    /// <c>{prefijo}</c>, <c>{nivel}</c>, <c>{instalacion}</c>, <c>{sector}</c>, <c>{contador}</c>.
    /// </summary>
    public string PatronNumeroHoja { get; set; } = "{prefijo}-{contador}";

    /// <summary>
    /// Patrón de tokens para el nombre de hoja (<c>SHEET_NAME</c>).  Mismos tokens
    /// que <see cref="PatronNumeroHoja"/>.
    /// </summary>
    public string PatronNombreHoja { get; set; } = "{instalacion} - {nivel}";

    /// <summary>Dígitos de relleno (padding) del token <c>{contador}</c>.  Ej. 2 → "01".</summary>
    public int PaddingContador { get; set; } = 2;

    /// <summary>
    /// Si es true, los sectores seleccionados se colocan JUNTOS en una sola hoja
    /// por instalación×nivel (N viewports). Si es false, cada sector va en su hoja.
    /// </summary>
    public bool AgruparSectoresPorHoja { get; set; }

    /// <summary>
    /// Si es true, solo se crean las vistas (con su nombre); no se crean hojas ni
    /// viewports.  Útil para generar y nombrar vistas en lote sin maquetar planos.
    /// </summary>
    public bool SoloVistas { get; set; }

    /// <summary>
    /// Posición del CENTRO del viewport dentro del rótulo, como fracción [0..1] del
    /// ancho de la hoja (0 = borde izquierdo, 1 = derecho).  0.5 = centrado.
    /// El usuario la ajusta arrastrando el viewport en el preview y se aplica a todas.
    /// </summary>
    public double ViewportFracX { get; set; } = 0.5;

    /// <summary>Posición vertical del centro del viewport [0..1] (0 = abajo, 1 = arriba).</summary>
    public double ViewportFracY { get; set; } = 0.5;

    /// <summary>
    /// Parámetros del rótulo a rellenar en cada hoja (opcional).  Clave = nombre de
    /// parámetro de la hoja/título; valor = patrón de tokens (mismos tokens).
    /// </summary>
    public IDictionary<string, string>? ParametrosRotulo { get; set; }

    /// <summary>Si es true, rellena la clasificación en las VISTAS con <see cref="ClasificacionVistas"/>.</summary>
    public bool RellenarClasifVistas { get; set; }

    /// <summary>Si es true, rellena la clasificación en las HOJAS con <see cref="ClasificacionPlanos"/>.</summary>
    public bool RellenarClasifPlanos { get; set; }

    /// <summary>
    /// Clasificación para las VISTAS (param → patrón de tokens).  En Revit las vistas
    /// y las hojas se organizan con esquemas distintos en el navegador, por eso van
    /// separadas de <see cref="ClasificacionPlanos"/>.
    /// </summary>
    public IDictionary<string, string>? ClasificacionVistas { get; set; }

    /// <summary>Clasificación para las HOJAS/planos (param → patrón de tokens).</summary>
    public IDictionary<string, string>? ClasificacionPlanos { get; set; }
}
