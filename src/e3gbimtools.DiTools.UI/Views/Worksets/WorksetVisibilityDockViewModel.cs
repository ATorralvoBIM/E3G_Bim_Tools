using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.RevitBridge;

namespace E3G.BimTools.DiTools.UI.Views.Worksets;

/// <summary>
/// ViewModel del panel acoplable de visibilidad de worksets (Bloque E).
///
/// Sigue la vista activa en vivo y, si ésta tiene una plantilla de vista
/// asignada, actúa sobre la PLANTILLA en lugar de sobre la vista. Cada
/// workset se muestra como un toggle que refleja el estado real y lo cambia
/// al instante (vía <see cref="WorksetDockExternalEventBus"/>, porque el panel
/// es modeless y no puede abrir transacciones fuera de contexto de la API).
/// </summary>
public sealed class WorksetVisibilityDockViewModel : INotifyPropertyChanged
{
    private UIApplication?                _uiApp;
    private IWorksetService?              _service;
    private WorksetDockExternalEventBus?  _bus;
    private bool                          _initialized;

    public ObservableCollection<WorksetToggleItem> Worksets { get; } = new();

    private string _targetLabel = "Sin vista activa.";
    public string TargetLabel
    {
        get => _targetLabel;
        private set { _targetLabel = value; OnPropertyChanged(); }
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    private bool _isWorkshared;
    public bool IsWorkshared
    {
        get => _isWorkshared;
        private set { _isWorkshared = value; OnPropertyChanged(); }
    }

    /// <summary>Inyecta las dependencias (idempotente; puede llamarse varias veces).</summary>
    public void Initialize(UIApplication uiApp, IWorksetService service, WorksetDockExternalEventBus bus)
    {
        _uiApp       = uiApp   ?? throw new ArgumentNullException(nameof(uiApp));
        _service     = service ?? throw new ArgumentNullException(nameof(service));
        _bus         = bus     ?? throw new ArgumentNullException(nameof(bus));
        _initialized = true;
    }

    /// <summary>
    /// Pide un refresco desde la UI modeless: se ejecuta en contexto válido
    /// de la API a través del bus de ExternalEvent.
    /// </summary>
    public void RequestRefresh()
    {
        if (!_initialized) return;
        _bus!.Enqueue(_ => LoadFromRevit());
    }

    /// <summary>
    /// Lee el estado real de visibilidad de cada workset en la vista/plantilla
    /// destino y rellena la lista. DEBE ejecutarse en contexto de la API de Revit
    /// (ViewActivated o dentro del bus).
    /// </summary>
    public void LoadFromRevit()
    {
        if (!_initialized) return;

        var doc = _uiApp!.ActiveUIDocument?.Document;
        if (doc is null)
        {
            ClearTo("Sin documento activo.");
            return;
        }

        IsWorkshared = _service!.IsWorkshared(doc);
        if (!IsWorkshared)
        {
            ClearTo("El documento no es workshared.");
            return;
        }

        var target = ResolveTargetView(doc);
        if (target is null)
        {
            ClearTo("La vista activa no admite visibilidad de worksets.");
            return;
        }

        TargetLabel = target.IsTemplate
            ? $"Plantilla de vista: {target.Name}"
            : $"Vista: {target.Name}";

        // Reusar las filas existentes cuando los worksets no cambian evita parpadeos.
        var descriptors = _service.GetWorksets(doc);
        Worksets.Clear();
        foreach (var d in descriptors)
        {
            var item = new WorksetToggleItem(d.Id, d.Name) { Suppress = true };
            try
            {
                var vis = target.GetWorksetVisibility(d.Id);
                item.IsVisible = vis != WorksetVisibility.Hidden;
            }
            catch { item.IsVisible = true; }
            item.Suppress = false;
            item.Toggled += OnItemToggled;
            Worksets.Add(item);
        }

        Status = $"{Worksets.Count} workset(s) · {(target.IsTemplate ? "plantilla" : "vista")} destino.";
    }

    /// <summary>Aplica visible/oculto a todos los worksets de la lista.</summary>
    public void SetAll(bool visible)
    {
        if (!_initialized || Worksets.Count == 0) return;
        var ids = Worksets.Select(w => w.Id).ToList();
        EnqueueApply(ids, visible, $"{(visible ? "Mostrando" : "Ocultando")} todos los worksets…");
    }

    // ------------------------------------------------------------------ //

    private void OnItemToggled(WorksetToggleItem item)
    {
        EnqueueApply(new[] { item.Id }, item.IsVisible,
            $"{(item.IsVisible ? "Mostrando" : "Ocultando")} '{item.Name}'…");
    }

    private void EnqueueApply(System.Collections.Generic.IReadOnlyList<WorksetId> ids, bool visible, string pending)
    {
        if (!_initialized) return;
        Status = pending;

        _bus!.Enqueue(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null) return;

            var target = ResolveTargetView(doc);
            if (target is null) return;

            var desired = visible ? WorksetVisibility.Visible : WorksetVisibility.Hidden;

            using var t = new Transaction(doc, "Worksets: visibilidad en vista/plantilla");
            t.Start();
            try
            {
                foreach (var id in ids)
                {
                    try { target.SetWorksetVisibility(id, desired); }
                    catch { /* algún workset puede no admitirse en esta vista */ }
                }
                t.Commit();
            }
            catch
            {
                if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
            }

            // Releer el estado real para que los toggles reflejen lo aplicado.
            LoadFromRevit();
        });
    }

    /// <summary>
    /// Vista destino: la activa, salvo que tenga plantilla de vista asignada,
    /// en cuyo caso es la plantilla. <c>null</c> si la vista no admite worksets.
    /// </summary>
    private static View? ResolveTargetView(Document doc)
    {
        if (doc.ActiveView is not View active) return null;

        View candidate = active;
        if (active.ViewTemplateId != ElementId.InvalidElementId &&
            doc.GetElement(active.ViewTemplateId) is View tpl)
        {
            candidate = tpl;
        }

        // Las vistas que no admiten worksets se gestionan con try/catch al leer/escribir.
        return candidate;
    }

    private void ClearTo(string message)
    {
        Worksets.Clear();
        TargetLabel = message;
        Status = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
