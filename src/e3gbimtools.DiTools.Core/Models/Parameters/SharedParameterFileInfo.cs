using System.Collections.Generic;

namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Vista del archivo de parametros compartidos (.txt FIEBDC) que tiene el
/// documento configurado.  Si no hay archivo, <see cref="Path"/> queda vacio.
/// </summary>
public sealed class SharedParameterFileInfo
{
    public string Path { get; set; } = string.Empty;

    public IList<SharedParameterGroupInfo> Groups { get; set; } = new List<SharedParameterGroupInfo>();

    public bool IsAvailable => !string.IsNullOrWhiteSpace(Path);
}

/// <summary>Grupo dentro del .txt de parametros compartidos.</summary>
public sealed class SharedParameterGroupInfo
{
    public string Name { get; set; } = string.Empty;
    public IList<SharedParameterDefinitionInfo> Definitions { get; set; } = new List<SharedParameterDefinitionInfo>();

    public override string ToString() => Name;
}

/// <summary>Definicion de un parametro compartido (no vinculada todavia al doc).</summary>
public sealed class SharedParameterDefinitionInfo
{
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string DataTypeName { get; set; } = string.Empty;
    public System.Guid Guid { get; set; }

    public override string ToString() => Name;
}
