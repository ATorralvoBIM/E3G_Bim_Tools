// ============================================================================
//  IRevitSyncService.cs
//  Contrato público que la UI consume.  La UI NO debe conocer la Revit API;
//  toda operación pasa por aquí.
// ============================================================================
using System;
using Autodesk.Revit.DB;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public interface IRevitSyncService : IDisposable
    {
        /// <summary>Se dispara cuando la selección cambia en Revit.</summary>
        event EventHandler<SyncSelectionEventArgs> SelectionChangedFromRevit;

        /// <summary>Inicia las suscripciones a eventos de Revit.</summary>
        void Start();

        /// <summary>Detiene suscripciones y limpia overrides pendientes.</summary>
        void Stop();

        /// <summary>Registra el binding entre una fila y su ElementId de Revit.</summary>
        void RegisterRow(ResultRow row, ElementId id);

        /// <summary>Borra todos los bindings de filas (al recalcular).</summary>
        void ClearRows();

        /// <summary>
        /// Selecciona en Revit la pipe correspondiente a la fila.
        /// Si <paramref name="zoom"/> es true hace ShowElements (zoom-fit).
        /// </summary>
        void SelectInRevit(ResultRow row, bool zoom);

        /// <summary>
        /// Resalta temporalmente (con color y línea más gruesa) el elemento
        /// asociado a la fila en la vista activa de Revit.
        /// </summary>
        void HighlightInRevit(ResultRow row, int durationMs = 1500);

        /// <summary>Limpia inmediatamente cualquier highlight pendiente.</summary>
        void ClearHighlights();
    }
}
