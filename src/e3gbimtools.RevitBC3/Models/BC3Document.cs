using System.Collections.Generic;
using System.Linq;

namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// Representacion en memoria de un archivo BC3 importado.
/// </summary>
public sealed class BC3Document
{
    /// <summary>Ruta del archivo original.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Version del formato FIEBDC-3 (campo VERSION del registro ~V).</summary>
    public string FormatVersion { get; set; } = string.Empty;

    /// <summary>Emisor del archivo (programa que lo genero — Presto, Arquimedes, etc.).</summary>
    public string Emitter { get; set; } = string.Empty;

    /// <summary>Fecha de emision tal cual viene en el ~V.</summary>
    public string EmissionDate { get; set; } = string.Empty;

    /// <summary>Codificacion detectada o declarada en el ~V.</summary>
    public string CharacterSet { get; set; } = string.Empty;

    /// <summary>
    /// Indice por codigo de TODOS los conceptos del documento (planos).
    /// La jerarquia se navega via <see cref="BC3Concept.Children"/>.
    /// </summary>
    public Dictionary<string, BC3Concept> Concepts { get; } = new();

    /// <summary>
    /// Conceptos raiz: aquellos que NO aparecen como hijo en ningun ~D.
    /// Son tipicamente capitulos de primer nivel (a veces uno solo con
    /// codigo especial tipo "##" que envuelve todo el presupuesto).
    /// </summary>
    public List<BC3Concept> Roots { get; } = new();

    /// <summary>Numero total de conceptos del documento.</summary>
    public int TotalConcepts => Concepts.Count;

    /// <summary>Numero de partidas (conceptos de tipo Partida).</summary>
    public int TotalPartidas =>
        Concepts.Values.Count(c => c.Type == ConceptType.Partida);

    /// <summary>Numero de capitulos.</summary>
    public int TotalCapitulos =>
        Concepts.Values.Count(c => c.Type == ConceptType.Capitulo);
}
