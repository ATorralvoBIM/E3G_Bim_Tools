using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using E3G.BimTools.DiTools.Core.Models.Filter;

namespace E3G.BimTools.DiTools.UI.Views.OneFilter;

/// <summary>
/// Editor recursivo de un <see cref="FilterGroup"/>.  Permite cambiar la logica
/// AND/OR, anadir/quitar condiciones y subgrupos.  Cada subgrupo se renderiza
/// usando otro FilterGroupEditor (anidacion ilimitada).
/// </summary>
public partial class FilterGroupEditor : UserControl
{
    public FilterGroup Group { get; private set; } = new();
    public IReadOnlyList<ParameterDescriptor> ParameterChoices { get; set; } = Array.Empty<ParameterDescriptor>();

    /// <summary>
    /// Si el padre permite eliminar este subgrupo, asigna esta accion para que
    /// el boton "Eliminar grupo" sea visible.
    /// </summary>
    public Action? OnRemoveSelf { get; set; }

    private readonly ObservableCollection<ConditionRow> _conditionRows = new();

    public FilterGroupEditor()
    {
        InitializeComponent();
        ConditionsList.ItemsSource = _conditionRows;
        LogicCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Inicializa el editor con un grupo y la lista de parametros disponibles.
    /// Llamar despues de InitializeComponent.
    /// </summary>
    public void Initialize(FilterGroup group, IReadOnlyList<ParameterDescriptor> parameters, bool allowRemove)
    {
        Group = group ?? new FilterGroup();
        ParameterChoices = parameters ?? Array.Empty<ParameterDescriptor>();

        LogicCombo.SelectedIndex = Group.Logic == FilterLogic.And ? 0 : 1;
        RemoveSelfButton.Visibility = allowRemove ? Visibility.Visible : Visibility.Collapsed;

        _conditionRows.Clear();
        foreach (var c in Group.Conditions)
            _conditionRows.Add(new ConditionRow(c, ParameterChoices));

        SubGroupsHost.Children.Clear();
        foreach (var sg in Group.SubGroups.ToList())
            AddSubGroupEditor(sg);
    }

    /// <summary>Refresca la lista de parametros (cuando cambia la seleccion de categorias).</summary>
    public void UpdateParameterChoices(IReadOnlyList<ParameterDescriptor> parameters)
    {
        ParameterChoices = parameters ?? Array.Empty<ParameterDescriptor>();
        foreach (var row in _conditionRows) row.UpdateParameters(ParameterChoices);
        foreach (var child in SubGroupsHost.Children.OfType<FilterGroupEditor>())
            child.UpdateParameterChoices(ParameterChoices);
    }

    // ============================================================
    // Handlers
    // ============================================================

    private void OnLogicChanged(object sender, SelectionChangedEventArgs e)
    {
        Group.Logic = LogicCombo.SelectedIndex == 1 ? FilterLogic.Or : FilterLogic.And;
    }

    private void OnAddConditionClick(object sender, RoutedEventArgs e)
    {
        var cond = new FilterCondition();
        Group.Conditions.Add(cond);
        _conditionRows.Add(new ConditionRow(cond, ParameterChoices));
    }

    private void OnRemoveConditionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ConditionRow row) return;

        Group.Conditions.Remove(row.Model);
        _conditionRows.Remove(row);
    }

    private void OnAddSubGroupClick(object sender, RoutedEventArgs e)
    {
        var sg = new FilterGroup();
        Group.SubGroups.Add(sg);
        AddSubGroupEditor(sg);
    }

    private void OnRemoveSelfClick(object sender, RoutedEventArgs e)
    {
        OnRemoveSelf?.Invoke();
    }

    /// <summary>
    /// Cuando el usuario elige un parametro del desplegable, copiamos su Name y
    /// el flag IsTypeParameter al modelo.  Ignoramos cambios sin selecion real
    /// (puede ocurrir al teclear texto libre).
    /// </summary>
    private void OnParameterComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.DataContext is not ConditionRow row) return;
        if (cb.SelectedItem is not ParameterDescriptor desc) return;

        row.Model.ParameterName = desc.Name;
        row.Model.UseTypeParameter = desc.IsTypeParameter;
    }

    private void AddSubGroupEditor(FilterGroup sg)
    {
        var editor = new FilterGroupEditor();
        editor.Initialize(sg, ParameterChoices, allowRemove: true);
        editor.OnRemoveSelf = () =>
        {
            Group.SubGroups.Remove(sg);
            SubGroupsHost.Children.Remove(editor);
        };
        SubGroupsHost.Children.Add(editor);
    }

    // ============================================================
    // Fila bindeable de una condicion
    // ============================================================

    public sealed class ConditionRow : INotifyPropertyChanged
    {
        public FilterCondition Model { get; }

        public ConditionRow(FilterCondition model, IReadOnlyList<ParameterDescriptor> parameterChoices)
        {
            Model = model;
            _parameterChoices = parameterChoices;
        }

        private IReadOnlyList<ParameterDescriptor> _parameterChoices;
        public IReadOnlyList<ParameterDescriptor> ParameterChoices
        {
            get => _parameterChoices;
            private set { _parameterChoices = value; OnPropertyChanged(); }
        }

        public void UpdateParameters(IReadOnlyList<ParameterDescriptor> parameters)
            => ParameterChoices = parameters;

        public IReadOnlyList<OperatorOption> OperatorChoices { get; } = new[]
        {
            new OperatorOption(FilterOperator.Equal,         "="),
            new OperatorOption(FilterOperator.NotEqual,      "≠"),
            new OperatorOption(FilterOperator.Greater,       ">"),
            new OperatorOption(FilterOperator.GreaterOrEqual,"≥"),
            new OperatorOption(FilterOperator.Less,          "<"),
            new OperatorOption(FilterOperator.LessOrEqual,   "≤"),
            new OperatorOption(FilterOperator.Contains,      "contiene"),
            new OperatorOption(FilterOperator.NotContains,   "no contiene"),
            new OperatorOption(FilterOperator.StartsWith,    "empieza por"),
            new OperatorOption(FilterOperator.EndsWith,      "acaba en"),
            new OperatorOption(FilterOperator.IsEmpty,       "está vacío"),
            new OperatorOption(FilterOperator.IsNotEmpty,    "no está vacío"),
        };

        public string ParameterName
        {
            get => Model.ParameterName;
            set
            {
                if (Model.ParameterName == value) return;
                Model.ParameterName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public FilterOperator Operator
        {
            get => Model.Operator;
            set
            {
                if (Model.Operator == value) return;
                Model.Operator = value;
                OnPropertyChanged();
            }
        }

        public string Value
        {
            get => Model.Value;
            set
            {
                if (Model.Value == value) return;
                Model.Value = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class OperatorOption
    {
        public OperatorOption(FilterOperator op, string display)
        {
            Operator = op;
            Display = display;
        }
        public FilterOperator Operator { get; }
        public string Display { get; }
    }
}
