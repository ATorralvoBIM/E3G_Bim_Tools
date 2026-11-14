using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Services;

/// <summary>
/// Implementacion de <see cref="ISelectionService"/> sobre <c>UIDocument.Selection</c>.
/// </summary>
public sealed class SelectionService : ISelectionService
{
    private readonly IRevitContext _context;

    public SelectionService(IRevitContext context)
    {
        _context = context;
    }

    public IReadOnlyList<ElementId> CurrentIds()
    {
        var uidoc = _context.ActiveUiDocument
            ?? throw new InvalidOperationException("No hay documento activo.");
        return uidoc.Selection.GetElementIds().ToList();
    }

    public IReadOnlyList<Element> CurrentElements()
    {
        var doc = _context.ActiveDocument
            ?? throw new InvalidOperationException("No hay documento activo.");
        return CurrentIds()
            .Select(id => doc.GetElement(id))
            .Where(e => e is not null)
            .ToList();
    }

    public IReadOnlyList<T> CurrentOfType<T>() where T : Element =>
        CurrentElements().OfType<T>().ToList();

    public IReadOnlyList<Element> PickObjects(string prompt, Predicate<Element>? filter = null)
    {
        var uidoc = _context.ActiveUiDocument
            ?? throw new InvalidOperationException("No hay documento activo.");

        ISelectionFilter? selectionFilter = filter is null
            ? null
            : new PredicateSelectionFilter(filter, uidoc.Document);

        var refs = selectionFilter is null
            ? uidoc.Selection.PickObjects(ObjectType.Element, prompt)
            : uidoc.Selection.PickObjects(ObjectType.Element, selectionFilter, prompt);

        return refs
            .Select(r => uidoc.Document.GetElement(r))
            .Where(e => e is not null)
            .ToList();
    }

    public void Set(IEnumerable<ElementId> ids)
    {
        var uidoc = _context.ActiveUiDocument
            ?? throw new InvalidOperationException("No hay documento activo.");
        uidoc.Selection.SetElementIds(ids.ToList());
    }

    public void Clear()
    {
        var uidoc = _context.ActiveUiDocument
            ?? throw new InvalidOperationException("No hay documento activo.");
        uidoc.Selection.SetElementIds(new List<ElementId>());
    }

    private sealed class PredicateSelectionFilter : ISelectionFilter
    {
        private readonly Predicate<Element> _predicate;
        private readonly Document _document;

        public PredicateSelectionFilter(Predicate<Element> predicate, Document document)
        {
            _predicate = predicate;
            _document = document;
        }

        public bool AllowElement(Element elem) => elem is not null && _predicate(elem);
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
