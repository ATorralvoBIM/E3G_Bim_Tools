// ============================================================================
//  PublishCommand.cs
//  BasePointView — Exportador Revit (Sprint 9).
//
//  «Publicar al visor»: exporta la vista 3D activa a GLB + JSON (en archivos
//  temporales) y los sube al backend de BasePointView por HTTP, sin que el
//  usuario maneje ningún archivo.  El backend versiona la publicación y el
//  modelo aparece en el catálogo del visor web.
//
//  La clave de API y la URL del backend se leen de la configuración del usuario
//  (%APPDATA%\BasePointView\config.json), nunca del binario.
// ============================================================================

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class PublishCommand : IExternalCommand
    {
        private const string Title = "BasePointView — Publicar al visor";
        private static readonly HttpClient Http = new();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var document = uidoc?.Document;
                if (document == null)
                {
                    message = "No hay documento activo.";
                    return Result.Failed;
                }

                var config = PublishConfig.LoadOrCreate();
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    TaskDialog.Show(Title,
                        "Configura la URL del backend y la clave de API en:\n\n" +
                        PublishConfig.ConfigPath + "\n\n" +
                        "(se ha creado una plantilla). Después vuelve a publicar.");
                    return Result.Cancelled;
                }

                var view3d = GlbExportRunner.ResolveView3D(uidoc!);
                if (view3d == null)
                {
                    TaskDialog.Show(Title, "Abre o crea una vista 3D antes de publicar.");
                    return Result.Cancelled;
                }

                var basePath = Path.Combine(Path.GetTempPath(), "bpv-" + Guid.NewGuid().ToString("N"));
                var glbPath = basePath + ".glb";
                var jsonPath = basePath + ".json";

                try
                {
                    var result = GlbExportRunner.Export(document, view3d, glbPath, jsonPath);

                    var project = Fallback(config.Project, DocumentName(document));
                    var model = Fallback(config.Model, view3d.Name);

                    // CDE: si hay ProjectId configurado, subir al árbol del proyecto;
                    // si no, publicar en el catálogo plano (comportamiento clásico).
                    if (!string.IsNullOrWhiteSpace(config.ProjectId))
                    {
                        var folder = Fallback(config.Folder, "Modelos");
                        PublishToProject(config, model, folder, glbPath, jsonPath);
                        TaskDialog.Show(Title,
                            "Publicado al proyecto del CDE.\n\n" +
                            "Proyecto (id): " + config.ProjectId + "\n" +
                            "Carpeta: " + folder + "\n" +
                            "Modelo: " + model + "\n" +
                            "Elementos: " + result.Elements + " · Mallas: " + result.Meshes +
                            " · Triángulos: " + result.Triangles + "\n\n" +
                            "Ábrelo en el visor desde el panel «Proyecto».");
                        return Result.Succeeded;
                    }

                    var version = Publish(config.BackendUrl, config.ApiKey, project, model, glbPath, jsonPath);

                    TaskDialog.Show(Title,
                        "Publicado al visor.\n\n" +
                        "Proyecto: " + project + "\n" +
                        "Modelo: " + model + "\n" +
                        "Versión: " + version + "\n" +
                        "Elementos: " + result.Elements + " · Mallas: " + result.Meshes +
                        " · Triángulos: " + result.Triangles + "\n\n" +
                        "Ábrelo en el visor web desde el botón «Catálogo».");
                    return Result.Succeeded;
                }
                finally
                {
                    TryDelete(glbPath);
                    TryDelete(jsonPath);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(Title, "Excepción al publicar:\n\n" + ex);
                return Result.Failed;
            }
        }

        /// <summary>Sube GLB + JSON al endpoint de ingesta. Devuelve la versión asignada.</summary>
        private static int Publish(
            string backendUrl, string apiKey, string project, string model,
            string glbPath, string jsonPath)
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(project), "project" },
                { new StringContent(model), "model" },
            };

            var glb = new ByteArrayContent(File.ReadAllBytes(glbPath));
            glb.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
            content.Add(glb, "glb", "model.glb");

            var json = new ByteArrayContent(File.ReadAllBytes(jsonPath));
            json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Add(json, "json", "model.json");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, backendUrl.TrimEnd('/') + "/api/ingest");
            request.Headers.Add("X-API-Key", apiKey);
            request.Content = content;

            using var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new Exception("El backend respondió " + (int)response.StatusCode + ":\n" + body);

            using var parsed = JsonDocument.Parse(body);
            return parsed.RootElement.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
        }

        /// <summary>Sube el modelo (GLB+JSON) a una carpeta del proyecto del CDE.</summary>
        private static void PublishToProject(
            PublishConfig config, string name, string folder, string glbPath, string jsonPath)
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent("model"), "kind" },
                { new StringContent(name), "name" },
                { new StringContent(folder), "folder" },
            };

            var glb = new ByteArrayContent(File.ReadAllBytes(glbPath));
            glb.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
            content.Add(glb, "glb", "model.glb");

            var json = new ByteArrayContent(File.ReadAllBytes(jsonPath));
            json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Add(json, "json", "model.json");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                config.BackendUrl.TrimEnd('/') + "/api/projects/" + config.ProjectId + "/items");
            request.Headers.Add("X-API-Key", config.ApiKey);
            request.Headers.Add("X-User", Environment.UserName);
            request.Content = content;

            using var response = Http.SendAsync(request).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new Exception("El backend respondió " + (int)response.StatusCode + ":\n" + body);
        }

        private static string Fallback(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string DocumentName(Document document)
            => string.IsNullOrWhiteSpace(document.Title) ? "Proyecto" : document.Title;

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* archivo temporal: ignorar */ }
        }
    }
}
