using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections;

/// <summary>
/// Implementacion principal del SectionBoxer (Sprint 5).  Orquesta el
/// resolver de origen, el calculo del bounding y la aplicacion a la vista,
/// todo en una unica Transaction nombrada.
///
/// Snapshot para Reset: guarda el BoundingBoxXYZ previo de cada vista en un
/// diccionario en memoria.  Sprint 5b persiste via extensible storage.
/// </summary>
public sealed class SectionBoxService : ISectionBoxService
{
    private readonly ITransactionService _tx;
    private readonly IBoundingBoxCalculator _calculator;
    private readonly IReadOnlyDictionary<SectionBoxSourceKind, ISectionBoxSource> _sources;
    private readonly ILogger _logger;

    // Snapshot por vista (clave: View3D.Id).
    private readonly Dictionary<ElementId, BoundingBoxXYZ?> _previousBoxes = new();

    public SectionBoxService(
        ITransactionService tx,
        IBoundingBoxCalculator calculator,
        IEnumerable<ISectionBoxSource> sources,
        ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources)))
            .ToDictionary(s => s.Kind, s => s);
        _logger = loggerFactory?.CreateLogger<SectionBoxService>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public SectionBoxResult Apply(Document doc, SectionBoxRequest request, View3D? target)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (!request.IsValid(out var error))
            return new SectionBoxResult { Success = false, Summary = error ?? "Request invalido." };

        if (!_sources.TryGetValue(request.Kind, out var source))
            return new SectionBoxResult { Success = false, Summary = $"Origen no soportado: {request.Kind}." };

        var elements = source.Resolve(doc, request);
        if (elements.Count == 0)
            return new SectionBoxResult { Success = false, Summary = "El origen no devolvio elementos." };

        // Para alineación a la vista necesitamos su orientación (solo si reutilizamos
        // una vista existente; en una vista 3D nueva la orientación es isométrica).
        Transform? frame = (request.Alignment == SectionBoxAlignment.View && target is not null)
            ? FrameFromView(target)
            : null;

        var bbox = _calculator.Compute(
            doc, elements, request.PaddingMeters, request.IncludeLinks, request.Alignment, frame);

        var result = new SectionBoxResult();
        _tx.Run(doc, $"SectionBoxer: aplicar a {(target is null ? "vista nueva" : $"'{target.Name}'")}", d =>
        {
            try
            {
                if (request.CreateDedicatedView || target is null)
                {
                    var vft = new FilteredElementCollector(d)
                        .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                    if (vft is null)
                    {
                        result.Success = false;
                        result.Summary = "No hay ningún tipo de vista 3D en el proyecto.";
                        return;
                    }

                    var nueva = View3D.CreateIsometric(d, vft.Id);
                    if (!string.IsNullOrWhiteSpace(request.DedicatedViewName))
                        try { nueva.Name = UniqueViewName(d, request.DedicatedViewName!.Trim()); } catch { /* auto */ }

                    nueva.IsSectionBoxActive = true;
                    nueva.SetSectionBox(bbox);
                    _previousBoxes[nueva.Id] = null;

                    result.Success = true;
                    result.ViewId = nueva.Id;
                    result.AppliedBox = bbox;
                    result.Summary = $"Section box aplicado a vista 3D nueva '{nueva.Name}' ({elements.Count} elementos).";
                }
                else
                {
                    var view = target;
                    _previousBoxes[view.Id] = view.GetSectionBox(); // snapshot
                    view.IsSectionBoxActive = true;
                    view.SetSectionBox(bbox);
                    result.Success = true;
                    result.ViewId = view.Id;
                    result.AppliedBox = bbox;
                    result.Summary = $"Section box aplicado a '{view.Name}' ({elements.Count} elementos).";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al aplicar section box");
                result.Success = false;
                result.Summary = ex.Message;
            }
        });

        return result;
    }

    /// <summary>Transform (rotación) derivado de la orientación de una vista 3D, para alinear la caja.</summary>
    private static Transform? FrameFromView(View3D v)
    {
        try
        {
            var o = v.GetOrientation();
            XYZ up = o.UpDirection.Normalize();
            XYZ fwd = o.ForwardDirection.Normalize();
            XYZ right = up.CrossProduct(fwd).Normalize();
            if (right.IsZeroLength()) return null;
            XYZ z = right.CrossProduct(up).Normalize();

            var t = Transform.Identity;
            t.BasisX = right;
            t.BasisY = up;
            t.BasisZ = z;
            t.Origin = XYZ.Zero;
            return t;
        }
        catch { return null; }
    }

    private static string UniqueViewName(Document doc, string name)
    {
        var usados = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);
        if (!usados.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            var cand = $"{name} ({i})";
            if (!usados.Contains(cand)) return cand;
        }
    }

    public SectionBoxResult Clear(View3D view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));

        var result = new SectionBoxResult();
        _tx.Run(view.Document, $"SectionBoxer: reset '{view.Name}'", d =>
        {
            try
            {
                if (_previousBoxes.TryGetValue(view.Id, out var previous) && previous is not null)
                {
                    view.SetSectionBox(previous);
                    result.Summary = $"Section box restaurado al estado previo en '{view.Name}'.";
                }
                else
                {
                    // No hay snapshot: desactivamos el flag.
                    view.IsSectionBoxActive = false;
                    result.Summary = $"Section box desactivado en '{view.Name}'.";
                }
                result.Success = true;
                result.ViewId = view.Id;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fallo al limpiar section box");
                result.Success = false;
                result.Summary = ex.Message;
            }
        });

        return result;
    }
}
