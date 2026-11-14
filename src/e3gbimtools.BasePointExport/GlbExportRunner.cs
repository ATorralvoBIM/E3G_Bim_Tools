// ============================================================================
//  GlbExportRunner.cs
//  BasePointView — Exportador Revit (Sprint 4/9).
//
//  Lógica compartida de exportación (resolver vista 3D + ejecutar el
//  CustomExporter) reutilizada por el comando de exportación a archivo y por el
//  de publicación al visor.
// ============================================================================

using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    /// <summary>Ejecuta la exportación a GLB + JSON sobre una vista 3D.</summary>
    public static class GlbExportRunner
    {
        /// <summary>Vista 3D activa o, en su defecto, la primera no-plantilla.</summary>
        public static View3D? ResolveView3D(UIDocument uidoc)
        {
            if (uidoc.ActiveView is View3D active && !active.IsTemplate)
                return active;

            return new FilteredElementCollector(uidoc.Document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);
        }

        /// <summary>Resultado de una exportación: recuentos para el resumen.</summary>
        public readonly struct ExportResult
        {
            public int Elements { get; init; }
            public int Triangles { get; init; }
            public int Meshes { get; init; }
        }

        /// <summary>Exporta la vista a GLB + JSON. Devuelve los recuentos.</summary>
        public static ExportResult Export(
            Document document, View3D view, string glbPath, string jsonPath)
        {
            var context = new GltfExportContext(document, glbPath, jsonPath);
            var exporter = new CustomExporter(document, context)
            {
                ShouldStopOnError = false,
                IncludeGeometricObjects = false, // solo teselación (OnPolymesh)
            };
            exporter.Export(view);
            return new ExportResult
            {
                Elements = context.ElementCount,
                Triangles = context.TriangleCount,
                Meshes = context.MeshCount,
            };
        }
    }
}
