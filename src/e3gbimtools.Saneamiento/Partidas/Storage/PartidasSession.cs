// ============================================================================
//  PartidasSession.cs
//  Caché estática en memoria de la plantilla actual del módulo Presupuesto.
//
//  Por qué existe:
//    - PartidasViewModel vive en AjustesWindow (modeless Page dentro del DockablePane).
//    - ExportarBc3Command corre desde SaneamientoWindow (otra Page).
//    - No comparten instancias.
//    - Persistir al .rvt vía Transaction NO funciona desde una Page modeless
//      (Revit lanza "Starting a transaction from an external application running
//      outside of API context is not allowed").
//
//  Solución pragmática: una caché estática que el ViewModel actualiza en cada
//  modificación, y que el comando lee directamente. Cero Transactions, cero
//  fricción. La persistencia al .rvt será una mejora posterior con
//  ExternalEvent.
//
//  Lifetime: durante toda la sesión de Revit. Se reinicia al cerrar Revit.
// ============================================================================

using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Storage
{
    public static class PartidasSession
    {
        /// <summary>
        /// Plantilla activa en memoria. La escribe PartidasViewModel después de
        /// cada modificación. La lee ExportarBc3Command. Null hasta que se
        /// inicializa el primer ViewModel.
        /// </summary>
        public static Plantilla PlantillaActual { get; set; }
    }
}
