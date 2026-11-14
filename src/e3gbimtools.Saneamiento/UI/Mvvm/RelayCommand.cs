// ============================================================================
//  RelayCommand.cs
//  Implementación mínima de ICommand para wireado declarativo en XAML.
//  Soporta CanExecute → IsEnabled binding y un RaiseCanExecuteChanged
//  imperativo para refrescar el estado cuando cambian las dependencias.
// ============================================================================
using System;
using System.Windows.Input;

namespace E3G.Saneamiento.UI.Mvvm
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>Sobrecarga sin parámetro.</summary>
        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(
                _ => execute(),
                canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        { }

        public bool CanExecute(object parameter)
            => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
