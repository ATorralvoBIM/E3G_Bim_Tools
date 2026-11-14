using System;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;

namespace E3G.BimTools.DiTools.Core.Services;

/// <summary>
/// Implementacion esqueletica de <see cref="IParameterService"/>.  Se ampliara
/// en Sprint 3 (ParaManager) con creacion/modificacion/eliminacion completas.
/// </summary>
public sealed class ParameterService : IParameterService
{
    public Parameter? Find(Element element, string parameterName)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentException("Nombre de parametro vacio.", nameof(parameterName));

        // LookupParameter es accesible en todas las versiones soportadas (Revit 2022+).
        return element.LookupParameter(parameterName);
    }

    public bool TrySet(Element element, string parameterName, object value)
    {
        var p = Find(element, parameterName);
        if (p is null || p.IsReadOnly) return false;

        return value switch
        {
            string s    => p.Set(s),
            int i       => p.Set(i),
            double d    => p.Set(d),
            bool b      => p.Set(b ? 1 : 0),
            ElementId e => p.Set(e),
            _           => p.Set(value?.ToString() ?? string.Empty)
        };
    }
}
