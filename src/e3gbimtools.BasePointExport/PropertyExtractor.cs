// ============================================================================
//  PropertyExtractor.cs
//  BasePointView — Exportador Revit (Sprint 4).
//
//  Extrae las propiedades de un Element de Revit al esquema JSON neutro que
//  consume el visor web (RevitElementData), indexado por UniqueId.  Es una
//  frontera del estilo RevitBridge: SI usa Autodesk.Revit.DB y convierte a
//  unidades de presentacion con UnitUtils (robusto entre versiones de Revit).
//
//  El esquema debe coincidir con src/viewer/PropertyBinding.ts del visor:
//    { name, category, attributes, dimensions{value,unit}, materials, psets }
// ============================================================================

using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace E3G.BimTools.BasePointExport
{
    /// <summary>Dimension con valor numerico y unidad (p. ej. 4000 "mm").</summary>
    public sealed class Dimension
    {
        public double Value { get; set; }
        public string? Unit { get; set; }
    }

    /// <summary>
    /// Propiedades de un elemento en el formato que espera el visor web.
    /// Los nombres se serializan en camelCase (name, category, attributes…).
    /// </summary>
    public sealed class RevitElementData
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public Dictionary<string, object> Attributes { get; set; } = new();
        public Dictionary<string, Dimension> Dimensions { get; set; } = new();
        public List<string> Materials { get; set; } = new();
        public Dictionary<string, Dictionary<string, object>> Psets { get; set; } = new();
    }

    /// <summary>
    /// Extrae propiedades de elementos de Revit al esquema <see cref="RevitElementData"/>.
    /// </summary>
    public static class PropertyExtractor
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // No escapar acentos ni los simbolos m², m³, Ø, etc.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        /// <summary>Serializa la tabla UniqueId → propiedades a JSON.</summary>
        public static string ToJson(IReadOnlyDictionary<string, RevitElementData> table)
            => JsonSerializer.Serialize(table, JsonOptions);

        /// <summary>Construye el <see cref="RevitElementData"/> de un elemento.</summary>
        public static RevitElementData Extract(Element element, Document document)
        {
            var data = new RevitElementData
            {
                Name = ReadableName(element),
                Category = element.Category != null ? element.Category.Name : "Sin categoría",
            };

            AddIdentity(element, document, data);
            AddMaterials(element, document, data);
            ScanParameters(element, data, instanceLevel: true);
            ScanParameters(TypeOf(element, document), data, instanceLevel: false);

            return data;
        }

        // ------------------------------------------------------------------
        //  Identidad
        // ------------------------------------------------------------------
        private static void AddIdentity(Element element, Document document, RevitElementData data)
        {
            var type = TypeOf(element, document);
            if (type != null && !string.IsNullOrWhiteSpace(type.Name))
                data.Attributes["Tipo"] = type.Name;

            var levelId = element.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                var level = document.GetElement(levelId);
                if (level != null) data.Attributes["Nivel"] = level.Name;
            }

            AddStringParam(element, BuiltInParameter.ALL_MODEL_MARK, "Marca", data.Attributes);
            AddStringParam(element, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comentarios", data.Attributes);
        }

        private static void AddStringParam(
            Element element, BuiltInParameter bip, string label, Dictionary<string, object> target)
        {
            var p = element.get_Parameter(bip);
            var value = p != null && p.HasValue ? p.AsString() : null;
            if (!string.IsNullOrWhiteSpace(value)) target[label] = value!;
        }

        // ------------------------------------------------------------------
        //  Materiales
        // ------------------------------------------------------------------
        private static void AddMaterials(Element element, Document document, RevitElementData data)
        {
            ICollection<ElementId>? ids = null;
            try { ids = element.GetMaterialIds(false); }
            catch { /* algunos elementos no soportan GetMaterialIds */ }
            if (ids == null) return;

            foreach (var id in ids)
            {
                if (document.GetElement(id) is Material material
                    && !string.IsNullOrWhiteSpace(material.Name)
                    && !data.Materials.Contains(material.Name))
                {
                    data.Materials.Add(material.Name);
                }
            }
        }

        // ------------------------------------------------------------------
        //  Parametros → dimensiones y property sets
        // ------------------------------------------------------------------
        private static void ScanParameters(Element? element, RevitElementData data, bool instanceLevel)
        {
            if (element == null) return;

            foreach (Parameter p in element.Parameters)
            {
                if (p == null || !p.HasValue || p.Definition is not Definition def) continue;
                var label = def.Name;
                if (string.IsNullOrWhiteSpace(label)) continue;

                // Dimensiones: longitud / area / volumen → mm, m², m³.
                if (p.StorageType == StorageType.Double && TryDimension(p, def, out var dim))
                {
                    if (!data.Dimensions.ContainsKey(label)) data.Dimensions[label] = dim!;
                    continue;
                }

                // Solo las instancias alimentan los property sets (evita duplicar
                // los del tipo, ya cubiertos por "Tipo" en Identidad).
                if (!instanceLevel) continue;

                var value = ReadValue(p, def);
                if (value == null) continue;

                var group = GroupLabel(def);
                if (!data.Psets.TryGetValue(group, out var bucket))
                {
                    bucket = new Dictionary<string, object>();
                    data.Psets[group] = bucket;
                }
                bucket[label] = value;
            }
        }

        private static bool TryDimension(Parameter p, Definition def, out Dimension? dim)
        {
            dim = null;
            ForgeTypeId spec;
            try { spec = def.GetDataType(); }
            catch { return false; }

            ForgeTypeId? unit = null;
            string? label = null;
            if (spec == SpecTypeId.Length) { unit = UnitTypeId.Millimeters; label = "mm"; }
            else if (spec == SpecTypeId.Area) { unit = UnitTypeId.SquareMeters; label = "m²"; }
            else if (spec == SpecTypeId.Volume) { unit = UnitTypeId.CubicMeters; label = "m³"; }
            if (unit == null) return false;

            var converted = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), unit);
            dim = new Dimension { Value = System.Math.Round(converted, 2), Unit = label };
            return true;
        }

        private static object? ReadValue(Parameter p, Definition def)
        {
            // Booleanos (Sí/No) como bool para que el visor muestre "Sí"/"No".
            try
            {
                if (p.StorageType == StorageType.Integer && def.GetDataType() == SpecTypeId.Boolean.YesNo)
                    return p.AsInteger() != 0;
            }
            catch { /* GetDataType puede no aplicar */ }

            switch (p.StorageType)
            {
                case StorageType.String:
                    var s = p.AsString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                case StorageType.Integer:
                case StorageType.Double:
                case StorageType.ElementId:
                    // AsValueString da el texto formateado con unidades de Revit.
                    var vs = SafeValueString(p);
                    return string.IsNullOrWhiteSpace(vs) ? null : vs;
                default:
                    return null;
            }
        }

        private static string? SafeValueString(Parameter p)
        {
            try { return p.AsValueString(); }
            catch { return null; }
        }

        private static string GroupLabel(Definition def)
        {
            try
            {
                var group = def.GetGroupTypeId();
                if (group != null && !group.Empty())
                {
                    var label = LabelUtils.GetLabelForGroup(group);
                    if (!string.IsNullOrWhiteSpace(label)) return label;
                }
            }
            catch { /* definiciones internas sin grupo */ }
            return "Otros";
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------
        private static Element? TypeOf(Element element, Document document)
        {
            var typeId = element.GetTypeId();
            return typeId != null && typeId != ElementId.InvalidElementId
                ? document.GetElement(typeId)
                : null;
        }

        private static string ReadableName(Element element)
        {
            var name = element.Name;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            var category = element.Category != null ? element.Category.Name : "Elemento";
            return category + " " + element.Id.ToString();
        }
    }
}
