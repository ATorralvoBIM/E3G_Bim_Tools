using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.DxfLegend;

namespace E3G.BimTools.DiTools.Core.Services.DxfLegend;

/// <summary>
/// Dibuja una <see cref="LegendDrawing"/> en una vista de leyenda o de dibujo.
///
/// Todo es dibujo de detalle nativo en coordenadas reales (la escala de la vista
/// lo reduce al papel, igual que la ventana grafica de AutoCAD):
///   • Rellenos → FilledRegion con un tipo solido por color y contorno invisible.
///   • Curvas → DetailCurve con un estilo de linea por color (+ patron de linea
///     equivalente al tipo de linea del DXF, medido en papel).
///   • Textos → un TextNote por linea con un tipo por fuente/tamaño/color.
/// Orden: rellenos, luego curvas, luego textos (los contornos quedan encima).
///
/// Revit borra al confirmar algunos rellenos que acepta al crearlos (puntas en
/// cuspide entre arcos tangentes).  Se detectan tras el primer commit y se
/// rehacen como poligono con las puntas recortadas en una segunda transaccion;
/// ambas van en un TransactionGroup para que un Ctrl+Z lo deshaga todo.
/// </summary>
internal sealed class LegendRevitBuilder
{
    private readonly Document _doc;
    private readonly View _view;
    private readonly DxfLegendRequest _req;
    private readonly ILegendTextMeasurer _measurer;
    private readonly List<string> _warnings = new();

    private readonly double _k;            // pies por unidad de dibujo
    private readonly double _dx, _dy;      // desplazamiento opcional al origen
    private readonly double _tol;          // ShortCurveTolerance (pies)

    private readonly Dictionary<string, GraphicsStyle> _lineStyles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementId> _linePatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementId> _fillTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementId> _textTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ElementId, (double Min, double Max)> _widthRange = new();
    private ElementId _invisibleStyle = ElementId.InvalidElementId;
    private ElementId _solidPattern = ElementId.InvalidElementId;

    private int _curves, _fills, _texts;

    public LegendRevitBuilder(Document doc, View view, LegendDrawing drawing, DxfLegendRequest req, ILegendTextMeasurer measurer)
    {
        _doc = doc;
        _view = view;
        _req = req;
        _measurer = measurer;
        _k = req.Units.ToFeet();
        if (req.MoveToOrigin) { _dx = -drawing.MinX; _dy = -drawing.MaxY; }
        _tol = doc.Application.ShortCurveTolerance;
    }

    public DxfLegendResult Build(LegendDrawing drawing)
    {
        var sw = Stopwatch.StartNew();
        var items = drawing.Items.Where(i => !_req.ExcludedLayers.Contains(i.Layer)).ToList();
        int deleted = 0;

        // Documento ya en transaccion (p. ej. llamado desde un script): se dibuja dentro de ella,
        // sin grupo ni reparacion posterior (los rellenos con cuspides ya van como poligono).
        if (_doc.IsModifiable)
        {
            DrawAll(items, out deleted);
            sw.Stop();
            return Result(deleted, sw);
        }

        using var group = new TransactionGroup(_doc, "Leyenda desde DXF");
        group.Start();

        List<(LegendFill Fill, List<ElementId> Ids)> created;
        using (var tx = new Transaction(_doc, "Leyenda desde DXF — dibujar"))
        {
            tx.SetFailureHandlingOptions(FailureOptions(tx));
            tx.Start();

            created = DrawAll(items, out deleted);

            if (tx.Commit() != TransactionStatus.Committed)
            {
                group.RollBack();
                return Fail("Revit no confirmó la transacción de dibujo.", sw);
            }
        }

        // Rellenos que Revit ha borrado al confirmar → reintento como poligono saneado.
        var lost = created.Where(c => c.Ids.Any(id => _doc.GetElement(id) is null)).ToList();
        if (lost.Count > 0)
        {
            using var tx = new Transaction(_doc, "Leyenda desde DXF — reparar rellenos");
            tx.SetFailureHandlingOptions(FailureOptions(tx));
            tx.Start();
            var repaired = new List<ElementId>();
            foreach (var (fill, ids) in lost)
            {
                // Se rehace el relleno entero: fuera tambien las partes que sobrevivieron.
                _fills -= ids.Count;
                var survivors = ids.Where(id => _doc.GetElement(id) is not null).ToList();
                if (survivors.Count > 0) _doc.Delete(survivors);
                repaired.AddRange(CreateFill(fill, exact: false));
            }
            BringCurvesAboveFills(repaired);
            tx.Commit();

            int stillLost = repaired.Count(id => _doc.GetElement(id) is null);
            if (stillLost > 0)
                _warnings.Add($"{stillLost} relleno(s) no se pudieron crear con una geometría válida para Revit.");
        }

        group.Assimilate();
        sw.Stop();
        return Result(deleted, sw);
    }

    private List<(LegendFill Fill, List<ElementId> Ids)> DrawAll(List<LegendItem> items, out int deleted)
    {
        deleted = 0;
        if (_view.Scale != _req.Scale) _view.Scale = _req.Scale;
        if (_req.ClearView) deleted = ClearView();
        PrepareShared();

        var created = new List<(LegendFill Fill, List<ElementId> Ids)>();
        double thinLimit = _req.ThinBandPaperMm / 304.8 * _req.Scale;   // pies reales
        foreach (var f in items.OfType<LegendFill>())
        {
            if (f is BandFill band && band.BandWidth * _k < thinLimit) continue; // va como linea
            var ids = CreateFill(f, exact: true);
            if (ids.Count > 0) created.Add((f, ids));
        }

        foreach (var item in items)
        {
            if (item is LegendCurve c) CreateCurve(c);
            else if (item is BandFill band && band.BandWidth * _k < thinLimit) CreateBandAsLine(band);
        }

        foreach (var t in items.OfType<LegendText>())
            CreateText(t);
        return created;
    }

    private DxfLegendResult Result(int deleted, Stopwatch sw)
    {
        var warnings = _warnings.Distinct().ToList();
        if (_tinyTexts > 0)
            warnings.Insert(0, $"{_tinyTexts} texto(s) de menos de {MinTextPaperMm.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))} mm en papel omitidos " +
                               "(ilegibles y Revit no admite tipos de texto tan pequeños).");
        if (_tinyCurves > 0)
            warnings.Insert(0, $"{_tinyCurves} línea(s)/arco(s) omitidos por ser más cortos que la tolerancia de Revit (≈0,8 mm reales).");
        return new DxfLegendResult
        {
            Success = true,
            Curves = _curves,
            Fills = _fills,
            Texts = _texts,
            Deleted = deleted,
            Warnings = warnings,
            ElapsedMs = sw.ElapsedMilliseconds,
        };
    }

    private DxfLegendResult Fail(string msg, Stopwatch sw) => new()
    {
        Success = false,
        ErrorMessage = msg,
        Warnings = _warnings.ToList(),
        ElapsedMs = sw.ElapsedMilliseconds,
    };

    /// <summary>
    /// Punto de Revit (pies) redondeado a 1e-7 ft: el DXF trae ruido de coma
    /// flotante (x = -1e-13 junto a x = 0) y dos lineas consecutivas casi
    /// colineales hacen que Revit falle al unirlas ("No es posible mantener
    /// unidos los elementos").
    /// </summary>
    private XYZ P(double x, double y) => new(Math.Round((x + _dx) * _k, 7), Math.Round((y + _dy) * _k, 7), 0);

    // ───────────────────────── Preparacion ─────────────────────────

    private int ClearView()
    {
        var ids = new FilteredElementCollector(_doc, _view.Id)
            .WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == _view.Id && (e is DetailCurve || e is FilledRegion || e is TextNote))
            .Select(e => e.Id)
            .ToList();
        if (ids.Count > 0) _doc.Delete(ids);
        return ids.Count;
    }

    private void PrepareShared()
    {
        _invisibleStyle = new FilteredElementCollector(_doc)
            .OfClass(typeof(GraphicsStyle)).Cast<GraphicsStyle>()
            .FirstOrDefault(g => g.GraphicsStyleCategory?.Id.Value == (long)BuiltInCategory.OST_InvisibleLines)?.Id
            ?? ElementId.InvalidElementId;

        var solids = new FilteredElementCollector(_doc)
            .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .Where(f => f.GetFillPattern().IsSolidFill).ToList();
        _solidPattern = (solids.FirstOrDefault(f => f.GetFillPattern().Target == FillPatternTarget.Drafting) ?? solids.FirstOrDefault())?.Id
            ?? ElementId.InvalidElementId;
        if (_solidPattern == ElementId.InvalidElementId)
            _warnings.Add("El documento no tiene patrón de relleno sólido; los rellenos usarán el del tipo base.");
    }

    private FailureHandlingOptions FailureOptions(Transaction tx) =>
        tx.GetFailureHandlingOptions()
          .SetFailuresPreprocessor(new SilentFailures(_warnings))
          .SetClearAfterRollback(true)
          .SetForcedModalHandling(false);

    /// <summary>
    /// Sin cuadros modales: borra avisos, resuelve los errores con su resolucion
    /// por defecto (normalmente borrar el elemento) y, si un error no tiene
    /// resolucion (p. ej. "Valor fuera de rango"), descarta los elementos que lo
    /// provocan para que no se cancele toda la leyenda.  Los rellenos borrados
    /// se reparan despues.
    /// </summary>
    private sealed class SilentFailures : IFailuresPreprocessor
    {
        private readonly List<string> _warnings;
        public SilentFailures(List<string> warnings) => _warnings = warnings;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            bool changed = false;
            foreach (var f in fa.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    fa.DeleteWarning(f);
                }
                else if (f.HasResolutions())
                {
                    fa.ResolveFailure(f);
                    changed = true;
                }
                else
                {
                    var ids = f.GetFailingElementIds();
                    if (ids.Count == 0) continue;
                    _warnings.Add($"Revit descartó {ids.Count} elemento(s): {f.GetDescriptionText()}");
                    fa.DeleteElements(ids.ToList());
                    changed = true;
                }
            }
            return changed ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }

    // ───────────────────────── Estilos ─────────────────────────

    private GraphicsStyle LineStyle(LegendColor color, LegendLinePattern? pattern)
    {
        ElementId patternId = pattern is null ? LinePatternElement.GetSolidPatternId() : LinePattern(pattern);
        string patternName = pattern is null || patternId == LinePatternElement.GetSolidPatternId()
            ? string.Empty
            : "_" + (_doc.GetElement(patternId)?.Name ?? pattern.Name).Replace(_req.StylePrefix + "_", string.Empty);
        string name = _req.LineStyleNames is not null && _req.LineStyleNames.TryGetValue(color.Key, out var fixedName)
            ? fixedName + patternName
            : $"{_req.StylePrefix}_{color.Key}{patternName}";
        if (_lineStyles.TryGetValue(name, out var gs)) return gs;
        int weight = _req.LineWeights is not null && _req.LineWeights.TryGetValue(name, out var w) ? Math.Clamp(w, 1, 16) : 1;

        var linesCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        var sub = linesCat.SubCategories.Contains(name)
            ? linesCat.SubCategories.get_Item(name)
            : _doc.Settings.Categories.NewSubcategory(linesCat, name);
        sub.LineColor = new Color(color.R, color.G, color.B);
        sub.SetLineWeight(weight, GraphicsStyleType.Projection);
        sub.SetLinePatternId(patternId, GraphicsStyleType.Projection);
        gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        _lineStyles[name] = gs;
        return gs;
    }

    /// <summary>Patron de linea de Revit (longitudes en papel) equivalente al tipo de linea del DXF.</summary>
    private ElementId LinePattern(LegendLinePattern p)
    {
        if (_linePatterns.TryGetValue(p.Key, out var cached)) return cached;

        double toPaper = _k / _req.Scale;                // pies de papel por unidad de dibujo
        double min = 0.1 / 304.8;
        var raw = new List<(LinePatternSegmentType Type, double Len)>();
        foreach (var s in p.Segments)
        {
            if (s > 0) raw.Add((LinePatternSegmentType.Dash, Math.Max(s * toPaper, min)));
            else if (s < 0) raw.Add((LinePatternSegmentType.Space, Math.Max(-s * toPaper, min)));
            else raw.Add((LinePatternSegmentType.Dot, 0));
        }

        // Revit exige alternar trazo/punto con hueco: se fusionan tramos consecutivos del mismo tipo.
        var segs = new List<(LinePatternSegmentType Type, double Len)>();
        foreach (var s in raw)
        {
            bool isSpace = s.Type == LinePatternSegmentType.Space;
            if (segs.Count > 0 && (segs[^1].Type == LinePatternSegmentType.Space) == isSpace)
            {
                var last = segs[^1];
                segs[^1] = (last.Type == LinePatternSegmentType.Dot ? s.Type : last.Type, last.Len + s.Len);
            }
            else segs.Add(s);
        }
        if (segs.Count > 0 && segs[0].Type == LinePatternSegmentType.Space) { segs.Add(segs[0]); segs.RemoveAt(0); }
        if (segs.Count > 1 && segs[^1].Type != LinePatternSegmentType.Space) segs.Add((LinePatternSegmentType.Space, min));
        if (segs.Count < 2 || segs.All(s => s.Type != LinePatternSegmentType.Space))
        {
            _linePatterns[p.Key] = LinePatternElement.GetSolidPatternId();
            return _linePatterns[p.Key];
        }

        double totalMm = segs.Sum(s => s.Len) * 304.8;
        string name = $"{_req.StylePrefix}_{p.Name}_{totalMm.ToString("0.##", CultureInfo.InvariantCulture)}mm";
        var pattern = new LinePattern(name);
        pattern.SetSegments(segs.Select(s => new LinePatternSegment(s.Type, s.Len)).ToList());

        var existing = new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>().FirstOrDefault(e => e.Name == name);
        ElementId id;
        if (existing is not null) { existing.SetLinePattern(pattern); id = existing.Id; }
        else id = LinePatternElement.Create(_doc, pattern).Id;
        _linePatterns[p.Key] = id;
        return id;
    }

    private ElementId FillType(LegendColor color)
    {
        string name = $"{_req.StylePrefix}_Sólido_{color.Key}";
        if (_fillTypes.TryGetValue(name, out var cached)) return cached;

        var all = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var type = all.FirstOrDefault(t => t.Name == name)
            ?? (all.FirstOrDefault() ?? throw new InvalidOperationException("El documento no tiene ningún tipo de región rellenada."))
                .Duplicate(name) as FilledRegionType
            ?? throw new InvalidOperationException("No se pudo crear el tipo de región " + name);

        if (_solidPattern != ElementId.InvalidElementId) type.ForegroundPatternId = _solidPattern;
        type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
        type.BackgroundPatternId = ElementId.InvalidElementId;
        type.IsMasking = false;
        type.LineWeight = 1;
        _fillTypes[name] = type.Id;
        return type.Id;
    }

    private ElementId TextType(LegendText t, out double sizePaperFt)
    {
        double sizeMm = t.Height * _req.Units.ToMm() / _req.Scale;
        sizePaperFt = sizeMm / 304.8;
        double wf = Math.Round(t.WidthFactor * _req.TextWidthFactor, 3);

        string name = $"{_req.StylePrefix}_{t.FontFamily}{(t.Bold ? " Negrita" : "")}{(t.Italic ? " Cursiva" : "")}_" +
                      sizeMm.ToString("0.00", CultureInfo.InvariantCulture) + "mm" +
                      (Math.Abs(wf - _req.TextWidthFactor) > 1e-3 ? "_w" + wf.ToString("0.###", CultureInfo.InvariantCulture) : "") +
                      (t.Color.IsBlack ? "" : "_" + t.Color.Key);
        if (_textTypes.TryGetValue(name, out var cached)) return cached;

        var all = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
        var type = all.FirstOrDefault(x => x.Name == name)
            ?? (all.FirstOrDefault() ?? throw new InvalidOperationException("El documento no tiene ningún tipo de texto."))
                .Duplicate(name) as TextNoteType
            ?? throw new InvalidOperationException("No se pudo crear el tipo de texto " + name);

        Set(type, BuiltInParameter.TEXT_FONT, t.FontFamily);
        Set(type, BuiltInParameter.TEXT_SIZE, sizePaperFt);
        Set(type, BuiltInParameter.TEXT_WIDTH_SCALE, wf);
        Set(type, BuiltInParameter.TEXT_BACKGROUND, 1);       // transparente
        Set(type, BuiltInParameter.TEXT_BOX_VISIBILITY, 0);
        Set(type, BuiltInParameter.LEADER_OFFSET_SHEET, 0.0);
        Set(type, BuiltInParameter.LINE_COLOR, t.Color.R | (t.Color.G << 8) | (t.Color.B << 16));
        Set(type, BuiltInParameter.TEXT_STYLE_BOLD, t.Bold ? 1 : 0);
        Set(type, BuiltInParameter.TEXT_STYLE_ITALIC, t.Italic ? 1 : 0);
        Set(type, BuiltInParameter.TEXT_STYLE_UNDERLINE, 0);
        // Los limites de ancho de un TextNote dependen del tamaño del tipo: hay que
        // regenerar para que GetMinimum/MaximumAllowedWidth vean el tamaño nuevo y
        // no el del tipo base duplicado (si no, el ancho sale "fuera de rango").
        _doc.Regenerate();
        _widthRange[type.Id] = (TextNote.GetMinimumAllowedWidth(_doc, type.Id), TextNote.GetMaximumAllowedWidth(_doc, type.Id));
        _textTypes[name] = type.Id;
        return type.Id;
    }

    private static void Set(Element e, BuiltInParameter bip, object value)
    {
        var p = e.get_Parameter(bip);
        if (p is null || p.IsReadOnly) return;
        switch (value)
        {
            case string s: p.Set(s); break;
            case int i: p.Set(i); break;
            case double d: p.Set(d); break;
        }
    }

    // ───────────────────────── Elementos ─────────────────────────

    private void CreateCurve(LegendCurve c)
    {
        try
        {
            Curve? curve = c.Kind switch
            {
                LegendCurveKind.Line => LineOrNull(P(c.X1, c.Y1), P(c.X2, c.Y2)),
                LegendCurveKind.Arc => c.R * _k < _tol ? null
                    : Arc.Create(P(c.Cx, c.Cy), c.R * _k, c.A0, c.A1, XYZ.BasisX, XYZ.BasisY),
                _ => EllipseCurve(c),
            };
            if (curve is null || curve.Length < _tol) { _tinyCurves++; return; }
            var dc = _doc.Create.NewDetailCurve(_view, curve);
            dc.LineStyle = LineStyle(c.Color, c.Pattern);
            _curves++;
        }
        catch (Exception ex) when (ex.Message.Contains("ShortCurveTolerance"))
        {
            _tinyCurves++;   // detalles microscopicos de bloques, por debajo de la tolerancia de Revit
        }
        catch (Exception ex)
        {
            _warnings.Add($"Curva en capa '{c.Layer}': {ex.Message}");
        }
    }

    private int _tinyCurves;

    private Curve? EllipseCurve(LegendCurve c)
    {
        if (c.R * _k < _tol || c.R2 * _k < _tol) return null;
        var xa = new XYZ(Math.Cos(c.Rotation), Math.Sin(c.Rotation), 0);
        var ya = new XYZ(-Math.Sin(c.Rotation), Math.Cos(c.Rotation), 0);
        return Ellipse.CreateCurve(P(c.Cx, c.Cy), c.R * _k, c.R2 * _k, xa, ya, c.A0, c.A1);
    }

    private Line? LineOrNull(XYZ a, XYZ b) => a.DistanceTo(b) < _tol ? null : Line.CreateBound(a, b);

    /// <summary>Polilinea con grosor tan fino que como region no se veria: se dibuja su eje.</summary>
    private void CreateBandAsLine(BandFill band)
    {
        try
        {
            Curve? curve = band.IsArc
                ? (band.R * _k < _tol ? null : Arc.Create(P(band.Cx, band.Cy), band.R * _k, band.A0, band.A1, XYZ.BasisX, XYZ.BasisY))
                : LineOrNull(P(band.A.X, band.A.Y), P(band.B.X, band.B.Y));
            if (curve is null) return;
            var dc = _doc.Create.NewDetailCurve(_view, curve);
            dc.LineStyle = LineStyle(band.Color, null);
            _curves++;
        }
        catch (Exception ex)
        {
            _warnings.Add($"Trazo fino en capa '{band.Layer}': {ex.Message}");
        }
    }

    /// <summary>
    /// Crea el relleno.  exact: contornos con arcos reales (todos los contornos
    /// juntos y, si Revit los rechaza, uno a uno).  Si no: poligonos saneados
    /// (sin puntos duplicados ni cuspides), uno por contorno.
    /// </summary>
    private List<ElementId> CreateFill(LegendFill f, bool exact)
    {
        var ids = new List<ElementId>();
        ElementId typeId;
        try { typeId = FillType(f.Color); }
        catch (Exception ex) { _warnings.Add(ex.Message); return ids; }

        double tolDrawing = Math.Max(_tol / _k * 1.5, 1e-9);

        var loops = new List<CurveLoop>();
        foreach (var loop in f.Loops)
        {
            var cl = exact ? ExactLoop(loop, tolDrawing) : PolygonLoop(loop, tolDrawing);
            if (cl is not null) loops.Add(cl);
            else if (exact) return CreateFill(f, exact: false);   // algun contorno no vale tal cual
        }

        if (loops.Count > 0)
        {
            // Todos juntos (respeta las islas/huecos) y, si Revit no los acepta, uno a uno.
            var region = TryRegion(typeId, loops);
            if (region is not null) { ids.Add(region); return ids; }
            foreach (var cl in loops)
            {
                var one = TryRegion(typeId, new List<CurveLoop> { cl });
                if (one is not null) ids.Add(one);
            }
        }

        if (ids.Count == 0)
        {
            if (exact) return CreateFill(f, exact: false);
            _warnings.Add($"Relleno en capa '{f.Layer}' omitido: contorno no válido para Revit.");
        }
        return ids;
    }

    /// <summary>Contorno como poligono saneado (sin puntos duplicados ni cuspides).  Null si no cierra.</summary>
    private CurveLoop? PolygonLoop(IReadOnlyList<LegendSegment> loop, double tolDrawing)
    {
        var poly = LegendGeometry.LoopToPolygon(loop, tolDrawing, Math.PI / 24);
        if (poly is null) return null;
        poly = LegendGeometry.CleanPolygon(poly, tolDrawing * 2);
        if (poly.Count < 3) return null;
        try
        {
            var cl = new CurveLoop();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                cl.Append(Line.CreateBound(P(a.X, a.Y), P(b.X, b.Y)));
            }
            return cl;
        }
        catch
        {
            return null;
        }
    }

    private CurveLoop? ExactLoop(IReadOnlyList<LegendSegment> loop, double tolDrawing)
    {
        var chained = LegendGeometry.Chain(loop, tolDrawing);
        // Contorno abierto, o con cuspides que Revit borraria al confirmar: va por la via del poligono saneado.
        if (chained is null || LegendGeometry.HasCusp(chained)) return null;
        try
        {
            var cl = new CurveLoop();
            foreach (var (s, reversed) in chained)
            {
                if (s.Kind == LegendSegmentKind.Line)
                {
                    var a = P(s.X1, s.Y1); var b = P(s.X2, s.Y2);
                    if (a.DistanceTo(b) < _tol) return null;
                    cl.Append(reversed ? Line.CreateBound(b, a) : Line.CreateBound(a, b));
                }
                else
                {
                    var c = P(s.Cx, s.Cy);
                    double r = s.R * _k;
                    // Circulo completo: dos semicirculos (un CurveLoop no admite un arco cerrado).
                    if (s.A1 - s.A0 >= 2 * Math.PI - 1e-9)
                    {
                        cl.Append(Arc.Create(c, r, s.A0, s.A0 + Math.PI, XYZ.BasisX, XYZ.BasisY));
                        cl.Append(Arc.Create(c, r, s.A0 + Math.PI, s.A0 + 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                        continue;
                    }
                    Curve arc = Arc.Create(c, r, s.A0, s.A1, XYZ.BasisX, XYZ.BasisY);
                    if (arc.Length < _tol) return null;
                    cl.Append(reversed ? arc.CreateReversed() : arc);
                }
            }
            return cl;
        }
        catch
        {
            return null;
        }
    }

    private ElementId? TryRegion(ElementId typeId, IList<CurveLoop> loops)
    {
        try
        {
            var region = FilledRegion.Create(_doc, typeId, _view.Id, loops);
            if (_invisibleStyle != ElementId.InvalidElementId) region.SetLineStyleId(_invisibleStyle);
            _fills++;
            return region.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Los rellenos rehechos se crean despues de las lineas: se devuelven las lineas que pisan encima.</summary>
    private void BringCurvesAboveFills(IList<ElementId> fills)
    {
        var boxes = fills.Select(id => _doc.GetElement(id)?.get_BoundingBox(_view)).Where(b => b is not null).ToList();
        if (boxes.Count == 0) return;
        double pad = _tol;
        var curves = new FilteredElementCollector(_doc, _view.Id).OfClass(typeof(CurveElement))
            .Where(e =>
            {
                var b = e.get_BoundingBox(_view);
                return b is not null && boxes.Any(f =>
                    b.Min.X >= f!.Min.X - pad && b.Max.X <= f.Max.X + pad &&
                    b.Min.Y >= f.Min.Y - pad && b.Max.Y <= f.Max.Y + pad);
            })
            .Select(e => e.Id).ToList();
        if (curves.Count > 0) DetailElementOrderUtils.BringToFront(_doc, _view, curves);
    }

    /// <summary>
    /// Revit no admite tipos de texto de menos de ~0,25 mm en papel: al regenerar
    /// lanza "Valor fuera de rango. Introduzca un número comprendido entre
    /// 0,2525 mm y …" sin elemento asociado (no se puede resolver ni descartar y
    /// cancela toda la transaccion).  Esos textos (p. ej. rotulos internos de
    /// bloques) son ilegibles incluso impresos: se omiten, con margen.
    /// </summary>
    private const double MinTextPaperMm = 0.3;
    private int _tinyTexts;

    private void CreateText(LegendText t)
    {
        double paperMm = t.Height * _req.Units.ToMm() / _req.Scale;
        if (paperMm < MinTextPaperMm)
        {
            _tinyTexts++;
            return;
        }

        try
        {
            // Tipo existente del proyecto (esquema de principio) o uno creado a medida.
            ElementId typeId;
            if (t.StyleKey is not null && _req.TextTypes is not null
                && _req.TextTypes.TryGetValue(t.StyleKey, out var fixedType) && fixedType != ElementId.InvalidElementId)
            {
                typeId = fixedType;
                if (!_widthRange.ContainsKey(typeId))
                    _widthRange[typeId] = (TextNote.GetMinimumAllowedWidth(_doc, typeId), TextNote.GetMaximumAllowedWidth(_doc, typeId));
            }
            else
                typeId = TextType(t, out _);
            // Revit centra la caja con las metricas win de la fuente: el centro queda
            // a una altura fija sobre la linea base (≈0,54·h en Calibri).
            double up = _measurer.BoxCenterAboveBaseline(t.FontFamily) * t.Height;
            double x = t.X - Math.Sin(t.Rotation) * up, y = t.BaselineY + Math.Cos(t.Rotation) * up;

            var opts = new TextNoteOptions(typeId)
            {
                HorizontalAlignment = t.HAlign switch
                {
                    LegendTextHAlign.Center => HorizontalTextAlignment.Center,
                    LegendTextHAlign.Right => HorizontalTextAlignment.Right,
                    _ => HorizontalTextAlignment.Left,
                },
                VerticalAlignment = VerticalTextAlignment.Middle,
                Rotation = t.Rotation,
            };

            // Ancho holgado para que Revit no parta la linea (en pies de papel).
            double width = (t.MeasuredWidth * 1.15 + t.Height * 2) * _k / _req.Scale;
            // Con margen: Revit valida el rango al confirmar y un valor en el limite exacto falla.
            var (minWidth, maxWidth) = _widthRange[typeId];
            width = Math.Min(Math.Max(width, minWidth * 1.05), maxWidth * 0.95);

            TextNote.Create(_doc, _view.Id, P(x, y), width, t.Text, opts);
            _texts++;
        }
        catch (Exception ex)
        {
            _warnings.Add($"Texto '{t.Text}': {ex.Message}");
        }
    }
}
