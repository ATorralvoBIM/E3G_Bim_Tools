// ============================================================================
//  ISubFiltro.cs
//  Interfaz + implementaciones P2 del filtro adicional.
//
//  P2: SubFiltroNinguno, SubFiltroRangoDiametro, SubFiltroParametro.
//  P3 añadirá SubFiltroCompuesto (AND/OR) y SubFiltroExpresion (DSL) sin
//  tocar motor/writer/storage.
// ============================================================================

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public interface ISubFiltro
    {
        bool Aplica(Element elemento, Document doc);
        string Descripcion { get; }
        string Tipo { get; }
    }

    public sealed class SubFiltroNinguno : ISubFiltro
    {
        public bool Aplica(Element elemento, Document doc) => true;
        public string Descripcion => "Tots els elements";
        public string Tipo => "ninguno";
    }

    public sealed class SubFiltroRangoDiametro : ISubFiltro
    {
        public double MinMm { get; init; } = 0.0;
        public double MaxMm { get; init; } = double.PositiveInfinity;

        public bool Aplica(Element elemento, Document doc)
        {
            var d = LeerDiametroMm(elemento);
            if (!d.HasValue) return false;
            return d.Value >= MinMm && d.Value < MaxMm;
        }

        public string Descripcion
        {
            get
            {
                if (MinMm <= 0 && double.IsPositiveInfinity(MaxMm)) return "Tots els Ø";
                if (MinMm <= 0)                                     return $"Ø < {MaxMm:0} mm";
                if (double.IsPositiveInfinity(MaxMm))               return $"Ø ≥ {MinMm:0} mm";
                return $"Ø {MinMm:0}–{MaxMm:0} mm";
            }
        }

        public string Tipo => "rangoDiametro";

        // Helper público para reutilizar desde SubFiltroDiametroExacto.
        internal static double? LeerDiametroMmInterno(Element e) => LeerDiametroMm(e);

        private static double? LeerDiametroMm(Element e)
        {
            if (e is Pipe pipe)
            {
                var feet = pipe.Diameter;
                return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
            }
            var p = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                 ?? e.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
            if (p != null && p.HasValue)
            {
                var feet = p.AsDouble();
                return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
            }
            return null;
        }
    }

    public sealed class SubFiltroParametro : ISubFiltro
    {
        public string ParametroNombre { get; init; }
        public OpComparacion Operador { get; init; } = OpComparacion.Igual;
        public string ValorComparado  { get; init; }

        public bool Aplica(Element elemento, Document doc)
        {
            if (string.IsNullOrEmpty(ParametroNombre)) return false;
            var p = elemento.LookupParameter(ParametroNombre)
                 ?? elemento.Document.GetElement(elemento.GetTypeId())?.LookupParameter(ParametroNombre);
            if (p == null) return false;
            return CompararTexto(ValorComoTexto(p), ValorComparado, Operador);
        }

        public string Descripcion =>
            $"{ParametroNombre} {OperadorComoTexto(Operador)} \"{ValorComparado}\"";
        public string Tipo => "parametro";

        private static string ValorComoTexto(Parameter p) => p.StorageType switch
        {
            StorageType.String    => p.AsString() ?? "",
            StorageType.Integer   => p.AsInteger().ToString(),
            StorageType.Double    => p.AsDouble().ToString("0.###"),
            StorageType.ElementId => p.AsElementId().Value.ToString(),
            _                     => p.AsValueString() ?? ""
        };

        private static bool CompararTexto(string a, string b, OpComparacion op)
        {
            a ??= ""; b ??= "";
            return op switch
            {
                OpComparacion.Igual       => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
                OpComparacion.Distinto    => !string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
                OpComparacion.Contiene    => a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0,
                OpComparacion.NoContiene  => a.IndexOf(b, StringComparison.OrdinalIgnoreCase) < 0,
                _                         => false
            };
        }

        private static string OperadorComoTexto(OpComparacion op) => op switch
        {
            OpComparacion.Igual      => "=",
            OpComparacion.Distinto   => "≠",
            OpComparacion.Contiene   => "conté",
            OpComparacion.NoContiene => "no conté",
            _                        => "?"
        };
    }

    public enum OpComparacion
    {
        Igual,
        Distinto,
        Contiene,
        NoContiene
    }

    /// <summary>
    /// Diámetro exacto en mm, con tolerancia de ±0.5 mm para absorber
    /// imprecisiones de modelado (Ø108 vs Ø110, etc.).
    ///
    /// Es el sub-filtro NATURAL para saneamiento — cada diámetro tiene
    /// su partida con código BEDEC propio, así que la regla típica es:
    /// "Ø=110 → ED15B871", "Ø=125 → ED15B873", etc.
    /// </summary>
    public sealed class SubFiltroDiametroExacto : ISubFiltro
    {
        public double DiametroMm { get; init; } = 0.0;

        /// <summary>Tolerancia en mm. ±0.5 cubre los casos típicos de redondeo.</summary>
        public double ToleranciaMm { get; init; } = 0.5;

        public bool Aplica(Element elemento, Document doc)
        {
            var d = SubFiltroRangoDiametro.LeerDiametroMmInterno(elemento);
            if (!d.HasValue) return false;
            return System.Math.Abs(d.Value - DiametroMm) <= ToleranciaMm;
        }

        public string Descripcion => $"Ø = {DiametroMm:0} mm";
        public string Tipo => "diametroExacto";
    }

    // P3 (futuro):
    // public sealed class SubFiltroCompuesto : ISubFiltro { ... }
    // public sealed class SubFiltroExpresion : ISubFiltro { ... }
}
