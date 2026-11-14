using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.ScheduleExcel;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// Tablas de planificacion ↔ Excel.
///   • Exportar «como en Revit»: titulo, encabezados, agrupaciones y colores (consulta).
///   • Exportar «editable»: una fila por elemento con su ElementId, para rellenar
///     parametros en Excel y volver a importarlos.
/// Varias tablas van a un mismo libro, una pestaña por tabla. La hoja oculta
/// "_Meta" identifica cada pestaña (tabla, documento, formato).
/// Todo se llama desde el hilo de Revit (comando o ventana modal).
/// </summary>
public interface IScheduleExcelService
{
    /// <summary>Tablas de planificacion del documento (sin plantillas ni de revisiones), por nombre.</summary>
    IReadOnlyList<ScheduleInfo> GetSchedules(Document doc);

    /// <summary>Lee la tabla tal y como se ve en Revit.</summary>
    ScheduleSnapshot Read(ViewSchedule schedule);

    /// <summary>Exporta una tabla «como en Revit».</summary>
    ScheduleExcelExportResult Export(ViewSchedule schedule, string filePath);

    /// <summary>Exporta varias tablas a un mismo libro, una pestaña por tabla.</summary>
    /// <param name="progress">Fraccion 0..1 y texto de la fase (para una barra de progreso).</param>
    ScheduleExcelExportResult ExportMany(Document doc, IReadOnlyList<ViewSchedule> schedules, string filePath, bool editable,
        Action<double, string>? progress = null);

    /// <summary>Vista previa de una tabla tal y como saldra en el Excel (sin calcular el mapa de filas).</summary>
    SheetGrid BuildPreview(ViewSchedule schedule, bool editable, int maxRows);

    /// <summary>Vista previa de las pestañas de un Excel tal y como estan guardadas.</summary>
    IReadOnlyList<SheetGrid> ReadWorkbookGrids(string filePath, int maxRows);

    /// <summary>Lee un Excel exportado y lo compara con el modelo (no modifica nada).</summary>
    ScheduleImportPreview PreviewImport(Document doc, string filePath, Action<double, string>? progress = null);

    /// <summary>Aplica los cambios marcados en una unica transaccion (un Ctrl+Z).</summary>
    ScheduleImportResult ApplyImport(Document doc, IReadOnlyList<ScheduleCellChange> changes, Action<double, string>? progress = null);
}
