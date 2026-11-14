// ============================================================================
//  GltfExportContext.cs
//  BasePointView — Exportador Revit (Sprint 4).
//
//  IExportContext sobre CustomExporter (patron base Tammik/McCulloch): Revit
//  ya tesela la geometria del modelo y la entrega por callbacks; aqui se
//  captura, se acumula por elemento y se serializa a GLB.  En paralelo se
//  extraen las propiedades a un JSON indexado por UniqueId.
//
//  Convenciones (documento de desarrollo §4.2):
//   - variable `document` en minuscula,
//   - geometria interna en pies → metros (×0,3048),
//   - sin Transaction (la exportacion es de solo lectura).
//   - eje Z-up de Revit → Y-up de glTF para abrir bien en visores estandar.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;

namespace E3G.BimTools.BasePointExport
{
    using GltfMeshBuilder = MeshBuilder<VertexPositionNormal>;

    /// <summary>
    /// Contexto de exportacion que convierte el modelo de Revit en GLB + JSON.
    /// </summary>
    public sealed class GltfExportContext : IExportContext
    {
        private const double FeetToMeters = 0.3048;

        private readonly Document _document;
        private readonly string _glbPath;
        private readonly string _jsonPath;

        private readonly Stack<Transform> _transforms = new();
        private readonly Dictionary<string, ElementGeometry> _elements = new();
        private readonly Dictionary<string, RevitElementData> _properties = new();
        private readonly Dictionary<int, MaterialBuilder> _materials = new();
        private readonly MaterialBuilder _defaultMaterial =
            new MaterialBuilder("default")
                .WithDoubleSide(true)
                .WithMetallicRoughnessShader()
                .WithBaseColor(new Vector4(0.78f, 0.78f, 0.80f, 1f));

        private string? _currentUniqueId;
        private MaterialBuilder _currentMaterial;
        private int _currentMaterialKey = -1;

        /// <summary>Número de mallas únicas generadas (tras deduplicar instancias).</summary>
        public int MeshCount { get; private set; }

        public int ElementCount => _elements.Count;
        public int TriangleCount { get; private set; }

        public GltfExportContext(Document document, string glbPath, string jsonPath)
        {
            _document = document;
            _glbPath = glbPath;
            _jsonPath = jsonPath;
            _currentMaterial = _defaultMaterial;
            _transforms.Push(Transform.Identity);
        }

        // ------------------------------------------------------------------
        //  Ciclo de vida
        // ------------------------------------------------------------------
        public bool Start() => true;

        public bool IsCanceled() => false;

        public void Finish()
        {
            var model = ModelRoot.CreateModel();
            var scene = model.UseScene("Revit");

            // Deduplicación de geometría repetida (instanciación por traslación).
            MeshCount = GeometryInstancing.Build(model, scene, _elements.Values);

            scene.Extras = BuildCoordinatesMetadata();
            model.SaveGLB(_glbPath);

            File.WriteAllText(_jsonPath, PropertyExtractor.ToJson(_properties), System.Text.Encoding.UTF8);
        }

        // ------------------------------------------------------------------
        //  Vista / elementos
        // ------------------------------------------------------------------
        public RenderNodeAction OnViewBegin(ViewNode node) => RenderNodeAction.Proceed;

        public void OnViewEnd(ElementId elementId) { }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            var element = _document.GetElement(elementId);
            if (element == null)
            {
                _currentUniqueId = null;
                return RenderNodeAction.Skip;
            }
            _currentUniqueId = element.UniqueId;
            return RenderNodeAction.Proceed;
        }

        public void OnElementEnd(ElementId elementId) => _currentUniqueId = null;

        // ------------------------------------------------------------------
        //  Transformaciones de instancia y de vinculo
        // ------------------------------------------------------------------
        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node) => _transforms.Pop();

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            _transforms.Push(_transforms.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node) => _transforms.Pop();

        public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;

        public void OnFaceEnd(FaceNode node) { }

        // ------------------------------------------------------------------
        //  Material
        // ------------------------------------------------------------------
        public void OnMaterial(MaterialNode node)
        {
            var color = node.Color;
            var alpha = (float)Math.Max(0d, Math.Min(1d, 1d - node.Transparency));
            int key;
            if (color != null && color.IsValid)
                key = (color.Red << 24) | (color.Green << 16) | (color.Blue << 8) | (int)(alpha * 255);
            else
                key = -1;

            _currentMaterialKey = key;

            if (_materials.TryGetValue(key, out var cached))
            {
                _currentMaterial = cached;
                return;
            }

            if (key == -1)
            {
                _currentMaterial = _defaultMaterial;
                _materials[key] = _defaultMaterial;
                return;
            }

            var baseColor = new Vector4(color!.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);
            var material = new MaterialBuilder("mat" + key.ToString("X"))
                .WithDoubleSide(true)
                .WithMetallicRoughnessShader()
                .WithBaseColor(baseColor);
            if (alpha < 1f) material.WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND);

            _materials[key] = material;
            _currentMaterial = material;
        }

        // ------------------------------------------------------------------
        //  Geometria
        // ------------------------------------------------------------------
        public void OnPolymesh(PolymeshTopology node)
        {
            if (_currentUniqueId == null) return;

            var element = GetOrCreateElement(_currentUniqueId);
            if (element == null) return;

            var transform = _transforms.Peek();
            var points = node.GetPoints();
            var primitive = element.Primitive(_currentMaterialKey, _currentMaterial);

            foreach (var facet in node.GetFacets())
            {
                var a = ToGltf(transform.OfPoint(points[facet.V1]));
                var b = ToGltf(transform.OfPoint(points[facet.V2]));
                var c = ToGltf(transform.OfPoint(points[facet.V3]));

                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < 1e-12f) continue; // triangulo degenerado
                normal = Vector3.Normalize(normal);

                primitive.Vertices.Add(new VertexPositionNormal(a, normal));
                primitive.Vertices.Add(new VertexPositionNormal(b, normal));
                primitive.Vertices.Add(new VertexPositionNormal(c, normal));
                TriangleCount++;
            }
        }

        public void OnRPC(RPCNode node) { }

        public void OnLight(LightNode node) { }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------
        private ElementGeometry? GetOrCreateElement(string uniqueId)
        {
            if (_elements.TryGetValue(uniqueId, out var existing)) return existing;

            var element = _document.GetElement(uniqueId);
            if (element == null) return null;

            var category = element.Category != null ? element.Category.Name : "Sin categoría";
            var geometry = new ElementGeometry(uniqueId, category);
            _elements[uniqueId] = geometry;
            _properties[uniqueId] = PropertyExtractor.Extract(element, _document);
            return geometry;
        }

        /// <summary>Pie (Revit, Z-up) → metro (glTF, Y-up): (x, z, −y).</summary>
        private static Vector3 ToGltf(XYZ p) => new(
            (float)(p.X * FeetToMeters),
            (float)(p.Z * FeetToMeters),
            (float)(-p.Y * FeetToMeters));

        /// <summary>
        /// Metadatos de coordenadas compartidas (Survey / Project Base Point) en
        /// metros, para la federacion del Sprint 6.  Van en scene.extras del GLB.
        /// </summary>
        private JsonObject BuildCoordinatesMetadata()
        {
            var meta = new JsonObject { ["units"] = "m" };
            try
            {
                var projectBase = BasePoint.GetProjectBasePoint(_document);
                if (projectBase != null)
                    meta["projectBasePoint"] = ToArray(projectBase.Position);

                var survey = BasePoint.GetSurveyPoint(_document);
                if (survey != null)
                    meta["surveyPoint"] = ToArray(survey.Position);
            }
            catch
            {
                // Modelos sin puntos base definidos: se omite el metadato.
            }
            return meta;
        }

        private static JsonArray ToArray(XYZ p) => new(
            (float)(p.X * FeetToMeters),
            (float)(p.Z * FeetToMeters),
            (float)(-p.Y * FeetToMeters));
    }
}
