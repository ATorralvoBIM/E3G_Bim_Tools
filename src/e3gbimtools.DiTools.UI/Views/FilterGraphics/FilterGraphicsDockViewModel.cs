using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.UI.RevitBridge;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>Qué modificación edita el usuario desde el panel.</summary>
public enum FilterGraphicsEdit { LineColor, PatternColor }

/// <summary>
/// ViewModel del panel acoplable "Grafismo de filtros".
///
/// Sigue la vista activa en vivo. El destino es la plantilla de la vista
/// solo si esa plantilla CONTROLA "V/G Filtros"; si no, la propia vista
/// (ver <see cref="FilterTargets"/>). Lista los filtros con el resumen de
/// proyección/superficie del cuadro V/G, permite editarlos al vuelo y
/// exportar/importar su grafismo (y sus reglas) a JSON.
///
/// El panel es modeless: todo lo que toca la API va por
/// <see cref="WorksetDockExternalEventBus"/>. Las ventanas de importación y
/// auditoría se abren DENTRO del bus para tener contexto API mientras están abiertas.
/// </summary>
public sealed class FilterGraphicsDockViewModel : INotifyPropertyChanged
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private UIApplication?               _uiApp;
    private WorksetDockExternalEventBus? _bus;
    private bool                         _initialized;
    private string?                      _targetName;

    public ObservableCollection<FilterGraphicsRowItem>    Filters { get; } = new();
    public ObservableCollection<FilterGraphicsPresetFile> Presets { get; } = new();

    private string _targetLabel = "Sin vista activa.";
    public string TargetLabel
    {
        get => _targetLabel;
        private set { _targetLabel = value; OnPropertyChanged(); }
    }

    private string _targetWarning = string.Empty;
    /// <summary>Aviso "afecta a N vistas" cuando el destino es una plantilla.</summary>
    public string TargetWarning
    {
        get => _targetWarning;
        private set { _targetWarning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTargetWarning)); }
    }
    public bool HasTargetWarning => !string.IsNullOrEmpty(_targetWarning);

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private FilterGraphicsPresetFile? _selectedPreset;
    public FilterGraphicsPresetFile? SelectedPreset
    {
        get => _selectedPreset;
        set { _selectedPreset = value; OnPropertyChanged(); }
    }

    public string LibraryLabel => FilterGraphicsLibrary.LibraryFolder is { } lib
        ? $"Biblioteca: {lib}"
        : "Sin biblioteca compartida (solo presets locales).";

    public bool HasFilters => Filters.Count > 0;

    /// <summary>Mensajes de resultado para la UI (título, texto).</summary>
    public event Action<string, string>? Notify;

    public void Initialize(UIApplication uiApp, WorksetDockExternalEventBus bus)
    {
        _uiApp       = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        _bus         = bus   ?? throw new ArgumentNullException(nameof(bus));
        _initialized = true;
        RefreshPresets();
    }

    public void RequestRefresh()
    {
        if (!_initialized) return;
        RefreshPresets();
        EnqueueSafe(_ => LoadFromRevit());
    }

    /// <summary>
    /// Lee los filtros de la vista/plantilla destino. DEBE ejecutarse en
    /// contexto de la API (ViewActivated o dentro del bus).
    /// </summary>
    public void LoadFromRevit()
    {
        if (!_initialized) return;

        var doc = _uiApp!.ActiveUIDocument?.Document;
        if (doc is null) { ClearTo("Sin documento activo."); return; }

        var target = FilterTargets.ResolveActive(doc);
        if (target is null || !SafeAllowsOverrides(target))
        {
            ClearTo("La vista activa no admite filtros.");
            return;
        }

        _targetName   = target.Name;
        TargetLabel   = FilterTargets.Describe(doc, target, out var warning);
        TargetWarning = warning;

        var previouslyChecked = new HashSet<string>(
            Filters.Where(f => f.IsChecked).Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        var mapper = new FilterGraphicsMapper(doc);
        Filters.Clear();
        foreach (var id in FilterGraphicsImporter.OrderedFilters(target))
        {
            if (doc.GetElement(id) is not FilterElement fe) continue;
            FilterGraphicsEntry entry;
            try { entry = mapper.Read(target, id, fe.Name); }
            catch { continue; }

            var row = new FilterGraphicsRowItem(id, entry) { IsChecked = previouslyChecked.Contains(fe.Name) };
            row.Toggled += OnRowToggled;
            Filters.Add(row);
        }

        OnPropertyChanged(nameof(HasFilters));
        Status = $"{Filters.Count} filtro(s) en la {(target.IsTemplate ? "plantilla" : "vista")}.";
    }

    public void SetAllChecked(bool value)
    {
        foreach (var f in Filters) f.IsChecked = value;
    }

    // ------------------------------------------------------------------ //
    //  Presets / biblioteca
    // ------------------------------------------------------------------ //

    public void RefreshPresets()
    {
        var keep = SelectedPreset?.Path;
        Presets.Clear();
        foreach (var p in FilterGraphicsLibrary.ListPresets()) Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(p => string.Equals(p.Path, keep, StringComparison.OrdinalIgnoreCase))
                         ?? Presets.FirstOrDefault();
        OnPropertyChanged(nameof(LibraryLabel));
    }

    public void SetLibraryFolder(string? folder)
    {
        FilterGraphicsLibrary.LibraryFolder = folder;
        RefreshPresets();
    }

    /// <summary>Carpeta por defecto para guardar: la biblioteca si existe, si no la local.</summary>
    public string DefaultSaveFolder()
    {
        var lib = FilterGraphicsLibrary.LibraryFolder;
        return lib is not null && Directory.Exists(lib) ? lib : FilterGraphicsLibrary.EnsureLocalFolder();
    }

    public string SuggestedFileName()
    {
        var name = string.IsNullOrWhiteSpace(_targetName) ? "Filtros" : _targetName!;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + ".json";
    }

    // ------------------------------------------------------------------ //
    //  Exportar (grafismo + reglas)
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Exporta los filtros marcados (o todos si no hay ninguno marcado) con su
    /// grafismo y su definición. Se ejecuta en el bus para leer las reglas.
    /// </summary>
    public void ExportTo(string path)
    {
        if (!_initialized) return;
        var names = Filters.Where(f => f.IsChecked).Select(f => f.Name).ToList();
        var onlyChecked = names.Count > 0;
        Status = "Exportando…";

        EnqueueSafe(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            var target = doc is null ? null : FilterTargets.ResolveActive(doc);
            if (doc is null || target is null) return;

            var mapper  = new FilterGraphicsMapper(doc);
            var entries = new List<FilterGraphicsEntry>();
            int noRules = 0;
            foreach (var id in FilterGraphicsImporter.OrderedFilters(target))
            {
                if (doc.GetElement(id) is not FilterElement fe) continue;
                if (onlyChecked && !names.Contains(fe.Name, StringComparer.OrdinalIgnoreCase)) continue;

                var e = mapper.Read(target, id, fe.Name);
                e.Definition = FilterDefinitionSerializer.Read(doc, fe);
                if (e.Definition is null) noRules++;
                entries.Add(e);
            }

            if (entries.Count == 0)
            {
                Notify?.Invoke("Grafismo de filtros", "La vista no tiene filtros que exportar.");
                return;
            }

            var preset = new FilterGraphicsPreset
            {
                SourceDocument = doc.Title,
                SourceView     = target.Name,
                ExportedAt     = DateTime.Now,
                Filters        = entries,
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOpts), Encoding.UTF8);
                Status = $"Exportados {entries.Count} filtro(s) a {Path.GetFileName(path)}.";
                RefreshPresets();
                Notify?.Invoke("Grafismo de filtros",
                    $"Exportados {entries.Count} filtro(s) con su grafismo" +
                    (noRules > 0 ? $" ({noRules} sin reglas: filtros de selección o reglas no soportadas)" : " y sus reglas") +
                    $".\n\n{path}");
            }
            catch (Exception ex)
            {
                Notify?.Invoke("Grafismo de filtros", $"No se pudo exportar:\n{ex.Message}");
            }
        });
    }

    // ------------------------------------------------------------------ //
    //  Importar (ventana con destinos múltiples + vista previa)
    // ------------------------------------------------------------------ //

    public void ImportFrom(string path)
    {
        if (!_initialized) return;

        FilterGraphicsPreset? preset;
        try { preset = JsonSerializer.Deserialize<FilterGraphicsPreset>(File.ReadAllText(path), JsonOpts); }
        catch (Exception ex)
        {
            Notify?.Invoke("Grafismo de filtros", $"No se pudo leer el fichero:\n{ex.Message}");
            return;
        }
        if (preset?.Filters is not { Count: > 0 })
        {
            Notify?.Invoke("Grafismo de filtros", "El fichero no contiene filtros.");
            return;
        }

        var presetName = Path.GetFileNameWithoutExtension(path);
        EnqueueSafe(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null) return;

            var target = FilterTargets.ResolveActive(doc);
            var win = new FilterImportWindow(doc, preset, presetName,
                target is not null && SafeAllowsOverrides(target) ? target : null, addMissingDefault: true);
            SetOwner(win, app);

            if (win.ShowDialog() == true && win.Report is { } report)
            {
                LoadFromRevit();
                Notify?.Invoke("Grafismo de filtros", report);
            }
        });
    }

    // ------------------------------------------------------------------ //
    //  Auditoría
    // ------------------------------------------------------------------ //

    public void OpenAudit()
    {
        if (!_initialized) return;
        EnqueueSafe(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null) return;
            var win = new FilterAuditWindow(doc);
            SetOwner(win, app);
            win.ShowDialog();
            if (win.Modified) LoadFromRevit();
        });
    }

    // ------------------------------------------------------------------ //
    //  Edición desde el panel
    // ------------------------------------------------------------------ //

    /// <summary>Abre el selector de color de Revit y aplica el color elegido.</summary>
    public void EditColor(FilterGraphicsRowItem row, FilterGraphicsEdit what)
    {
        if (!_initialized) return;
        var id = row.FilterId;
        var current = FilterGraphicsMapper.ParseColor(what == FilterGraphicsEdit.LineColor
            ? row.Entry.ProjectionLine?.Color
            : row.Entry.SurfaceForeground?.Color);

        EnqueueSafe(app =>
        {
            Color picked;
            using (var dlg = new ColorSelectionDialog())
            {
                if (current is not null) dlg.OriginalColor = current;
                if (dlg.Show() != ItemSelectionDialogResult.Confirmed) return;
                picked = dlg.SelectedColor;
            }

            ModifyInContext(app, id, what == FilterGraphicsEdit.LineColor ? "color de línea" : "color de patrón",
                (doc, ogs) =>
                {
                    if (what == FilterGraphicsEdit.LineColor)
                    {
                        ogs.SetProjectionLineColor(picked);
                        return;
                    }
                    ogs.SetSurfaceForegroundPatternColor(picked);
                    ogs.SetSurfaceForegroundPatternVisible(true);
                    // Como en V/G: un color sin patrón no se ve; se pone relleno sólido.
                    if (ogs.SurfaceForegroundPatternId == ElementId.InvalidElementId && SolidFillId(doc) is { } solid)
                        ogs.SetSurfaceForegroundPatternId(solid);
                });
        });
    }

    public void SetTransparency(FilterGraphicsRowItem row, int value)
        => Modify(row, $"transparencia {value}%", (_, ogs) => ogs.SetSurfaceTransparency(Math.Clamp(value, 0, 100)));

    public void SetLineWeight(FilterGraphicsRowItem row, int? weight)
        => Modify(row, weight is null ? "grosor por defecto" : $"grosor {weight}", (_, ogs) =>
            ogs.SetProjectionLineWeight(weight ?? OverrideGraphicSettings.InvalidPenNumber));

    public void ToggleHalftone(FilterGraphicsRowItem row)
    {
        var value = !row.Entry.Halftone;
        Modify(row, value ? "medio tono" : "sin medio tono", (_, ogs) => ogs.SetHalftone(value));
    }

    public void ClearLineColor(FilterGraphicsRowItem row)
        => Modify(row, "quitar color de línea", (_, ogs) => ogs.SetProjectionLineColor(Color.InvalidColorValue));

    public void ClearPattern(FilterGraphicsRowItem row)
        => Modify(row, "quitar patrón de superficie", (_, ogs) =>
        {
            ogs.SetSurfaceForegroundPatternId(ElementId.InvalidElementId);
            ogs.SetSurfaceForegroundPatternColor(Color.InvalidColorValue);
        });

    public void ClearAll(FilterGraphicsRowItem row)
        => Replace(row, "quitar todas las modificaciones", new OverrideGraphicSettings());

    /// <summary>Copia el grafismo completo de <paramref name="source"/> a los filtros marcados.</summary>
    public void CopyGraphicsToChecked(FilterGraphicsRowItem source)
    {
        if (!_initialized) return;
        var targets = Filters.Where(f => f.IsChecked && f.FilterId != source.FilterId).Select(f => f.FilterId).ToList();
        if (targets.Count == 0)
        {
            Notify?.Invoke("Grafismo de filtros", "Marca (✓) los filtros a los que quieres copiar el grafismo.");
            return;
        }
        var srcId = source.FilterId;
        Status = $"Copiando grafismo de '{source.Name}' a {targets.Count} filtro(s)…";
        RunTransaction("copiar grafismo", (doc, view) =>
        {
            var ogs = view.GetFilterOverrides(srcId);
            foreach (var id in targets) view.SetFilterOverrides(id, ogs);
        });
    }

    private void Modify(FilterGraphicsRowItem row, string label, Action<Document, OverrideGraphicSettings> mutate)
    {
        if (!_initialized) return;
        var id = row.FilterId;
        Status = $"Aplicando {label} a '{row.Name}'…";
        EnqueueSafe(app => ModifyInContext(app, id, label, mutate));
    }

    private void Replace(FilterGraphicsRowItem row, string label, OverrideGraphicSettings ogs)
    {
        var id = row.FilterId;
        Status = $"Aplicando {label} a '{row.Name}'…";
        RunTransaction(label, (_, view) => view.SetFilterOverrides(id, ogs));
    }

    private void ModifyInContext(UIApplication app, ElementId id, string label, Action<Document, OverrideGraphicSettings> mutate)
    {
        var doc = app.ActiveUIDocument?.Document;
        var view = doc is null ? null : FilterTargets.ResolveActive(doc);
        if (doc is null || view is null) return;

        using var t = new Transaction(doc, $"Grafismo de filtros: {label}");
        try
        {
            t.Start();
            var ogs = view.GetFilterOverrides(id);
            mutate(doc, ogs);
            view.SetFilterOverrides(id, ogs);
            t.Commit();
        }
        catch (Exception ex)
        {
            if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
            Notify?.Invoke("Grafismo de filtros", $"No se pudo aplicar {label}:\n{ex.Message}");
        }
        LoadFromRevit();
    }

    private void RunTransaction(string label, Action<Document, View> action)
    {
        if (!_initialized) return;
        EnqueueSafe(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            var view = doc is null ? null : FilterTargets.ResolveActive(doc);
            if (doc is null || view is null) return;

            using var t = new Transaction(doc, $"Grafismo de filtros: {label}");
            try
            {
                t.Start();
                action(doc, view);
                t.Commit();
            }
            catch (Exception ex)
            {
                if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
                Notify?.Invoke("Grafismo de filtros", $"No se pudo {label}:\n{ex.Message}");
            }
            LoadFromRevit();
        });
    }

    // ------------------------------------------------------------------ //
    //  Toggles al vuelo
    // ------------------------------------------------------------------ //

    private void OnRowToggled(FilterGraphicsRowItem row)
    {
        var id = row.FilterId;
        bool enabled = row.IsEnabled, visible = row.IsVisible;
        Status = $"Actualizando '{row.Name}'…";
        RunTransaction("activo/visible", (_, view) =>
        {
            view.SetIsFilterEnabled(id, enabled);
            view.SetFilterVisibility(id, visible);
        });
    }

    // ------------------------------------------------------------------ //

    /// <summary>
    /// Encola en el bus mostrando cualquier excepción: el bus las traga en
    /// silencio y, sin esto, un fallo deja el panel "Exportando…" sin más.
    /// </summary>
    private void EnqueueSafe(Action<UIApplication> action)
    {
        _bus!.Enqueue(app =>
        {
            try { action(app); }
            catch (Exception ex)
            {
                Status = "Error: " + ex.Message;
                Notify?.Invoke("Grafismo de filtros", $"Se produjo un error:\n{ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    private static ElementId? SolidFillId(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .Where(p => p.GetFillPattern().IsSolidFill)
            .OrderBy(p => p.GetFillPattern().Target == FillPatternTarget.Drafting ? 0 : 1)
            .FirstOrDefault()?.Id;

    private static bool SafeAllowsOverrides(View v)
    {
        try { return v.AreGraphicsOverridesAllowed(); } catch { return false; }
    }

    private static void SetOwner(System.Windows.Window win, UIApplication app)
    {
        try { new WindowInteropHelper(win).Owner = app.MainWindowHandle; } catch { /* ignore */ }
    }

    private void ClearTo(string message)
    {
        Filters.Clear();
        OnPropertyChanged(nameof(HasFilters));
        TargetLabel = message;
        TargetWarning = string.Empty;
        Status = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
