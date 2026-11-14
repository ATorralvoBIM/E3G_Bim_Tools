using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// "Leyenda desde DXF": lee una leyenda dibujada en AutoCAD (pensada para una
/// ventana grafica a escala 1:N) y la recrea con dibujo de detalle nativo en una
/// vista de leyenda o de dibujo de Revit, con las mismas medidas.
/// </summary>
public interface IDxfLegendService
{
    /// <summary>Lee y aplana el DXF/DWG.  No necesita contexto de Revit.</summary>
    LegendDrawing Read(string filePath, ILegendTextMeasurer measurer);

    /// <summary>Vistas de leyenda y de dibujo del documento (la activa primero si es valida).</summary>
    IReadOnlyList<LegendTargetView> GetTargetViews(Document doc, View? activeView);

    /// <summary>Tipos de texto del documento (fuente, tamaño en papel, negrita), ordenados por nombre.</summary>
    IReadOnlyList<LegendTextTypeInfo> GetTextTypes(Document doc);

    /// <summary>Dibuja la leyenda en la vista del request.  Hilo de Revit; deshacer con un solo Ctrl+Z.</summary>
    DxfLegendResult Generate(Document doc, LegendDrawing drawing, DxfLegendRequest request, ILegendTextMeasurer measurer);
}
