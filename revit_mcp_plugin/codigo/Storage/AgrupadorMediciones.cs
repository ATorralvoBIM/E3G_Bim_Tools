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

// revit_mcp_plugin/codigo/Protocol/AgrupadorMedicions.cs
using System;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Retorna el grup d'agrupació d'un pipe per al comentari BC3.
    /// Llegeix el Protocol actiu — si no hi ha protocol, usa fallback hardcoded.
    /// </summary>
    public static class AgrupadorMedicions
    {
        public static string ObtenerGrupo(Element pipe, Document doc)
        {
            try
            {
                var protocol = ProtocolSession.ProtocolActual;
                if (protocol != null)
                {
                    var avaluador = new ProtocolEvaluador(protocol, doc);
                    var grup = avaluador.GrupDePipe(pipe);
                    if (grup != null) return grup;
                }
                else
                {
                    // Fallback hardcoded (comportament anterior)
                    return FallbackHardcoded(pipe, doc);
                }
            }
            catch { /* Si peta, fallback */ }

            return FallbackSistemaINivell(pipe);
        }

        // ── Fallback si no hi ha Protocol configurat ─────────────────────────
        private static string FallbackHardcoded(Element pipe, Document doc)
        {
            try
            {
                // Interior vivendes
                if (pipe.GroupId != ElementId.InvalidElementId)
                    return "Interior vivendes";

                var nivellNom = ObtenirNivellNom(pipe);

                // Aparcament
                if (nivellNom != null &&
                    (nivellNom.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
                     nivellNom.StartsWith("B", StringComparison.OrdinalIgnoreCase)))
                    return "Aparcament";

                // Bajants / Col·lectors
                if (pipe.Location is LocationCurve lc)
                {
                    var p0 = lc.Curve.GetEndPoint(0);
                    var p1 = lc.Curve.GetEndPoint(1);
                    var dz = Math.Abs(p1.Z - p0.Z);
                    var dxy = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                    var longM = lc.Curve.Length * 0.3048;
                    if (dz > dxy && longM >= 1.0) return "Bajants";
                    if (dxy >= dz) return "Col·lectors P00-P01";
                }
            }
            catch { /* ignored */ }

            return FallbackSistemaINivell(pipe);
        }

        private static string FallbackSistemaINivell(Element pipe)
        {
            try
            {
                var sistem = (pipe as Autodesk.Revit.DB.Plumbing.Pipe)?.MEPSystem?.Name ?? "—";
                var nivell = ObtenirNivellNom(pipe) ?? "—";
                return $"{sistem} · {nivell}";
            }
            catch { return "Sense grup"; }
        }

        private static string? ObtenirNivellNom(Element element)
        {
            var p = element.LookupParameter("Reference Level")
                 ?? element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)
                 ?? element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
            return p?.AsValueString();
        }
    }
}
