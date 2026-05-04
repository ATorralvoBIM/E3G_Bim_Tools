// ============================================================================
//  ICriterio.cs
//  Función que toma una colección de elementos filtrados y produce líneas
//  de medición + cantidad total para el registro ~M del BC3.
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

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
                        Comentario = ConstruirComentario(pipe, doc),
                        Unidades = 1m,
                        Longitud = (decimal)Math.Round(longitudM, 3),
                        ElementoOrigen = pipe.Id
                    });
                }
            }
            return lineas;
        }

        /// <summary>
        /// Comentari amb prefix d'agrupació perquè TCQ/Presto agrupin visualment
        /// les línies quan ordenen alfabèticament.
        ///
        /// PRIORITAT del prefix:
        ///   1. Grup oficial del panell de Sanejament (Bajantes/Colectores/
        ///      Interior vivendes/Aparcamiento) si l'usuari ha pulsat "Medir".
        ///   2. Fallback per pipes no inclosos al panell: Sistema MEP + Nivell.
        ///
        /// Format: "[Bajantes · P01.A] Pipe B01-1"
        ///         "[Interior vivendes · P02.A] Pipe Id 12345"
        ///         "[SAN-Fecals B-01 · P01.A] Pipe B01-1"  (fallback)
        /// </summary>
        private static string ConstruirComentario(Pipe p, Document doc)
        {
            var mark = p.LookupParameter("Mark")?.AsString();
            var idEl = p.Id != null ? p.Id.Value.ToString() : "?";
            var ident = !string.IsNullOrEmpty(mark) ? $"Pipe {mark}" : $"Pipe Id {idEl}";

            var nivel = p.ReferenceLevel?.Name ?? "";

            var grupoPanel = E3G.Saneamiento.Protocol.AgrupadorMedicions.ObtenerGrupo(p, doc);

            var partsPrefix = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(grupoPanel))
            {
                partsPrefix.Add(grupoPanel);
                if (!string.IsNullOrEmpty(nivel)) partsPrefix.Add(nivel);
            }
            else
            {
                string sistema = "";
                try { var sys = p.MEPSystem; if (sys != null) sistema = sys.Name ?? ""; }
                catch { }

                if (!string.IsNullOrEmpty(sistema)) partsPrefix.Add(sistema);
                if (!string.IsNullOrEmpty(nivel)) partsPrefix.Add(nivel);
            }

            return partsPrefix.Count > 0
                ? $"[{string.Join(" · ", partsPrefix)}] {ident}"
                : ident;
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
                Comentario = ConstruirComentario(e, doc),
                Unidades = 1m,
                ElementoOrigen = e.Id
            }).ToList();
        }

        private string ConstruirComentario(Element element, Document doc)
        {
            try
            {
                var grup = E3G.Saneamiento.Protocol.AgrupadorMedicions.ObtenerGrupo(element, doc);
                var nivell = element.LookupParameter("Reference Level")
                          ?? element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                var nivellNom = nivell?.AsValueString() ?? "—";
                var mark = element.LookupParameter("Mark")?.AsString();
                var id = string.IsNullOrEmpty(mark) ? element.Id.IntegerValue.ToString() : mark;
                return $"[{grup} · {nivellNom}] Pipe {id}";
            }
            catch { return $"Pipe {element?.Id?.IntegerValue}"; }
        }
    }

    // P3 (futuro):
    // public sealed class CriterioFormula : ICriterio { ... }
}
