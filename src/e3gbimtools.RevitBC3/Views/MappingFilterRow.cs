using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using E3G.BimTools.RevitBC3.Mapping;

namespace E3G.BimTools.RevitBC3.Views;

/// <summary>
/// Wrapper de UI para una fila del DataGrid de filtros en NewRuleDialog.
/// Implementa INotifyPropertyChanged para que la ventana pueda detectar
/// cambios de parametro y poblar la lista de valores sugeridos.
/// </summary>
public sealed class MappingFilterRow : INotifyPropertyChanged
{
    private string _parameterName = string.Empty;
    private MappingOperator _operator = MappingOperator.Equals;
    private string _value = string.Empty;

    public string ParameterName
    {
        get => _parameterName;
        set
        {
            if (_parameterName == value) return;
            _parameterName = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public MappingOperator Operator
    {
        get => _operator;
        set
        {
            if (_operator == value) return;
            _operator = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Valores existentes en el modelo para el parametro seleccionado.
    /// Se actualiza desde fuera (NewRuleDialog) cuando cambia el parametro.
    /// </summary>
    public ObservableCollection<string> AvailableValues { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MappingFilter ToFilter() => new()
    {
        ParameterName = ParameterName,
        ParameterDisplayName = ParameterName,
        Operator = Operator,
        Value = Value,
    };

    public static MappingFilterRow From(MappingFilter filter) => new()
    {
        ParameterName = filter.ParameterName,
        Operator = filter.Operator,
        Value = filter.Value,
    };
}
