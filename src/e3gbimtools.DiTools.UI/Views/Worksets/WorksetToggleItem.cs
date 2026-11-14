using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace E3G.BimTools.DiTools.UI.Views.Worksets;

/// <summary>
/// Fila del panel acoplable de visibilidad: un workset con un toggle
/// que refleja (y cambia al vuelo) su estado de visibilidad en la vista
/// o plantilla de vista destino.
/// </summary>
public sealed class WorksetToggleItem : INotifyPropertyChanged
{
    public WorksetToggleItem(WorksetId id, string name)
    {
        Id   = id;
        Name = name;
    }

    public WorksetId Id   { get; }
    public string    Name { get; }

    /// <summary>
    /// Cuando es <c>true</c>, los cambios de <see cref="IsVisible"/> no disparan
    /// <see cref="Toggled"/> (se usa al refrescar el estado desde Revit).
    /// </summary>
    public bool Suppress { get; set; }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
            if (!Suppress) Toggled?.Invoke(this);
        }
    }

    /// <summary>Se dispara cuando el usuario cambia el toggle (no en refrescos).</summary>
    public event Action<WorksetToggleItem>? Toggled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
