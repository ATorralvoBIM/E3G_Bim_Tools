using System;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Protocol
{
    public static class AgrupadorMedicions
    {
        public static string? ObtenerGrupo(Element pipe, Document doc)
        {
            try
            {
                var protocol = ProtocolSession.ProtocolActual;
                if (protocol != null && protocol.Seccions.Count > 0)
                {
                    var avaluador = new ProtocolEvaluador(protocol, doc);
                    return avaluador.GrupDePipe(pipe);
                }
            }
            catch { }

            return FallbackHardcoded(pipe, doc);
        }

        private static string? FallbackHardcoded(Element pipe, Document doc)
        {
            try
            {
                if (pipe.GroupId != ElementId.InvalidElementId)
                    return "Interior vivendes";

                var p = pipe.LookupParameter("Reference Level")
                     ?? pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                var nivell = p?.AsValueString() ?? "";

                if (nivell.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
                    nivell.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                    return "Aparcament";

                if (pipe.Location is LocationCurve lc)
                {
                    var p0 = lc.Curve.GetEndPoint(0);
                    var p1 = lc.Curve.GetEndPoint(1);
                    var dz = Math.Abs(p1.Z - p0.Z);
                    var dxy = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                    var lon = lc.Curve.Length * 0.3048;
                    if (dz > dxy && lon >= 1.0) return "Bajants";
                    if (dxy >= dz) return "Col·lectors P00-P01";
                }
            }
            catch { }
            return null;
        }
    }
}