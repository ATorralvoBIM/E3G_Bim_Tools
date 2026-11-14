using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.UI.Controls;

namespace E3G.BimTools.DiTools.UI.Views.FilterGraphics;

/// <summary>
/// Ventana modal de importación: destinos múltiples (vistas y plantillas),
/// opciones, vista previa y aplicar. Se abre DESDE el bus de ExternalEvent,
/// así que mientras está abierta hay contexto válido de la API y puede leer
/// y escribir el documento directamente.
/// </summary>
public partial class FilterImportWindow : Window
{
    private readonly Document _doc;
    private readonly FilterGraphicsPreset _preset;
    private readonly List<TargetItem> _targets;
    private readonly ICollectionView _targetsView;

    /// <summary>Informe final (null si se canceló).</summary>
    public string? Report { get; private set; }

    internal FilterImportWindow(Document doc, FilterGraphicsPreset preset, string presetName,
                                View? preselected, bool addMissingDefault)
    {
        InitializeComponent();
        E3gWindowBase.ApplyTheme(this);

        _doc = doc;
        _preset = preset;

        var withRules = preset.Filters.Count(f => f.Definition is not null);
        PresetText.Text =
            $"{presetName} · {preset.Filters.Count} filtro(s), {withRules} con reglas" +
            (preset.SourceView is null ? "" : $" · origen: {preset.SourceDocument} / {preset.SourceView}");

        OptAdd.IsChecked = addMissingDefault;

        _targets = BuildTargets(doc, preselected);
        _targetsView = CollectionViewSource.GetDefaultView(_targets);
        _targetsView.Filter = o => o is TargetItem t && MatchesFilter(t);
        TargetsGrid.ItemsSource = _targetsView;
    }

    // ------------------------------------------------------------------ //

    private static List<TargetItem> BuildTargets(Document doc, View? preselected)
    {
        var list = new List<TargetItem>();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            bool allowed;
            try { allowed = v.AreGraphicsOverridesAllowed(); } catch { allowed = false; }
            if (!allowed) continue;

            if (v.IsTemplate)
            {
                int n = FilterTargets.CountViewsUsing(doc, v);
                var info = FilterTargets.TemplateControlsFilters(v)
                    ? $"usada por {n} vista(s)"
                    : $"no controla filtros · {n} vista(s)";
                list.Add(new TargetItem(v, "Plantilla", info));
            }
            else if (FilterTargets.ControllingTemplate(doc, v) is null)
            {
                list.Add(new TargetItem(v, "Vista", v.ViewType.ToString()));
            }
        }

        foreach (var t in list)
            t.IsChecked = preselected is not null && t.View.Id == preselected.Id;

        return list
            .OrderByDescending(t => t.IsChecked)
            .ThenBy(t => t.Kind)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private bool MatchesFilter(TargetItem t)
    {
        if (t.IsChecked) return true;
        if (t.View.IsTemplate && ShowTemplates?.IsChecked != true) return false;
        if (!t.View.IsTemplate && ShowViews?.IsChecked != true) return false;
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || t.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _targetsView?.Refresh();

    private FilterImportOptions Options() => new()
    {
        AddMissingToView     = OptAdd.IsChecked == true,
        CreateMissingFilters = OptCreate.IsChecked == true,
        UpdateDifferentRules = OptUpdate.IsChecked == true,
        KeepJsonOrder        = OptOrder.IsChecked == true,
        RemoveNotInJson      = OptReplace.IsChecked == true,
    };

    private List<View> CheckedViews() => _targets.Where(t => t.IsChecked).Select(t => t.View).ToList();

    // ------------------------------------------------------------------ //

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        var views = CheckedViews();
        if (views.Count == 0) { MessageBox.Show(this, "Marca al menos una vista o plantilla.", Title); return; }

        try
        {
            var rows = new FilterGraphicsImporter(_doc, _preset, Options()).Run(views, dryRun: true);
            PreviewGrid.ItemsSource = rows;
            PreviewSummary.Text =
                $"Vista previa: {rows.Count(r => r.Level == 1)} cambio(s), " +
                $"{rows.Count(r => r.Level == 0)} sin cambios, {rows.Count(r => r.Level == 2)} aviso(s) " +
                $"en {views.Count} destino(s)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo calcular la vista previa:\n{ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var views = CheckedViews();
        if (views.Count == 0) { MessageBox.Show(this, "Marca al menos una vista o plantilla.", Title); return; }

        int affected = views.Where(v => v.IsTemplate).Sum(v => FilterTargets.CountViewsUsing(_doc, v));
        var opts = Options();
        if (affected > 0 || opts.RemoveNotInJson)
        {
            var msg = $"Se modificarán {views.Count} destino(s)";
            if (affected > 0) msg += $"; las plantillas marcadas afectan a {affected} vista(s)";
            if (opts.RemoveNotInJson) msg += ".\nSe QUITARÁN de esos destinos los filtros que no estén en el JSON";
            if (MessageBox.Show(this, msg + ".\n\n¿Continuar?", Title,
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        var importer = new FilterGraphicsImporter(_doc, _preset, opts);
        using var t = new Transaction(_doc, "Grafismo de filtros: importar");
        try
        {
            t.Start();
            var rows = importer.Run(views, dryRun: false);
            t.Commit();

            Report = $"Destinos: {views.Count}\n" + FilterGraphicsImporter.Summarize(rows, importer.MissingPatterns);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
            MessageBox.Show(this, $"No se aplicó nada (se deshizo la transacción):\n{ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------ //

    public sealed class TargetItem : INotifyPropertyChanged
    {
        public TargetItem(View view, string kind, string info)
        {
            View = view;
            Kind = kind;
            Info = info;
            Name = view.Name;
        }

        public View   View { get; }
        public string Kind { get; }
        public string Name { get; }
        public string Info { get; }

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
