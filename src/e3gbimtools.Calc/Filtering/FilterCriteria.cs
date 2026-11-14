// ============================================================================
//  FilterCriteria.cs
//  Modelo inmutable de criterios de filtrado.  El FilterService consume una
//  instancia de esta clase y la traduce a un predicado precompilado.
//
//  Diseño:
//    - Inmutable post-construcción → comparable, cacheable, hilo-seguro.
//    - Combinación lógica AND entre campos: TODOS los criterios activos
//      deben cumplirse para que la fila aparezca.  Dentro de un campo
//      multi-valor (ej. Levels, Severities) la combinación es OR.
//    - Empty() == no filtra nada → atajo rápido en el FilterService.
//
//  Para la API pública (rendimiento), las colecciones se exponen como
//  IReadOnlyList<> ya construidas; el caller no las muta.
// ============================================================================
using System.Collections.Generic;

namespace e3gbimtools.Calc.Filtering
{
    public sealed class FilterCriteria
    {
        /// <summary>Texto libre — busca en NodePath, Status, Size, etc.</summary>
        public string FreeText { get; }

        /// <summary>Índice BFS exacto o rango "5-10".  -1 = sin filtro.</summary>
        public int BfsFrom { get; }
        public int BfsTo   { get; }

        /// <summary>ElementId exacto (-1 = ignorar).</summary>
        public long ElementId { get; }

        /// <summary>DN exacto (sin "DN ").  0 = ignorar.</summary>
        public int DnExact { get; }

        /// <summary>Niveles a incluir (case-insensitive). Vacío = todos.</summary>
        public IReadOnlyList<string> Levels { get; }

        /// <summary>Severidades visibles. Vacío = todas.</summary>
        public IReadOnlyList<string> Severities { get; }

        /// <summary>Si true, solo los tramos del camino crítico.</summary>
        public bool OnlyCriticalPath { get; }

        /// <summary>Si true, ocultar las filas de accesorio (IsFitting).</summary>
        public bool HideFittings { get; }

        /// <summary>Si true, mostrar solo errores y avisos (oculta OK).</summary>
        public bool OnlyIssues { get; }

        public FilterCriteria(
            string freeText = null,
            int bfsFrom = -1,
            int bfsTo = -1,
            long elementId = -1,
            int dnExact = 0,
            IReadOnlyList<string> levels = null,
            IReadOnlyList<string> severities = null,
            bool onlyCriticalPath = false,
            bool hideFittings = false,
            bool onlyIssues = false)
        {
            FreeText         = freeText ?? "";
            BfsFrom          = bfsFrom;
            BfsTo            = bfsTo;
            ElementId        = elementId;
            DnExact          = dnExact;
            Levels           = levels     ?? System.Array.Empty<string>();
            Severities       = severities ?? System.Array.Empty<string>();
            OnlyCriticalPath = onlyCriticalPath;
            HideFittings     = hideFittings;
            OnlyIssues       = onlyIssues;
        }

        public static FilterCriteria Empty { get; } = new FilterCriteria();

        public bool IsEmpty =>
            string.IsNullOrEmpty(FreeText)
            && BfsFrom < 0 && BfsTo < 0
            && ElementId < 0
            && DnExact == 0
            && Levels.Count == 0
            && Severities.Count == 0
            && !OnlyCriticalPath && !HideFittings && !OnlyIssues;
    }
}
