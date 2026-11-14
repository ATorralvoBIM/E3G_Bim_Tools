// ============================================================================
//  SchemeFilterRenderer.cs   —   FASE 4: Atenuación del esquema
//
//  Aplica atenuación de opacidad (dimming) a los glifos del esquema
//  según el conjunto de filas visibles devuelto por FilterService.
//
//  Diseño:
//    · Recibe dos diccionarios: glifos (FrameworkElement por ResultRow) y
//      líneas (Line por ResultRow).  Ambos los llena BuildSchematic().
//    · Apply(visible): elements en `visible` → opacidad 1.0 animada;
//                      el resto → opacidad DimOpacity (0.12) animada.
//    · Reset():        devuelve TODOS los elementos a opacidad 1.0.
//    · Si visible == null o está vacío AND el criterio está vacío,
//      Reset() se llama automáticamente (fast path).
//
//  Rendimiento:
//    · Usa DoubleAnimation de 150 ms → suave pero no lento.
//    · Solo actualiza elementos cuya opacidad cambia (evita re-render
//      de FrameworkElements que ya están en el estado correcto).
//    · Single-pass sobre ambos diccionarios.
//    · Llamar siempre desde el hilo de la UI (no thread-safe a propósito,
//      porque todos los consumidores son event handlers de WPF).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ResultRow = e3gbimtools.Calc.Core.Common.ResultRow;

namespace e3gbimtools.Calc.Filtering
{
    public sealed class SchemeFilterRenderer
    {
        // ── Constantes ────────────────────────────────────────────────────
        private const double DimOpacity      = 0.12;   // no-match → muy tenue
        private const double FullOpacity     = 1.00;   // match → opaco
        private const double AnimDurationMs  = 150;    // duración de la transición
        private const double Tolerance       = 0.01;   // para comparar opacidades

        // ── Dependencias inyectadas desde CalcWindow ──────────────────────

        /// <summary>Diccionario fila → glifo principal (rectángulo/texto del nodo).</summary>
        public Dictionary<ResultRow, FrameworkElement> Glyphs { get; set; }

        /// <summary>Diccionario fila → línea del tronco vertical.</summary>
        public Dictionary<ResultRow, Line> Lines { get; set; }

        // ── Animación compartida (reutilizable) ───────────────────────────
        private static readonly Duration AnimDuration
            = new Duration(TimeSpan.FromMilliseconds(AnimDurationMs));

        // ── API pública ───────────────────────────────────────────────────

        /// <summary>
        /// Aplica el dimming según el conjunto de filas visibles.
        /// <para>Si <paramref name="visibleRows"/> es null → llama Reset().</para>
        /// <para>Si está vacío pero el criterio NO es Empty → atenúa todo.</para>
        /// </summary>
        public void Apply(ISet<ResultRow> visibleRows, bool criteriaIsEmpty)
        {
            if (criteriaIsEmpty || visibleRows == null)
            {
                Reset();
                return;
            }

            ApplyToDict(Glyphs, visibleRows);
            ApplyToLines(Lines, visibleRows);
        }

        /// <summary>Restaura todos los elementos a opacidad 1.0.</summary>
        public void Reset()
        {
            if (Glyphs != null)
                foreach (var el in Glyphs.Values)
                    AnimateTo(el, FullOpacity);

            if (Lines != null)
                foreach (var ln in Lines.Values)
                    AnimateTo(ln, FullOpacity);
        }

        // ── Internos ─────────────────────────────────────────────────────

        private void ApplyToDict(Dictionary<ResultRow, FrameworkElement> dict,
                                  ISet<ResultRow> visible)
        {
            if (dict == null) return;
            foreach (var kvp in dict)
            {
                double target = visible.Contains(kvp.Key) ? FullOpacity : DimOpacity;
                AnimateTo(kvp.Value, target);
            }
        }

        private void ApplyToLines(Dictionary<ResultRow, Line> dict,
                                   ISet<ResultRow> visible)
        {
            if (dict == null) return;
            foreach (var kvp in dict)
            {
                double target = visible.Contains(kvp.Key) ? FullOpacity : DimOpacity;
                AnimateTo(kvp.Value, target);
            }
        }

        /// <summary>
        /// Anima la opacidad de un UIElement al valor objetivo.
        /// Si ya está en el valor objetivo, no anima (evita thrashing del
        /// animation system).
        /// </summary>
        private static void AnimateTo(UIElement el, double target)
        {
            if (el == null) return;
            // Evitar animación innecesaria.
            if (Math.Abs(el.Opacity - target) < Tolerance) return;

            var anim = new DoubleAnimation(target, AnimDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
