using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.DiTools.Core.Models.ViewSheets;

/// <summary>
/// Un viewport planificado dentro de una hoja: la vista (recortada a un sector,
/// opcional) y su posición relativa en la hoja.  Una hoja normal tiene 1; en modo
/// "agrupar sectores por hoja" tiene N (uno por Scope Box).
/// </summary>
public sealed class PlanViewport
{
    /// <summary><c>ElementId.Value</c> del Scope Box (sector). &lt;= 0 = sin recorte.</summary>
    public long SectorId { get; set; } = -1;
    public string Sector { get; set; } = string.Empty;

    public string NombreVista { get; set; } = string.Empty;

    /// <summary>Centro del viewport como fracción [0..1] de la hoja (X izq→der, Y abajo→arriba).</summary>
    public double FracX { get; set; } = 0.5;
    public double FracY { get; set; } = 0.5;

    public bool TieneSector => SectorId > 0;
}

/// <summary>
/// Una HOJA planificada (instalación × nivel), con sus nombres/números calculados y
/// la lista de viewports a colocar.  Es lo que se muestra en el árbol de
/// previsualización y lo que <c>Generar</c> ejecuta si <see cref="Seleccionado"/>.
/// </summary>
public sealed class PlanItem
{
    public string Instalacion { get; set; } = string.Empty;
    public string Prefijo { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#2D7A5F";

    public long NivelId { get; set; } = -1;
    public string Nivel { get; set; } = string.Empty;

    public long ViewFamilyTypeId { get; set; } = -1;
    public long PlantillaVistaId { get; set; } = -1;
    public int Escala { get; set; } = 50;

    public string NumeroHoja { get; set; } = string.Empty;
    public string NombreHoja { get; set; } = string.Empty;

    /// <summary>Viewports a colocar en la hoja (1 normal; N si se agrupan sectores).</summary>
    public IList<PlanViewport> Viewports { get; set; } = new List<PlanViewport>();

    /// <summary>Marcado para generar (el usuario puede desmarcarlo en el árbol).</summary>
    public bool Seleccionado { get; set; } = true;

    /// <summary>Aviso calculado en dry-run (ej. el número chocaba y se ajustó).</summary>
    public string Aviso { get; set; } = string.Empty;

    public bool EsMulti => Viewports.Count > 1;

    /// <summary>Resumen de sectores para el árbol/resultados.</summary>
    public string SectoresTexto =>
        Viewports.Count == 0 ? string.Empty
        : Viewports.Count == 1 ? Viewports[0].Sector
        : string.Join(" + ", Viewports.Select(v => string.IsNullOrEmpty(v.Sector) ? "—" : v.Sector));
}

/// <summary>
/// Resultado de <c>PrepararPlan</c>: el conjunto de combinaciones planificadas más
/// la petición original (con los valores globales que <c>Generar</c> necesita:
/// title block, padding, parámetros de rótulo…).
/// </summary>
public sealed class GeneracionPlan
{
    public GeneracionPlan(GeneracionRequest request) => Request = request;

    public GeneracionRequest Request { get; }
    public IList<PlanItem> Items { get; } = new List<PlanItem>();

    public int TotalMarcados => Items.Count(i => i.Seleccionado);
}

/// <summary>
/// Medición REAL de la composición de un plano, obtenida creando vista + hoja +
/// viewport en una transacción que se revierte.  Refleja el tamaño verdadero del
/// viewport (incluye el rótulo de la vista y el recorte real), no una estimación.
/// </summary>
public readonly struct ComposicionMedida
{
    public ComposicionMedida(double hojaAnchoMm, double hojaAltoMm, double vpAnchoMm, double vpAltoMm)
    {
        HojaAnchoMm = hojaAnchoMm;
        HojaAltoMm = hojaAltoMm;
        VpAnchoMm = vpAnchoMm;
        VpAltoMm = vpAltoMm;
    }

    public double HojaAnchoMm { get; }
    public double HojaAltoMm { get; }

    /// <summary>Ancho/alto reales del viewport en la hoja (mm). 0 si no se pudo medir.</summary>
    public double VpAnchoMm { get; }
    public double VpAltoMm { get; }

    public bool HojaMedida => HojaAnchoMm > 0 && HojaAltoMm > 0;
    public bool ViewportMedido => VpAnchoMm > 0 && VpAltoMm > 0;
}
