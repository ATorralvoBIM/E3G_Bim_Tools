using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Views.Worksets;

namespace E3G.BimTools.DiTools.UI.Views.FamilyReviser;

/// <summary>
/// Code-behind de FamilyReviserWindow (Sprint 7).
/// La lógica de negocio vive en <see cref="FamilyReviserViewModel"/>;
/// aquí solo se conectan los eventos de UI.
/// </summary>
public partial class FamilyReviserWindow : Window
{
    private readonly FamilyReviserViewModel _vm;
    private readonly UIDocument _uidoc;
    private readonly IRevitUiDispatcher _dispatcher;

    public FamilyReviserWindow(
        UIDocument            uidoc,
        IFamilyReviserService familyService,
        IWorksetService       worksetService,
        IRevitUiDispatcher    dispatcher)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        _uidoc      = uidoc;
        _dispatcher = dispatcher;
        _vm         = new FamilyReviserViewModel(uidoc, familyService, worksetService);
        DataContext = _vm;

        // Mostrar banner de "no workshared" si aplica
        NotWorksharedBanner.Visibility = _vm.IsWorkshared
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Ejecuta en el hilo/contexto API de Revit (vía ExternalEvent) una operación
    /// que modifica el documento. Imprescindible porque la ventana es NO modal:
    /// los clics de botón no ocurren dentro de un contexto API válido y abrir una
    /// transacción directamente lanzaría una excepción.
    /// </summary>
    private async void RunOnRevit(Action action)
    {
        try
        {
            await _dispatcher.InvokeAsync(action);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "No se pudo completar la operación en Revit.\n\n" + ex.Message,
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================================================================== //
    //  PESTAÑA 1 — Renombrar                                              //
    // ================================================================== //

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectAll();
    }

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
    {
        _vm.SelectNone();
    }

    private void OnPreviewRenameClick(object sender, RoutedEventArgs e)
    {
        _vm.PreviewRename();
    }

    private void OnApplyRenameClick(object sender, RoutedEventArgs e)
    {
        var count = _vm.Families.Count(f => f.IsSelected && f.HasRenameChange);
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "No hay familias seleccionadas con cambios de nombre.\n" +
                "Usa 'Vista previa' primero para calcular los nombres propuestos.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Renombrar {count} familia(s)?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.ApplyRename);
    }

    // ================================================================== //
    //  PESTAÑA 2 — Exportar                                               //
    // ================================================================== //

    private void OnBrowseExportFolderClick(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog disponible en .NET 8 + WPF (Microsoft.Win32)
        var dlg = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta raíz para el export de familias",
        };
        if (dlg.ShowDialog() == true)
            _vm.ExportFolder = dlg.FolderName;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.ExportFolder))
        {
            System.Windows.MessageBox.Show(
                "Selecciona una carpeta de destino primero.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var count = _vm.Families.Count(f => f.IsSelected);
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "No hay familias seleccionadas para exportar.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Exportar {count} familia(s) a:\n{_vm.ExportFolder}?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.ExportToDisk);
    }

    // ================================================================== //
    //  PESTAÑA 3 — Worksets                                               //
    // ================================================================== //

    private void OnCreateWorksetClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.NewWorksetName))
        {
            System.Windows.MessageBox.Show("Escribe un nombre para el nuevo workset.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RunOnRevit(_vm.CreateWorkset);
    }

    private void OnImportWorksetListClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Selecciona un archivo de texto con la lista de worksets",
            Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _vm.BatchWorksetNames = System.IO.File.ReadAllText(dlg.FileName);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"No se pudo leer el archivo:\n{ex.Message}",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCreateWorksetsBatchClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var count = _vm.BatchWorksetNameLines.Count;
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "Escribe o importa al menos un nombre de workset (uno por línea).",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Crear hasta {count} workset(s)?\nSe omitirán los duplicados y los que ya existan.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.CreateWorksetsBatch);
    }

    private void OnRenameWorksetClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedWorkset is null)
        {
            System.Windows.MessageBox.Show("Selecciona un workset primero.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newName = _vm.SelectedWorkset.ProposedName;
        if (string.IsNullOrWhiteSpace(newName) || newName == _vm.SelectedWorkset.Name)
        {
            System.Windows.MessageBox.Show(
                "Edita el 'Nuevo nombre' del workset en la tabla antes de renombrar.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RunOnRevit(() => _vm.RenameSelectedWorkset(newName));
    }

    private void OnBulkRenameWorksetsClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var count = _vm.WorksetsWithRenameChange;
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "No hay worksets con cambios en la columna 'Nuevo nombre'.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Renombrar {count} workset(s) según la columna 'Nuevo nombre'?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.BulkRenameWorksets);
    }

    private void OnDeleteWorksetClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedWorkset is null)
        {
            System.Windows.MessageBox.Show("Selecciona un workset primero.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Eliminar el workset '{_vm.SelectedWorkset.Name}'?\n" +
            "Los elementos pasarán al workset activo por defecto.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.DeleteSelectedWorkset);
    }

    private void OnAssignFamiliesToWorksetClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedWorkset is null)
        {
            System.Windows.MessageBox.Show("Selecciona un workset de destino en la tabla.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var count = _vm.Families.Count(f => f.IsSelected);
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                "Selecciona familias en la pestaña 'Renombrar familias' primero.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Asignar {count} familia(s) al workset '{_vm.SelectedWorkset.Name}'?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.AssignSelectedFamiliesToWorkset);
    }

    private void OnLoadAssignablesClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_vm.ShowFamilies && !_vm.ShowRevitLinks && !_vm.ShowCadLinks)
        {
            System.Windows.MessageBox.Show("Marca al menos un tipo (familias o vínculos) para cargar la lista.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.LoadAssignableElements();
    }

    private void OnAssignCheckedElementsClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedWorkset is null)
        {
            System.Windows.MessageBox.Show("Selecciona un workset de destino en la tabla de arriba.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_vm.AssignableCheckedCount == 0)
        {
            System.Windows.MessageBox.Show(
                "Marca al menos un elemento en la lista (columna ✓). Usa 'Cargar lista' si está vacía.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Asignar {_vm.AssignableCheckedCount} elemento(s) al workset '{_vm.SelectedWorkset.Name}'?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.AssignCheckedElementsToWorkset);
    }

    private void OnConsolidateGridsLevelsClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "¿Mover todas las rejillas y niveles al workset 'Shared Levels and Grids'?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.ConsolidateGridsAndLevels);
    }

    private void OnOrganizeLinksClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_vm.OrganizeRevitLinks && !_vm.OrganizeCadLinks)
        {
            System.Windows.MessageBox.Show("Marca al menos un tipo de vínculo (Revit o CAD).",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "¿Crear un workset por cada vínculo y mover sus instancias?",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.OrganizeLinksIntoWorksets);
    }

    private void OnShowWorksetsClick(object sender, RoutedEventArgs e) => ApplyWorksetVisibility(true);

    private void OnHideWorksetsClick(object sender, RoutedEventArgs e) => ApplyWorksetVisibility(false);

    private void ApplyWorksetVisibility(bool visible)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_vm.WorksetsCheckedCount == 0)
        {
            System.Windows.MessageBox.Show(
                "Marca al menos un workset en la tabla (columna ✓).",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RunOnRevit(() => _vm.ApplyWorksetVisibility(visible));
    }

    private void OnVisibilityReportClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.GenerateVisibilityReport();
    }

    private void OnOpenVisibilityDockClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var pane = _uidoc.Application.GetDockablePane(WorksetDock.PaneId);
            pane?.Show();
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show(
                "No se pudo abrir el panel acoplable de visibilidad.\n\n" + ex.Message,
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCreateViewsPerWorksetClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsWorkshared)
        {
            System.Windows.MessageBox.Show("El documento no es workshared.",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_vm.WorksetsCheckedCount == 0)
        {
            System.Windows.MessageBox.Show(
                "Marca al menos un workset en la tabla (columna ✓).",
                "FamilyReviser", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var message = _vm.ViewModeIndex == 1
            ? $"¿Crear UNA vista 3D de coordinación con los {_vm.WorksetsCheckedCount} workset(s) marcado(s) visibles a la vez?"
            : $"¿Crear una vista 3D por cada uno de los {_vm.WorksetsCheckedCount} workset(s) marcado(s)?\n" +
              "En cada vista solo quedará visible su workset.";

        var confirm = System.Windows.MessageBox.Show(
            message, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        RunOnRevit(_vm.CreateViewsPerWorkset);
    }

    private void OnAuditWorksetsClick(object sender, RoutedEventArgs e)
    {
        _vm.AuditWorksets();
    }

    // ================================================================== //
    //  Footer                                                             //
    // ================================================================== //

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _vm.RefreshFamilies();
        _vm.RefreshWorksets();
        NotWorksharedBanner.Visibility = _vm.IsWorkshared
            ? Visibility.Collapsed
            : Visibility.Visible;
        FooterStatus.Text = "Actualizado.";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
