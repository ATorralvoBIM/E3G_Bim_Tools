// ============================================================================
//  SyncMessages.cs
//  Tipos de mensaje del bus de sincronización Revit ↔ UI.
//
//  Estos tipos viven en la capa Sync para que la UI no tenga que conocer la
//  Revit API directamente — todo lo que la UI recibe son ResultRow + flags.
// ============================================================================
using System;
using System.Collections.Generic;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    /// <summary>Origen del evento de cambio de selección.</summary>
    public enum SelectionOrigin
    {
        /// <summary>El usuario seleccionó algo dentro de Revit.</summary>
        Revit,
        /// <summary>El usuario seleccionó algo dentro de la UI de MEPCalc.</summary>
        Ui,
        /// <summary>Selección programática (ej.: tras un Calcular).</summary>
        Programmatic,
    }

    /// <summary>
    /// Cambio de selección propagado desde Revit hacia la UI.
    /// Contiene las filas a marcar (resueltas vía RowElementMap a partir de
    /// los ElementId de la selección de Revit).
    /// </summary>
    public sealed class SyncSelectionEventArgs : EventArgs
    {
        public SelectionOrigin Origin { get; }
        public IReadOnlyList<ResultRow> Rows { get; }

        public SyncSelectionEventArgs(SelectionOrigin origin, IReadOnlyList<ResultRow> rows)
        {
            Origin = origin;
            Rows = rows ?? Array.Empty<ResultRow>();
        }
    }

    /// <summary>Solicitud de resaltar visualmente una fila en la UI.</summary>
    public sealed class SyncHighlightEventArgs : EventArgs
    {
        public ResultRow Row { get; }
        /// <summary>Duración en ms; 0 = permanente hasta que se limpie.</summary>
        public int DurationMs { get; }

        public SyncHighlightEventArgs(ResultRow row, int durationMs)
        {
            Row = row;
            DurationMs = durationMs;
        }
    }
}
