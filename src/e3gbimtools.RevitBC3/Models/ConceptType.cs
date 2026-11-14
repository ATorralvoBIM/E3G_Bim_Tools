namespace E3G.BimTools.RevitBC3.Models;

/// <summary>
/// Tipos de concepto en un BC3 (FIEBDC-3).  Se infiere a partir del campo
/// TIPO del registro ~C y de la posicion en la jerarquia (un concepto con
/// descendientes en ~D es habitualmente un capitulo).
/// </summary>
public enum ConceptType
{
    /// <summary>Tipo desconocido o no clasificado.</summary>
    Unknown,

    /// <summary>Capitulo (agrupa partidas/sub-capitulos).</summary>
    Capitulo,

    /// <summary>Partida (linea de medicion presupuestable).</summary>
    Partida,

    /// <summary>Material basico.</summary>
    Material,

    /// <summary>Mano de obra.</summary>
    ManoDeObra,

    /// <summary>Maquinaria.</summary>
    Maquinaria,

    /// <summary>Auxiliar / partida alzada.</summary>
    Auxiliar,
}
