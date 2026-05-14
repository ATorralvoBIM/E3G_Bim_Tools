// ============================================================================
//  SchemaSelectionController.cs
//  Une los clicks sobre los glifos del esquema (flechas de pipe, glifos de
//  fitting) con el servicio de sync.
//
//  El esquema mantiene un Dictionary<ResultRow, FrameworkElement> en
//  CalcWindow (_schemaGlyphs).  Cuando el usuario hace clic sobre un glifo,
//  CalcWindow ya rutea ese click a OnGlyphClicked que cambia la selección
//  del DataGrid; este controlador solo añade la parte de zoom-on-click y
//  unifica el comportamiento.
//
//  Implementación pragmática: este controlador se suscribe al
//  SelectionChangedFromRevit y aplica un "glow" temporal al glifo
//  correspondiente, para que el usuario vea con claridad qué tramo de Revit
//  está activo en su esquema.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using E3G.Saneamiento.RevitBridge.Sync;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.UI.Sync
{
    public sealed class SchemaSelectionController : IDisposable
    {
        private readonly IRevitSyncService _sync;
        private readonly Func<ResultRow, FrameworkElement> _glyphLookup;
        private readonly Dictionary<FrameworkElement, DispatcherTimer> _activeGlows
            = new Dictionary<FrameworkElement, DispatcherTimer>();
        private bool _disposed;

        /// <summary>
        /// <paramref name="glyphLookup"/> es una función inyectada para que
        /// el controlador no dependa del Dictionary interno de CalcWindow.
        /// Se le pasa una fila y devuelve el FrameworkElement correspondiente
        /// (o null si la fila no tiene glifo).
        /// </summary>
        public SchemaSelectionController(IRevitSyncService sync,
                                         Func<ResultRow, FrameworkElement> glyphLookup)
        {
            _sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _glyphLookup = glyphLookup ?? throw new ArgumentNullException(nameof(glyphLookup));
            _sync.SelectionChangedFromRevit += OnSyncSelectionFromRevit;
        }

        private void OnSyncSelectionFromRevit(object sender, SyncSelectionEventArgs e)
        {
            if (_disposed || e?.Rows == null || e.Rows.Count == 0) return;
            foreach (var row in e.Rows)
            {
                var glyph = _glyphLookup(row);
                if (glyph != null) ApplyGlow(glyph, 1500);
            }
        }

        private void ApplyGlow(FrameworkElement glyph, int durationMs)
        {
            // Si ya tenía un glow activo, reiniciar.
            if (_activeGlows.TryGetValue(glyph, out var oldTimer))
            {
                oldTimer.Stop();
                _activeGlows.Remove(glyph);
            }

            glyph.Effect = new DropShadowEffect
            {
                Color = Colors.Gold,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.95,
            };

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs),
            };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                _activeGlows.Remove(glyph);
                glyph.Effect = null;
            };
            _activeGlows[glyph] = timer;
            timer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sync.SelectionChangedFromRevit -= OnSyncSelectionFromRevit;
            foreach (var t in _activeGlows.Values) t.Stop();
            foreach (var fe in _activeGlows.Keys) fe.Effect = null;
            _activeGlows.Clear();
        }
    }
}
