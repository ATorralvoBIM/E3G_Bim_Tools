using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Sections;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.SectionBoxer;

/// <summary>
/// Code-behind del SectionBoxer (Sprint 5).  Mantenemos por consistencia
/// con el resto del modulo el patron "code-behind delgado": la ventana
/// construye un <see cref="SectionBoxRequest"/> y llama al servicio de Core.
/// </summary>
public partial class SectionBoxerWindow : Window
{
    private readonly UIDocument _uidoc;
    private readonly ISectionBoxService _service;
    private readonly IFilterTemplateService _filterTemplates;

    private SectionBoxSourceKind _currentKind = SectionBoxSourceKind.Selection;
    private IReadOnlyList<ElementId>? _pickedSelection;
    private ElementId? _pickedRoomId  = null;   // se asigna al seleccionar habitacion
    private ElementId? _pickedLevelId = null;   // se asigna al seleccionar nivel
    private string? _pickedFilterName = null;   // plantilla OneFilter elegida

    public SectionBoxerWindow(UIDocument uidoc, ISectionBoxService service, IFilterTemplateService filterTemplates)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _filterTemplates = filterTemplates ?? throw new ArgumentNullException(nameof(filterTemplates));

        Loaded += (_, __) => RefreshSourcePicker();
    }

    // =========================================================================
    // Cambio de origen
    // =========================================================================

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (SourceSelection.IsChecked == true)        _currentKind = SectionBoxSourceKind.Selection;
        else if (SourceRoom.IsChecked == true)        _currentKind = SectionBoxSourceKind.Room;
        else if (SourceLevel.IsChecked == true)       _currentKind = SectionBoxSourceKind.Level;
        else if (SourceSavedFilter.IsChecked == true) _currentKind = SectionBoxSourceKind.SavedFilter;
        RefreshSourcePicker();
    }

    private void RefreshSourcePicker()
    {
        switch (_currentKind)
        {
            case SectionBoxSourceKind.Selection:
                SourcePickerLabel.Text = "Selección actual:";
                _pickedSelection = _uidoc.Selection.GetElementIds().ToList();
                SourcePickerValue.Text = $"{_pickedSelection.Count} elemento(s) seleccionados";
                break;
            case SectionBoxSourceKind.Room:
                SourcePickerLabel.Text = "Habitación:";
                SourcePickerValue.Text = _pickedRoomId is null ? "(ninguna)" : $"Id {_pickedRoomId.Value.ToString()}";
                break;
            case SectionBoxSourceKind.Level:
                SourcePickerLabel.Text = "Nivel:";
                SourcePickerValue.Text = _pickedLevelId is null ? "(ninguno)" : $"Id {_pickedLevelId.Value.ToString()}";
                break;
            case SectionBoxSourceKind.SavedFilter:
                SourcePickerLabel.Text = "Filtro guardado:";
                SourcePickerValue.Text = string.IsNullOrEmpty(_pickedFilterName) ? "(ninguno)" : _pickedFilterName;
                break;
        }
    }

    private void OnPickSourceClick(object sender, RoutedEventArgs e)
    {
        var doc = _uidoc.Document;
        switch (_currentKind)
        {
            case SectionBoxSourceKind.Selection:
                _pickedSelection = _uidoc.Selection.GetElementIds().ToList();
                break;

            case SectionBoxSourceKind.Room:
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Select(r => new PickItem
                    {
                        Text = $"{r.LookupParameter("Número")?.AsString() ?? r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""} · {r.Name}",
                        Value = r.Id,
                    })
                    .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToList();
                if (PickOne("Elegir habitación", rooms) is ElementId rid) _pickedRoomId = rid;
                break;
            }

            case SectionBoxSourceKind.Level:
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new PickItem { Text = l.Name, Value = l.Id }).ToList();
                if (PickOne("Elegir nivel", levels) is ElementId lid) _pickedLevelId = lid;
                break;
            }

            case SectionBoxSourceKind.SavedFilter:
            {
                var plantillas = _filterTemplates.ListTemplates()
                    .Select(n => new PickItem { Text = n, Value = n }).ToList();
                if (plantillas.Count == 0)
                {
                    TaskDialog.Show("SectionBoxer", "No hay filtros guardados en OneFilter. Crea uno primero.");
                    break;
                }
                if (PickOne("Elegir filtro guardado", plantillas) is string fn) _pickedFilterName = fn;
                break;
            }
        }
        RefreshSourcePicker();
    }

    /// <summary>Diálogo simple de lista (construido en código, sin XAML) para elegir un origen.</summary>
    private object? PickOne(string title, IList<PickItem> items)
    {
        var win = new Window
        {
            Title = title, Width = 380, Height = 440, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        E3gWindowBase.ApplyTheme(win);

        var dock = new DockPanel { Margin = new Thickness(12) };
        var ok = new Button { Content = "Aceptar", Padding = new Thickness(16, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(ok, Dock.Bottom);
        var list = new ListBox { ItemsSource = items, DisplayMemberPath = nameof(PickItem.Text) };

        void Accept() { if (list.SelectedItem is PickItem) { win.DialogResult = true; win.Close(); } }
        ok.Click += (_, __) => Accept();
        list.MouseDoubleClick += (_, __) => Accept();

        dock.Children.Add(ok);
        dock.Children.Add(list);
        win.Content = dock;

        return win.ShowDialog() == true && list.SelectedItem is PickItem p ? p.Value : null;
    }

    private sealed class PickItem
    {
        public string Text { get; set; } = string.Empty;
        public object Value { get; set; } = null!;
    }

    private void OnCreateDedicatedChanged(object sender, RoutedEventArgs e)
    {
        DedicatedViewNamePanel.IsEnabled = CreateDedicatedViewCheck.IsChecked == true;
    }

    // =========================================================================
    // Aplicar
    // =========================================================================

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PaddingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var padding) &&
            !double.TryParse(PaddingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out padding))
        {
            StatusText.Text = "Padding inválido. Usa un número (admite '.' o ',').";
            return;
        }

        var alignment = (AlignmentCombo.SelectedIndex == 1) ? SectionBoxAlignment.View : SectionBoxAlignment.Project;
        var includeLinks = IncludeLinksCheck.IsChecked == true;
        var createDedicated = CreateDedicatedViewCheck.IsChecked == true;
        var dedicatedName = DedicatedViewNameBox.Text?.Trim();

        var request = new SectionBoxRequest(
            kind: _currentKind,
            paddingMeters: padding,
            includeLinks: includeLinks,
            alignment: alignment,
            createDedicatedView: createDedicated,
            dedicatedViewName: dedicatedName,
            selectedElementIds: _pickedSelection,
            roomId: _pickedRoomId,
            levelId: _pickedLevelId,
            savedFilterTemplateId: _pickedFilterName);

        // View 3D activa: si no es 3D y no marcamos "crear dedicada", error.
        var view3d = _uidoc.ActiveView as View3D;
        if (view3d is null && !createDedicated)
        {
            StatusText.Text = "La vista activa no es 3D. Marca 'Crear vista 3D dedicada' o cambia a una vista 3D.";
            return;
        }

        var result = _service.Apply(_uidoc.Document, request, view3d);
        StatusText.Text = result.ToSummary();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_uidoc.ActiveView is not View3D view3d)
        {
            StatusText.Text = "La vista activa no es 3D.";
            return;
        }
        var result = _service.Clear(view3d);
        StatusText.Text = result.ToSummary();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
