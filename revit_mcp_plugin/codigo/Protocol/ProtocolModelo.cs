// revit_mcp_plugin/codigo/Protocol/ProtocolModelo.cs
using System;
using System.Collections.Generic;

namespace E3G.Saneamiento.Protocol
{
    // ── Tipus d'element sobre el qual s'aplica la regla ──────────────────────
    public enum ElementTipus
    {
        Canonada,       // OST_PipeCurves
        GrupTipus,      // GroupType (per identificar vivendes)
        Nivell,         // Level (per identificar plantes, aparcament)
        Workset         // Workset (per identificar edificis)
    }

    // ── Paràmetres virtuals per a criteris geomètrics ────────────────────────
    public static class ParamVirtual
    {
        public const string Vertical = "__GEOM_VERTICAL__";
        public const string Horitzontal = "__GEOM_HORITZONTAL__";
        public const string LongitudM = "__GEOM_LONGITUD_M__";
        public const string CentreZ_M = "__GEOM_CENTRE_Z_M__";
        public const string ElevacioNivell_M = "__GEOM_ELEVACIO_NIVELL_M__";
    }

    // ── Una condició dins d'una regla ────────────────────────────────────────
    public sealed class FilterCondicio
    {
        public string ParamNom { get; init; } = "";  // nom del paràmetre o ParamVirtual.*
        public FilterOperator Operador { get; init; }
        public string Valor { get; init; } = "";  // buit si l'operador és TeValor/NoTeValor
    }

    // ── Combinació de condicions d'una regla ─────────────────────────────────
    public enum LogicaCondicions { Totes, Qualsevol }   // AND / OR

    // ── Una regla del protocol ───────────────────────────────────────────────
    public sealed class ProtocolRegla
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Nom { get; init; } = "";
        public ElementTipus Aplica { get; init; }
        public LogicaCondicions Logica { get; init; } = LogicaCondicions.Totes;
        public List<FilterCondicio> Condicions { get; init; } = new();
    }

    // ── Secció del protocol: un grup de regles per a una categoria ───────────
    public sealed class ProtocolSeccio
    {
        public string Etiqueta { get; init; } = "";
        public List<ProtocolRegla> Regles { get; init; } = new();

        public ProtocolSeccio ConRegla(ProtocolRegla r)
        {
            var nova = new List<ProtocolRegla>(Regles) { r };
            return new ProtocolSeccio { Etiqueta = Etiqueta, Regles = nova };
        }
    }

    // ── El Protocol sencer per a una instal·lació ────────────────────────────
    public sealed class ProtocolInstalacio
    {
        public string Instalacio { get; init; } = "Sanejament";
        public ProtocolSeccio Edificis { get; init; } = new() { Etiqueta = "Edificis" };
        public ProtocolSeccio Vivendes { get; init; } = new() { Etiqueta = "Interior vivendes" };
        public ProtocolSeccio Bajants { get; init; } = new() { Etiqueta = "Bajants" };
        public ProtocolSeccio Colectors { get; init; } = new() { Etiqueta = "Col·lectors P00-P01" };
        public ProtocolSeccio Aparcament { get; init; } = new() { Etiqueta = "Aparcament" };

        // Criteris geomètrics globals (editables des de la UI)
        public double BajantLongitudMinM { get; init; } = 1.0;
        public double ToleranciaElevacioM { get; init; } = 0.5;

        public static ProtocolInstalacio Buit() => new();
    }
}