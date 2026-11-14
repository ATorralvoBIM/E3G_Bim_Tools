using System;
using System.Text.Json.Serialization;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Subcapitulo del presupuesto BC3 como entidad de primera clase (Sprint B).
///
/// Pertenece a un <see cref="Chapter"/> via <see cref="ChapterId"/>.
/// Las reglas que pertenecen a este subcapitulo se enlazan via
/// <see cref="MappingRule.SubChapterId"/>.
/// </summary>
public sealed class SubChapter
{
    /// <summary>Identificador unico (GUID estable). Inmutable.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK al capitulo padre.  No nullable: un subcapitulo siempre cuelga de un capitulo.</summary>
    public string ChapterId { get; set; } = string.Empty;

    /// <summary>Codigo del subcapitulo. Ej. "AFS".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Nombre descriptivo. Ej. "Aigua Freda Sanitaria".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Orden de aparicion dentro de su capitulo y en el BC3 exportado.
    /// </summary>
    public int Order { get; set; }

    /// <summary>Etiqueta legible para listas y arbol.</summary>
    [JsonIgnore]
    public string Display
    {
        get
        {
            var c = (Code ?? string.Empty).Trim();
            var n = (Name ?? string.Empty).Trim();
            if (c.Length == 0 && n.Length == 0) return "(sin nombre)";
            if (c.Length == 0) return n;
            if (n.Length == 0) return c;
            return $"{c} — {n}";
        }
    }

    public override string ToString() => Display;
}
