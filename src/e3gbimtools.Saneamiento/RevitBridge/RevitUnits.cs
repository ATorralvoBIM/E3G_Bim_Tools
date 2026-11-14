// ============================================================================
//  RevitUnits.cs
//  RevitBridge — Conversiones entre unidades internas de Revit (imperial,
//  variables segun la magnitud) y Sistema Internacional (metros, Pa, m3/s).
//
//  Briefing tecnico v1.0 — Mayo 2026, secciones 3.1 y 6 (Regla 2).
//  Usa UnitUtils.ConvertFromInternalUnits / ConvertToInternalUnits para que
//  el codigo sea robusto a cambios entre versiones de Revit (ForgeTypeId).
//
//  Esta clase SI usa Autodesk.Revit.DB porque vive en RevitBridge.
// ============================================================================

using Autodesk.Revit.DB;

namespace E3G.Saneamiento.RevitBridge
{
    /// <summary>
    /// Helpers de conversion Revit ↔ SI.  Funciones estaticas, sin estado.
    /// Toda la API del motor de calculo vive en SI; este archivo es la
    /// frontera explicita.
    /// </summary>
    public static class RevitUnits
    {
        // -------------------------------------------------------------------
        //  Longitud (Revit interno: ft → SI: m)
        // -------------------------------------------------------------------
        public static double LengthToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.Meters);

        public static double LengthFromSI(double meters)
            => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

        // -------------------------------------------------------------------
        //  Caudal de tuberia y caudal de conducto.  Revit 2024+ almacena
        //  internamente en ft^3/s; ConvertFromInternalUnits hace el resto.
        // -------------------------------------------------------------------
        public static double FlowToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.CubicMetersPerSecond);

        public static double FlowFromSI(double m3s)
            => UnitUtils.ConvertToInternalUnits(m3s, UnitTypeId.CubicMetersPerSecond);

        // -------------------------------------------------------------------
        //  Velocidad
        // -------------------------------------------------------------------
        public static double VelocityToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.MetersPerSecond);

        public static double VelocityFromSI(double ms)
            => UnitUtils.ConvertToInternalUnits(ms, UnitTypeId.MetersPerSecond);

        // -------------------------------------------------------------------
        //  Presion absoluta o diferencial (Pa)
        // -------------------------------------------------------------------
        public static double PressureToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.Pascals);

        public static double PressureFromSI(double pa)
            => UnitUtils.ConvertToInternalUnits(pa, UnitTypeId.Pascals);

        // -------------------------------------------------------------------
        //  Perdida lineal (Pa/m).  Revit usa internamente "in WC per 100 ft".
        // -------------------------------------------------------------------
        public static double FrictionToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.PascalsPerMeter);

        public static double FrictionFromSI(double paPerM)
            => UnitUtils.ConvertToInternalUnits(paPerM, UnitTypeId.PascalsPerMeter);

        // -------------------------------------------------------------------
        //  Temperatura
        // -------------------------------------------------------------------
        public static double TemperatureToSI(double revitInternal)
            => UnitUtils.ConvertFromInternalUnits(revitInternal, UnitTypeId.Celsius);

        public static double TemperatureFromSI(double celsius)
            => UnitUtils.ConvertToInternalUnits(celsius, UnitTypeId.Celsius);

        // -------------------------------------------------------------------
        //  Helpers seguros para parametros que pueden no existir
        // -------------------------------------------------------------------

        /// <summary>Lee un parametro como double SI usando la conversion adecuada.</summary>
        public static double? GetLengthSI(Element e, BuiltInParameter bip)
        {
            var p = e?.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;
            return LengthToSI(p.AsDouble());
        }

        public static double? GetFlowSI(Element e, BuiltInParameter bip)
        {
            var p = e?.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;
            return FlowToSI(p.AsDouble());
        }

        public static double? GetVelocitySI(Element e, BuiltInParameter bip)
        {
            var p = e?.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;
            return VelocityToSI(p.AsDouble());
        }

        public static double? GetPressureSI(Element e, BuiltInParameter bip)
        {
            var p = e?.get_Parameter(bip);
            if (p == null || !p.HasValue) return null;
            return PressureToSI(p.AsDouble());
        }
    }
}
