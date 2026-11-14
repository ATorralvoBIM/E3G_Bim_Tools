using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.TableGen;

namespace E3G.BimTools.DiTools.Core.Services.TableGen;

/// <summary>
/// Genera una Drafting View en Revit a partir de un <see cref="ExcelSheetData"/>
/// (Sprint 8 — TableGen).
///
/// Elementos creados por celda:
///   • FilledRegion  — fondo de color (si HasBackground).
///   • DetailLine    — bordes de celda (Top, Bottom, Left, Right) segun estilo.
///   • TextNote      — texto, si la celda no esta vacia.
///
/// Sistema de coordenadas:
///   • Origen (0,0) en la esquina superior-izquierda de la tabla.
///   • X crece hacia la derecha.
///   • Y crece hacia abajo (en Revit Y crece hacia arriba, por lo que
///     multiplicamos la Y calculada por -1 al crear los elementos).
/// </summary>
internal sealed class DraftingViewGenerator
{
    // ── Constantes ───────────────────────────────────────────────────────
    private const double DefaultColWidthMm  = 25.4;
    private const double DefaultRowHeightMm = 5.0;

    // ────────────────────────────────────────────────────────────────────

    public TableGenResult Generate(
        Document        doc,
        ExcelSheetData  data,
        TableGenRequest request,
        IList<string>   warnings)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. ViewFamilyType de tipo Drafting.
            var draftingVft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting)
                ?? throw new InvalidOperationException(
                    "El documento no contiene ningun ViewFamilyType de tipo Drafting.");

            // 2. Mapeo de fuentes.
            var fontMapper = new FontMappingConfig(doc, warnings);

            // 3. Patron de relleno solido para FilledRegions.
            var solidPatternId = GetSolidFillPatternId(doc);

            // 4. Nombre de la vista.
            var viewName = !string.IsNullOrWhiteSpace(request.ViewName)
                ? request.ViewName
                : $"TableGen_{Path.GetFileNameWithoutExtension(request.FilePath)}_{data.SheetName}";
            viewName = EnsureUniqueViewName(doc, viewName);

            ElementId viewId;

            using (var tx = new Transaction(doc, "TableGen — Crear Drafting View"))
            {
                tx.Start();

                // 4a. Crear la vista.
                var view = ViewDrafting.Create(doc, draftingVft.Id);
                view.Name = viewName;
                viewId = view.Id;

                // 4b. Preparar FilledRegionTypes por color.
                var fillTypeCache = solidPatternId != ElementId.InvalidElementId
                    ? PrepareFilledRegionTypes(doc, data, solidPatternId, warnings)
                    : new Dictionary<uint, ElementId>();

                // 4c. Crear elementos por celda.
                foreach (var cell in data.Cells)
                {
                    var pos = GetCellPosition(cell, data, request.MmToFeet);

                    // FilledRegion (fondo).
                    if (cell.HasBackground
                        && fillTypeCache.TryGetValue(cell.BackgroundColor, out var fillTypeId))
                    {
                        TryCreateFilledRegion(doc, viewId, cell, pos, fillTypeId, warnings);
                    }

                    // Bordes (DetailLines).
                    TryCreateBorderLines(doc, view, cell, pos, warnings);

                    // TextNote.
                    if (!string.IsNullOrWhiteSpace(cell.Value))
                    {
                        var typeId = fontMapper.GetTextNoteTypeId(
                            cell.FontSizePt, cell.Bold, cell.Italic);
                        TryCreateTextNote(doc, viewId, cell, pos, typeId, warnings);
                    }
                }

                tx.Commit();
            }

            sw.Stop();
            return new TableGenResult
            {
                Success        = true,
                DraftingViewId = viewId,
                ViewName       = viewName,
                Warnings       = warnings.ToList().AsReadOnly(),
                ElapsedMs      = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TableGenResult
            {
                Success      = false,
                ErrorMessage = ex.Message,
                Warnings     = warnings.ToList().AsReadOnly(),
                ElapsedMs    = sw.ElapsedMilliseconds,
            };
        }
    }

    // ── Posicionamiento ──────────────────────────────────────────────────

    /// <summary>
    /// Calcula las coordenadas de la celda en pies de Revit.
    /// Devuelve (xLeft, xRight, yTop, yBottom) con Y positivo = arriba
    /// (es decir, yTop > yBottom para celdas en la parte superior).
    /// </summary>
    private static (double xL, double xR, double yT, double yB) GetCellPosition(
        ExcelCellData cell, ExcelSheetData data, double mmToFeet)
    {
        double xLeftMm  = data.ColumnLeftMm(cell.Column);
        double yTopMm   = data.RowTopMm(cell.Row);
        double xRightMm = xLeftMm  + cell.WidthMm;
        double yBotMm   = yTopMm   + cell.HeightMm;

        // En Revit: Y hacia arriba → negamos el mm-Y para que la tabla
        // crezca hacia abajo en la vista.
        return (
            xLeftMm  * mmToFeet,
            xRightMm * mmToFeet,
           -yTopMm   * mmToFeet,   // yTop en Revit es el valor menos negativo
           -yBotMm   * mmToFeet    // yBot en Revit es el valor mas negativo
        );
    }

    // ── FilledRegion ─────────────────────────────────────────────────────

    private static ElementId GetSolidFillPatternId(Document doc)
    {
        var pat = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

        return pat?.Id ?? ElementId.InvalidElementId;
    }

    /// <summary>
    /// Para cada color de fondo unico en la tabla, duplica un
    /// FilledRegionType existente (o lo crea desde el primero disponible)
    /// y le asigna el color y patron solido.
    /// </summary>
    private static Dictionary<uint, ElementId> PrepareFilledRegionTypes(
        Document doc, ExcelSheetData data,
        ElementId solidPatternId, IList<string> warnings)
    {
        var uniqueColors = data.Cells
            .Where(c => c.HasBackground)
            .Select(c => c.BackgroundColor)
            .Distinct()
            .ToList();

        var result = new Dictionary<uint, ElementId>();

        if (uniqueColors.Count == 0) return result;

        // Base: el primer FilledRegionType existente en el documento.
        var baseType = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .FirstOrDefault();

        if (baseType is null)
        {
            warnings.Add("No se encontro ningun FilledRegionType en el documento; se omiten los fondos.");
            return result;
        }

        foreach (uint argb in uniqueColors)
        {
            try
            {
                byte r = (byte)(argb >> 16 & 0xFF);
                byte g = (byte)(argb >>  8 & 0xFF);
                byte b = (byte)(argb        & 0xFF);

                var newType = (FilledRegionType)baseType.Duplicate(
                    $"TableGen_Fill_{r:X2}{g:X2}{b:X2}_{Guid.NewGuid():N}");

                newType.ForegroundPatternId = solidPatternId;
                newType.ForegroundPatternColor = new Color(r, g, b);
                newType.BackgroundPatternId = ElementId.InvalidElementId;

                result[argb] = newType.Id;
            }
            catch (Exception ex)
            {
                warnings.Add($"No se pudo crear FilledRegionType para color {argb:X8}: {ex.Message}");
            }
        }

        return result;
    }

    private static void TryCreateFilledRegion(
        Document doc, ElementId viewId,
        ExcelCellData cell,
        (double xL, double xR, double yT, double yB) pos,
        ElementId fillTypeId,
        IList<string> warnings)
    {
        try
        {
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(pos.xL, pos.yT, 0), new XYZ(pos.xR, pos.yT, 0)));
            loop.Append(Line.CreateBound(new XYZ(pos.xR, pos.yT, 0), new XYZ(pos.xR, pos.yB, 0)));
            loop.Append(Line.CreateBound(new XYZ(pos.xR, pos.yB, 0), new XYZ(pos.xL, pos.yB, 0)));
            loop.Append(Line.CreateBound(new XYZ(pos.xL, pos.yB, 0), new XYZ(pos.xL, pos.yT, 0)));

            FilledRegion.Create(doc, fillTypeId, viewId, new List<CurveLoop> { loop });
        }
        catch (Exception ex)
        {
            warnings.Add($"FilledRegion ({cell.Row},{cell.Column}): {ex.Message}");
        }
    }

    // ── DetailLines (bordes) ─────────────────────────────────────────────

    private static void TryCreateBorderLines(
        Document doc, View view,
        ExcelCellData cell,
        (double xL, double xR, double yT, double yB) pos,
        IList<string> warnings)
    {
        TryDrawLine(doc, view, cell.TopBorder,
            new XYZ(pos.xL, pos.yT, 0), new XYZ(pos.xR, pos.yT, 0),
            $"Top ({cell.Row},{cell.Column})", warnings);

        TryDrawLine(doc, view, cell.BottomBorder,
            new XYZ(pos.xL, pos.yB, 0), new XYZ(pos.xR, pos.yB, 0),
            $"Bottom ({cell.Row},{cell.Column})", warnings);

        TryDrawLine(doc, view, cell.LeftBorder,
            new XYZ(pos.xL, pos.yT, 0), new XYZ(pos.xL, pos.yB, 0),
            $"Left ({cell.Row},{cell.Column})", warnings);

        TryDrawLine(doc, view, cell.RightBorder,
            new XYZ(pos.xR, pos.yT, 0), new XYZ(pos.xR, pos.yB, 0),
            $"Right ({cell.Row},{cell.Column})", warnings);
    }

    private static void TryDrawLine(
        Document doc, View view,
        CellBorderStyle borderStyle,
        XYZ start, XYZ end,
        string label, IList<string> warnings)
    {
        if (borderStyle == CellBorderStyle.None) return;

        // Ignorar lineas degeneradas (celdas de ancho/alto cero).
        if (start.IsAlmostEqualTo(end)) return;

        try
        {
            var line = Line.CreateBound(start, end);
            doc.Create.NewDetailCurve(view, line);
        }
        catch (Exception ex)
        {
            warnings.Add($"DetailLine {label}: {ex.Message}");
        }
    }

    // ── TextNote ─────────────────────────────────────────────────────────

    private static void TryCreateTextNote(
        Document doc, ElementId viewId,
        ExcelCellData cell,
        (double xL, double xR, double yT, double yB) pos,
        ElementId textNoteTypeId,
        IList<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(cell.Value)) return;

        try
        {
            // Posicion: esquina superior-izquierda de la celda con un pequeño margen.
            double marginFt = 0.5 / 304.8; // 0.5 mm de margen interior
            var origin = new XYZ(pos.xL + marginFt, pos.yT - marginFt, 0);

            double widthFt = Math.Max(pos.xR - pos.xL - 2 * marginFt, 0.01);

            var opts = new TextNoteOptions(textNoteTypeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment   = VerticalTextAlignment.Top,
            };

            TextNote.Create(doc, viewId, origin, widthFt, cell.Value!, opts);
        }
        catch (Exception ex)
        {
            warnings.Add($"TextNote ({cell.Row},{cell.Column}): {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string EnsureUniqueViewName(Document doc, string baseName)
    {
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName)) return baseName;

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate)) return candidate;
        }

        return $"{baseName}_{Guid.NewGuid():N}";
    }
}
