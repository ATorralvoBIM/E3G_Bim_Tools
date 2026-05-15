// ============================================================================
//  IFamilyValidator.cs
//  Abstracción mínima que el ViewModel usa para validar un .rfa antes de
//  asignarlo a una fila de Mapping.  Mantiene el VM sin dependencias de la
//  API de Revit — el adapter concreto (RevitFamilyValidator) vive en
//  RevitBridge.Schematic.
//
//  Devuelve un resultado simple: válido / no válido + motivo legible.
//  Si la implementación no puede validar (no hay sesión Revit), debe
//  devolver Unknown — el VM dejará pasar el archivo y la validación real
//  ocurrirá al generar.
// ============================================================================
namespace E3G.Saneamiento.UI.Schematic
{
    public enum FamilyValidationKind
    {
        /// <summary>El .rfa es Detail Item o Generic Annotation — apto para una Drafting View.</summary>
        Ok,
        /// <summary>La familia existe pero su categoría no se puede insertar en una Drafting View.</summary>
        WrongCategory,
        /// <summary>El archivo no se pudo leer (corrupto, ruta inválida, no es un .rfa de Revit).</summary>
        CannotRead,
        /// <summary>No hay sesión Revit disponible — validación diferida al momento de generar.</summary>
        Unknown,
    }

    public sealed class FamilyValidationResult
    {
        public FamilyValidationKind Kind { get; }
        /// <summary>Categoría detectada (texto legible, ej. "Plumbing Fixtures") — null si no se pudo leer.</summary>
        public string DetectedCategory { get; }
        /// <summary>Nombre interno de la familia (sin extensión).</summary>
        public string FamilyName { get; }
        /// <summary>Mensaje listo para mostrar al usuario.</summary>
        public string Message { get; }

        public bool IsAcceptable => Kind == FamilyValidationKind.Ok
                                 || Kind == FamilyValidationKind.Unknown;

        public FamilyValidationResult(FamilyValidationKind kind, string familyName,
            string detectedCategory, string message)
        {
            Kind = kind;
            FamilyName = familyName;
            DetectedCategory = detectedCategory;
            Message = message;
        }

        public static FamilyValidationResult Ok(string familyName, string detectedCategory)
            => new FamilyValidationResult(FamilyValidationKind.Ok, familyName, detectedCategory,
                "OK · " + detectedCategory);

        public static FamilyValidationResult Wrong(string familyName, string detectedCategory)
            => new FamilyValidationResult(FamilyValidationKind.WrongCategory, familyName,
                detectedCategory,
                $"No es un Detail Item / Generic Annotation (categoría: {detectedCategory}). " +
                "No se puede insertar en una vista de dibujo.");

        public static FamilyValidationResult Unreadable(string filename, string err)
            => new FamilyValidationResult(FamilyValidationKind.CannotRead, filename, null,
                "No se pudo leer el archivo: " + err);

        public static FamilyValidationResult Deferred()
            => new FamilyValidationResult(FamilyValidationKind.Unknown, null, null,
                "Validación diferida (no hay sesión Revit activa).");
    }

    /// <summary>
    /// Servicio que valida un .rfa para uso en esquema MEP (Drafting View).
    /// El VM lo recibe por constructor para poder inyectar un validator real
    /// (sesión Revit) o nulo (preview sin Revit — validación diferida).
    /// </summary>
    public interface IFamilyValidator
    {
        FamilyValidationResult Validate(string rfaPath);
    }
}
