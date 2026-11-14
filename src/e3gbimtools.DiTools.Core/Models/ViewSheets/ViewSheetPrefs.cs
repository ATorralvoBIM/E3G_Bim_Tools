using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.ViewSheets;

/// <summary>
/// Preferencias del panel "View &amp; Sheet Manager" que se recuerdan entre
/// sesiones (independientes del proyecto).  Las selecciones de elementos del
/// modelo (title block, tipo de vista, tipo de viewport) se guardan por NOMBRE
/// y se re-resuelven contra el documento al abrir, porque los ElementId cambian
/// entre proyectos.  Se serializa con <c>System.Text.Json</c>.
/// </summary>
public sealed class ViewSheetPrefs
{
    public int EscalaGlobal { get; set; } = 50;
    public int PaddingContador { get; set; } = 2;
    public string PatronNumeroHoja { get; set; } = "{prefijo}-{contador}";
    public string PatronNombreHoja { get; set; } = "{instalacion} - {nivel}";

    public int SubcapNiveles { get; set; } = 1;
    public bool AgruparSectores { get; set; }
    public bool SoloVistas { get; set; }

    public bool RellenarClasifVistas { get; set; }
    public bool RellenarClasifPlanos { get; set; }

    /// <summary>Patrones de clasificación de las VISTAS (param → patrón).</summary>
    public Dictionary<string, string> ClasificacionVistas { get; set; } = new();

    /// <summary>Patrones de clasificación de los PLANOS/hojas (param → patrón).</summary>
    public Dictionary<string, string> ClasificacionPlanos { get; set; } = new();

    /// <summary>Selecciones recordadas por NOMBRE (se re-resuelven al abrir).</summary>
    public string? TitleBlockName { get; set; }
    public string? ViewFamilyTypeName { get; set; }
    public string? ViewportTypeName { get; set; }
}
