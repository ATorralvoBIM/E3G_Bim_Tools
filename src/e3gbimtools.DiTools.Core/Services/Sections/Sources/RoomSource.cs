using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;

namespace E3G.BimTools.DiTools.Core.Services.Sections.Sources;

/// <summary>
/// Origen "Room" del SectionBoxer (Sprint 5).
/// Devuelve la habitacion como unico elemento; el bounding del SpatialElement
/// engloba su volumen, que es lo que el usuario espera.
/// </summary>
public sealed class RoomSource : ISectionBoxSource
{
    public SectionBoxSourceKind Kind => SectionBoxSourceKind.Room;

    public IReadOnlyList<Element> Resolve(Document doc, SectionBoxRequest request)
    {
        if (request.RoomId is null) return new List<Element>();
        var room = doc.GetElement(request.RoomId);
        return room is null ? new List<Element>() : new List<Element> { room };
    }
}
