using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sheets;

namespace E3G.BimTools.DiTools.UI.Views.SheetGen;

/// <summary>
/// ViewModel de la ventana SheetGen (Sprint 6).
/// Expone propiedades observables para las 4 pestañas.
/// </summary>
public sealed class SheetGenViewModel : INotifyPropertyChanged
{
    private readonly UIDocument         _uidoc;
    private readonly Document           _doc;
    private readonly ISheetGenService   _service;

    public SheetGenViewModel(UIDocument uidoc, ISheetGenService service)
    {
        _uidoc   = uidoc   ?? throw new ArgumentNullException(nameof(uidoc));
        _doc     = uidoc.Document;
        _service = service ?? throw new ArgumentNullException(nameof(service));

        // Carga inicial
        RefreshLayouts();
        RefreshSheetList();
        RefreshRevisions();
        RefreshTitleBlocks();
    }

    // ================================================================== //
    //  PESTAÑA 1 — Crear / Importar                                        //
    // ================================================================== //

    /// <summary>Filas editables de la grilla de creación manual.</summary>
    public ObservableCollection<SheetDescriptor> NewSheets { get; } = new();

    /// <summary>Title blocks disponibles en el documento.</summary>
    public ObservableCollection<string> TitleBlocks { get; } = new();

    /// <summary>Layouts para el ComboBox (null = sin layout).</summary>
    public ObservableCollection<SheetLayoutDefinition?> LayoutsForCombo { get; } = new();

    private SheetLayoutDefinition? _selectedLayoutForCreate;
    public SheetLayoutDefinition? SelectedLayoutForCreate
    {
        get => _selectedLayoutForCreate;
        set { _selectedLayoutForCreate = value; OnPropertyChanged(); }
    }

    private string _createStatus = string.Empty;
    public string CreateStatus
    {
        get => _createStatus;
        set { _createStatus = value; OnPropertyChanged(); }
    }

    private int _createProgress;
    public int CreateProgress
    {
        get => _createProgress;
        set { _createProgress = value; OnPropertyChanged(); }
    }

    public void AddSheetRow()
    {
        NewSheets.Add(new SheetDescriptor
        {
            TitleBlockFamilyTypeName = TitleBlocks.FirstOrDefault() ?? string.Empty
        });
    }

    public void RemoveSheetRow(SheetDescriptor? row)
    {
        if (row is not null) NewSheets.Remove(row);
    }

    public async Task CreateSheetsAsync()
    {
        if (NewSheets.Count == 0)
        {
            CreateStatus = "Añade al menos una fila antes de crear.";
            return;
        }

        CreateStatus   = "Creando sheets…";
        CreateProgress = 0;

        var progress = new Progress<int>(v =>
        {
            CreateProgress = v;
            CreateStatus   = $"Creando… {v} %";
        });

        var result = await _service.BulkCreateAsync(
            _doc, NewSheets.ToList(), SelectedLayoutForCreate, progress);

        CreateProgress = 100;
        CreateStatus   = result.ToSummary();

        if (result.SkippedNumbers.Count > 0)
            MessageBox.Show(
                string.Join("\n", result.SkippedNumbers),
                "Sheets omitidas",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        RefreshSheetList();
        NewSheets.Clear();
    }

    public void ImportFromExcel(string filePath)
    {
        CreateStatus = "Importando desde Excel…";
        var result = _service.ImportFromExcel(_doc, filePath, SelectedLayoutForCreate);
        CreateStatus = result.ToSummary();

        if (result.SkippedNumbers.Count > 0)
            MessageBox.Show(
                string.Join("\n", result.SkippedNumbers),
                "Sheets omitidas",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        RefreshSheetList();
    }

    public void ExportToExcel(string filePath)
    {
        _service.ExportToExcel(_doc, filePath);
        MessageBox.Show($"Exportadas a '{filePath}'.", "Exportar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ================================================================== //
    //  PESTAÑA 2 — Layouts                                                 //
    // ================================================================== //

    public ObservableCollection<SheetLayoutDefinition> Layouts { get; } = new();

    private SheetLayoutDefinition? _selectedLayout;
    public SheetLayoutDefinition? SelectedLayout
    {
        get => _selectedLayout;
        set { _selectedLayout = value; OnPropertyChanged(); OnPropertyChanged(nameof(EditingPlacements)); }
    }

    public ObservableCollection<ViewPlacementSpec> EditingPlacements =>
        _selectedLayout is not null
            ? new ObservableCollection<ViewPlacementSpec>(_selectedLayout.Placements)
            : new ObservableCollection<ViewPlacementSpec>();

    public void NewLayout()
    {
        var layout = new SheetLayoutDefinition { Name = "Nuevo layout" };
        _service.SaveLayout(layout);
        RefreshLayouts();
        SelectedLayout = Layouts.FirstOrDefault(l => l.Id == layout.Id);
    }

    public void SaveSelectedLayout(string name, string? description, IList<ViewPlacementSpec> placements)
    {
        if (SelectedLayout is null) return;
        SelectedLayout.Name        = name;
        SelectedLayout.Description = description;
        SelectedLayout.Placements  = placements.ToList();
        _service.SaveLayout(SelectedLayout);
        RefreshLayouts();
    }

    public void DeleteSelectedLayout()
    {
        if (SelectedLayout is null) return;
        _service.DeleteLayout(SelectedLayout.Id);
        RefreshLayouts();
        SelectedLayout = null;
    }

    // ================================================================== //
    //  PESTAÑA 3 — Revisiones                                              //
    // ================================================================== //

    public ObservableCollection<SheetInfo>     AllSheets  { get; } = new();
    public ObservableCollection<RevisionInfo>  Revisions  { get; } = new();

    private RevisionInfo? _selectedRevision;
    public RevisionInfo? SelectedRevision
    {
        get => _selectedRevision;
        set { _selectedRevision = value; OnPropertyChanged(); }
    }

    private bool _addRevision = true;
    public bool AddRevision
    {
        get => _addRevision;
        set { _addRevision = value; OnPropertyChanged(); }
    }

    public void ApplyRevisions(IList<SheetInfo> selectedSheets)
    {
        if (SelectedRevision is null)
        {
            MessageBox.Show("Selecciona una revisión.", "SheetGen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var request = new SheetRevisionRequest
        {
            SheetIds   = selectedSheets.Select(s => s.ElementId).ToList(),
            RevisionId = SelectedRevision.ElementId,
            Add        = AddRevision
        };

        _service.ApplyRevisions(_doc, request);

        var action = AddRevision ? "asignada" : "quitada";
        MessageBox.Show(
            $"Revisión {action} a {selectedSheets.Count} sheet(s).",
            "SheetGen", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ================================================================== //
    //  PESTAÑA 4 — Auditoría / Renumerar                                  //
    // ================================================================== //

    private SheetAuditResult? _auditResult;
    public SheetAuditResult? AuditResult
    {
        get => _auditResult;
        set { _auditResult = value; OnPropertyChanged(); }
    }

    public string RenumPrefix   { get; set; } = string.Empty;
    public string RenumSuffix   { get; set; } = string.Empty;
    public int    RenumStart    { get; set; } = 1;
    public int    RenumPadding  { get; set; } = 2;

    private IList<(ElementId SheetId, string OldNumber, string NewNumber)> _renumPreview = new List<(ElementId, string, string)>();
    public IList<(ElementId SheetId, string OldNumber, string NewNumber)> RenumPreview
    {
        get => _renumPreview;
        private set { _renumPreview = value; OnPropertyChanged(); }
    }

    public void RunAudit()
    {
        AuditResult = _service.Audit(_doc);
    }

    public void PreviewRenumber(IList<SheetInfo> selectedSheets)
    {
        var request = BuildRenumberRequest(selectedSheets);
        if (!request.IsValid(out var err))
        {
            MessageBox.Show(err, "SheetGen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RenumPreview = _service.PreviewRenumber(_doc, request);
    }

    public void ApplyRenumber(IList<SheetInfo> selectedSheets)
    {
        var request = BuildRenumberRequest(selectedSheets);
        if (!request.IsValid(out var err))
        {
            MessageBox.Show(err, "SheetGen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _service.RenumberSheets(_doc, request);
        RefreshSheetList();
        RenumPreview = new List<(ElementId, string, string)>();
        MessageBox.Show(
            $"{selectedSheets.Count} sheet(s) renumeradas.",
            "SheetGen", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ================================================================== //
    //  Helpers                                                             //
    // ================================================================== //

    public void RefreshAll()
    {
        RefreshLayouts();
        RefreshSheetList();
        RefreshRevisions();
        RefreshTitleBlocks();
    }

    private void RefreshLayouts()
    {
        Layouts.Clear();
        foreach (var l in _service.GetLayouts())
            Layouts.Add(l);

        LayoutsForCombo.Clear();
        LayoutsForCombo.Add(null); // "Sin layout"
        foreach (var l in Layouts)
            LayoutsForCombo.Add(l);
    }

    private void RefreshSheetList()
    {
        AllSheets.Clear();
        foreach (var s in _service.GetAllSheets(_doc))
            AllSheets.Add(s);
    }

    private void RefreshRevisions()
    {
        Revisions.Clear();
        var revisions = new FilteredElementCollector(_doc)
            .OfClass(typeof(Revision))
            .Cast<Revision>()
            .Select(r => new RevisionInfo { ElementId = r.Id, Description = r.Description, RevisionDate = r.RevisionDate })
            .ToList();
        foreach (var r in revisions)
            Revisions.Add(r);
    }

    private void RefreshTitleBlocks()
    {
        TitleBlocks.Clear();
        var types = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .Cast<FamilySymbol>()
            .Select(fs => $"{fs.Family.Name} : {fs.Name}")
            .OrderBy(n => n);
        foreach (var t in types)
            TitleBlocks.Add(t);
    }

    private SheetRenumberRequest BuildRenumberRequest(IList<SheetInfo> selectedSheets) =>
        new SheetRenumberRequest
        {
            SheetIds   = selectedSheets.Select(s => s.ElementId).ToList(),
            Prefix     = RenumPrefix,
            Suffix     = RenumSuffix,
            StartIndex = RenumStart,
            PadDigits  = RenumPadding
        };

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Resumen de una Revision para la UI.</summary>
public sealed class RevisionInfo
{
    public ElementId ElementId   { get; set; } = ElementId.InvalidElementId;
    public string    Description { get; set; } = string.Empty;
    public string    RevisionDate{ get; set; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(RevisionDate)
        ? Description
        : $"{RevisionDate} — {Description}";
}
