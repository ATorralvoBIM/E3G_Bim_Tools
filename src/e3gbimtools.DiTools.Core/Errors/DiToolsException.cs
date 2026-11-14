using System;

namespace E3G.BimTools.DiTools.Core.Errors;

/// <summary>
/// Excepcion base del modulo DiTools.  Permite distinguir errores propios
/// (que sabemos como presentar al usuario) de errores genericos del .NET o
/// del Revit API.
/// </summary>
public class DiToolsException : Exception
{
    public DiToolsException(string message) : base(message) { }
    public DiToolsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Excepcion de validacion: el usuario hizo algo que no tiene sentido
/// (vista incorrecta, sin seleccion, parametro inexistente, etc.).
/// Se muestra al usuario sin stack trace.
/// </summary>
public sealed class DiToolsValidationException : DiToolsException
{
    public DiToolsValidationException(string message) : base(message) { }
}
