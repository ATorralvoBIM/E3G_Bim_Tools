// ============================================================================
//  ClasificacionSession.cs
//  Caché estática en memoria de las asignaciones PipingSystemType → CategoriaSistema.
//
//  Por qué existe (mismo motivo que PartidasSession):
//    - El DataGrid de Ajustes › Classificació de sistemes deja al usuario
//      cambiar el dropdown de cada fila.
//    - Esos cambios viven en la propiedad FilaClasificacion.CategoriaActual,
//      pero NO se persisten a ExtensibleStorage del .rvt — Transaction.Start()
//      desde una Page modeless no funciona.
//    - El EvaluadorReglas necesita esas asignaciones para categorizar tubos.
//
//  Solución: el code-behind de AjustesWindow escribe a esta caché cuando el
//  usuario edita una fila, y el motor la lee primero antes de tocar el .rvt.
// ============================================================================

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace E3G.Saneamiento.Presupuesto.Storage
{
    public static class ClasificacionSession
    {
        /// <summary>
        /// Mapeo PipingSystemType.Id → CategoriaSistema, escrito desde el
        /// DataGrid de Ajustes en cada cambio. Null hasta que se inicialice.
        /// </summary>
        public static Dictionary<ElementId, E3G.Saneamiento.CategoriaSistema> AsignacionesActuales { get; set; }
    }
}
