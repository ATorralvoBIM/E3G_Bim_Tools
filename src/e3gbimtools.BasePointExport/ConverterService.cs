// ============================================================================
//  ConverterService.cs
//  BasePointView — Modo conversor de .rvt.
//
//  Convierte este Revit en el «motor» del CDE: un bucle en segundo plano
//  consulta la cola del backend; cuando hay un .rvt subido lo descarga, pide a
//  Revit (vía ExternalEvent, en contexto de API) que lo convierta con
//  RvtConverter y sube los resultados. Quien consulta el visor no necesita
//  Revit: solo este equipo, con Revit abierto y el conversor activo.
//
//  Toda la red ocurre fuera del hilo de Revit; Revit solo trabaja durante la
//  conversión. Registro en %APPDATA%\BasePointView\converter.log.
// ============================================================================

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace E3G.BimTools.BasePointExport
{
    public sealed class ConverterService
    {
        public static ConverterService Instance { get; } = new();

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

        private readonly ConversionHandler handler = new();
        private ExternalEvent? externalEvent;
        private CancellationTokenSource? cts;

        /// <summary>Se dispara (en cualquier hilo) al arrancar/parar el conversor.</summary>
        public event Action<bool>? RunningChanged;

        public bool IsRunning => cts != null;

        public static string LogPath => Path.Combine(
            Path.GetDirectoryName(PublishConfig.ConfigPath)!, "converter.log");

        /// <summary>Arranca el conversor. Debe llamarse en contexto de API (comando u OnStartup).</summary>
        public void Start()
        {
            if (IsRunning) return;
            externalEvent ??= ExternalEvent.Create(handler);
            cts = new CancellationTokenSource();
            var token = cts.Token;
            _ = Task.Run(() => LoopAsync(token));
            Log("Conversor iniciado en " + Environment.MachineName);
            RunningChanged?.Invoke(true);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            cts!.Cancel();
            cts = null;
            Log("Conversor detenido.");
            RunningChanged?.Invoke(false);
        }

        // --- Bucle (hilo de fondo) ---------------------------------------------

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var config = PublishConfig.LoadOrCreate();
                var delay = TimeSpan.FromSeconds(Math.Max(5, config.ConverterPollSeconds));
                try
                {
                    if (string.IsNullOrWhiteSpace(config.ApiKey))
                    {
                        Log("Sin clave de API en " + PublishConfig.ConfigPath + "; esperando.");
                    }
                    else
                    {
                        var job = await ClaimAsync(config, token);
                        if (job != null)
                        {
                            await ProcessAsync(config, job, token);
                            continue; // puede haber más en cola: no esperar
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log("Error consultando la cola: " + ex.Message);
                }

                try { await Task.Delay(delay, token); }
                catch (OperationCanceledException) { return; }
            }
        }

        private sealed class Job
        {
            public string ProjectId { get; set; } = "";
            public string ItemId { get; set; } = "";
            public int Version { get; set; }
            public string Name { get; set; } = "";
            public string RvtUrl { get; set; } = "";
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static async Task<Job?> ClaimAsync(PublishConfig config, CancellationToken token)
        {
            using var request = Request(config, HttpMethod.Post, "/api/conversions/claim");
            request.Content = JsonContent.Create(new { worker = Worker });
            using var response = await Http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            await EnsureOk(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return doc.RootElement.GetProperty("job").Deserialize<Job>(JsonOptions);
        }

        private async Task ProcessAsync(PublishConfig config, Job job, CancellationToken token)
        {
            var work = Path.Combine(Path.GetTempPath(), "bpv-conv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var started = DateTime.Now;
            Log($"Convirtiendo «{job.Name}» v{job.Version}…");
            using var progress = new ProgressReporter(config, job);
            try
            {
                progress.Set(2, "Descargando el .rvt");
                var rvt = Path.Combine(work, SafeFileName(job.Name, ".rvt"));
                await DownloadAsync(config, job.RvtUrl, rvt, token);

                progress.Set(10, "Esperando a que Revit esté libre");
                var result = await handler.RunAsync(
                    externalEvent!, rvt, Path.Combine(work, "out"), config.ConverterMaxPlans, progress.Set);
                foreach (var w in result.Warnings) Log("  aviso: " + w);

                progress.Set(95, "Subiendo los resultados");
                await UploadAsync(config, job, result, token);
                Log($"«{job.Name}» convertido en {(DateTime.Now - started).TotalSeconds:F0} s: " +
                    $"{result.Elements} elementos, {result.Sheets.Count} láminas, " +
                    $"{result.Plans.Count} vistas, {result.Tables.Count} tablas.");
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Log($"«{job.Name}» falló: {ex}");
                try { await FailAsync(config, job, ex.Message, CancellationToken.None); }
                catch (Exception inner) { Log("No se pudo informar del fallo: " + inner.Message); }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* temporal: ignorar */ }
            }
        }

        // --- HTTP ----------------------------------------------------------------

        private static string Worker => Environment.MachineName + "\\" + Environment.UserName;

        private static HttpRequestMessage Request(PublishConfig config, HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, config.BackendUrl.TrimEnd('/') + path);
            request.Headers.Add("X-API-Key", config.ApiKey);
            request.Headers.Add("X-User", Worker);
            return request;
        }

        private static async Task EnsureOk(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception("El backend respondió " + (int)response.StatusCode + ": " + body);
        }

        private static async Task DownloadAsync(PublishConfig config, string url, string target, CancellationToken token)
        {
            using var request = Request(config, HttpMethod.Get, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await EnsureOk(response);
            await using var file = File.Create(target);
            await response.Content.CopyToAsync(file, token);
        }

        private static async Task UploadAsync(PublishConfig config, Job job, ConversionResult result, CancellationToken token)
        {
            using var content = new MultipartFormDataContent();
            var plans = new object[result.Plans.Count];
            var sheets = new object[result.Sheets.Count];
            var tables = new object[result.Tables.Count];

            if (result.GlbPath != null) AddFile(content, "glb", result.GlbPath, "model/gltf-binary");
            if (result.JsonPath != null) AddFile(content, "json", result.JsonPath, "application/json");
            for (var i = 0; i < result.Plans.Count; i++)
            {
                var (name, path, georef) = result.Plans[i];
                AddFile(content, $"plan_{i}", path, "image/png");
                plans[i] = new { name, georef = JsonDocument.Parse(georef).RootElement, file = $"plan_{i}" };
            }
            for (var i = 0; i < result.Sheets.Count; i++)
            {
                var (name, path) = result.Sheets[i];
                AddFile(content, $"sheet_{i}", path, "application/pdf");
                sheets[i] = new { name, file = $"sheet_{i}" };
            }
            for (var i = 0; i < result.Tables.Count; i++)
            {
                var (name, path) = result.Tables[i];
                AddFile(content, $"table_{i}", path, "application/json");
                tables[i] = new { name, file = $"table_{i}" };
            }
            var manifest = new { elements = result.Elements, plans, sheets, tables, warnings = result.Warnings };
            content.Add(new StringContent(JsonSerializer.Serialize(manifest)), "manifest");

            using var request = Request(config, HttpMethod.Post,
                $"/api/conversions/{job.ProjectId}/{job.ItemId}/{job.Version}/complete");
            request.Content = content;
            using var response = await Http.SendAsync(request, token);
            await EnsureOk(response);
        }

        private static void AddFile(MultipartFormDataContent content, string field, string path, string type)
        {
            var part = new StreamContent(File.OpenRead(path));
            part.Headers.ContentType = new MediaTypeHeaderValue(type);
            content.Add(part, field, Path.GetFileName(path));
        }

        private static async Task FailAsync(PublishConfig config, Job job, string error, CancellationToken token)
        {
            using var request = Request(config, HttpMethod.Post,
                $"/api/conversions/{job.ProjectId}/{job.ItemId}/{job.Version}/fail");
            request.Content = JsonContent.Create(new { error });
            using var response = await Http.SendAsync(request, token);
            await EnsureOk(response);
        }

        /// <summary>
        /// Envía el avance al backend cada 2 s si ha cambiado. <see cref="Set"/> solo
        /// guarda el valor (se llama desde el hilo de Revit sin bloquearlo); la red
        /// va en un bucle aparte y sus fallos se ignoran: el avance es orientativo.
        /// </summary>
        private sealed class ProgressReporter : IDisposable
        {
            private readonly CancellationTokenSource stop = new();
            private volatile int percent;
            private volatile string stage = "";

            public ProgressReporter(PublishConfig config, Job job)
            {
                _ = Task.Run(() => LoopAsync(config, job, stop.Token));
            }

            public void Set(int value, string label)
            {
                percent = value;
                stage = label;
            }

            private async Task LoopAsync(PublishConfig config, Job job, CancellationToken token)
            {
                int sentPercent = -1;
                string sentStage = "";
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var (p, s) = (percent, stage);
                        if (p != sentPercent || s != sentStage)
                        {
                            using var request = Request(config, HttpMethod.Post,
                                $"/api/conversions/{job.ProjectId}/{job.ItemId}/{job.Version}/progress");
                            request.Content = JsonContent.Create(new { progress = p, stage = s });
                            using var response = await Http.SendAsync(request, token);
                            (sentPercent, sentStage) = (p, s);
                        }
                        await Task.Delay(TimeSpan.FromSeconds(2), token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch { /* avance orientativo: se reintenta en la siguiente vuelta */ }
                }
            }

            public void Dispose() => stop.Cancel();
        }

        // --- Utilidades ----------------------------------------------------------

        private static string SafeFileName(string name, string ext)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
        }

        private static readonly object LogLock = new();

        public static void Log(string line)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // el registro nunca debe tumbar el conversor
            }
        }

        // --- Contexto de API -----------------------------------------------------

        /// <summary>
        /// Ejecuta la conversión en el hilo de Revit. Mientras dura, responde
        /// automáticamente a los diálogos (vínculos no encontrados, avisos de
        /// apertura…) y descarta las advertencias, para que nadie tenga que estar
        /// delante del equipo.
        /// </summary>
        private sealed class ConversionHandler : IExternalEventHandler
        {
            private string rvtPath = "";
            private string outDir = "";
            private int maxPlans;
            private Action<int, string> report = (_, _) => { };
            private TaskCompletionSource<ConversionResult>? pending;

            public Task<ConversionResult> RunAsync(
                ExternalEvent ev, string rvt, string output, int plans, Action<int, string> progress)
            {
                rvtPath = rvt;
                outDir = output;
                maxPlans = plans;
                report = progress;
                pending = new TaskCompletionSource<ConversionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var request = ev.Raise();
                if (request != ExternalEventRequest.Accepted)
                    pending.SetException(new Exception("Revit no aceptó la conversión (" + request + ")."));
                return pending.Task;
            }

            public void Execute(UIApplication app)
            {
                var tcs = pending;
                if (tcs == null) return;
                app.DialogBoxShowing += OnDialog;
                app.Application.FailuresProcessing += OnFailures;
                try
                {
                    tcs.SetResult(RvtConverter.Convert(app, rvtPath, outDir, maxPlans, report));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    app.DialogBoxShowing -= OnDialog;
                    app.Application.FailuresProcessing -= OnFailures;
                }
            }

            public string GetName() => "BasePointView — Conversor de .rvt";

            private static void OnDialog(object? sender, DialogBoxShowingEventArgs e)
            {
                Log("  diálogo descartado: " + e.DialogId);
                // Cerrar los TaskDialog y aceptar los cuadros de mensaje (IDOK).
                e.OverrideResult(e is TaskDialogShowingEventArgs ? (int)TaskDialogResult.Close : 1);
            }

            private static void OnFailures(object? sender, FailuresProcessingEventArgs e)
            {
                e.GetFailuresAccessor().DeleteAllWarnings();
            }
        }
    }
}
