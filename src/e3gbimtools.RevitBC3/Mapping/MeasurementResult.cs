using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Resultado acumulado para una partida BC3 tras aplicar las reglas.
/// Implementa INotifyPropertyChanged para que la UI refresque
/// FinalQuantity / Importe / HasOverride en tiempo real cuando el
/// usuario edita el override en la pestania 4.
/// </summary>
public sealed class MeasurementResult : INotifyPropertyChanged
{
    /// <summary>Codigo de la partida BC3 destino.</summary>
    public string BC3Code { get; set; } = string.Empty;

    /// <summary>Resumen/descripcion de la partida (si esta disponible del BC3 fuente).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Unidad de medida (m, m2, m3, ud, kg...).</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Capitulo al que pertenece la partida (segun la(s) regla(s) que matchearon).</summary>
    public string ChapterDisplay { get; set; } = string.Empty;

    private double _quantity;
    /// <summary>Cantidad acumulada en unidades metricas (m, m2, m3 o unidades).</summary>
    public double Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value) return;
            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FinalQuantity));
            OnPropertyChanged(nameof(Importe));
        }
    }

    /// <summary>Numero de elementos del modelo que aportaron a esta medicion.</summary>
    public int ElementCount { get; set; }

    private double _priceUnit;
    /// <summary>Precio unitario tomado del BC3 fuente (€/Ud).</summary>
    public double PriceUnit
    {
        get => _priceUnit;
        set
        {
            if (_priceUnit == value) return;
            _priceUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Importe));
        }
    }

    private double? _manualOverride;
    /// <summary>
    /// Override manual aplicado por el usuario (null = no aplicado).
    /// </summary>
    public double? ManualOverride
    {
        get => _manualOverride;
        set
        {
            if (_manualOverride == value) return;
            _manualOverride = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOverride));
            OnPropertyChanged(nameof(FinalQuantity));
            OnPropertyChanged(nameof(Importe));
        }
    }

    /// <summary>True si el usuario ha aplicado override manual sobre la cantidad calculada.</summary>
    [JsonIgnore]
    public bool HasOverride => _manualOverride.HasValue;

    /// <summary>Cantidad final (override si esta, calculada en otro caso).</summary>
    [JsonIgnore]
    public double FinalQuantity => _manualOverride ?? _quantity;

    /// <summary>Importe = FinalQuantity * PriceUnit.</summary>
    [JsonIgnore]
    public double Importe => FinalQuantity * _priceUnit;

    /// <summary>IDs de los elementos Revit que aportaron (util para depuracion).</summary>
    public List<long> ElementIds { get; set; } = new();

    /// <summary>
    /// Contribuciones por elemento (id Revit + IfcGUID + cantidad metrica).
    /// Mantiene el detalle de cada aporte para poder emitirlo como linea de
    /// medicion detallada en el BC3 exportado, llevando el IfcGUID como
    /// comentario por linea (visible en Presto/Arquimedes al hacer doble click
    /// sobre la partida).
    /// </summary>
    public List<ElementMeasurementContribution> ElementContributions { get; set; } = new();

    /// <summary>
    /// Nombres de las reglas que han contribuido a esta medicion (puede ser
    /// mas de una si distintas reglas apuntan a la misma partida BC3).
    /// </summary>
    public List<string> ContributingRules { get; set; } = new();

    /// <summary>
    /// Ids de las reglas (MappingRule.Id) que contribuyeron, en el mismo orden
    /// que <see cref="ContributingRules"/>.  Util para abrir el diagnostico
    /// desde la pestania de calculo / revisar.
    /// </summary>
    [JsonIgnore]
    public List<string> ContributingRuleIds { get; set; } = new();

    /// <summary>Resumen legible de las reglas contribuyentes para mostrar en la UI.</summary>
    [JsonIgnore]
    public string ContributingRulesSummary =>
        ContributingRules.Count == 0 ? "(sin regla)" : string.Join(" + ", ContributingRules);

    /// <summary>True si la medicion no proviene de ningun elemento (regla vacia).</summary>
    [JsonIgnore]
    public bool IsEmpty => ElementCount == 0 && !_manualOverride.HasValue;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => $"{BC3Code} = {Quantity:F2} {Unit} ({ElementCount} elem.)";
}
