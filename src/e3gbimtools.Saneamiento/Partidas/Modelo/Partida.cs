// ============================================================================
//  Partida.cs
//  Una unidad de obra del catálogo BC3. Inmutable.
//
//  CatalogoPartidas ahora tiene Id (Guid estable) y Etiqueta (legible) para
//  que múltiples catálogos puedan convivir en una Plantilla.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public sealed class Partida
    {
        public string Codigo       { get; init; }
        public string Descripcion  { get; init; }
        public string Unidad       { get; init; }
        public decimal Precio      { get; init; }
        public string TextoPliego  { get; init; }
        public string Origen       { get; init; }   // "BEDEC 2025", "Custom"...

        public Partida() { }

        public bool EsValida() =>
            !string.IsNullOrWhiteSpace(Codigo) &&
            !string.IsNullOrWhiteSpace(Unidad);

        public override string ToString() =>
            $"{Codigo} ({Unidad}) — {Descripcion}";
    }

    /// <summary>
    /// Catálogo de partidas. Identificable por Id y mostrable en UI con Etiqueta.
    /// Puede convivir con otros catálogos dentro de la misma Plantilla.
    /// </summary>
    public sealed class CatalogoPartidas
    {
        /// <summary>Identificador estable. Generado al importar; nunca cambia.</summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>Texto legible para la UI: "BEDEC 2025", "Plantilla E3G 2026".</summary>
        public string Etiqueta { get; init; }

        public IReadOnlyList<Partida> Partidas { get; init; } = new List<Partida>();
        public string OrigenArchivo            { get; init; }
        public string NombreCatalogo           { get; init; }
        public DateTime FechaImportacion       { get; init; } = DateTime.Now;

        private Dictionary<string, Partida> _porCodigo;

        public Partida Buscar(string codigo)
        {
            if (string.IsNullOrEmpty(codigo)) return null;
            if (_porCodigo == null)
                _porCodigo = Partidas.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
            _porCodigo.TryGetValue(codigo, out var p);
            return p;
        }

        public IEnumerable<Partida> Filtrar(string termino, int max = 50)
        {
            if (string.IsNullOrWhiteSpace(termino))
                return Partidas.Take(max);
            var t = termino.Trim();
            return Partidas
                .Where(p =>
                    p.Codigo.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (p.Descripcion ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(max);
        }

        public static CatalogoPartidas Vacio() => new CatalogoPartidas
        {
            Id = Guid.NewGuid(),
            Etiqueta = "(buit)",
            Partidas = new List<Partida>(),
            NombreCatalogo = "(sense catàleg)",
            OrigenArchivo = null
        };
    }

    /// <summary>
    /// Resultado de búsqueda multi-catálogo: una partida + el catálogo de origen.
    /// Lo que devuelve la búsqueda del modal de regla cuando hay varios catálogos
    /// cargados — permite mostrar el badge de procedencia y resolver ambigüedades
    /// cuando dos catálogos comparten un código.
    /// </summary>
    public sealed class ResultadoBusquedaPartida
    {
        public Partida Partida { get; init; }
        public CatalogoPartidas Catalogo { get; init; }
        public bool EsPrimario { get; init; }   // viene del catálogo marcado como primario
    }
}
