// ============================================================================
//  PublishConfig.cs
//  BasePointView — Exportador Revit (Sprint 9).
//
//  Configuración de publicación (URL del backend + clave de API). Vive en el
//  perfil del usuario (%APPDATA%\BasePointView\config.json), NUNCA dentro del
//  binario distribuido, de modo que cada cliente usa su propia clave.
// ============================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace E3G.BimTools.BasePointExport
{
    public sealed class PublishConfig
    {
        /// <summary>URL base del backend de ingesta/catálogo.</summary>
        public string BackendUrl { get; set; } = "http://localhost:3001";

        /// <summary>Clave de API para autenticar la ingesta.</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>Proyecto (opcional; por defecto, el nombre del documento).</summary>
        public string Project { get; set; } = "";

        /// <summary>Modelo (opcional; por defecto, el nombre de la vista 3D).</summary>
        public string Model { get; set; } = "";

        /// <summary>
        /// Id de proyecto del CDE (opcional). Si se indica, «Publicar al visor» y
        /// «Publicar lámina» suben al árbol del proyecto (/api/projects/&lt;id&gt;/items)
        /// en lugar del catálogo plano. Se obtiene del panel «Proyecto» del visor.
        /// </summary>
        public string ProjectId { get; set; } = "";

        /// <summary>
        /// Carpeta destino dentro del proyecto (por nombre; se crea si no existe).
        /// Por defecto «Modelos» para modelos y «Planos» para láminas.
        /// </summary>
        public string Folder { get; set; } = "";

        /// <summary>
        /// Modo conversor: arrancarlo automáticamente al abrir Revit, para que el
        /// equipo convierta los .rvt subidos al CDE sin intervención.
        /// </summary>
        public bool ConverterAutoStart { get; set; } = false;

        /// <summary>Segundos entre consultas a la cola de conversión.</summary>
        public int ConverterPollSeconds { get; set; } = 15;

        /// <summary>Máximo de plantas georreferenciadas a exportar por modelo.</summary>
        public int ConverterMaxPlans { get; set; } = 40;

        /// <summary>Ruta del archivo de configuración en el perfil del usuario.</summary>
        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BasePointView", "config.json");

        /// <summary>
        /// Motivo por el que no se pudo leer el archivo (null si se leyó bien o
        /// no existía). Los comandos lo muestran en vez de un aviso genérico.
        /// </summary>
        [JsonIgnore]
        public string? LoadError { get; private set; }

        /// <summary>
        /// Carga la configuración; si no existe, crea una plantilla. Se lee campo
        /// a campo con JsonDocument (sin serialización por reflexión, que el host
        /// de Revit 2026 puede restringir) y un archivo ilegible NUNCA se
        /// sobrescribe: se informa del error en <see cref="LoadError"/>.
        /// </summary>
        public static PublishConfig LoadOrCreate()
        {
            var path = ConfigPath;
            var config = new PublishConfig();
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, config.ToJson());
                return config;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    switch (p.Name.ToLowerInvariant())
                    {
                        case "backendurl": config.BackendUrl = Str(p.Value, config.BackendUrl); break;
                        case "apikey": config.ApiKey = Str(p.Value, config.ApiKey); break;
                        case "project": config.Project = Str(p.Value, config.Project); break;
                        case "model": config.Model = Str(p.Value, config.Model); break;
                        case "projectid": config.ProjectId = Str(p.Value, config.ProjectId); break;
                        case "folder": config.Folder = Str(p.Value, config.Folder); break;
                        case "converterautostart":
                            config.ConverterAutoStart = p.Value.ValueKind == JsonValueKind.True;
                            break;
                        case "converterpollseconds":
                            if (p.Value.TryGetInt32(out var poll)) config.ConverterPollSeconds = poll;
                            break;
                        case "convertermaxplans":
                            if (p.Value.TryGetInt32(out var plans)) config.ConverterMaxPlans = plans;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                config.LoadError = ex.GetType().Name + ": " + ex.Message;
            }
            return config;
        }

        private static string Str(JsonElement value, string fallback)
            => value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

        /// <summary>Plantilla del archivo (escrita a mano: sin reflexión).</summary>
        private string ToJson()
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("backendUrl", BackendUrl);
                w.WriteString("apiKey", ApiKey);
                w.WriteString("project", Project);
                w.WriteString("model", Model);
                w.WriteString("projectId", ProjectId);
                w.WriteString("folder", Folder);
                w.WriteBoolean("converterAutoStart", ConverterAutoStart);
                w.WriteNumber("converterPollSeconds", ConverterPollSeconds);
                w.WriteNumber("converterMaxPlans", ConverterMaxPlans);
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Texto para el aviso de «falta configuración»: indica el archivo y, si
        /// no se pudo leer, el motivo exacto.
        /// </summary>
        public string MissingConfigMessage() =>
            (LoadError != null
                ? "No se pudo leer la configuración:\n" + LoadError + "\n\nArchivo: "
                : "Falta la clave de API (apiKey). Configura la URL del backend y la clave en:\n\n") +
            ConfigPath;
    }
}
