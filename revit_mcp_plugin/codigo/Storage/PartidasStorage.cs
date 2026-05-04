// ============================================================================
//  PartidasStorage.cs
//  Persistencia de la Plantilla en el .rvt vía Revit ExtensibleStorage.
//
//  ⚠ REGENERAR el SchemaGuid antes de poner en producción. Una vez fijado,
//  NUNCA se cambia.
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Storage
{
    public static class PartidasStorage
    {
        // ⚠ REGENERA ESTE GUID antes de poner en producción.
        private static readonly Guid SchemaGuid =
            new Guid("7B8C9D2E-1F3A-4B5C-9D6E-7F8A9B0C1D2E");

        private const string SchemaName = "E3GPresupuestoPlantilla";
        private const string DataStorageName = "E3G.Saneamiento.Presupuesto.Plantilla";
        private const string FieldVersion = "SchemaVersion";
        private const string FieldJson = "PlantillaJson";

        public const int SchemaVersionActual = 1;

        public static Plantilla Cargar(Document doc)
        {
            var entity = ObtenerEntity(doc);
            if (entity == null || !entity.IsValid())
                return PlantillaPorDefecto.Crear();

            int version = entity.Get<int>(FieldVersion);
            if (version > SchemaVersionActual)
                throw new InvalidOperationException(
                    $"La plantilla del .rvt és de la versió {version}; aquesta versió de l'app només llegeix fins a {SchemaVersionActual}. Actualitza l'add-in.");

            string json = entity.Get<string>(FieldJson);
            if (string.IsNullOrWhiteSpace(json))
                return PlantillaPorDefecto.Crear();

            json = MigrarJsonSiCal(json, version);
            return DeserializarPlantilla(json);
        }

        /// <summary>
        /// DEBE llamarse dentro de una Transaction abierta por el caller.
        /// </summary>
        public static void Guardar(Document doc, Plantilla plantilla)
        {
            var schema = ObtenerOCrearSchema();
            var dataStorage = ObtenerOCrearDataStorage(doc);

            string json = SerializarPlantilla(plantilla);

            var entity = new Entity(schema);
            entity.Set(FieldVersion, SchemaVersionActual);
            entity.Set(FieldJson, json);

            dataStorage.SetEntity(entity);
        }

        /// <summary>DEBE llamarse dentro de una Transaction.</summary>
        public static void Limpiar(Document doc)
        {
            var ds = ObtenerDataStorageExistente(doc);
            if (ds != null) doc.Delete(ds.Id);
        }

        // ─── Schema y DataStorage ──────────────────────────────────────────
        private static Schema ObtenerOCrearSchema()
        {
            var existente = Schema.Lookup(SchemaGuid);
            if (existente != null) return existente;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName(SchemaName);
            sb.SetVendorId("E3GBimTools"); // 4-19 ASCII alfanumèrics, comença per lletra
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(FieldVersion, typeof(int));
            sb.AddSimpleField(FieldJson, typeof(string));
            return sb.Finish();
        }

        private static DataStorage ObtenerDataStorageExistente(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .OfType<DataStorage>()
                .FirstOrDefault(ds => ds.Name == DataStorageName);

        private static DataStorage ObtenerOCrearDataStorage(Document doc) =>
            ObtenerDataStorageExistente(doc) ?? CrearDataStorage(doc);

        private static DataStorage CrearDataStorage(Document doc)
        {
            var ds = DataStorage.Create(doc);
            ds.Name = DataStorageName;
            return ds;
        }

        private static Entity ObtenerEntity(Document doc)
        {
            var ds = ObtenerDataStorageExistente(doc);
            if (ds == null) return null;
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;
            var entity = ds.GetEntity(schema);
            return entity != null && entity.IsValid() ? entity : null;
        }

        // ─── Serialización JSON ─────────────────────────────────────────────
        private static JsonSerializerOptions OpcionesJson() => new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new SubFiltroJsonConverter(),
                new CriterioJsonConverter(),
                new JsonStringEnumConverter()
            }
        };

        private static string SerializarPlantilla(Plantilla p) =>
            JsonSerializer.Serialize(p, OpcionesJson());

        private static Plantilla DeserializarPlantilla(string json) =>
            JsonSerializer.Deserialize<Plantilla>(json, OpcionesJson());

        private static string MigrarJsonSiCal(string json, int versionLeida)
        {
            // Cuando entre P3 (v2) aquí entran las transformaciones JSON.
            return json;
        }
    }

    // ─── Conversores polimórficos ──────────────────────────────────────────
    public sealed class SubFiltroJsonConverter : JsonConverter<ISubFiltro>
    {
        public override ISubFiltro Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("$tipo", out var tipoEl))
                return new SubFiltroNinguno();

            var tipo = tipoEl.GetString();
            var raw = root.GetRawText();

            return tipo switch
            {
                "ninguno"        => JsonSerializer.Deserialize<SubFiltroNinguno>(raw, SinConv(options)),
                "diametroExacto" => JsonSerializer.Deserialize<SubFiltroDiametroExacto>(raw, SinConv(options)),
                "rangoDiametro"  => JsonSerializer.Deserialize<SubFiltroRangoDiametro>(raw, SinConv(options)),
                "parametro"      => JsonSerializer.Deserialize<SubFiltroParametro>(raw, SinConv(options)),
                _                => new SubFiltroNinguno()
            };
        }

        public override void Write(Utf8JsonWriter writer, ISubFiltro value, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), SinConv(options)));
            writer.WriteStartObject();
            writer.WriteString("$tipo", value.Tipo);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "$tipo" || prop.Name == "tipo") continue;
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        private static JsonSerializerOptions SinConv(JsonSerializerOptions src)
        {
            var clone = new JsonSerializerOptions(src);
            for (int i = clone.Converters.Count - 1; i >= 0; i--)
                if (clone.Converters[i] is SubFiltroJsonConverter)
                    clone.Converters.RemoveAt(i);
            return clone;
        }
    }

    public sealed class CriterioJsonConverter : JsonConverter<ICriterio>
    {
        public override ICriterio Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("$tipo", out var tipoEl))
                return new CriterioLongitudTotal();

            var tipo = tipoEl.GetString();
            var raw = root.GetRawText();

            return tipo switch
            {
                "longitudTotal" => JsonSerializer.Deserialize<CriterioLongitudTotal>(raw, SinConv(options)),
                "contar"        => JsonSerializer.Deserialize<CriterioContar>(raw, SinConv(options)),
                _               => new CriterioLongitudTotal()
            };
        }

        public override void Write(Utf8JsonWriter writer, ICriterio value, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), SinConv(options)));
            writer.WriteStartObject();
            writer.WriteString("$tipo", value.Tipo);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "$tipo" || prop.Name == "tipo") continue;
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        private static JsonSerializerOptions SinConv(JsonSerializerOptions src)
        {
            var clone = new JsonSerializerOptions(src);
            for (int i = clone.Converters.Count - 1; i >= 0; i--)
                if (clone.Converters[i] is CriterioJsonConverter)
                    clone.Converters.RemoveAt(i);
            return clone;
        }
    }
}
