// ============================================================================
//  FamilyWarningEventArgs.cs
//  Mensaje que el ViewModel emite hacia el host cuando hay algo que comunicar
//  al usuario tras una validación o carga masiva de familias.  La severidad
//  permite al host elegir el formato (status bar, message box, dialog).
//
//  DetailedItems (opcional) lleva el detalle por archivo cuando la carga
//  fue masiva — el host puede preferir mostrarlo como tabla en un dialog
//  modal en lugar de meter todo en un MessageBox.
// ============================================================================
using System;
using System.Collections.Generic;

namespace E3G.Saneamiento.UI.Schematic
{
    public enum FamilyWarningSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class FamilyWarningEventArgs : EventArgs
    {
        public string Title { get; }
        public string Body  { get; }
        public FamilyWarningSeverity Severity { get; }

        /// <summary>Detalle por archivo (path + resultado).  Vacío si no
        /// aplica (ej. carga de un solo archivo o aviso genérico).</summary>
        public IReadOnlyList<(string Path, FamilyValidationResult Result)> DetailedItems { get; }

        public bool HasDetail => DetailedItems != null && DetailedItems.Count > 1;

        public FamilyWarningEventArgs(string title, string body, FamilyWarningSeverity sev,
            IReadOnlyList<(string, FamilyValidationResult)> detailed = null)
        {
            Title = title ?? "";
            Body  = body  ?? "";
            Severity = sev;
            DetailedItems = detailed;
        }
    }
}
