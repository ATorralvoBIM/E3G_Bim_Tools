// revit_mcp_plugin/codigo/Protocol/FilterOperator.cs
using System;

namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Operadors de filtre idèntics als de Revit Selection Filters.
    /// </summary>
    public enum FilterOperator
    {
        EsIgualA,
        NoEsIgualA,
        Conte,
        NoCalConte,
        ComencaPer,
        NoComencaPer,
        AcabaPer,
        NoAcabaPer,
        TeValor,
        NoTeValor,
        MajorQue,
        MenorQue,
        MajorIgualQue,
        MenorIgualQue
    }

    public static class FilterOperatorExtensions
    {
        public static string Etiqueta(this FilterOperator op) => op switch
        {
            FilterOperator.EsIgualA => "és igual a",
            FilterOperator.NoEsIgualA => "no és igual a",
            FilterOperator.Conte => "conté",
            FilterOperator.NoCalConte => "no conté",
            FilterOperator.ComencaPer => "comença per",
            FilterOperator.NoComencaPer => "no comença per",
            FilterOperator.AcabaPer => "acaba per",
            FilterOperator.NoAcabaPer => "no acaba per",
            FilterOperator.TeValor => "té valor",
            FilterOperator.NoTeValor => "no té valor",
            FilterOperator.MajorQue => ">",
            FilterOperator.MenorQue => "<",
            FilterOperator.MajorIgualQue => "≥",
            FilterOperator.MenorIgualQue => "≤",
            _ => op.ToString()
        };

        public static bool EsNumeric(this FilterOperator op) => op switch
        {
            FilterOperator.MajorQue or FilterOperator.MenorQue or
            FilterOperator.MajorIgualQue or FilterOperator.MenorIgualQue => true,
            _ => false
        };

        public static bool SenseValor(this FilterOperator op) =>
            op == FilterOperator.TeValor || op == FilterOperator.NoTeValor;

        /// <summary>Avalua l'operador sobre un valor real de string.</summary>
        public static bool Avaluar(this FilterOperator op, string valorElement, string valorRegla)
        {
            if (op.SenseValor())
                return op == FilterOperator.TeValor
                    ? !string.IsNullOrEmpty(valorElement)
                    : string.IsNullOrEmpty(valorElement);

            var e = valorElement ?? "";
            var r = valorRegla ?? "";
            return op switch
            {
                FilterOperator.EsIgualA => e.Equals(r, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NoEsIgualA => !e.Equals(r, StringComparison.OrdinalIgnoreCase),
                FilterOperator.Conte => e.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0,
                FilterOperator.NoCalConte => e.IndexOf(r, StringComparison.OrdinalIgnoreCase) < 0,
                FilterOperator.ComencaPer => e.StartsWith(r, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NoComencaPer => !e.StartsWith(r, StringComparison.OrdinalIgnoreCase),
                FilterOperator.AcabaPer => e.EndsWith(r, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NoAcabaPer => !e.EndsWith(r, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>Avalua l'operador numèric sobre un valor double.</summary>
        public static bool AvalurNumeric(this FilterOperator op, double valorElement, double valorRegla) =>
            op switch
            {
                FilterOperator.MajorQue => valorElement > valorRegla,
                FilterOperator.MenorQue => valorElement < valorRegla,
                FilterOperator.MajorIgualQue => valorElement >= valorRegla,
                FilterOperator.MenorIgualQue => valorElement <= valorRegla,
                _ => false
            };
    }
}