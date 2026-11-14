using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DxfLegend;

/// <summary>Implementacion de <see cref="IDxfLegendService"/>.</summary>
public sealed class DxfLegendService : IDxfLegendService
{
    private readonly ILogger _logger;

    public DxfLegendService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<DxfLegendService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public LegendDrawing Read(string filePath, ILegendTextMeasurer measurer)
    {
        try
        {
            var drawing = new DxfLegendReader(measurer).Read(filePath);
            _logger.Information(
                "DxfLegendService.Read: '{File}' → {Items} elementos, {Layers} capas, {W}×{H} uds.",
                filePath, drawing.Items.Count, drawing.Layers.Count, drawing.Width, drawing.Height);
            return drawing;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DxfLegendService.Read: error leyendo '{File}'.", filePath);
            throw;
        }
    }

    public IReadOnlyList<LegendTargetView> GetTargetViews(Document doc, View? activeView)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && (v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView))
            .Select(v => new LegendTargetView
            {
                Id = v.Id,
                Name = v.Name,
                Kind = v.ViewType == ViewType.Legend ? "Leyenda" : "Dibujo",
                Scale = v.Scale,
                IsActive = activeView is not null && v.Id == activeView.Id,
            })
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.Kind)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<LegendTextTypeInfo> GetTextTypes(Document doc)
    {
        static double D(Element e, BuiltInParameter p) => e.get_Parameter(p)?.AsDouble() ?? 0;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .Select(t => new LegendTextTypeInfo
            {
                Id = t.Id,
                Name = t.Name,
                Font = t.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString() ?? string.Empty,
                SizeMm = Math.Round(D(t, BuiltInParameter.TEXT_SIZE) * 304.8, 3),
                Bold = (t.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.AsInteger() ?? 0) == 1,
                WidthFactor = D(t, BuiltInParameter.TEXT_WIDTH_SCALE) is > 0 and var wf ? wf : 1.0,
            })
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public DxfLegendResult Generate(Document doc, LegendDrawing drawing, DxfLegendRequest request, ILegendTextMeasurer measurer)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (drawing is null) throw new ArgumentNullException(nameof(drawing));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (doc.GetElement(request.ViewId) is not View view
            || (view.ViewType != ViewType.Legend && view.ViewType != ViewType.DraftingView))
        {
            return new DxfLegendResult { Success = false, ErrorMessage = "La vista destino debe ser una leyenda o una vista de dibujo." };
        }

        _logger.Information(
            "DxfLegendService.Generate: '{File}' → vista '{View}' 1:{Scale}, unidades {Units}, {Excluded} capas excluidas.",
            drawing.FilePath, view.Name, request.Scale, request.Units, request.ExcludedLayers.Count);
        try
        {
            var result = new LegendRevitBuilder(doc, view, drawing, request, measurer).Build(drawing);
            _logger.Information("DxfLegendService.Generate: {Summary} Avisos: {Warnings}.", result.ToSummary(), result.Warnings.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DxfLegendService.Generate: error.");
            return new DxfLegendResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
