using System.Collections.Generic;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.DialuxImport;

namespace E3G.BimTools.DiTools.Core.Abstractions;

/// <summary>
/// "DIALux → Revit": lee el DWG de luminarias exportado de DIALux, asocia cada
/// bloque a un tipo de familia de luminaria del modelo y coloca, rehace o
/// audita las luminarias.  Sustituye a la rutina Dynamo DIALux_a_Revit.dyn
/// (modos PREVIA / SIMULAR / CREAR / REHACER / AUDITAR).
/// </summary>
public interface IDialuxImportService
{
    /// <summary>Lee el DWG/DXF.  No necesita contexto de Revit.</summary>
    DialuxDrawing Read(string filePath);

    /// <summary>Tipos de familia colocables (luminarias y afines; todas las categorias de modelo si <paramref name="allCategories"/>).</summary>
    IReadOnlyList<DialuxFamilyType> GetFamilyTypes(Document doc, bool allCategories);

    IReadOnlyList<DialuxLevel> GetLevels(Document doc);

    /// <summary>
    /// Propone un tipo para los bloques sin mapeo cuyo nombre (o atributos)
    /// coincide con el nombre de una familia/tipo.  Devuelve cuantos ha asignado.
    /// </summary>
    int SuggestMappings(DialuxDrawing drawing, DialuxConfig config, IReadOnlyList<DialuxFamilyType> types);

    /// <summary>
    /// Calcula el desfase y giro que llevan el DWG sobre la arquitectura del modelo
    /// casando los pilares del DWG con los pilares de Revit (modelo y vinculos) del
    /// nivel de la config.  No modifica nada: devuelve los valores para la config.
    /// </summary>
    DialuxAlignment AutoAlign(Document doc, DialuxDrawing drawing, DialuxConfig config);

    /// <summary>SIMULAR / AUDITAR: compara el DWG con lo ya colocado sin tocar el modelo.</summary>
    DialuxPlan BuildPlan(Document doc, DialuxDrawing drawing, DialuxConfig config);

    /// <summary>CREAR / REHACER / SINCRONIZAR.  Hilo de Revit; se deshace con un solo Ctrl+Z.</summary>
    DialuxResult Apply(Document doc, DialuxDrawing drawing, DialuxConfig config, DialuxAction action);
}
