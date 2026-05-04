// ============================================================================
//  Regla.cs — la unidad mínima del sistema P2/P3.
//
//  Multi-catálogo:
//   - PartidaCodigo: el código BC3 (ED15B871, E3G-SAN-04...)
//   - PartidaCatalogoId: id del catálogo del que vino la partida — desambigua
//     cuando dos catálogos comparten un código. Si vale Guid.Empty, el motor
//     busca primero en el primario.
// ============================================================================

using System;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public sealed class Regla
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Nombre { get; init; }

        public CategoriaSistema Categoria { get; init; }
        public ISubFiltro SubFiltro { get; init; } = new SubFiltroNinguno();
        public ICriterio Criterio { get; init; } = new CriterioLongitudTotal();

        /// <summary>Código de la partida en su catálogo de origen.</summary>
        public string PartidaCodigo { get; init; }

        /// <summary>
        /// Id del catálogo del que se eligió la partida. Permite desambiguar si
        /// dos catálogos contienen el mismo código. Guid.Empty significa "no
        /// fijado — resolver por orden de búsqueda (primario primero)".
        /// </summary>
        public Guid PartidaCatalogoId { get; init; } = Guid.Empty;

        public string CapituloDestino { get; init; }
        public bool Activa { get; init; } = true;
        public string Notas { get; init; }

        public bool EsValida() =>
            !string.IsNullOrWhiteSpace(Nombre) &&
            !string.IsNullOrWhiteSpace(PartidaCodigo);

        public override string ToString() =>
            $"{Categoria}: {Nombre} → {PartidaCodigo} [{(Activa ? "✓" : "·")}]";
    }
}
