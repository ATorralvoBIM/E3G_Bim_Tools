using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.Levels;
using E3G.BimTools.DiTools.UI.Controls;
using E3G.BimTools.DiTools.UI.Dialogs;

namespace E3G.BimTools.DiTools.UI.Views.CopyLevels;

/// <summary>
/// Ventana CopyLevels (Sprint 1).  Pone delante del usuario los vinculos
/// cargados del documento anfitrion, le permite seleccionar niveles uno a uno
/// y los copia al anfitrion (con prefijo/sufijo opcional + vistas de planta).
///
/// La ventana es modal (ShowDialog).  Reutiliza el tema e3g via
/// <see cref="E3gWindowBase.ApplyTheme"/>.
/// </summary>
public partial class CopyLevelsWindow : Window
{
    private readonly Document _hostDocument;
    private readonly ILevelCopyService _service;
    private readonly ObservableCollection<LevelRow> _rows = new();

    public CopyLevelsWindow(Document hostDocument, ILevelCopyService service)
    {
        _hostDocument = hostDocument ?? throw new ArgumentNullException(nameof(hostDocument));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        LevelsGrid.ItemsSource = _rows;
        ReloadLinks();
    }

    // ============================================================
    // Carga inicial / refresco de vinculos
    // ============================================================

    private void ReloadLinks()
    {
        var links = _service.GetLoadedLinks(_hostDocument);
        LinksCombo.ItemsSource = links;

        if (links.Count == 0)
        {
            LinksHint.Text =
                "No hay vinculos cargados en el documento. " +
                "Cárgalos desde 'Gestionar vínculos' antes de copiar niveles.";
            LinksCombo.SelectedIndex = -1;
            CopyButton.IsEnabled = false;
            return;
        }

        LinksHint.Text = $"{links.Count} vínculo(s) cargado(s) disponibles.";
        LinksCombo.SelectedIndex = 0;
        CopyButton.IsEnabled = true;
    }

    // ============================================================
    // Handlers
    // ============================================================

    private void OnRefreshLinksClick(object sender, RoutedEventArgs e) => ReloadLinks();

    private void OnLinkChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _rows.Clear();
        if (LinksCombo.SelectedItem is not RevitLinkOption opt)
        {
            UpdateCountText();
            return;
        }

        try
        {
            var levels = _service.GetLevelsFromLink(_hostDocument, opt.InstanceId);
            foreach (var lvl in levels)
                _rows.Add(new LevelRow(lvl));
        }
        catch (Exception ex)
        {
            ShowFriendlyError("No se pudieron leer los niveles del vínculo.", ex);
        }

        UpdateCountText();
        StatusText.Text = _rows.Count == 0
            ? "Este vínculo no contiene niveles."
            : "Marca los niveles que quieras traer al documento anfitrión.";
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.IsSelected = true;
    }

    private void OnDeselectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.IsSelected = false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (LinksCombo.SelectedItem is not RevitLinkOption opt)
        {
            MessageBox.Show(this, "Selecciona primero un vínculo.", "Copiar niveles",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = _rows.Where(r => r.IsSelected).Select(r => r.Descriptor).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this,
                "Marca al menos un nivel para copiar.",
                "Copiar niveles",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = new LevelCopyOptions
        {
            NamePrefix = PrefixBox.Text ?? string.Empty,
            NameSuffix = SuffixBox.Text ?? string.Empty,
            CreateFloorPlans = CreatePlansCheck.IsChecked == true,
        };

        try
        {
            var result = _service.CopyLevels(_hostDocument, opt.InstanceId, selected, options);
            StatusText.Text = result.ToSummary();

            if (result.Warnings.Count > 0)
            {
                ShowWarningsDialog(result);
            }
            else
            {
                MessageBox.Show(this,
                    result.ToSummary(),
                    "Copiar niveles · OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Refrescamos la columna "Estado" porque los nombres ahora pueden
            // existir en el anfitrion.
            ReloadCurrentLink();
        }
        catch (Exception ex)
        {
            ShowFriendlyError("La copia de niveles falló y se hizo rollback.", ex);
        }
    }

    private void ReloadCurrentLink()
    {
        if (LinksCombo.SelectedItem is not RevitLinkOption opt) return;
        var selectedNames = _rows.Where(r => r.IsSelected).Select(r => r.Descriptor.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _rows.Clear();
        var levels = _service.GetLevelsFromLink(_hostDocument, opt.InstanceId);
        foreach (var lvl in levels)
        {
            var row = new LevelRow(lvl);
            if (selectedNames.Contains(lvl.Name)) row.IsSelected = true;
            _rows.Add(row);
        }
        UpdateCountText();
    }

    private void UpdateCountText()
    {
        var ci = CultureInfo.GetCultureInfo("es-ES");
        if (_rows.Count == 0)
        {
            LevelsCountText.Text = "Sin niveles que mostrar";
            return;
        }
        var sel = _rows.Count(r => r.IsSelected);
        LevelsCountText.Text = $"{_rows.Count.ToString("N0", ci)} nivel(es), {sel.ToString("N0", ci)} marcado(s)";
    }

    private void ShowFriendlyError(string title, Exception ex)
    {
        try
        {
            var dlg = FriendlyErrorDialog.ForException(ex);
            dlg.Owner = this;
            dlg.ShowDialog();
        }
        catch
        {
            // Fallback si el diálogo amigable falla.
            MessageBox.Show(this, $"{title}\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowWarningsDialog(LevelCopyResult result)
    {
        var body = result.ToSummary() + "\n\nAvisos:\n  · " + string.Join("\n  · ", result.Warnings);
        MessageBox.Show(this, body, "Copiar niveles · con avisos",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ============================================================
    // Fila bindeable del DataGrid
    // ============================================================

    public sealed class LevelRow : INotifyPropertyChanged
    {
        public LevelDescriptor Descriptor { get; }

        public LevelRow(LevelDescriptor d)
        {
            Descriptor = d;
        }

        public string Name => Descriptor.Name;
        public string ElevationDisplay => Descriptor.ElevationMeters.ToString("F3", CultureInfo.GetCultureInfo("es-ES"));
        public string StatusDisplay => Descriptor.ExistsInHost ? "⚠ Ya existe en el anfitrión" : "Nuevo";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
