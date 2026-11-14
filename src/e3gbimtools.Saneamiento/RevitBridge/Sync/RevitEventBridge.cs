// ============================================================================
//  RevitEventBridge.cs
//  Puente entre los eventos nativos de Revit y un evento .NET propio.
//
//  Hoy en día sólo escuchamos UIApplication.SelectionChanged (Revit 2017+),
//  pero la clase está preparada para añadir más (ViewActivated, DocumentChanged,
//  etc.) sin tocar el resto del sistema.
//
//  Throttle:
//    SelectionChanged se dispara muy seguido durante drag selection. El
//    bridge agrupa los disparos en ventanas de 60 ms y emite UN solo evento
//    .NET al final.  La UI no se redibuja N veces por segundo.
// ============================================================================
using System;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class RevitEventBridge : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly DispatcherTimer _throttle;
        private bool _subscribed;
        private bool _disposed;

        /// <summary>Disparado cuando la selección de Revit cambió.</summary>
        public event EventHandler SelectionChanged;

        public RevitEventBridge(UIApplication uiApp, int throttleMs = 60)
        {
            _uiApp = uiApp;
            _throttle = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(throttleMs),
            };
            _throttle.Tick += OnThrottleTick;
        }

        public void Subscribe()
        {
            if (_subscribed || _uiApp == null) return;
            try { _uiApp.SelectionChanged += OnRevitSelectionChanged; }
            catch { return; }
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed || _uiApp == null) return;
            try { _uiApp.SelectionChanged -= OnRevitSelectionChanged; }
            catch { }
            _subscribed = false;
        }

        private void OnRevitSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Reiniciar el throttle: si llegan más disparos antes de 60 ms,
            // el timer se reagenda; sólo cuando deja de haber actividad
            // disparamos el evento .NET una sola vez.
            _throttle.Stop();
            _throttle.Start();
        }

        private void OnThrottleTick(object sender, EventArgs e)
        {
            _throttle.Stop();
            if (_disposed) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _throttle.Stop();
            _throttle.Tick -= OnThrottleTick;
            Unsubscribe();
        }
    }
}
