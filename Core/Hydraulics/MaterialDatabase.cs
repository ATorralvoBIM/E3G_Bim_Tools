// ============================================================================
//  MaterialDatabase.cs
//  Core.Hydraulics — Rugosidades absolutas de los materiales más usados.
//
//  Briefing técnico v1.0 — Mayo 2026, sección 3.6.
//  Valores normalizados según ISO 4200 y literatura técnica (Crane TP-410,
//  ASHRAE HOF, Moody).
//
//  Independencia: NO usa Autodesk.Revit.DB.
// ============================================================================

using System;
using System.Collections.Generic;

namespace E3G.Saneamiento.Core.Hydraulics
{
    /// <summary>
    /// Base de datos de rugosidades absolutas (épsilon) en metros.  La rugosidad
    /// es uno de los parámetros más influyentes en la pérdida de carga turbulenta
    /// vía Darcy-Weisbach + Colebrook-White.
    /// </summary>
    public static class MaterialDatabase
    {
        /// <summary>Rugosidad por defecto si la clave no existe (acero galv. nuevo, m).</summary>
        public const double DefaultRoughnessM = 0.000150;

        private static readonly Dictionary<string, double> _roughness =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "STEEL_GALV",   0.000150 },  // Acero galvanizado nuevo
            { "STEEL_BLACK",  0.000046 },  // Acero negro / carbono
            { "COPPER",       0.0000015 }, // Cobre comercial
            { "PVC",          0.0000015 }, // PVC rígido
            { "PE",           0.0000015 }, // Polietileno
            { "PP",           0.0000015 }, // Polipropileno
            { "PEX",          0.0000015 }, // Polietileno reticulado
            { "STAINLESS",    0.000015  }, // Acero inoxidable
            { "CAST_IRON",    0.000260  }, // Hierro fundido
            { "DUCTILE_IRON", 0.000260  }, // Fundición dúctil
            { "CONCRETE",     0.000300  }, // Hormigón liso
            { "BRASS",        0.0000015 }, // Latón
        };

        private static readonly Dictionary<string, string> _displayName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "STEEL_GALV",   "Acero galvanizado" },
            { "STEEL_BLACK",  "Acero negro (carbono)" },
            { "COPPER",       "Cobre comercial" },
            { "PVC",          "PVC rígido" },
            { "PE",           "Polietileno (PE)" },
            { "PP",           "Polipropileno (PP)" },
            { "PEX",          "Polietileno reticulado (PEX)" },
            { "STAINLESS",    "Acero inoxidable" },
            { "CAST_IRON",    "Hierro fundido" },
            { "DUCTILE_IRON", "Fundición dúctil" },
            { "CONCRETE",     "Hormigón liso" },
            { "BRASS",        "Latón" },
        };

        /// <summary>
        /// Devuelve la rugosidad absoluta del material en metros.  Si la clave
        /// no existe usa <see cref="DefaultRoughnessM"/> (acero galv.).
        /// </summary>
        public static double GetRoughness(string key)
        {
            if (string.IsNullOrEmpty(key)) return DefaultRoughnessM;
            return _roughness.TryGetValue(key, out var v) ? v : DefaultRoughnessM;
        }

        /// <summary>Etiqueta humana del material.  Si no existe devuelve la clave.</summary>
        public static string GetDisplayName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(material no especificado)";
            return _displayName.TryGetValue(key, out var v) ? v : key;
        }

        /// <summary>True si el material está registrado.</summary>
        public static bool Contains(string key)
            => !string.IsNullOrEmpty(key) && _roughness.ContainsKey(key);

        /// <summary>Lista de claves disponibles (orden de inserción).</summary>
        public static IEnumerable<string> Keys => _roughness.Keys;

        /// <summary>
        /// Heurística para mapear nombres de material de Revit (texto libre) a
        /// una clave de la BD.  Útil al leer parámetros RBS_PIPE_MATERIAL_PARAM.
        /// </summary>
        public static string ResolveFromRevitName(string revitName)
        {
            if (string.IsNullOrWhiteSpace(revitName)) return "STEEL_GALV";
            string n = revitName.ToUpperInvariant();
            if (n.Contains("GALV")) return "STEEL_GALV";
            if (n.Contains("STAINLESS") || n.Contains("INOX")) return "STAINLESS";
            if (n.Contains("COPPER")    || n.Contains("COBRE")) return "COPPER";
            if (n.Contains("PVC"))                              return "PVC";
            if (n.Contains("PEX"))                              return "PEX";
            if (n.Contains("PE")  || n.Contains("POLIETIL"))   return "PE";
            if (n.Contains("PP")  || n.Contains("POLIPROPIL")) return "PP";
            if (n.Contains("DUCTILE") || n.Contains("DUCTIL"))  return "DUCTILE_IRON";
            if (n.Contains("CAST")    || n.Contains("FUNDID"))  return "CAST_IRON";
            if (n.Contains("CONCRETE")|| n.Contains("HORMIG"))  return "CONCRETE";
            if (n.Contains("BRASS")   || n.Contains("LATON"))   return "BRASS";
            if (n.Contains("STEEL")   || n.Contains("ACERO"))   return "STEEL_BLACK";
            return "STEEL_GALV";
        }
    }
}
