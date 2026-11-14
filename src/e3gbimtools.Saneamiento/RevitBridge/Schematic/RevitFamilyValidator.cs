// ============================================================================
//  RevitFamilyValidator.cs
//  Implementación de IFamilyValidator que usa la API de Revit para abrir el
//  .rfa como FamilyDocument en modo solo lectura, leer la categoría y cerrarlo.
//
//  Coste: abrir un FamilyDocument es relativamente caro (puede tardar
//  100–500 ms por archivo grande).  Se cachean los resultados por path para
//  no repetir la inspección si el usuario re-selecciona el mismo archivo.
//
//  Categorías aceptadas:
//    - OST_DetailComponents  → familias "Detail Item"
//    - OST_GenericAnnotation → familias "Generic Annotation"
//  Cualquier otra (Plumbing Fixtures, Pipe Fittings, etc.) se rechaza.
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.Saneamiento.UI.Schematic;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public sealed class RevitFamilyValidator : IFamilyValidator
    {
        private readonly Application _app;
        private readonly ConcurrentDictionary<string, FamilyValidationResult> _cache
            = new ConcurrentDictionary<string, FamilyValidationResult>(StringComparer.OrdinalIgnoreCase);

        public RevitFamilyValidator(Application app)
        {
            _app = app;
        }

        public FamilyValidationResult Validate(string rfaPath)
        {
            if (string.IsNullOrWhiteSpace(rfaPath) || !File.Exists(rfaPath))
                return FamilyValidationResult.Unreadable(rfaPath ?? "(null)", "archivo no existe");

            if (_cache.TryGetValue(rfaPath, out var cached)) return cached;

            // Sin Application no podemos abrir → diferimos.
            if (_app == null)
            {
                var deferred = FamilyValidationResult.Deferred();
                _cache[rfaPath] = deferred;
                return deferred;
            }

            FamilyValidationResult result;
            Document famDoc = null;
            try
            {
                famDoc = _app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    result = FamilyValidationResult.Unreadable(
                        Path.GetFileName(rfaPath), "no es un archivo de familia");
                }
                else
                {
                    var fam = famDoc.OwnerFamily;
                    var cat = fam?.FamilyCategory;
                    string catName = cat?.Name ?? "(sin categoría)";
                    var bic = cat != null ? (BuiltInCategory)cat.Id.Value : BuiltInCategory.INVALID;

                    bool ok = bic == BuiltInCategory.OST_DetailComponents
                           || bic == BuiltInCategory.OST_GenericAnnotation;

                    string famName = Path.GetFileNameWithoutExtension(rfaPath);
                    result = ok
                        ? FamilyValidationResult.Ok(famName, catName)
                        : FamilyValidationResult.Wrong(famName, catName);
                }
            }
            catch (Exception ex)
            {
                result = FamilyValidationResult.Unreadable(
                    Path.GetFileName(rfaPath), ex.Message);
            }
            finally
            {
                // Cerrar sin guardar — la inspección no debe ensuciar nada.
                try { famDoc?.Close(false); } catch { }
            }

            _cache[rfaPath] = result;
            return result;
        }

        /// <summary>
        /// Limpia la caché — útil si el usuario sustituye un .rfa en disco
        /// y quiere re-validar sin reiniciar la sesión.
        /// </summary>
        public void Invalidate() => _cache.Clear();
    }
}
