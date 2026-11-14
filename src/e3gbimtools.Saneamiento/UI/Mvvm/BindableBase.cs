// ============================================================================
//  BindableBase.cs
//  Base mínima INotifyPropertyChanged para todos los ViewModels.
//  Sin dependencias externas (no Prism / no MvvmLight / no CommunityToolkit).
// ============================================================================
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace E3G.Saneamiento.UI.Mvvm
{
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Asigna el campo y dispara PropertyChanged si cambia.</summary>
        protected bool Set<T>(ref T field, T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        /// <summary>Dispara PropertyChanged manualmente.</summary>
        protected void Raise([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
