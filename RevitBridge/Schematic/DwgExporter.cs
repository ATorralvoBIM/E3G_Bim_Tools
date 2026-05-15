// ============================================================================
//  DwgExporter.cs
//  Exporta una View concreta a DWG usando los DWGExportOptions estándar de
//  Revit.  No abre diálogo — recibe la ruta destino y guarda directamente.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public static class DwgExporter
    {
        public sealed class Result
        {
            public bool Success { get; set; }
            public string Path { get; set; }
            public string Error { get; set; }
        }

        public static Result Export(Document doc, ElementId viewId,
            string folderPath, string fileNameNoExt)
        {
            if (doc == null) return Fail("Document nulo");
            if (viewId == ElementId.InvalidElementId) return Fail("ViewId inválido");
            if (string.IsNullOrWhiteSpace(folderPath)) return Fail("Carpeta vacía");

            try
            {
                Directory.CreateDirectory(folderPath);

                var opts = new DWGExportOptions
                {
                    MergedViews = true,
                    SharedCoords = false,
                };

                var views = new List<ElementId> { viewId };
                bool ok = doc.Export(folderPath, fileNameNoExt, views, opts);
                string full = Path.Combine(folderPath, fileNameNoExt + ".dwg");
                if (!ok) return Fail("Document.Export devolvió false");
                if (!File.Exists(full)) return Fail($"Archivo no creado: {full}");

                return new Result { Success = true, Path = full };
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static Result Fail(string msg) => new Result { Success = false, Error = msg };
    }
}
