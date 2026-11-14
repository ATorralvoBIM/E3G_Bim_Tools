// ============================================================================
//  GeometryInstancing.cs
//  BasePointView — Exportador Revit (Sprint 10).
//
//  Instanciación de geometría repetida: en lugar de duplicar las mallas de
//  familias/instancias idénticas (muy común en MEP: tramos de tubería, fittings,
//  aparatos), se almacena la geometría UNA sola vez y cada instancia se referencia
//  con su traslación. Reduce drásticamente el tamaño del GLB y prepara la
//  instanciación GPU (EXT_mesh_gpu_instancing) que aplica el backend.
//
//  Se deduplica por firma invariante a traslación (geometría relativa a su
//  centroide), por lo que las copias trasladadas comparten malla sin tocar el
//  sistema de coordenadas ya validado del exportador. Las instancias rotadas no
//  se fusionan (caso menos frecuente); aun así el backend las comprime con Draco.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using GltfMeshBuilder = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal>;

namespace E3G.BimTools.BasePointExport
{
    /// <summary>Una primitiva (triángulos) de un elemento, con su material.</summary>
    public sealed class PrimitiveGeometry
    {
        public MaterialBuilder Material { get; }
        public int MaterialKey { get; }
        /// <summary>Lista plana de vértices (3 por triángulo), en coords glTF.</summary>
        public List<VertexPositionNormal> Vertices { get; } = new();

        public PrimitiveGeometry(MaterialBuilder material, int materialKey)
        {
            Material = material;
            MaterialKey = materialKey;
        }
    }

    /// <summary>Geometría de un elemento (sus primitivas por material).</summary>
    public sealed class ElementGeometry
    {
        public string UniqueId { get; }
        public string Category { get; }
        private readonly Dictionary<int, PrimitiveGeometry> _primitives = new();

        public ElementGeometry(string uniqueId, string category)
        {
            UniqueId = uniqueId;
            Category = category;
        }

        public IReadOnlyCollection<PrimitiveGeometry> Primitives => _primitives.Values;

        public bool HasGeometry => _primitives.Values.Any(p => p.Vertices.Count > 0);

        public PrimitiveGeometry Primitive(int materialKey, MaterialBuilder material)
        {
            if (!_primitives.TryGetValue(materialKey, out var prim))
            {
                prim = new PrimitiveGeometry(material, materialKey);
                _primitives[materialKey] = prim;
            }
            return prim;
        }
    }

    /// <summary>
    /// Construye la escena glTF deduplicando geometría repetida por traslación.
    /// </summary>
    public static class GeometryInstancing
    {
        /// <summary>
        /// Crea los nodos de la escena reutilizando una malla por cada geometría
        /// distinta. Devuelve cuántas mallas únicas se generaron (vs. elementos).
        /// </summary>
        public static int Build(ModelRoot model, Scene scene, IEnumerable<ElementGeometry> elements)
        {
            var meshBySignature = new Dictionary<string, Mesh>();

            foreach (var element in elements)
            {
                if (!element.HasGeometry) continue;

                var centroid = Centroid(element);
                var signature = Signature(element, centroid);

                if (!meshBySignature.TryGetValue(signature, out var mesh))
                {
                    mesh = BuildMesh(model, element, centroid);
                    meshBySignature[signature] = mesh;
                }

                var node = scene.CreateNode(element.UniqueId);
                node.Mesh = mesh;
                node.WithLocalTranslation(centroid);
                node.Extras = new System.Text.Json.Nodes.JsonObject
                {
                    ["uniqueId"] = element.UniqueId,
                    ["category"] = element.Category,
                };
            }

            return meshBySignature.Count;
        }

        /// <summary>Centroide (media de posiciones) de toda la geometría del elemento.</summary>
        private static Vector3 Centroid(ElementGeometry element)
        {
            var sum = Vector3.Zero;
            var count = 0;
            foreach (var prim in element.Primitives)
            {
                foreach (var v in prim.Vertices)
                {
                    sum += v.Position;
                    count++;
                }
            }
            return count > 0 ? sum / count : Vector3.Zero;
        }

        /// <summary>Construye una malla con la geometría relativa al centroide.</summary>
        private static Mesh BuildMesh(ModelRoot model, ElementGeometry element, Vector3 centroid)
        {
            var builder = new GltfMeshBuilder(element.UniqueId);
            foreach (var prim in element.Primitives)
            {
                if (prim.Vertices.Count < 3) continue;
                var primitive = builder.UsePrimitive(prim.Material);
                for (var i = 0; i + 2 < prim.Vertices.Count; i += 3)
                {
                    primitive.AddTriangle(
                        Local(prim.Vertices[i], centroid),
                        Local(prim.Vertices[i + 1], centroid),
                        Local(prim.Vertices[i + 2], centroid));
                }
            }
            return model.CreateMeshes(builder)[0];
        }

        private static VertexPositionNormal Local(VertexPositionNormal v, Vector3 centroid)
            => new(v.Position - centroid, v.Normal);

        /// <summary>
        /// Firma invariante a traslación: hash de las primitivas (material +
        /// posiciones relativas al centroide + normales), cuantizadas para tolerar
        /// el ruido de coma flotante.
        /// </summary>
        private static string Signature(ElementGeometry element, Vector3 centroid)
        {
            using var sha = SHA256.Create();
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                foreach (var prim in element.Primitives.OrderBy(p => p.MaterialKey))
                {
                    writer.Write(prim.MaterialKey);
                    writer.Write(prim.Vertices.Count);
                    foreach (var v in prim.Vertices)
                    {
                        WriteQuantized(writer, v.Position - centroid);
                        WriteQuantized(writer, v.Normal);
                    }
                }
            }
            return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
        }

        private static void WriteQuantized(System.IO.BinaryWriter writer, Vector3 v)
        {
            writer.Write((int)Math.Round(v.X * 1e4f));
            writer.Write((int)Math.Round(v.Y * 1e4f));
            writer.Write((int)Math.Round(v.Z * 1e4f));
        }
    }
}
