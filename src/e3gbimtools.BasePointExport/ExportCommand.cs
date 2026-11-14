// ============================================================================
//  ExportCommand.cs
//  BasePointView — Exportador Revit (Sprint 4).
//
//  Comando externo que exporta la vista 3D activa a GLB (geometria) + JSON
//  (propiedades por UniqueId) para el visor web BasePointView.  Sin dependencia
//  de Autodesk APS/Forge: todo el proceso es local con la API de Revit.
//
//  La exportacion es de solo lectura: NO abre Transaction.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace E3G.BimTools.BasePointExport
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportCommand : IExternalCommand
    {
        private const string Title = "BasePointView — Exportar a GLB";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var document = uidoc?.Document;
                if (document == null)
                {
                    message = "No hay documento activo.";
                    return Result.Failed;
                }

                var view3d = GlbExportRunner.ResolveView3D(uidoc!);
                if (view3d == null)
                {
                    TaskDialog.Show(Title,
                        "Abre o crea una vista 3D antes de exportar.\n\n" +
                        "El exportador serializa la vista 3D activa.");
                    return Result.Cancelled;
                }

                var glbPath = AskOutputPath(document);
                if (glbPath == null) return Result.Cancelled;
                var jsonPath = Path.ChangeExtension(glbPath, ".json");

                var result = GlbExportRunner.Export(document, view3d, glbPath, jsonPath);

                TaskDialog.Show(Title,
                    "Exportación completada.\n\n" +
                    "Elementos: " + result.Elements + "\n" +
                    "Mallas únicas: " + result.Meshes + " (instanciación)\n" +
                    "Triángulos: " + result.Triangles + "\n\n" +
                    "GLB:  " + glbPath + "\n" +
                    "JSON: " + jsonPath + "\n\n" +
                    "Arrastra ambos archivos al visor BasePointView.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(Title, "Excepción durante la exportación:\n\n" + ex);
                return Result.Failed;
            }
        }

        /// <summary>Pide al usuario la ruta del GLB (el JSON va junto, mismo nombre).</summary>
        private static string? AskOutputPath(Document document)
        {
            var suggested = string.IsNullOrWhiteSpace(document.Title) ? "modelo" : document.Title;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Title,
                Filter = "glTF binario (*.glb)|*.glb",
                FileName = suggested + ".glb",
                DefaultExt = ".glb",
                AddExtension = true,
                OverwritePrompt = true,
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
