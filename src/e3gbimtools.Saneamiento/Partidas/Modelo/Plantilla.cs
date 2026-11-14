// ============================================================================
//  Plantilla.cs — contenedor de reglas + N catálogos asociados.
//
//  Multi-catálogo:
//   - Catalogos: lista de catálogos cargados simultáneamente (BEDEC + plantilla
//     E3G + custom...).
//   - CatalogoPrimarioId: cuál de ellos sale primero en las búsquedas.
//   - BuscarPartida(codigo, catalogoId?): resuelve cruzando catálogos, con
//     desambiguación por catalogoId si dos catálogos comparten un código.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public sealed class Plantilla
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Nombre { get; init; } = "Per defecte";
        public IReadOnlyList<Regla> Reglas { get; init; } = new List<Regla>();

        /// <summary>
        /// Catálogos cargados en el proyecto. Cero o más. Pueden convivir
        /// (p.ej. BEDEC + plantilla del estudio).
        /// </summary>
        public IReadOnlyList<CatalogoPartidas> Catalogos { get; init; }
            = new List<CatalogoPartidas>();

        /// <summary>
        /// Id del catálogo marcado como primario. Sus partidas salen primero
        /// en la búsqueda y son las que se sugieren por defecto en autocompletado.
        /// Empty si no hay catálogos cargados.
        /// </summary>
        public Guid CatalogoPrimarioId { get; init; } = Guid.Empty;

        public string Version { get; init; } = "1.0";
        public DateTime FechaModificacion { get; init; } = DateTime.Now;

        // ─── Constructores estáticos ────────────────────────────────────────
        public static Plantilla Vacia() => new Plantilla
        {
            Reglas = new List<Regla>(),
            Catalogos = new List<CatalogoPartidas>(),
            CatalogoPrimarioId = Guid.Empty,
            Nombre = "Per defecte"
        };

        // ─── Inmutabilidad: copias con cambios puntuales ────────────────────
        public Plantilla ConReglas(IReadOnlyList<Regla> nuevasReglas) => new Plantilla
        {
            Id = this.Id,
            Nombre = this.Nombre,
            Reglas = nuevasReglas,
            Catalogos = this.Catalogos,
            CatalogoPrimarioId = this.CatalogoPrimarioId,
            Version = this.Version,
            FechaModificacion = DateTime.Now
        };

        public Plantilla ConCatalogos(
            IReadOnlyList<CatalogoPartidas> nuevosCatalogos,
            Guid? nuevoPrimarioId = null) => new Plantilla
        {
            Id = this.Id,
            Nombre = this.Nombre,
            Reglas = this.Reglas,
            Catalogos = nuevosCatalogos,
            CatalogoPrimarioId = nuevoPrimarioId ?? AutoseleccionarPrimario(nuevosCatalogos),
            Version = this.Version,
            FechaModificacion = DateTime.Now
        };

        public Plantilla AnadirCatalogo(CatalogoPartidas nuevoCatalogo)
        {
            var lista = Catalogos.Append(nuevoCatalogo).ToList();
            // Si no hay primario aún, este pasa a serlo.
            var primario = CatalogoPrimarioId == Guid.Empty
                ? nuevoCatalogo.Id
                : CatalogoPrimarioId;
            return ConCatalogos(lista, primario);
        }

        public Plantilla EliminarCatalogo(Guid catalogoId)
        {
            var lista = Catalogos.Where(c => c.Id != catalogoId).ToList();
            var primario = CatalogoPrimarioId == catalogoId
                ? AutoseleccionarPrimario(lista)
                : CatalogoPrimarioId;
            return ConCatalogos(lista, primario);
        }

        public Plantilla MarcarPrimario(Guid catalogoId)
        {
            if (Catalogos.All(c => c.Id != catalogoId)) return this;
            return new Plantilla
            {
                Id = this.Id,
                Nombre = this.Nombre,
                Reglas = this.Reglas,
                Catalogos = this.Catalogos,
                CatalogoPrimarioId = catalogoId,
                Version = this.Version,
                FechaModificacion = DateTime.Now
            };
        }

        // ─── Accessors útiles ───────────────────────────────────────────────
        public CatalogoPartidas CatalogoPrimario =>
            Catalogos.FirstOrDefault(c => c.Id == CatalogoPrimarioId)
            ?? Catalogos.FirstOrDefault();

        public bool TieneCatalogos => Catalogos != null && Catalogos.Count > 0;

        public int TotalPartidasCargadas => Catalogos?.Sum(c => c.Partidas.Count) ?? 0;

        // ─── Búsqueda multi-catálogo ────────────────────────────────────────

        /// <summary>
        /// Resuelve una partida por código. Si <paramref name="catalogoId"/> está
        /// presente, busca exclusivamente en ese catálogo (clave para reglas que
        /// han fijado el catálogo de origen y deben ser deterministas). Si no se
        /// pasa, intenta primero el primario y luego el resto.
        /// </summary>
        public Partida BuscarPartida(string codigo, Guid? catalogoId = null)
        {
            if (string.IsNullOrEmpty(codigo)) return null;

            // Caso desambiguado: el catalogoId está fijado.
            if (catalogoId.HasValue && catalogoId.Value != Guid.Empty)
            {
                var c = Catalogos.FirstOrDefault(x => x.Id == catalogoId.Value);
                return c?.Buscar(codigo);
            }

            // Sin catalogoId — intentar primero el primario, luego el resto.
            var primario = CatalogoPrimario;
            var p = primario?.Buscar(codigo);
            if (p != null) return p;

            foreach (var cat in Catalogos.Where(x => x.Id != CatalogoPrimarioId))
            {
                var hit = cat.Buscar(codigo);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Búsqueda libre por substring en código o descripción, cruzando todos
        /// los catálogos cargados. Los resultados del catálogo primario salen
        /// primero, luego el resto en orden de carga.
        /// </summary>
        public IEnumerable<ResultadoBusquedaPartida> BuscarPartidaPorTexto(
            string termino, int maxPorCatalogo = 30)
        {
            var ordenado = OrdenarConPrimarioPrimero();

            foreach (var catalogo in ordenado)
            {
                var esPrimario = catalogo.Id == CatalogoPrimarioId;
                foreach (var p in catalogo.Filtrar(termino, maxPorCatalogo))
                {
                    yield return new ResultadoBusquedaPartida
                    {
                        Partida = p,
                        Catalogo = catalogo,
                        EsPrimario = esPrimario
                    };
                }
            }
        }

        /// <summary>
        /// Localiza en qué catálogo está una partida dada su código. Útil para
        /// la UI que pinta el badge de procedencia.
        /// </summary>
        public CatalogoPartidas LocalizarCatalogoDePartida(string codigo, Guid? catalogoId = null)
        {
            if (catalogoId.HasValue && catalogoId.Value != Guid.Empty)
                return Catalogos.FirstOrDefault(c => c.Id == catalogoId.Value);

            // Sin id explícito — devolver el primer catálogo que tenga el código.
            return Catalogos.FirstOrDefault(c => c.Buscar(codigo) != null);
        }

        // ─── Ayudas existentes ──────────────────────────────────────────────
        public IEnumerable<IGrouping<CategoriaSistema, Regla>> ReglasActivasAgrupadas() =>
            Reglas.Where(r => r.Activa).GroupBy(r => r.Categoria);

        public IEnumerable<Regla> ReglasConPartidaInvalida() =>
            Reglas.Where(r => BuscarPartida(r.PartidaCodigo, r.PartidaCatalogoId) == null);

        // ─── Privados ───────────────────────────────────────────────────────
        private IEnumerable<CatalogoPartidas> OrdenarConPrimarioPrimero()
        {
            if (Catalogos.Count == 0) yield break;
            var primario = Catalogos.FirstOrDefault(c => c.Id == CatalogoPrimarioId);
            if (primario != null) yield return primario;
            foreach (var c in Catalogos)
                if (c.Id != CatalogoPrimarioId) yield return c;
        }

        private static Guid AutoseleccionarPrimario(IReadOnlyList<CatalogoPartidas> lista) =>
            lista != null && lista.Count > 0 ? lista[0].Id : Guid.Empty;
    }
}
