// ============================================================================
//  ICriterio.cs
//  Función que toma una colección de elementos filtrados y produce líneas
//  de medición + cantidad total para el registro ~M del BC3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public interface ICriterio
    {
        IReadOnlyList<LineaMedicion> GenerarLineas(IEnumerable<Element> elementos, Document doc);
        string Unidad { get; }
        string Descripcion { get; }
        string Tipo { get; }
    }

    public sealed class CriterioLongitudTotal : ICriterio
    {
        public string Unidad => "m";
        public string Descripcion => "Longitud total (m)";
        public string Tipo => "longitudTotal";

        public IReadOnlyList<LineaMedicion> GenerarLineas(IEnumerable<Element> elementos, Document doc)
        {
            var lineas = new List<LineaMedicion>();
            foreach (var e in elementos)
            {
                if (e is Pipe pipe)
                {
                    var longitudFt = pipe.LookupParameter("Length")?.AsDouble()
                                    ?? pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble()
                                    ?? 0.0;
                    var longitudM = UnitUtils.ConvertFromInternalUnits(longitudFt, UnitTypeId.Meters);
                    if (longitudM <= 0) continue;
                    lineas.Add(new LineaMedicion
                    {
                        Comentario = ConstruirComentario(pipe),
                        Unidades = 1m,
                        Longitud = (decimal)Math.Round(longitudM, 3),
                        ElementoOrigen = pipe.Id
                    });
                }
            }
            return lineas;
        }

        private static string ConstruirComentario(Pipe p)
        {
            var nivel = p.ReferenceLevel?.Name ?? "";
            var mark = p.LookupParameter("Mark")?.AsString();
            var comentario = string.IsNullOrEmpty(mark) ? $"Pipe Id {p.Id.Value}" : $"Pipe {mark}";
            if (!string.IsNullOrEmpty(nivel)) comentario += $" · {nivel}";
            return comentario;
        }
    }

    public sealed class CriterioContar : ICriterio
    {
        public bool Agrupado { get; init; } = false;
        public string Unidad => "ud";
        public string Descripcion => Agrupado ? "Comptar (agrupat)" : "Comptar elements";
        public string Tipo => "contar";

        public IReadOnlyList<LineaMedicion> GenerarLineas(IEnumerable<Element> elementos, Document doc)
        {
            var lista = elementos.ToList();
            if (lista.Count == 0) return new List<LineaMedicion>();

            if (Agrupado)
            {
                return new List<LineaMedicion>
                {
                    new LineaMedicion
                    {
                        Comentario = $"Total elements ({lista.Count})",
                        Unidades = lista.Count,
                        ElementoOrigen = ElementId.InvalidElementId
                    }
                };
            }
            return lista.Select(e => new LineaMedicion
            {
                Comentario = ConstruirComentario(e),
                Unidades = 1m,
                ElementoOrigen = e.Id
            }).ToList();
        }

        private static string ConstruirComentario(Element e)
        {
            var name = e.Name ?? e.Category?.Name ?? "Element";
            var mark = e.LookupParameter("Mark")?.AsString();
            return string.IsNullOrEmpty(mark) ? name : $"{name} {mark}";
        }
    }

    // P3 (futuro):
    // public sealed class CriterioFormula : ICriterio { ... }
}
