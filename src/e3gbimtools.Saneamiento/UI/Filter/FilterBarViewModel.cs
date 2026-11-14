// ============================================================================
//  FilterBarViewModel.cs   —   FASE 4: Filtros + Búsqueda
//
//  ViewModel MVVM para la barra de búsqueda y filtros rápidos.
//
//  Ciclo de vida:
//    1. El usuario escribe en el SearchBox → SearchText cambia.
//    2. Un DispatcherTimer (debounce 350 ms) evita refiltrara en cada
//       pulsación de tecla → dispara CriteriaChanged solo cuando el usuario
//       deja de escribir.
//    3. Los chips de filtro rápido (OnlyIssues, OnlyCriticalPath, HideFittings)
//       disparan CriteriaChanged inmediatamente (no requieren debounce).
//    4. MatchCount / TotalCount exponen el resumen "N / M tramos" al View.
//    5. ClearAllCommand resetea todos los campos y emite un criterio vacío.
//
//  Thread safety: todos los Set<> ocurren en el hilo de la UI (WPF dispatcher).
// ============================================================================
using System;
using System.Windows.Threading;
using E3G.Saneamiento.Core.Filtering;
using E3G.Saneamiento.UI.Mvvm;

namespace E3G.Saneamiento.UI.Filter
{
    public sealed class FilterBarViewModel : BindableBase, IDisposable
    {
        // ── Debounce ──────────────────────────────────────────────────────
        private readonly DispatcherTimer _debounce;
        private const int DebounceMs = 350;

        // ── Backing fields ───────────────────────────────────────────────
        private string _searchText        = "";
        private bool   _onlyIssues;
        private bool   _onlyCriticalPath;
        private bool   _hideFittings;
        private int    _matchCount;
        private int    _totalCount;

        // ── Evento público ───────────────────────────────────────────────
        /// <summary>Se dispara cada vez que los criterios de filtrado cambian.</summary>
        public event Action<FilterCriteria> CriteriaChanged;

        // ── Constructor ──────────────────────────────────────────────────
        public FilterBarViewModel()
        {
            _debounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMs)
            };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                EmitCriteria();
            };

            ClearAllCommand = new RelayCommand(ClearAll);
        }

        // ── Propiedades observables ───────────────────────────────────────

        /// <summary>Texto libre en el SearchBox. Arranca el debounce timer.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value ?? ""))
                {
                    Raise(nameof(HasActiveFilter));
                    _debounce.Stop();
                    _debounce.Start();
                }
            }
        }

        /// <summary>Chip: mostrar solo filas con Warning/Error.</summary>
        public bool OnlyIssues
        {
            get => _onlyIssues;
            set { if (Set(ref _onlyIssues, value)) { Raise(nameof(HasActiveFilter)); EmitCriteria(); } }
        }

        /// <summary>Chip: mostrar solo el camino crítico.</summary>
        public bool OnlyCriticalPath
        {
            get => _onlyCriticalPath;
            set { if (Set(ref _onlyCriticalPath, value)) { Raise(nameof(HasActiveFilter)); EmitCriteria(); } }
        }

        /// <summary>Chip: ocultar filas de accesorio (IsFitting).</summary>
        public bool HideFittings
        {
            get => _hideFittings;
            set { if (Set(ref _hideFittings, value)) { Raise(nameof(HasActiveFilter)); EmitCriteria(); } }
        }

        /// <summary>Filas que pasan el filtro actual.</summary>
        public int MatchCount
        {
            get => _matchCount;
            set => Set(ref _matchCount, value);
        }

        /// <summary>Total de filas sin filtrar.</summary>
        public int TotalCount
        {
            get => _totalCount;
            set => Set(ref _totalCount, value);
        }

        /// <summary>True si hay algún filtro activo.</summary>
        public bool HasActiveFilter =>
            !string.IsNullOrEmpty(_searchText) || _onlyIssues || _onlyCriticalPath || _hideFittings;

        /// <summary>Etiqueta del contador "12 / 48".</summary>
        public string MatchLabel =>
            HasActiveFilter
                ? $"{_matchCount} / {_totalCount}"
                : (_totalCount > 0 ? _totalCount.ToString() : "");

        // ── Comandos ─────────────────────────────────────────────────────

        public RelayCommand ClearAllCommand { get; }

        // ── API pública ──────────────────────────────────────────────────

        /// <summary>Construye el FilterCriteria actual combinando el texto libre
        /// (parseado por SearchService) y el estado de los chips.</summary>
        public FilterCriteria BuildCriteria()
        {
            var base_ = SearchService.Parse(_searchText);
            return new FilterCriteria(
                freeText:         base_.FreeText,
                bfsFrom:          base_.BfsFrom,
                bfsTo:            base_.BfsTo,
                elementId:        base_.ElementId,
                dnExact:          base_.DnExact,
                levels:           base_.Levels,
                severities:       base_.Severities,
                onlyCriticalPath: base_.OnlyCriticalPath || _onlyCriticalPath,
                hideFittings:     base_.HideFittings     || _hideFittings,
                onlyIssues:       base_.OnlyIssues       || _onlyIssues);
        }

        /// <summary>Actualiza los contadores tras la evaluación en CalcWindow.</summary>
        public void UpdateCounters(int matchCount, int totalCount)
        {
            MatchCount = matchCount;
            TotalCount = totalCount;
            Raise(nameof(MatchLabel));
        }

        // ── Internos ─────────────────────────────────────────────────────

        private void EmitCriteria() => CriteriaChanged?.Invoke(BuildCriteria());

        private void ClearAll()
        {
            _debounce.Stop();
            _searchText       = "";
            _onlyIssues       = false;
            _onlyCriticalPath = false;
            _hideFittings     = false;
            Raise(nameof(SearchText));
            Raise(nameof(OnlyIssues));
            Raise(nameof(OnlyCriticalPath));
            Raise(nameof(HideFittings));
            Raise(nameof(HasActiveFilter));
            Raise(nameof(MatchLabel));
            EmitCriteria();
        }

        public void Dispose() => _debounce?.Stop();
    }
}
