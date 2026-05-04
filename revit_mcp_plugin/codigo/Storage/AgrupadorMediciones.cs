// ============================================================================
//  AgrupadorMediciones.cs
//  Classifica un pipe en un dels 4 grups del panell de mediciones de Sanejament,
//  aplicant la mateixa lògica que MedicionMotor però per QUALSEVOL pipe (no només
//  Fecals del sistema 10_10).
//
//  Grups (per ordre de prioritat):
//      1. "Interior vivendes" — pipe dins d'un Group de Revit
//      2. "Aparcamiento"      — nivell de referència és S01.A, S02.A o S03.A
//      3. "Bajantes"          — pipe vertical (Δz > Δxy), longitud > 1m, elev ≥ P00.A
//      4. "Colectores P00-P01"— pipe horitzontal entre P00.A i P01.A
//      5. null (servicios comunes / sense classificar)
//
//  Cau a null si:
//      - No hi ha nivells P00.A o P01.A al projecte (no es pot calcular elev)
//      - El pipe no compleix cap dels 4 criteris
//
//  Usa una caché estàtica de elevacions de nivells perquè cada pipe no
//  reconsulti tots els Level del document.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace E3G.Saneamiento.Presupuesto.Storage
{
    public static class AgrupadorMediciones
    {
        // Tolerància per comparar elevacions, en metres
        private const double TOL_M = 0.5;

        // Caché: doc → diccionari nom-de-nivell → elevació en metres.
        // Es reseteja amb LimpiarCache() per si el doc canvia.
        private static readonly Dictionary<int, Dictionary<string, double>> _cacheElevacionsPerDoc
            = new Dictionary<int, Dictionary<string, double>>();

        public static void LimpiarCache() => _cacheElevacionsPerDoc.Clear();

        /// <summary>
        /// Retorna el grup del pipe. Null si no es pot classificar.
        /// </summary>
        public static string ObtenerGrupo(Element pipe)
        {
            if (pipe == null) return null;
            var doc = pipe.Document;
            if (doc == null) return null;

            // 1. Interior vivendes — el pipe està dins d'un Group
            try
            {
                var groupId = pipe.GroupId;
                if (groupId != null && groupId != ElementId.InvalidElementId)
                    return "Interior vivendes";
            }
            catch { /* alguns elements no exposen GroupId */ }

            // Necessitem el nivell del pipe
            Level level = null;
            try { level = (pipe as Pipe)?.ReferenceLevel; } catch { }
            if (level == null) return null;

            var nivelName = level.Name ?? "";

            // 2. Aparcamiento — nivell S01.A, S02.A, S03.A (en general "S*.A")
            if (EsNivelAparcamiento(nivelName)) return "Aparcamiento";

            // Per Bajantes/Colectores necessitem geometria + elevacions de P00.A i P01.A
            var elevations = ObtenerElevacionsCache(doc);
            if (!elevations.TryGetValue("P00.A", out var zP00A)) return null;
            elevations.TryGetValue("P01.A", out var zP01A);

            // Elevació del nivell del pipe
            var elevNivelM = ConvertirAMetros(level.Elevation);

            // Geometria del pipe
            if (!(pipe is Pipe p)) return null;
            var curve = (p.Location as LocationCurve)?.Curve;
            if (curve == null) return null;

            var p1 = curve.GetEndPoint(0);
            var p2 = curve.GetEndPoint(1);
            var dx = Math.Abs(p2.X - p1.X);
            var dy = Math.Abs(p2.Y - p1.Y);
            var dz = Math.Abs(p2.Z - p1.Z);
            var dxy = Math.Sqrt(dx * dx + dy * dy);

            // Si Δz > Δxy → vertical; si Δxy > Δz → horizontal.
            // Si són gairebé iguals (45°) ho considerem horizontal (cas excepcional).
            bool esVertical = dz > dxy;
            var longitudM = ConvertirAMetros(curve.Length);

            // 3. Bajantes — vertical, longitud > 1m, elev ≥ P00.A
            if (esVertical && longitudM > 1.0 && elevNivelM >= zP00A - TOL_M)
                return "Bajantes";

            // 4. Colectores P00-P01 — horizontal, entre P00.A i P01.A
            if (!esVertical && elevNivelM >= zP00A - TOL_M
                && zP01A > 0 && elevNivelM <= zP01A + TOL_M)
                return "Colectores P00-P01";

            // 5. No classificable — "Servicios comunes" o fora de criteris
            return null;
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static bool EsNivelAparcamiento(string nivelName)
        {
            if (string.IsNullOrEmpty(nivelName)) return false;
            // Patrons típics: "S01.A", "S02.A", "S03.A", "S1.A"...
            return nivelName.StartsWith("S", StringComparison.OrdinalIgnoreCase)
                   && nivelName.Contains(".A");
        }

        private static Dictionary<string, double> ObtenerElevacionsCache(Document doc)
        {
            int key;
            try { key = (int)doc.GetHashCode(); }
            catch { key = 0; }

            if (_cacheElevacionsPerDoc.TryGetValue(key, out var existent))
                return existent;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            var dic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in levels)
            {
                if (string.IsNullOrEmpty(l?.Name)) continue;
                if (!dic.ContainsKey(l.Name))
                    dic[l.Name] = ConvertirAMetros(l.Elevation);
            }

            _cacheElevacionsPerDoc[key] = dic;
            return dic;
        }

        private static double ConvertirAMetros(double valorPies)
        {
            try { return UnitUtils.ConvertFromInternalUnits(valorPies, UnitTypeId.Meters); }
            catch { return valorPies * 0.3048; } // fallback
        }
    }
}
