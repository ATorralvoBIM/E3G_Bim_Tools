using System;
using System.Threading.Tasks;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Marshalling de codigo al hilo principal de Revit.  Imprescindible cuando
/// una operacion asincrona (descarga, calculo en hilo de fondo) necesita
/// volver a modificar el modelo.  Implementado sobre <c>ExternalEvent</c>.
/// </summary>
public interface IRevitUiDispatcher
{
    /// <summary>Ejecuta <paramref name="action"/> en el hilo de UI de Revit.</summary>
    Task InvokeAsync(Action action);

    /// <summary>Ejecuta <paramref name="func"/> en el hilo de UI de Revit y devuelve su resultado.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}
