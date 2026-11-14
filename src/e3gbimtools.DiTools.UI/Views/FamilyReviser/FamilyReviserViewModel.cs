using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Families;

namespace E3G.BimTools.DiTools.UI.Views.FamilyReviser;

/// <summary>
/// ViewModel de FamilyReviserWindow (Sprint 7).
/// Expone propiedades observables para las tres pestañas:
///   1. Renombrar familias
///   2. Exportar familias a disco
///   3. Worksets
/// </summary>
public sealed class FamilyReviserViewModel : INotifyPropertyChanged
{
    private readonly UIDocument            _uidoc;
    private readonly Autodesk.Revit.DB.Document _doc;
    private readonly IFamilyReviserService _familyService;
    private readonly IWorksetService       _worksetService;

    public FamilyReviserViewModel(
        UIDocument           uidoc,
        IFamilyReviserService familyService,
        IWorksetService       worksetService)
    {
        _uidoc          = uidoc          ?? throw new ArgumentNullException(nameof(uidoc));
        _doc            = uidoc.Document;
        _familyService  = familyService  ?? throw new ArgumentNullException(nameof(familyService));
        _worksetService = worksetService ?? throw new ArgumentNullException(nameof(worksetService));

        RefreshFamilies();
        RefreshWorksets();
    }

    // ================================================================== //
    //  PESTAÑA 1 — Renombrar                                              //
    // ================================================================== //

    public ObservableCollection<FamilyDescriptor> Families { get; } = new();

    private string _renamePrefix = string.Empty;
    public string RenamePrefix
    {
        get => _renamePrefix;
        set { _renamePrefix = value; OnPropertyChanged(); }
    }

    private string _renameSuffix = string.Empty;
    public string RenameSuffix
    {
        get => _renameSuffix;
        set { _renameSuffix = value; OnPropertyChanged(); }
    }

    private string _renameFindText = string.Empty;
    public string RenameFindText
    {
        get => _renameFindText;
        set { _renameFindText = value; OnPropertyChanged(); }
    }

    private string _renameReplaceText = string.Empty;
    public string RenameReplaceText
    {
        get => _renameReplaceText;
        set { _renameReplaceText = value; OnPropertyChanged(); }
    }

    private bool _renameCaseSensitive;
    public bool RenameCaseSensitive
    {
        get => _renameCaseSensitive;
        set { _renameCaseSensitive = value; OnPropertyChanged(); }
    }

    private string _renameStatus = string.Empty;
    public string RenameStatus
    {
        get => _renameStatus;
        set { _renameStatus = value; OnPropertyChanged(); }
    }

    private bool _workshared;
    public bool IsWorkshared
    {
        get => _workshared;
        private set { _workshared = value; OnPropertyChanged(); }
    }

    /// <summary>Calcula el ProposedName de cada familia seleccionada sin tocar el modelo.</summary>
    public void PreviewRename()
    {
        var request = BuildRenameRequest();
        _familyService.PreviewRename(Families.ToList(), request);
        // Notificar refrescando la colección en vista
        var snap = Families.ToList();
        Families.Clear();
        foreach (var f in snap) Families.Add(f);
        RenameStatus = "Vista previa aplicada.";
    }

    /// <summary>Aplica el renombrado al modelo Revit.</summary>
    public void ApplyRename()
    {
        var request = BuildRenameRequest();
        var result  = _familyService.BulkRename(_doc, request);
        RenameStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Skipped.Select(s => $"  • {s.OriginalName}: {s.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Renombrar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshFamilies();
    }

    public void SelectAll()
    {
        foreach (var f in Families) f.IsSelected = true;
        RefreshFamilies();
    }

    public void SelectNone()
    {
        foreach (var f in Families) f.IsSelected = false;
        RefreshFamilies();
    }

    // ================================================================== //
    //  PESTAÑA 2 — Exportar                                               //
    // ================================================================== //

    private string _exportFolder = string.Empty;
    public string ExportFolder
    {
        get => _exportFolder;
        set { _exportFolder = value; OnPropertyChanged(); }
    }

    private string _exportPathTemplate = @"{Categoria}\{Familia}.rfa";
    public string ExportPathTemplate
    {
        get => _exportPathTemplate;
        set { _exportPathTemplate = value; OnPropertyChanged(); }
    }

    private bool _exportOverwrite = true;
    public bool ExportOverwrite
    {
        get => _exportOverwrite;
        set { _exportOverwrite = value; OnPropertyChanged(); }
    }

    private string _exportStatus = string.Empty;
    public string ExportStatus
    {
        get => _exportStatus;
        set { _exportStatus = value; OnPropertyChanged(); }
    }

    public void ExportToDisk()
    {
        var request = new FamilyExportRequest
        {
            Families = Families.Where(f => f.IsSelected).ToList(),
            RootFolder = ExportFolder,
            Rule = new FamilyExportRule { PathTemplate = ExportPathTemplate },
            Overwrite = ExportOverwrite,
        };

        var result = _familyService.ExportToDisk(_doc, request);
        ExportStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Skipped.Select(s => $"  • {s.FamilyName}: {s.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Exportar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================================================================== //
    //  PESTAÑA 3 — Worksets                                               //
    // ================================================================== //

    public ObservableCollection<WorksetDescriptor> Worksets { get; } = new();

    private WorksetDescriptor? _selectedWorkset;
    public WorksetDescriptor? SelectedWorkset
    {
        get => _selectedWorkset;
        set { _selectedWorkset = value; OnPropertyChanged(); }
    }

    private string _newWorksetName = string.Empty;
    public string NewWorksetName
    {
        get => _newWorksetName;
        set { _newWorksetName = value; OnPropertyChanged(); }
    }

    private string _worksetStatus = string.Empty;
    public string WorksetStatus
    {
        get => _worksetStatus;
        set { _worksetStatus = value; OnPropertyChanged(); }
    }

    private string _batchWorksetNames = string.Empty;
    public string BatchWorksetNames
    {
        get => _batchWorksetNames;
        set
        {
            _batchWorksetNames = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BatchWorksetNameLines));
            OnPropertyChanged(nameof(HasBatchNames));
        }
    }

    /// <summary>True cuando hay al menos un nombre de workset detectado para crear en lote.</summary>
    public bool HasBatchNames => BatchWorksetNameLines.Count > 0;

    public void CreateWorkset()
    {
        if (string.IsNullOrWhiteSpace(NewWorksetName)) return;
        var ws = _worksetService.CreateWorkset(_doc, NewWorksetName.Trim());
        Worksets.Add(ws);
        NewWorksetName = string.Empty;
        WorksetStatus  = $"Workset '{ws.Name}' creado.";
    }

    /// <summary>Nombres no vacíos detectados en el cuadro de texto de creación en lote.</summary>
    public IReadOnlyList<string> BatchWorksetNameLines =>
        (BatchWorksetNames ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    public void CreateWorksetsBatch()
    {
        var result = _worksetService.CreateWorksets(_doc, BatchWorksetNameLines);
        WorksetStatus = result.ToSummary();

        if (result.Created.Count > 0)
            BatchWorksetNames = string.Empty;

        var skipped = new List<string>();
        if (result.AlreadyExisting.Count > 0)
            skipped.Add("Ya existían:\n" + string.Join("\n", result.AlreadyExisting.Select(n => $"  • {n}")));
        if (result.DuplicateInInput.Count > 0)
            skipped.Add("Duplicados en la lista:\n" + string.Join("\n", result.DuplicateInInput.Select(n => $"  • {n}")));
        if (result.HasErrors)
            skipped.Add("Errores:\n" + string.Join("\n", result.Errors.Select(e => $"  • {e.Name}: {e.Reason}")));

        if (skipped.Count > 0)
        {
            MessageBox.Show($"{result.ToSummary()}\n\n{string.Join("\n\n", skipped)}",
                "FamilyReviser — Crear worksets en lote",
                MessageBoxButton.OK,
                result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        RefreshWorksets();
    }

    public void RenameSelectedWorkset(string newName)
    {
        if (SelectedWorkset is null || string.IsNullOrWhiteSpace(newName)) return;
        _worksetService.RenameWorkset(_doc, SelectedWorkset, newName.Trim());
        WorksetStatus = $"Workset renombrado a '{newName.Trim()}'.";
        RefreshWorksets();
    }

    /// <summary>Worksets con un cambio de nombre pendiente en la columna "Nuevo nombre".</summary>
    public int WorksetsWithRenameChange => Worksets.Count(w => w.HasRenameChange);

    public void BulkRenameWorksets()
    {
        var result = _worksetService.BulkRenameWorksets(_doc, Worksets.ToList());
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n",
                result.Skipped.Select(s => $"  • {s.OriginalName} → {s.ProposedName}: {s.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Renombrar worksets en lote",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshWorksets();
    }

    public void DeleteSelectedWorkset()
    {
        if (SelectedWorkset is null) return;
        _worksetService.DeleteWorkset(_doc, SelectedWorkset);
        WorksetStatus = $"Workset '{SelectedWorkset.Name}' eliminado.";
        RefreshWorksets();
    }

    public void AssignSelectedFamiliesToWorkset()
    {
        if (SelectedWorkset is null) return;

        var request = new WorksetAssignRequest
        {
            TargetWorksetId = SelectedWorkset.Id,
            ElementIds       = Families
                .Where(f => f.IsSelected)
                .Select(f => f.FamilyId)
                .ToList(),
        };

        var result = _worksetService.AssignElements(_doc, request);
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Errors.Take(10).Select(e => $"  • Id {e.ElementIdValue}: {e.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Worksets",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshWorksets();
    }

    // ------------------------------------------------------------------ //
    //  Lista de elementos asignables (familias y vínculos)                 //
    // ------------------------------------------------------------------ //

    public ObservableCollection<WorksetAssignableElement> AssignableElements { get; } = new();

    private bool _showFamilies = true;
    public bool ShowFamilies
    {
        get => _showFamilies;
        set { _showFamilies = value; OnPropertyChanged(); }
    }

    private bool _showRevitLinks = true;
    public bool ShowRevitLinks
    {
        get => _showRevitLinks;
        set { _showRevitLinks = value; OnPropertyChanged(); }
    }

    private bool _showCadLinks = true;
    public bool ShowCadLinks
    {
        get => _showCadLinks;
        set { _showCadLinks = value; OnPropertyChanged(); }
    }

    /// <summary>Elementos marcados (columna ✓) en la lista de asignables.</summary>
    public int AssignableCheckedCount => AssignableElements.Count(e => e.IsSelected);

    /// <summary>Carga la lista de familias y vínculos según los filtros marcados.</summary>
    public void LoadAssignableElements()
    {
        AssignableElements.Clear();
        if (!IsWorkshared) return;

        var rows = _worksetService.GetAssignableElements(
            _doc, ShowFamilies, ShowRevitLinks, ShowCadLinks);

        foreach (var r in rows)
            AssignableElements.Add(r);

        WorksetStatus = $"Lista cargada: {AssignableElements.Count} elemento(s).";
    }

    /// <summary>Asigna al workset seleccionado los elementos marcados en la lista.</summary>
    public void AssignCheckedElementsToWorkset()
    {
        if (SelectedWorkset is null) return;

        var ids = AssignableElements
            .Where(e => e.IsSelected)
            .Select(e => e.Id)
            .ToList();
        if (ids.Count == 0) return;

        var request = new WorksetAssignRequest
        {
            TargetWorksetId = SelectedWorkset.Id,
            ElementIds      = ids,
        };

        var result = _worksetService.AssignElements(_doc, request);
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Errors.Take(10).Select(e => $"  • Id {e.ElementIdValue}: {e.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Asignar elementos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshWorksets();
        LoadAssignableElements();
    }

    public void ConsolidateGridsAndLevels()
    {
        var result = _worksetService.ConsolidateGridsAndLevels(_doc);
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Errors.Take(10).Select(e => $"  • {e.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Consolidar grids y niveles",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshWorksets();
    }

    private bool _organizeRevitLinks = true;
    public bool OrganizeRevitLinks
    {
        get => _organizeRevitLinks;
        set { _organizeRevitLinks = value; OnPropertyChanged(); }
    }

    private bool _organizeCadLinks = true;
    public bool OrganizeCadLinks
    {
        get => _organizeCadLinks;
        set { _organizeCadLinks = value; OnPropertyChanged(); }
    }

    private string _linkWorksetPrefix = string.Empty;
    public string LinkWorksetPrefix
    {
        get => _linkWorksetPrefix;
        set { _linkWorksetPrefix = value; OnPropertyChanged(); }
    }

    public void OrganizeLinksIntoWorksets()
    {
        var result = _worksetService.OrganizeLinksIntoWorksets(
            _doc, OrganizeRevitLinks, OrganizeCadLinks, LinkWorksetPrefix);
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Errors.Take(10).Select(e => $"  • {e.Name}: {e.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Organizar vínculos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshWorksets();
    }

    /// <summary>Worksets marcados (columna ✓) en la tabla.</summary>
    public int WorksetsCheckedCount => Worksets.Count(w => w.IsSelected);

    /// <summary>Índice del ámbito de vistas: 0 activa, 1 todas, 2 plantillas, 3 ambas.</summary>
    private int _viewScopeIndex;
    public int ViewScopeIndex
    {
        get => _viewScopeIndex;
        set { _viewScopeIndex = value; OnPropertyChanged(); }
    }

    public void ApplyWorksetVisibility(bool visible)
    {
        var ids = Worksets.Where(w => w.IsSelected).Select(w => w.Id).ToList();
        if (ids.Count == 0) return;

        var scope = ViewScopeIndex switch
        {
            0 => Core.Models.Families.WorksetViewScope.ActiveView,
            1 => Core.Models.Families.WorksetViewScope.AllViews,
            2 => Core.Models.Families.WorksetViewScope.AllViewTemplates,
            _ => Core.Models.Families.WorksetViewScope.AllViewsAndTemplates,
        };

        var result = _worksetService.SetWorksetVisibility(_doc, ids, scope, visible);
        WorksetStatus = result.ToSummary();

        if (result.HasErrors)
        {
            var details = string.Join("\n", result.Errors.Take(10).Select(e => $"  • {e.ViewName}: {e.Reason}"));
            MessageBox.Show($"{result.ToSummary()}\n\nDetalles:\n{details}",
                "FamilyReviser — Visibilidad de worksets",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void GenerateVisibilityReport()
    {
        var ids = Worksets.Where(w => w.IsSelected).Select(w => w.Id).ToList();

        var scope = ViewScopeIndex switch
        {
            0 => Core.Models.Families.WorksetViewScope.ActiveView,
            1 => Core.Models.Families.WorksetViewScope.AllViews,
            2 => Core.Models.Families.WorksetViewScope.AllViewTemplates,
            _ => Core.Models.Families.WorksetViewScope.AllViewsAndTemplates,
        };

        var report = _worksetService.GetVisibilityReport(_doc, ids, scope);
        WorksetStatus = $"Reporte de visibilidad: {report.ViewsInspected} vista(s) inspeccionada(s).";

        MessageBox.Show(report.ToReportText(),
            "Estado de visibilidad de worksets",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string _viewPerWorksetPrefix = "WS - ";
    public string ViewPerWorksetPrefix
    {
        get => _viewPerWorksetPrefix;
        set { _viewPerWorksetPrefix = value; OnPropertyChanged(); }
    }

    /// <summary>Modo de creación de vistas 3D: 0 = una por workset; 1 = una combinada (coordinación).</summary>
    private int _viewModeIndex;
    public int ViewModeIndex
    {
        get => _viewModeIndex;
        set { _viewModeIndex = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Crea vistas 3D a partir de los worksets marcados (columna ✓), según el modo seleccionado:
    /// una vista aislada por workset, o una sola vista de coordinación con todos visibles.
    /// </summary>
    public void CreateViewsPerWorkset()
    {
        var ids = Worksets.Where(w => w.IsSelected).Select(w => w.Id).ToList();
        if (ids.Count == 0) return;

        var prefix = ViewPerWorksetPrefix ?? string.Empty;

        var result = ViewModeIndex == 1
            ? _worksetService.CreateCoordinationView(_doc, ids, $"{prefix}Coordinación")
            : _worksetService.CreateViewsPerWorkset(_doc, ids, prefix);
        WorksetStatus = result.ToSummary();

        var created = result.Created.Count > 0
            ? "Creadas:\n" + string.Join("\n", result.Created.Select(c => $"  • {c.ViewName}"))
            : null;
        var errors = result.HasErrors
            ? "Errores:\n" + string.Join("\n", result.Errors.Select(e => $"  • {e.WorksetName}: {e.Reason}"))
            : null;

        var body = string.Join("\n\n", new[] { created, errors }.Where(s => s != null));
        if (!string.IsNullOrEmpty(body))
        {
            MessageBox.Show($"{result.ToSummary()}\n\n{body}",
                "FamilyReviser — Vistas 3D por workset",
                MessageBoxButton.OK,
                result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        RefreshWorksets();
    }

    public void AuditWorksets()
    {
        var result = _worksetService.Audit(_doc);
        WorksetStatus = result.ToSummary();
        MessageBox.Show(result.ToSummary(), "Auditoría de Worksets",
            MessageBoxButton.OK,
            result.IsClean ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    // ================================================================== //
    //  Refresh                                                             //
    // ================================================================== //

    public void RefreshFamilies()
    {
        var all = _familyService.GetFamilies(_doc);
        // Preservar selección existente por FamilyId
        var selected = new HashSet<Autodesk.Revit.DB.ElementId>(
            Families.Where(f => f.IsSelected).Select(f => f.FamilyId));

        Families.Clear();
        foreach (var f in all)
        {
            f.IsSelected = selected.Contains(f.FamilyId);
            Families.Add(f);
        }
    }

    public void RefreshWorksets()
    {
        IsWorkshared = _worksetService.IsWorkshared(_doc);

        Worksets.Clear();
        if (!IsWorkshared) return;

        foreach (var w in _worksetService.GetWorksets(_doc))
            Worksets.Add(w);
    }

    // ================================================================== //
    //  Helpers                                                             //
    // ================================================================== //

    private FamilyRenameRequest BuildRenameRequest() =>
        new()
        {
            Families      = Families.ToList(),
            Prefix        = RenamePrefix,
            Suffix        = RenameSuffix,
            FindText      = RenameFindText,
            ReplaceText   = RenameReplaceText,
            CaseSensitive = RenameCaseSensitive,
        };

    // ================================================================== //
    //  INotifyPropertyChanged                                              //
    // ================================================================== //

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
