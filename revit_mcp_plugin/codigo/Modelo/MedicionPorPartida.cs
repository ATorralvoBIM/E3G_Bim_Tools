// ============================================================================
//  MedicionPorPartida.cs + LineaMedicion + MedicionAcumulador
//  Contrato motor → writer.
//
//  Drill-down: LineaMedicion lleva ahora un flag Excluida (transitorio, no se
//  persiste) para que el panel Saneamiento pueda excluir líneas concretas
//  del próximo export sin afectar al modelo ni a la persistencia.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Presupuesto.Modelo
{
    public sealed class MedicionPorPartida
    {
        public Partida Partida          { get; init; }
        public string CapituloDestino   { get; init; }
        public decimal CantidadTotal    { get; init; }
        public IReadOnlyList<LineaMedicion> Lineas { get; init; } = new List<LineaMedicion>();
        public decimal Importe => CantidadTotal * (Partida?.Precio ?? 0m);

        /// <summary>
        /// Líneas que entrarán en el BC3 — descarta las marcadas como Excluida.
        /// </summary>
        public IEnumerable<LineaMedicion> LineasParaExportar =>
            Lineas?.Where(l => !l.Excluida) ?? Enumerable.Empty<LineaMedicion>();

        /// <summary>
        /// Cantidad efectiva tras descartar exclusiones — para el preview en UI.
        /// </summary>
        public decimal CantidadParaExportar =>
            LineasParaExportar.Sum(l => l.Cantidad);

        public override string ToString() =>
            $"{CapituloDestino}/{Partida?.Codigo}: {CantidadTotal:0.###} {Partida?.Unidad}";
    }

    public sealed class LineaMedicion
    {
        public string Comentario        { get; init; }
        public decimal Unidades         { get; set; } = 1m;
        public decimal Longitud         { get; set; }
        public decimal Latitud          { get; set; }
        public decimal Altura           { get; set; }
        public ElementId ElementoOrigen { get; init; } = ElementId.InvalidElementId;

        /// <summary>
        /// Si true, la línea NO se exporta al BC3 — el usuario la ha excluido
        /// puntualmente desde el drill-down del panel Saneamiento. No se
        /// persiste: en cuanto se hace Recalcular, el flag se pierde.
        /// </summary>
        public bool Excluida            { get; set; } = false;

        /// <summary>
        /// Si true, la línea no proviene del modelo Revit sino que la añadió
        /// el usuario manualmente (p.a. trabajos auxiliares, partidas alzadas).
        /// Tampoco se persiste — vive sólo durante esta sesión de exportación.
        /// </summary>
        public bool EsManual            { get; init; } = false;

        public decimal Cantidad
        {
            get
            {
                decimal r = Unidades == 0 ? 1m : Unidades;
                if (Longitud > 0) r *= Longitud;
                if (Latitud  > 0) r *= Latitud;
                if (Altura   > 0) r *= Altura;
                return r;
            }
        }

        public override string ToString() =>
            $"{Comentario}: {Unidades} × {Longitud} × {Latitud} × {Altura} = {Cantidad}";
    }

    /// <summary>
    /// Acumulador interno del motor.
    /// </summary>
    public sealed class MedicionAcumulador
    {
        public Partida Partida { get; }
        public string CapituloDestino { get; }
        private readonly List<LineaMedicion> _lineas = new List<LineaMedicion>();

        public MedicionAcumulador(Partida partida, string capituloDestino)
        {
            Partida = partida;
            CapituloDestino = capituloDestino;
        }

        public void Anadir(IEnumerable<LineaMedicion> lineas)
        {
            if (lineas != null) _lineas.AddRange(lineas);
        }

        public MedicionPorPartida Cerrar()
        {
            // Ordenar les línies pel comentari (que comença per [Sistema · Nivell])
            // perquè TCQ/Presto les agrupin visualment quan ordenen alfabèticament.
            _lineas.Sort((a, b) =>
                System.StringComparer.OrdinalIgnoreCase.Compare(a.Comentario ?? "", b.Comentario ?? ""));
            return CerrarInternal();
        }

        private MedicionPorPartida CerrarInternal() => new MedicionPorPartida
        {
            Partida = Partida,
            CapituloDestino = CapituloDestino,
            Lineas = _lineas,
            CantidadTotal = _lineas.Sum(l => l.Cantidad)
        };
    }
}
