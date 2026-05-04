// revit_mcp_plugin/codigo/Protocol/ProtocolSession.cs
namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Caché en memòria del protocol actiu.
    /// Mateix patró que PartidasSession / ClasificacionSession.
    /// </summary>
    public static class ProtocolSession
    {
        /// <summary>Protocol actual (null = no carregat)</summary>
        public static ProtocolInstalacio? ProtocolActual { get; set; }

        public static ProtocolInstalacio ObtenerOBuit()
            => ProtocolActual ?? ProtocolInstalacio.Buit();
    }
}