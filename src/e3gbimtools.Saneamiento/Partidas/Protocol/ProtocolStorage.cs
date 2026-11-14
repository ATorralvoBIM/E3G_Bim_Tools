// revit_mcp_plugin/codigo/Protocol/ProtocolStorage.cs
using System;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace E3G.Saneamiento.Protocol
{
    /// <summary>
    /// Persisteix el ProtocolInstalacio al .rvt via ExtensibleStorage.
    /// Mateix patró que PartidasStorage / ClasificacionSistemasService.
    /// </summary>
    public sealed class ProtocolStorage
    {
        // GUID únic — NO canviar un cop en producció
        private static readonly Guid SchemaGuid = new("A7F3C2D1-88E4-4B5F-9C6A-1D2E3F4A5B6C");
        private const string FieldNom = "ProtocolJson";
        private const string DataNom = "E3G_ProtocolSaneament";
        private const int SchemaVersio = 1;

        // ── Llegir ──────────────────────────────────────────────────────────
        public ProtocolInstalacio Cargar(Document doc)
        {
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return ProtocolInstalacio.Buit();

                var ds = ObtenirDataStorage(doc);
                if (ds == null) return ProtocolInstalacio.Buit();

                var entity = ds.GetEntity(schema);
                if (!entity.IsValid()) return ProtocolInstalacio.Buit();

                var json = entity.Get<string>(schema.GetField(FieldNom));
                if (string.IsNullOrEmpty(json)) return ProtocolInstalacio.Buit();

                return JsonSerializer.Deserialize<ProtocolInstalacio>(json)
                       ?? ProtocolInstalacio.Buit();
            }
            catch { return ProtocolInstalacio.Buit(); }
        }

        // ── Guardar ─────────────────────────────────────────────────────────
        public bool Guardar(Document doc, ProtocolInstalacio protocol)
        {
            try
            {
                using var tx = new Transaction(doc, "E3G: Guardar Protocol");
                tx.Start();
                var schema = ObtenerOCrearSchema();
                var ds = ObtenirOCrearDataStorage(doc, schema);
                var entity = new Entity(schema);
                entity.Set(schema.GetField(FieldNom),
                           JsonSerializer.Serialize(protocol));
                ds.SetEntity(entity);
                tx.Commit();
                return true;
            }
            catch { return false; }
        }

        // ── Schema ───────────────────────────────────────────────────────────
        private Schema ObtenerOCrearSchema()
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("E3G_ProtocolSaneament_v" + SchemaVersio);
            sb.SetVendorId("E3G_BIMT");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(FieldNom, typeof(string));
            return sb.Finish();
        }

        // ── DataStorage ──────────────────────────────────────────────────────
        private DataStorage? ObtenirDataStorage(Document doc)
        {
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.GetEntity(schema).IsValid());
        }

        private DataStorage ObtenirOCrearDataStorage(Document doc, Schema schema)
        {
            var existent = ObtenirDataStorage(doc);
            if (existent != null) return existent;

            var ds = DataStorage.Create(doc);
            ds.Name = DataNom;
            return ds;
        }
    }
}