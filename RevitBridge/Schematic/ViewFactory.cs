// ============================================================================
//  ViewFactory.cs
//  Crea ViewDrafting nuevas con escala configurable y un nombre único basado
//  en sistema + timestamp.  La vista resultante es 2D, sin sketch plane (en
//  ViewDrafting el plano se gestiona implícito) y lista para recibir
//  DetailCurve/TextNote/FilledRegion.
// ============================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.Saneamiento.Core.Schematic;

namespace E3G.Saneamiento.RevitBridge.Schematic
{
    public static class ViewFactory
    {
        /// <summary>
        /// Crea una ViewDrafting nueva.  El llamador debe abrir/cerrar la
        /// Transaction; esta función SOLO crea el elemento.
        /// </summary>
        public static ViewDrafting Create(Document doc, string viewName, SchematicScale scale)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // 1) Encontrar un ViewFamilyType de tipo Drafting.
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (vft == null)
                throw new InvalidOperationException(
                    "El documento no tiene ningún ViewFamilyType de Drafting disponible.");

            // 2) Crear la vista
            var view = ViewDrafting.Create(doc, vft.Id);
            view.Scale = (int)scale;

            // 3) Asignar el nombre — si ya existe uno igual, añadir sufijo.
            string baseName = SanitizeName(viewName);
            string finalName = baseName;
            int suffix = 2;
            while (NameExists(doc, finalName))
            {
                finalName = $"{baseName} ({suffix})";
                suffix++;
            }
            try { view.Name = finalName; }
            catch (ArgumentException) { /* Revit pone nombre por defecto si falla */ }

            return view;
        }

        public static string BuildDefaultName(string systemName)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            return $"Esquema · {SanitizeName(systemName)} · {stamp}";
        }

        private static bool NameExists(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<View>()
                .Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Esquema";
            // Caracteres prohibidos en nombres de vista de Revit
            char[] bad = { ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '\\', '/' };
            foreach (var c in bad) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
