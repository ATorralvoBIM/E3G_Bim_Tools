using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using System;

namespace E3G.Saneamiento.Hs5
{
    /// <summary>
    /// Datos HS5 propios de cada proyecto, guardados dentro del .rvt (Extensible
    /// Storage en la Información del proyecto): la zona pluviométrica y la isoyeta del
    /// Anejo B, de las que sale el régimen i y el factor f.
    /// </summary>
    public static class Hs5Proyecto
    {
        private static readonly Guid SchemaGuid = new Guid("9A4E1C27-5B3D-4F86-8E2A-7C1D3B6F0E58");
        private const string CampoZona = "Zona";
        private const string CampoIsoyeta = "Isoyeta";

        private static Schema ObtenerSchema()
        {
            var s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;
            var b = new SchemaBuilder(SchemaGuid);
            b.SetSchemaName("E3G_HS5_Pluviometria");
            b.SetDocumentation("Zona pluviométrica e isoyeta (CTE DB-HS5, Anejo B) de E3G BIM Tools para este proyecto.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(CampoZona, typeof(string));
            b.AddSimpleField(CampoIsoyeta, typeof(int));
            return b.Finish();
        }

        /// <summary>Zona ("A"/"B") e isoyeta del proyecto; (null, 0) si no están definidas.</summary>
        public static (string Zona, int Isoyeta) LeerPluviometria(Document doc)
        {
            var info = doc?.ProjectInformation;
            var s = Schema.Lookup(SchemaGuid);
            if (info == null || s == null) return (null, 0);
            var ent = info.GetEntity(s);
            if (!ent.IsValid()) return (null, 0);
            string zona = ent.Get<string>(CampoZona);
            int iso = ent.Get<int>(CampoIsoyeta);
            return string.IsNullOrEmpty(zona) || iso <= 0 ? (null, 0) : (zona, iso);
        }

        /// <summary>Guarda zona e isoyeta en el proyecto; zona null las borra. Requiere transacción abierta.</summary>
        public static void GuardarPluviometria(Document doc, string zona, int isoyeta)
        {
            var info = doc.ProjectInformation;
            var s = ObtenerSchema();
            if (string.IsNullOrEmpty(zona) || isoyeta <= 0) { info.DeleteEntity(s); return; }
            var ent = new Entity(s);
            ent.Set(CampoZona, zona);
            ent.Set(CampoIsoyeta, isoyeta);
            info.SetEntity(ent);
        }

        /// <summary>Carga en la configuración la pluviometría del proyecto activo.</summary>
        public static void Aplicar(Document doc, Hs5Config cfg)
        {
            var (zona, iso) = LeerPluviometria(doc);
            cfg.ZonaProyecto = zona;
            cfg.IsoyetaProyecto = iso;
            cfg.IntensidadProyecto = zona != null ? Hs5Tablas.Intensidad(zona[0], iso) : 0;
            cfg.NombreProyecto = doc?.Title;
        }
    }

    /// <summary>Lee o guarda la pluviometría (zona e isoyeta) del proyecto activo desde el panel.</summary>
    public class EventoPluviometriaProyecto : IExternalEventHandler
    {
        public SaneamientoWindow Pagina { get; set; }
        /// <summary>null = sólo leer; un valor = guardar (Zona null = borrar).</summary>
        public (string Zona, int Isoyeta)? Guardar { get; set; }

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) return;
            string error = null;
            if (Guardar.HasValue)
            {
                try
                {
                    using (var tr = new Transaction(doc, "E3G HS5 — Pluviometría del proyecto"))
                    {
                        tr.Start();
                        Hs5Proyecto.GuardarPluviometria(doc, Guardar.Value.Zona, Guardar.Value.Isoyeta);
                        tr.Commit();
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                Guardar = null;
            }
            Hs5Proyecto.Aplicar(doc, Hs5Config.Actual);
            Pagina?.Dispatcher.Invoke(() => Pagina.MostrarPluviometriaProyecto(error));
        }

        public string GetName() => "E3G HS5 — Pluviometría del proyecto";
    }
}
