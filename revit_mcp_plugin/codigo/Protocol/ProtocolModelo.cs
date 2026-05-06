using System;
using System.Collections.Generic;

namespace E3G.Saneamiento.Protocol
{
    public enum FilterOperator
    {
        EsIgualA, NoEsIgualA, Conte, NoCalConte,
        ComencaPer, NoComencaPer, AcabaPer, NoAcabaPer,
        TeValor, NoTeValor,
        MajorQue, MenorQue, MajorIgualQue, MenorIgualQue
    }

    public enum ElementTipus
    {
        Canonada,
        GrupTipus,
        Nivell,
        Workset
    }

    public static class ParamVirtual
    {
        public const string Vertical = "__GEOM_VERTICAL__";
        public const string Horitzontal = "__GEOM_HORITZONTAL__";
        public const string LongitudM = "__GEOM_LONGITUD_M__";
        public const string CentreZ_M = "__GEOM_CENTRE_Z_M__";
        public const string ElevacioNivell_M = "__GEOM_ELEVACIO_NIVELL_M__";
    }

    public enum LogicaCondicions { Totes, Qualsevol }

    public sealed class FilterCondicio
    {
        public string ParamNom { get; set; } = "";
        public FilterOperator Operador { get; set; }
        public string Valor { get; set; } = "";
    }

    public sealed class ProtocolRegla
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Nom { get; set; } = "";
        public ElementTipus Aplica { get; set; }
        public LogicaCondicions Logica { get; set; } = LogicaCondicions.Totes;
        public List<FilterCondicio> Condicions { get; set; } = new();
    }

    public sealed class ProtocolSeccio
    {
        public string Etiqueta { get; set; } = "";
        public List<ProtocolRegla> Regles { get; set; } = new();
    }

    public sealed class ProtocolInstalacio
    {
        public string Instalacio { get; set; } = "Sanejament";
        public List<ProtocolSeccio> Seccions { get; set; } = new();
        public double BajantLongitudMinM { get; set; } = 1.0;
        public double ToleranciaElevacioM { get; set; } = 0.5;

        public static ProtocolInstalacio Buit() => new ProtocolInstalacio
        {
            Seccions = new List<ProtocolSeccio>
            {
                new() { Etiqueta = "Interior vivendes" },
                new() { Etiqueta = "Aparcament" },
                new() { Etiqueta = "Bajants" },
                new() { Etiqueta = "Col·lectors P00-P01" },
                new() { Etiqueta = "Edificis" }
            }
        };
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