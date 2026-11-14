using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.UI.Controls;
using Microsoft.Win32;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Auditoría de filtros del proyecto: dónde se usa cada filtro, si tiene
/// grafismo y cuáles no se usan en ninguna vista ni plantilla. Se abre desde
/// el bus de ExternalEvent (contexto API válido mientras está abierta).
/// </summary>
public partial class FilterAuditWindow : Window
{
    private readonly Document _doc;
    private List<AuditRow> _rows = new();

    /// <summary>True si se eliminó algún filtro (el panel debe refrescarse).</summary>
    public bool Modified { get; private set; }

    internal FilterAuditWindow(Document doc)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);
        _doc = doc;
        Reload();
    }

    private void Reload()
    {
        var filters = new FilteredElementCollector(_doc)
            .OfClass(typeof(FilterElement)).Cast<FilterElement>().ToList();
        var usage = filters.ToDictionary(f => f.Id, _ => new Usage());
        var mapper = new FilterGraphicsMapper(_doc);

        foreach (var v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
        {
            bool allowed;
            try { allowed = v.AreGraphicsOverridesAllowed(); } catch { allowed = false; }
            if (!allowed) continue;
            // Las vistas controladas por plantilla heredan sus filtros: cuentan en la plantilla.
            if (!v.IsTemplate && FilterTargets.ControllingTemplate(_doc, v) is not null) continue;

            IEnumerable<ElementId> ids;
            try { ids = v.GetFilters(); } catch { continue; }

            foreach (var id in ids)
            {
                if (!usage.TryGetValue(id, out var u)) continue;
                if (v.IsTemplate) u.Templates++; else u.Views++;
                if (u.Where.Count < 15) u.Where.Add((v.IsTemplate ? "Plantilla: " : "Vista: ") + v.Name);

                try
                {
                    var e = mapper.Read(v, id, string.Empty);
                    if (e.HasAnyOverride) u.WithGraphics++;
                    if (!e.Enabled) u.Disabled++;
                }
                catch { /* ignore */ }
            }
        }

        _rows = filters.Select(f =>
        {
            var u = usage[f.Id];
            int uses = u.Views + u.Templates;
            int cats = f is ParameterFilterElement p ? p.GetCategories().Count : 0;

            (string status, int level) =
                uses == 0                 ? ("Sin usar en ninguna vista ni plantilla", 2) :
                u.WithGraphics == 0       ? ("Se usa pero sin grafismo (solo visibilidad)", 1) :
                u.Disabled == uses        ? ("Desactivado en todos los usos", 1) :
                                            ("OK", 0);

            if (f is ParameterFilterElement pfe && FilterDefinitionSerializer.Read(_doc, pfe) is null)
                status += " · reglas no exportables";

            return new AuditRow
            {
                Id = f.Id, Name = f.Name,
                Kind = f is SelectionFilterElement ? "Selección" : "Reglas",
                Categories = f is SelectionFilterElement ? "—" : cats.ToString(),
                Views = u.Views, Templates = u.Templates, WithGraphics = u.WithGraphics,
                Status = status, Level = level, IsUnused = uses == 0,
                UsedIn = uses == 0 ? "No se usa" : string.Join("\n", u.Where) + (uses > u.Where.Count ? $"\n… y {uses - u.Where.Count} más" : ""),
            };
        })
        .OrderByDescending(r => r.Level)
        .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        Grid.ItemsSource = _rows;
        SummaryText.Text =
            $"{_rows.Count} filtro(s) · {_rows.Count(r => r.IsUnused)} sin usar · " +
            $"{_rows.Count(r => r.Level == 1)} con observaciones";
    }

    private void OnCheckUnusedClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.IsChecked = r.IsUnused;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var toDelete = _rows.Where(r => r.IsChecked && r.IsUnused).ToList();
        int skipped = _rows.Count(r => r.IsChecked && !r.IsUnused);
        if (toDelete.Count == 0)
        {
            MessageBox.Show(this, "Marca filtros SIN USAR para eliminar (los que se usan no se eliminan).", Title);
            return;
        }

        var msg = $"Se eliminarán {toDelete.Count} filtro(s) sin usar del proyecto." +
                  (skipped > 0 ? $"\n{skipped} marcado(s) en uso se ignoran." : "") +
                  "\nSe puede deshacer con Ctrl+Z.\n\n¿Continuar?";
        if (MessageBox.Show(this, msg, Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        using var t = new Transaction(_doc, "Auditoría de filtros: eliminar sin usar");
        try
        {
            t.Start();
            _doc.Delete(toDelete.Select(r => r.Id).ToList());
            t.Commit();
            Modified = true;
        }
        catch (Exception ex)
        {
            if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
            MessageBox.Show(this, $"No se pudieron eliminar:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Reload();
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar auditoría de filtros",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"Auditoria_filtros_{_doc.Title}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;

        var sb = new StringBuilder("Filtro;Tipo;Categorias;Vistas;Plantillas;ConGrafismo;Estado;UsadoEn\r\n");
        foreach (var r in _rows)
            sb.Append(string.Join(";", Csv(r.Name), r.Kind, r.Categories, r.Views, r.Templates, r.WithGraphics,
                Csv(r.Status), Csv(r.UsedIn.Replace('\n', '|')))).Append("\r\n");

        try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    // ------------------------------------------------------------------ //

    private sealed class Usage
    {
        public int Views, Templates, WithGraphics, Disabled;
        public List<string> Where { get; } = new();
    }

    public sealed class AuditRow : INotifyPropertyChanged
    {
        public ElementId Id { get; init; } = ElementId.InvalidElementId;
        public string Name { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Categories { get; init; } = string.Empty;
        public int    Views { get; init; }
        public int    Templates { get; init; }
        public int    WithGraphics { get; init; }
        public string Status { get; init; } = string.Empty;
        public int    Level { get; init; }
        public bool   IsUnused { get; init; }
        public string UsedIn { get; init; } = string.Empty;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
