// ============================================================================
//  EvaluadorReglas.cs
//  Aplica una Plantilla sobre el documento Revit y devuelve mediciones
//  agrupadas por partida.
//
//  Multi-catálogo: la resolución de la partida usa Plantilla.BuscarPartida
//  con la pareja (PartidaCodigo, PartidaCatalogoId), de modo que reglas que
//  apuntan a partidas de catálogos distintos conviven en el mismo proyecto.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Motor
{
    public sealed class EvaluadorReglas
    {
        private readonly ClasificacionSistemasService _clasif;

        public EvaluadorReglas(ClasificacionSistemasService clasif)
        {
            _clasif = clasif ?? throw new ArgumentNullException(nameof(clasif));
        }

        public IReadOnlyList<MedicionPorPartida> Evaluar(
            Plantilla plantilla,
            Document doc,
            List<string> warnings = null)
        {
            if (plantilla == null) throw new ArgumentNullException(nameof(plantilla));
            if (doc == null)        throw new ArgumentNullException(nameof(doc));
            warnings ??= new List<string>();

            // Cargar el mapeo PipingSystemType → CategoriaSistema una sola vez.
            // ClasificacionSistemasService.Leer() lee del .rvt el diccionario completo.
            if (!_clasif.IsAvailable(doc))
            {
                warnings.Add("La classificació de sistemes no està disponible al projecte. " +
                             "Vés a Ajustes › Classificació de sistemes per configurar-la.");
                return new List<MedicionPorPartida>();
            }
            var mapaCategorias = _clasif.Leer(doc);

            var candidatos = RecolectarElementosCandidatos(doc).ToList();
            var porCategoria = AgruparPorCategoria(candidatos, doc, mapaCategorias);
            var resultado = new Dictionary<string, MedicionAcumulador>();

            foreach (var regla in plantilla.Reglas.Where(r => r.Activa))
            {
                // Resolución multi-catálogo:
                //  - Si la regla fija PartidaCatalogoId, busca SÓLO en ese catálogo.
                //  - Si no, intenta primario primero y luego el resto.
                var partida = plantilla.BuscarPartida(regla.PartidaCodigo, regla.PartidaCatalogoId);
                if (partida == null)
                {
                    var detalle = regla.PartidaCatalogoId == Guid.Empty
                        ? $"al catàleg primari ni a la resta"
                        : $"al catàleg fixat (Id {regla.PartidaCatalogoId})";
                    warnings.Add(
                        $"La regla \"{regla.Nombre}\" apunta a la partida {regla.PartidaCodigo}, " +
                        $"que no existeix {detalle}. S'ignorarà.");
                    continue;
                }

                if (!porCategoria.TryGetValue(regla.Categoria, out var listaCategoria))
                    continue;

                var filtrados = listaCategoria
                    .Where(e => SeguraAplicar(regla.SubFiltro, e, doc, warnings))
                    .ToList();
                if (filtrados.Count == 0) continue;

                IReadOnlyList<LineaMedicion> lineas;
                try { lineas = regla.Criterio.GenerarLineas(filtrados, doc); }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"La regla \"{regla.Nombre}\" ha fallat en calcular el criteri: {ex.Message}. S'ignora.");
                    Debug.WriteLine($"[EvaluadorReglas] {ex}");
                    continue;
                }
                if (lineas == null || lineas.Count == 0) continue;

                // Agrupar por (capítulo, código) — una misma partida puede recibir
                // contribuciones de varias reglas y las sumamos. Cuando la partida
                // viene de catálogos distintos para el mismo código, la entrada
                // de la primera regla "gana" como representante (es coherente con
                // la decisión del usuario que fijó el catálogo en cada regla).
                var key = $"{regla.CapituloDestino}|{partida.Codigo}";
                if (!resultado.TryGetValue(key, out var acc))
                {
                    acc = new MedicionAcumulador(partida, regla.CapituloDestino);
                    resultado[key] = acc;
                }
                acc.Anadir(lineas);
            }

            return resultado.Values
                .Select(a => a.Cerrar())
                .OrderBy(m => m.CapituloDestino)
                .ThenBy(m => m.Partida.Codigo)
                .ToList();
        }

        private static IEnumerable<Element> RecolectarElementosCandidatos(Document doc)
        {
            var collectors = new[]
            {
                new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)    .WhereElementIsNotElementType(),
                new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting)  .WhereElementIsNotElementType(),
                new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeAccessory).WhereElementIsNotElementType(),
            };
            foreach (var c in collectors)
                foreach (var e in c)
                    yield return e;
        }

        private static Dictionary<CategoriaSistema, List<Element>> AgruparPorCategoria(
            IEnumerable<Element> elementos,
            Document doc,
            Dictionary<ElementId, CategoriaSistema> mapaCategorias)
        {
            var dict = new Dictionary<CategoriaSistema, List<Element>>();
            foreach (var e in elementos)
            {
                var sysType = ResolverPipingSystemType(e, doc);
                if (sysType == null) continue;

                // Lookup directo en el diccionario que ya cargamos arriba.
                if (!mapaCategorias.TryGetValue(sysType.Id, out var categoria))
                    continue;
                if (categoria == CategoriaSistema.SinAsignar ||
                    categoria == CategoriaSistema.Ignorar)
                    continue;

                if (!dict.TryGetValue(categoria, out var lista))
                    dict[categoria] = lista = new List<Element>();
                lista.Add(e);
            }
            return dict;
        }

        private static PipingSystemType ResolverPipingSystemType(Element e, Document doc)
        {
            if (e is Pipe pipe)
            {
                var sys = pipe.MEPSystem as PipingSystem;
                if (sys?.GetTypeId() != null)
                    return doc.GetElement(sys.GetTypeId()) as PipingSystemType;
            }
            var p = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
            if (p != null)
            {
                var id = p.AsElementId();
                if (id != ElementId.InvalidElementId)
                    return doc.GetElement(id) as PipingSystemType;
            }
            return null;
        }

        private static bool SeguraAplicar(ISubFiltro f, Element e, Document doc, List<string> warnings)
        {
            try { return f.Aplica(e, doc); }
            catch (Exception ex)
            {
                warnings.Add($"Sub-filtre {f?.GetType().Name ?? "?"} ha llançat: {ex.Message}");
                return false;
            }
        }
    }
}
