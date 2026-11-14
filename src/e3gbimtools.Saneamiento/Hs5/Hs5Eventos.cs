using Autodesk.Revit.UI;

namespace E3G.Saneamiento.Hs5
{
    /// <summary>Lee los tipos de aparato sanitario del modelo para la pestaña de mapeo.</summary>
    public class EventoLeerAparatos : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var tipos = Hs5Analisis.ListarTiposAparato(doc, Hs5Config.Actual);
            Pagina?.Dispatcher.Invoke(() => Pagina.MostrarAparatos(tipos));
        }

        public string GetName() => "E3G HS5 — Leer aparatos";
    }
}
