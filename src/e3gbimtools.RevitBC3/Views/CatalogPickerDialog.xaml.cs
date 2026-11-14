using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using E3G.BimTools.RevitBC3.Models;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Diálogo modal para seleccionar una partida del catálogo BC3 importado.
///
/// Sprint A6: se abre al pulsar el botón lupa (🔍) junto al campo
/// "PARTIDA BC3" en el editor de reglas o en la tabla de reglas.
/// Muestra todas las entradas del catálogo (capítulos y partidas) con
/// una caja de búsqueda que filtra en tiempo real por código y descripción.
///
/// Uso:
/// <code>
///   var dlg = new CatalogPickerDialog(catalogEntries) { Owner = this };
///   if (dlg.ShowDialog() == true)
///       rule.BC3Code = dlg.SelectedEntry!.Code;
/// </code>
/// </summary>
public partial class CatalogPickerDialog : Window
{
    // Todas las entradas cargadas al abrir el diálogo.
    private readonly IReadOnlyList<CatalogEntry> _allEntries;

    // Lista actualmente visible (filtrada).
    private List<CatalogEntry> _filtered = new();

    /// <summary>
    /// Entrada seleccionada por el usuario. Solo válida si
    /// <c>ShowDialog() == true</c>.
    /// </summary>
    public CatalogEntry? SelectedEntry { get; private set; }

    public CatalogPickerDialog(IReadOnlyList<CatalogEntry> entries)
    {
        _allEntries = entries ?? throw new ArgumentNullException(nameof(entries));
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        ApplyFilter(string.Empty);
        SearchBox.Focus();
    }

    // ----------------------------------------------------------------
    // Filtrado
    // ----------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text ?? string.Empty;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter(query.Trim());
    }

    private void ApplyFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _filtered = _allEntries.ToList();
        }
        else
        {
            var q = query.ToUpperInvariant();
            _filtered = _allEntries
                .Where(e =>
                    e.Code.ToUpperInvariant().Contains(q) ||
                    e.Description.ToUpperInvariant().Contains(q))
                .ToList();
        }

        ResultsGrid.ItemsSource = _filtered;
        ResultCountText.Text = _filtered.Count == 1
            ? "1 resultado"
            : $"{_filtered.Count} resultados";

        // Si hay exactamente un resultado, lo pre-seleccionamos.
        if (_filtered.Count == 1)
            ResultsGrid.SelectedIndex = 0;
    }

    // ----------------------------------------------------------------
    // Selección
    // ----------------------------------------------------------------

    private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = ResultsGrid.SelectedItem as CatalogEntry;
        SelectButton.IsEnabled = entry is not null;

        if (entry is not null)
        {
            SelectionPreviewCode.Text = entry.Code;
            SelectionPreviewDesc.Text = entry.Description;
        }
        else
        {
            SelectionPreviewCode.Text = string.Empty;
            SelectionPreviewDesc.Text = string.Empty;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Doble clic sobre una fila confirma la selección directamente.
        if (ResultsGrid.SelectedItem is CatalogEntry)
            Confirm();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        // Enter en la caja de búsqueda confirma si hay algo seleccionado.
        if (e.Key == Key.Enter && SelectButton.IsEnabled)
            Confirm();
    }

    private void OnSelectClick(object sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm()
    {
        SelectedEntry = ResultsGrid.SelectedItem as CatalogEntry;
        if (SelectedEntry is null) return;
        DialogResult = true;
        Close();
    }
}
