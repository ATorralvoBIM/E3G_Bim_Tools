using System;
using System.Collections.Generic;
using System.Linq;
using E3G.BimTools.RevitBC3.Models;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Importa un archivo BC3 (FIEBDC-3) de TCQ (o cualquier software compatible)
/// y extrae una lista plana de <see cref="CatalogEntry"/> con código, unidad,
/// descripción, precio y relación padre-hijo (deducida de los registros ~D).
///
/// Sprint A6: se usa para poblar el <c>CatalogPickerDialog</c> desde el que
/// el usuario puede buscar y seleccionar partidas reales al crear/editar reglas.
///
/// Internamente delega la lectura FIEBDC-3 en <see cref="IBC3ImporterService"/>
/// (ya probado en Sprint A1), lo que garantiza compatibilidad de encoding
/// (Latin-1 / UTF-8) y robustez ante registros malformados.
/// </summary>
public sealed class BC3CatalogImporterService : IBC3CatalogImporterService
{
    private readonly IBC3ImporterService _inner;

    /// <summary>Constructor de producción: usa la implementación estándar.</summary>
    public BC3CatalogImporterService() : this(new BC3ImporterService()) { }

    /// <summary>Constructor para tests: permite inyectar un stub de <see cref="IBC3ImporterService"/>.</summary>
    public BC3CatalogImporterService(IBC3ImporterService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public List<CatalogEntry> Import(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Ruta vacía.", nameof(filePath));

        var doc = _inner.Load(filePath);

        // Mapa inverso: código hijo → código padre inmediato.
        // El BC3Document sólo guarda padre→hijos (BC3Concept.Children),
        // así que invertimos el grafo aquí.
        var parentByChild = BuildParentMap(doc);

        var entries = doc.Concepts.Values
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .Select(c => new CatalogEntry
            {
                Code        = c.Code,
                Unit        = c.Unit,
                Description = c.Summary,
                PriceUnit   = c.Price,
                Type        = MapType(c.Type),
                ParentCode  = parentByChild.TryGetValue(c.Code, out var p) ? p : null,
            })
            .OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return entries;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Construye un mapa childCode → parentCode a partir de los registros ~D
    /// ya parseados en <see cref="BC3Concept.Children"/>.
    /// Si un hijo aparece bajo varios padres (caso raro pero posible en BC3
    /// con referencias cruzadas), se conserva el primer padre encontrado.
    /// </summary>
    private static Dictionary<string, string> BuildParentMap(BC3Document doc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var concept in doc.Concepts.Values)
        {
            foreach (var dec in concept.Children)
            {
                if (!string.IsNullOrEmpty(dec.ChildCode) &&
                    !map.ContainsKey(dec.ChildCode))
                {
                    map[dec.ChildCode] = concept.Code;
                }
            }
        }

        return map;
    }

    private static string MapType(ConceptType t) => t switch
    {
        ConceptType.Capitulo   => "Capitulo",
        ConceptType.Partida    => "Partida",
        ConceptType.Material   => "Material",
        ConceptType.ManoDeObra => "ManoDeObra",
        ConceptType.Maquinaria => "Maquinaria",
        ConceptType.Auxiliar   => "Auxiliar",
        _                      => "Desconocido",
    };
}
