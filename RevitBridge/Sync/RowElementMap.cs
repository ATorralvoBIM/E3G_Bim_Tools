// ============================================================================
//  RowElementMap.cs
//  Bimap thread-safe entre ResultRow del cálculo y ElementId de Revit.
//
//  El mapa se llena cuando MEPCalc construye sus filas (cada ResultRow tiene
//  su RevitId), y se consulta en los dos sentidos:
//    Row → ElementId  (al seleccionar en la UI → seleccionar en Revit)
//    ElementId → Row  (al seleccionar en Revit → marcar la fila)
//
//  Implementado con dos Dictionary indexados mutuamente para que las dos
//  direcciones sean O(1).  Lecturas frecuentes desde el thread de la UI;
//  escrituras puntuales tras Calcular.  Se sincroniza con un lock simple.
// ============================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.RevitBridge.Sync
{
    public sealed class RowElementMap
    {
        private readonly object _gate = new object();
        private readonly Dictionary<long, ResultRow> _byElementId
            = new Dictionary<long, ResultRow>();
        private readonly Dictionary<ResultRow, long> _byRow
            = new Dictionary<ResultRow, long>();

        /// <summary>Registra el binding bidireccional.</summary>
        public void Register(ResultRow row, ElementId id)
        {
            if (row == null || id == null || id == ElementId.InvalidElementId) return;
            long value = id.Value;
            lock (_gate)
            {
                // Si la fila ya estaba mapeada a otro id, lo quitamos primero.
                if (_byRow.TryGetValue(row, out long prev) && prev != value)
                    _byElementId.Remove(prev);
                _byRow[row] = value;
                _byElementId[value] = row;
            }
        }

        public bool TryGetRow(ElementId id, out ResultRow row)
        {
            if (id == null || id == ElementId.InvalidElementId)
            { row = null; return false; }
            lock (_gate) { return _byElementId.TryGetValue(id.Value, out row); }
        }

        public bool TryGetId(ResultRow row, out ElementId id)
        {
            if (row == null) { id = ElementId.InvalidElementId; return false; }
            lock (_gate)
            {
                if (_byRow.TryGetValue(row, out long v))
                { id = new ElementId(v); return true; }
            }
            id = ElementId.InvalidElementId;
            return false;
        }

        public void Unregister(ResultRow row)
        {
            if (row == null) return;
            lock (_gate)
            {
                if (_byRow.TryGetValue(row, out long v))
                {
                    _byRow.Remove(row);
                    _byElementId.Remove(v);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _byElementId.Clear();
                _byRow.Clear();
            }
        }

        public int Count
        {
            get { lock (_gate) { return _byRow.Count; } }
        }
    }
}
