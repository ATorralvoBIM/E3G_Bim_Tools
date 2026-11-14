using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace E3G.BimTools.RevitBC3.Mapping;

/// <summary>
/// Capitulo del presupuesto BC3 como entidad de primera clase (Sprint B).
///
/// Sustituye al "capitulo derivado del string ChapterCode de cada regla":
/// antes el arbol de capitulos se recomponia en cada export agrupando las
/// reglas por ChapterCode, lo que impedia crear capitulos vacios, mover
/// reglas entre capitulos sin retipear, reordenar capitulos y demas
/// fricciones recogidas en BRIEFING_Sprints_Partidas_BC3_UX.md.
///
/// Ahora <see cref="MappingRule.ChapterId"/> apunta a uno de estos por
/// Id (GUID).  El nombre y codigo se editan AQUI, no en la regla, lo
/// que elimina los "capitulos fantasma" por typos.
/// </summary>
public sealed class Chapter
{
    /// <summary>Identificador unico (GUID estable). Inmutable.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Codigo del capitulo, lo introduce el usuario. Ej. "01".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Nombre descriptivo. Ej. "SANEAMIENTO EDIFICIO A".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Orden de aparicion en el arbol y en el BC3 exportado.  Permite
    /// reordenar capitulos sin tocar codigos.  El writer respeta este
    /// campo (AC5.2) en lugar del orden alfabetico de <see cref="Code"/>.
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
