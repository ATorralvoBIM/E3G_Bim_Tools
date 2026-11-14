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

            // Cargar el mapeo PipingSystemType → CategoriaSistema.
            // Prioridad:
            //   1. Caché en memoria (lo que el usuario ha tocado en Ajustes durante esta sesión).
            //   2. Si no hay caché, leer del .rvt vía ExtensibleStorage.
            // La caché es necesaria porque el DataGrid de Ajustes no puede escribir
            // a ExtensibleStorage desde una Page modeless (Transaction.Start falla).
            if (!_clasif.IsAvailable(doc))
            {
                warnings.Add("La classificació de sistemes no està disponible al projecte. " +
                             "Vés a Ajustes › Classificació de sistemes per configurar-la.");
                return new List<MedicionPorPartida>();
            }
            // Cadena de prioritat:
            //  1. Caché en memòria (l'usuari ha tocat el panell de Classificació en aquesta sessió)
            //  2. Lectura del .rvt (classificació guardada explícitament)
            //  3. LeerConSugerencias: classificació guardada + suggeriments automàtics
            //     (heurística per noms de sistema). Així el motor sempre té dades
            //     per categoritzar encara que l'usuari no hagi obert el panell.
            var mapaCategorias = E3G.Saneamiento.Presupuesto.Storage.ClasificacionSession.AsignacionesActuales;
            if (mapaCategorias == null || mapaCategorias.Count == 0)
            {
                mapaCategorias = _clasif.Leer(doc);
                if (mapaCategorias == null || mapaCategorias.Count == 0)
                {
                    try { mapaCategorias = _clasif.LeerConSugerencias(doc); }
                    catch { /* no totes les versions del servei tenen aquest mètode */ }
                    if (mapaCategorias != null && mapaCategorias.Count > 0)
                        warnings.Add($"ℹ Classificació automàtica aplicada (suggeriments per nom de sistema). " +
                                     $"Per controlar-la manualment, vés a Ajustes › Classificació de sistemes.");
                }
            }
            if (mapaCategorias == null || mapaCategorias.Count == 0)
            {
                warnings.Add("⚠ No s'ha pogut classificar cap PipingSystemType ni amb suggeriments automàtics. " +
                             "Vés a Ajustes › Classificació de sistemes i tria una categoria manualment.");
                return new List<MedicionPorPartida>();
            }

            var candidatos = RecolectarElementosCandidatos(doc).ToList();
            var porCategoria = AgruparPorCategoria(candidatos, doc, mapaCategorias);
            var resultado = new Dictionary<string, MedicionAcumulador>();

            // ─── Diagnòstic transparent ─────────────────────────────────────
            warnings.Add($"📊 Elements de canonada al model: {candidatos.Count}");
            if (porCategoria.Count == 0)
            {
                warnings.Add("⚠ Cap element està classificat com a categoria E3G. " +
                             "Revisa Ajustes › Classificació de sistemes — assegura't " +
                             "que els PipingSystemType del projecte estiguin assignats " +
                             "a Sanejament-Fecals/Pluvials/Grises, etc.");
            }
            else
            {
                foreach (var kv in porCategoria.OrderBy(x => x.Key.ToString()))
                    warnings.Add($"  · {kv.Key}: {kv.Value.Count} elements classificats");
            }
            warnings.Add($"📋 Regles actives: {plantilla.Reglas.Count(r => r.Activa)}");

            foreach (var regla in plantilla.Reglas.Where(r => r.Activa))
            {
                var partida = plantilla.BuscarPartida(regla.PartidaCodigo, regla.PartidaCatalogoId);
                if (partida == null)
                {
                    warnings.Add(
                        $"  ✗ Regla \"{regla.Nombre}\": partida {regla.PartidaCodigo} NO trobada al catàleg. Ignorada.");
                    continue;
                }

                if (!porCategoria.TryGetValue(regla.Categoria, out var listaCategoria))
                {
                    warnings.Add(
                        $"  ⊘ Regla \"{regla.Nombre}\": cap element classificat com a {regla.Categoria}. Saltada.");
                    continue;
                }

                var filtrados = listaCategoria
                    .Where(e => SeguraAplicar(regla.SubFiltro, e, doc, warnings))
                    .ToList();
                if (filtrados.Count == 0)
                {
                    warnings.Add(
                        $"  ⊘ Regla \"{regla.Nombre}\" ({regla.SubFiltro.Descripcion}): 0 elements de {listaCategoria.Count} compleixen el sub-filtre.");
                    continue;
                }

                IReadOnlyList<LineaMedicion> lineas;
                try { lineas = regla.Criterio.GenerarLineas(filtrados, doc); }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"  ✗ Regla \"{regla.Nombre}\" — error en criteri: {ex.Message}");
                    Debug.WriteLine($"[EvaluadorReglas] {ex}");
                    continue;
                }
                if (lineas == null || lineas.Count == 0)
                {
                    warnings.Add(
                        $"  ⊘ Regla \"{regla.Nombre}\": criteri ha generat 0 línies (possible: pipes amb longitud=0).");
                    continue;
                }
                warnings.Add(
                    $"  ✓ Regla \"{regla.Nombre}\": {filtrados.Count} elements → {lineas.Count} línies → {partida.Codigo}");

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
