using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Abstractions;
using E3G.BimTools.DiTools.Core.Models.ViewSheets;

namespace E3G.BimTools.DiTools.Core.Services.ViewSheets;

/// <summary>
/// Implementación del generador de planos MEP (módulo "View &amp; Sheet Manager").
///
/// Flujo en dos pasos:
///   · <see cref="PrepararPlan"/> — dry-run: recorre el triple bucle
///     instalaciones × niveles × sectores y calcula nombres/números únicos SIN
///     tocar el modelo.  Alimenta el árbol de previsualización.
///   · <see cref="Generar"/> — ejecuta los <see cref="PlanItem.Seleccionado"/> en
///     UNA sola transacción, con el orden crítico:
///       1. Crear todas las vistas + plantilla + escala + recorte a Scope Box.
///       2. <c>Regenerate()</c>  (geometría de vistas).
///       3. Crear hojas, numerar y colocar viewports en el centro del rótulo.
///       4. <c>Regenerate()</c>  (bounding box real del viewport).
///       5. Recentrar viewports, rellenar rótulos y commit.
///
/// La plantilla se aplica ANTES de la escala (puede sobrescribirla); si la
/// plantilla controla la escala, no se intenta fijar (ver <see cref="TrySetScale"/>).
/// </summary>
public sealed class ViewSheetGenerator : IViewSheetGenerator
{
    private readonly ITransactionService _tx;
    private readonly ILogger _logger;

    public ViewSheetGenerator(ITransactionService tx, ILoggerFactory loggerFactory)
    {
        _tx = tx ?? throw new ArgumentNullException(nameof(tx));
        _logger = loggerFactory?.CreateLogger<ViewSheetGenerator>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // =====================================================================
    // Paso 1 — Dry-run (sin transacción, no muta el modelo)
    // =====================================================================

    public GeneracionPlan PrepararPlan(Document doc, GeneracionRequest request)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var plan = new GeneracionPlan(request);

        if (request.NivelIds is not { Count: > 0 } || request.Instalaciones is not { Count: > 0 })
            return plan; // sin niveles o sin instalaciones → plan vacío

        // Sectores: si no hay, usamos un pseudo-sector sin recorte.
        var sectores = (request.SectorIds is { Count: > 0 })
            ? request.SectorIds.Select(id => (Id: id, Nombre: NombreElemento(doc, id))).ToList()
            : new List<(long Id, string Nombre)> { (-1, string.Empty) };

        var numerosUsados = new HashSet<string>(CollectSheetNumbers(doc), StringComparer.OrdinalIgnoreCase);
        var nombresVista = new HashSet<string>(CollectViewNames(doc), StringComparer.OrdinalIgnoreCase);

        bool agrupar = request.AgruparSectoresPorHoja && sectores.Count > 1;
        int contador = 1;

        foreach (var inst in request.Instalaciones)
        {
            long vftId = inst.ViewFamilyTypeId > 0 ? inst.ViewFamilyTypeId : request.ViewFamilyTypeId;
            int escala = inst.Escala > 0 ? inst.Escala : request.EscalaGlobal;

            foreach (var nivelId in request.NivelIds)
            {
                string nivelNombre = NombreElemento(doc, nivelId.Value);

                if (agrupar)
                {
                    // UNA hoja por instalación×nivel con N viewports (uno por sector).
                    int idx = contador++;
                    var item = NuevoItem(inst, vftId, escala, nivelId.Value, nivelNombre);

                    if (!request.SoloVistas)
                    {
                        string aviso = string.Empty;
                        item.NumeroHoja = BuildUniqueNumber(request.PatronNumeroHoja,
                            inst.PrefijoHoja, nivelNombre, inst.Nombre, string.Empty, idx,
                            request.PaddingContador, numerosUsados, ref aviso);
                        numerosUsados.Add(item.NumeroHoja);
                        item.NombreHoja = ReplaceTokens(request.PatronNombreHoja,
                            inst.PrefijoHoja, nivelNombre, inst.Nombre, string.Empty, idx, request.PaddingContador);
                        item.Aviso = aviso;
                    }

                    int n = sectores.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var (sectorId, sectorNombre) = sectores[i];
                        string nombreVista = EnsureUnique(ReplaceTokens(inst.PatronNombre,
                            inst.PrefijoHoja, nivelNombre, inst.Nombre, sectorNombre, contador++, request.PaddingContador), nombresVista);
                        var (fx, fy) = AutoGrid(i, n);
                        item.Viewports.Add(new PlanViewport
                        {
                            SectorId = sectorId, Sector = sectorNombre, NombreVista = nombreVista, FracX = fx, FracY = fy,
                        });
                    }
                    plan.Items.Add(item);
                }
                else
                {
                    // Una hoja por sector (comportamiento normal).
                    foreach (var (sectorId, sectorNombre) in sectores)
                    {
                        int idx = contador++;
                        string nombreVista = EnsureUnique(ReplaceTokens(inst.PatronNombre,
                            inst.PrefijoHoja, nivelNombre, inst.Nombre, sectorNombre, idx, request.PaddingContador), nombresVista);

                        var item = NuevoItem(inst, vftId, escala, nivelId.Value, nivelNombre);
                        if (!request.SoloVistas)
                        {
                            string aviso = string.Empty;
                            item.NumeroHoja = BuildUniqueNumber(request.PatronNumeroHoja,
                                inst.PrefijoHoja, nivelNombre, inst.Nombre, sectorNombre, idx,
                                request.PaddingContador, numerosUsados, ref aviso);
                            numerosUsados.Add(item.NumeroHoja);
                            item.NombreHoja = ReplaceTokens(request.PatronNombreHoja,
                                inst.PrefijoHoja, nivelNombre, inst.Nombre, sectorNombre, idx, request.PaddingContador);
                            item.Aviso = aviso;
                        }
                        item.Viewports.Add(new PlanViewport
                        {
                            SectorId = sectorId, Sector = sectorNombre, NombreVista = nombreVista,
                            FracX = Clamp01(request.ViewportFracX), FracY = Clamp01(request.ViewportFracY),
                        });
                        plan.Items.Add(item);
                    }
                }
            }
        }

        return plan;
    }

    private static PlanItem NuevoItem(InstalacionConfig inst, long vftId, int escala, long nivelId, string nivelNombre) => new()
    {
        Instalacion = inst.Nombre,
        Prefijo = inst.PrefijoHoja,
        ColorHex = inst.ColorHex,
        NivelId = nivelId,
        Nivel = nivelNombre,
        ViewFamilyTypeId = vftId,
        PlantillaVistaId = inst.PlantillaVistaId,
        Escala = escala,
    };

    /// <summary>Posición por defecto del viewport i de n en una rejilla de 2 columnas (centros de celda).</summary>
    private static (double X, double Y) AutoGrid(int i, int n)
    {
        if (n <= 1) return (0.5, 0.5);
        int cols = 2;
        int rows = (n + cols - 1) / cols;
        int col = i % cols, row = i / cols;
        double fx = (col + 0.5) / cols;
        double fy = 1.0 - (row + 0.5) / rows;
        return (fx, fy);
    }

    // =====================================================================
    // Paso 2 — Generación real (una sola transacción)
    // =====================================================================

    public GeneracionResumen Generar(Document doc, GeneracionPlan plan)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var resumen = new GeneracionResumen();
        var items = plan.Items.Where(i => i.Seleccionado).ToList();
        if (items.Count == 0)
        {
            resumen.Mensaje = "No hay combinaciones marcadas para generar.";
            return resumen;
        }

        var request = plan.Request;
        var titleBlockId = InstalacionConfig.ToElementId(request.TitleBlockTypeId);
        FamilySymbol? titleBlock = doc.GetElement(titleBlockId) as FamilySymbol;
        if (!request.SoloVistas && titleBlock is null)
        {
            resumen.Mensaje = "Title block no cargado o inválido. Selecciona un rótulo válido del proyecto.";
            return resumen;
        }

        var sw = Stopwatch.StartNew();

        // Unicidad re-verificada contra el estado vivo del documento.
        var numerosUsados = new HashSet<string>(CollectSheetNumbers(doc), StringComparer.OrdinalIgnoreCase);
        var nombresVista = new HashSet<string>(CollectViewNames(doc), StringComparer.OrdinalIgnoreCase);

        _tx.Run(doc, "View & Sheet Manager: generar planos", d =>
        {
            if (titleBlock is { IsActive: false }) titleBlock.Activate();

            // ── FASE 1 — Crear todas las vistas (una por viewport) ──────────
            var combinaciones = new List<Combinacion>();

            foreach (var item in items)
            {
                var vftId = InstalacionConfig.ToElementId(item.ViewFamilyTypeId);
                if (d.GetElement(vftId) is not ViewFamilyType vft || vft.ViewFamily != ViewFamily.FloorPlan)
                {
                    resumen.Resultados.Add(Error(item, "ViewFamilyType (tipo de vista de planta) inválido."));
                    continue;
                }
                if (d.GetElement(new ElementId(item.NivelId)) is not Level)
                {
                    resumen.Resultados.Add(Error(item, "Nivel inválido o borrado."));
                    continue;
                }

                var plantillaId = InstalacionConfig.ToElementId(item.PlantillaVistaId);
                var combo = new Combinacion { Item = item };

                foreach (var spec in item.Viewports)
                {
                    try
                    {
                        var view = ViewPlan.Create(d, vftId, new ElementId(item.NivelId));
                        try { view.Name = EnsureUnique(spec.NombreVista, nombresVista); } catch { }

                        string? avisoPlantilla = null;
                        if (plantillaId != ElementId.InvalidElementId)
                        {
                            if (d.GetElement(plantillaId) is View tpl && tpl.IsTemplate)
                                view.ViewTemplateId = plantillaId;
                            else
                                avisoPlantilla = "plantilla de vista inválida o borrada";
                        }

                        TrySetScale(view, item.Escala, out string? avisoEscala);

                        string? avisoSector = null;
                        if (spec.SectorId > 0)
                            avisoSector = AplicarScopeBox(view, new ElementId(spec.SectorId));

                        // Clasificación en la VISTA (esquema de vistas del navegador).
                        if (request.RellenarClasifVistas)
                            RellenarClasificacion(view, request.ClasificacionVistas, item, spec.Sector, request.PaddingContador);

                        combo.Vps.Add(new VpCreado
                        {
                            Spec = spec, View = view, NombreVista = view.Name,
                            Aviso = JoinAvisos(avisoPlantilla, avisoEscala, avisoSector),
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("ViewSheetGenerator: fallo creando vista {0}: {1}", spec.NombreVista, ex.Message);
                        combo.Aviso = JoinAvisos(combo.Aviso, $"vista '{spec.NombreVista}' no creada: {ex.Message}");
                    }
                }
                combinaciones.Add(combo);
            }

            d.Regenerate();

            // ── Modo SOLO VISTAS: no se crean hojas ni viewports ────────────
            if (request.SoloVistas)
            {
                foreach (var c in combinaciones)
                    foreach (var vp in c.Vps)
                        resumen.Resultados.Add(new ResultadoGeneracion
                        {
                            Instalacion = c.Item.Instalacion,
                            Nivel = vp.Spec.TieneSector ? $"{c.Item.Nivel} · {vp.Spec.Sector}" : c.Item.Nivel,
                            NombreVista = vp.NombreVista,
                            NumeroHoja = string.Empty,
                            Exito = true,
                            Motivo = JoinAvisos(c.Item.Aviso, vp.Aviso),
                        });
                return;
            }

            // ── FASE 2 — Crear una hoja por item y colocar sus viewports ────
            foreach (var c in combinaciones)
            {
                if (c.Vps.Count == 0)
                {
                    resumen.Resultados.Add(Error(c.Item, "No se creó ninguna vista para la hoja. " + c.Aviso));
                    continue;
                }
                try
                {
                    string numero = c.Item.NumeroHoja;
                    while (numerosUsados.Contains(numero))
                    {
                        numero = $"{c.Item.NumeroHoja}-{numerosUsados.Count}";
                        c.Aviso = JoinAvisos(c.Aviso, $"número ajustado a '{numero}' (ya existía)");
                    }

                    var sheet = ViewSheet.Create(d, titleBlockId);
                    sheet.SheetNumber = numero;
                    numerosUsados.Add(numero);
                    sheet.Name = c.Item.NombreHoja;
                    c.Sheet = sheet;
                    c.Numero = numero;

                    // Regenerar para que el rótulo tenga geometría real: si no, sheet.Outline
                    // puede ser (0,0) y la posición fraccional del viewport se colapsa al origen.
                    d.Regenerate();
                    BoundingBoxUV box = sheet.Outline;
                    foreach (var vp in c.Vps)
                    {
                        if (!Viewport.CanAddViewToSheet(d, sheet.Id, vp.View.Id))
                        {
                            vp.Aviso = JoinAvisos(vp.Aviso, "no admite colocarse (CanAddViewToSheet=false)");
                            continue;
                        }
                        var objetivo = new XYZ(
                            box.Min.U + Clamp01(vp.Spec.FracX) * (box.Max.U - box.Min.U),
                            box.Min.V + Clamp01(vp.Spec.FracY) * (box.Max.V - box.Min.V),
                            0);
                        vp.Viewport = Viewport.Create(d, sheet.Id, vp.View.Id, objetivo);
                        AplicarTipoViewport(vp.Viewport, request.ViewportTypeId);
                        vp.Target = objetivo;
                    }

                    RellenarRotulo(sheet, request.ParametrosRotulo, c.Item, request.PaddingContador);
                    // Clasificación en la HOJA (esquema de planos del navegador).
                    if (request.RellenarClasifPlanos)
                        RellenarClasificacion(sheet, request.ClasificacionPlanos, c.Item, c.Item.SectoresTexto, request.PaddingContador);
                }
                catch (Exception ex)
                {
                    _logger.Warning("ViewSheetGenerator: fallo creando hoja {0}: {1}", c.Item.NumeroHoja, ex.Message);
                    resumen.Resultados.Add(Error(c.Item, "No se pudo crear la hoja: " + ex.Message));
                }
            }

            d.Regenerate();

            // ── FASE 3 — Centrar viewports + cerrar resultados (1 fila/hoja) ─
            foreach (var c in combinaciones)
            {
                if (c.Sheet is null) continue;

                int colocados = 0;
                foreach (var vp in c.Vps)
                {
                    if (vp.Viewport is null || vp.Target is null) continue;
                    try
                    {
                        XYZ delta = vp.Target - vp.Viewport.GetBoxCenter();
                        if (!delta.IsZeroLength()) ElementTransformUtils.MoveElement(d, vp.Viewport.Id, delta);
                        colocados++;
                    }
                    catch (Exception ex) { vp.Aviso = JoinAvisos(vp.Aviso, "no centrado: " + ex.Message); }
                }

                var avisosVp = c.Vps.Select(v => v.Aviso).Where(a => !string.IsNullOrEmpty(a));
                resumen.Resultados.Add(new ResultadoGeneracion
                {
                    Instalacion = c.Item.Instalacion,
                    Nivel = $"{c.Item.Nivel}{(string.IsNullOrEmpty(c.Item.SectoresTexto) ? "" : " · " + c.Item.SectoresTexto)}",
                    NombreVista = c.Vps.Count == 1 ? c.Vps[0].NombreVista : $"{colocados}/{c.Vps.Count} vistas",
                    NumeroHoja = c.Numero,
                    HojaId = c.Sheet.Id.Value,
                    Exito = colocados > 0,
                    Motivo = JoinAvisos(c.Aviso, string.Join("; ", avisosVp)),
                });
            }
        });

        sw.Stop();
        resumen.ElapsedMs = sw.ElapsedMilliseconds;
        return resumen;
    }

    // =====================================================================
    // Medición REAL de composición (transacción revertida → no deja rastro)
    // =====================================================================

    public ComposicionMedida MedirComposicion(Document doc, GeneracionPlan plan, PlanItem item, PlanViewport viewport)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (plan is null || item is null || viewport is null) return default;

        var tbId = InstalacionConfig.ToElementId(plan.Request.TitleBlockTypeId);
        if (doc.GetElement(tbId) is not FamilySymbol tb) return default;
        var vftId = InstalacionConfig.ToElementId(item.ViewFamilyTypeId);
        if (doc.GetElement(vftId) is not ViewFamilyType vft || vft.ViewFamily != ViewFamily.FloorPlan) return default;
        if (doc.GetElement(new ElementId(item.NivelId)) is not Level) return default;

        try
        {
            using var t = new Transaction(doc, "Medir composición (temporal)");
            t.Start();
            if (!tb.IsActive) tb.Activate();

            // Vista con plantilla + escala + recorte, igual que en la generación real.
            var view = ViewPlan.Create(doc, vftId, new ElementId(item.NivelId));
            var plantillaId = InstalacionConfig.ToElementId(item.PlantillaVistaId);
            if (plantillaId != ElementId.InvalidElementId &&
                doc.GetElement(plantillaId) is View tpl && tpl.IsTemplate)
                view.ViewTemplateId = plantillaId;
            TrySetScale(view, item.Escala, out _);
            if (viewport.SectorId > 0) AplicarScopeBox(view, new ElementId(viewport.SectorId));
            doc.Regenerate();

            var sheet = ViewSheet.Create(doc, tbId);
            doc.Regenerate();
            BoundingBoxUV ol = sheet.Outline;
            double hojaW = Mm(ol.Max.U - ol.Min.U);
            double hojaH = Mm(ol.Max.V - ol.Min.V);

            double vpW = 0, vpH = 0;
            if (Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
            {
                var centro = new XYZ((ol.Min.U + ol.Max.U) / 2, (ol.Min.V + ol.Max.V) / 2, 0);
                var vp = Viewport.Create(doc, sheet.Id, view.Id, centro);
                AplicarTipoViewport(vp, plan.Request.ViewportTypeId); // el tipo afecta al tamaño medido
                doc.Regenerate();
                Outline outline = vp.GetBoxOutline(); // incluye el rótulo de la vista
                vpW = Mm(outline.MaximumPoint.X - outline.MinimumPoint.X);
                vpH = Mm(outline.MaximumPoint.Y - outline.MinimumPoint.Y);
            }

            t.RollBack(); // descarta todo lo temporal
            return new ComposicionMedida(hojaW, hojaH, vpW, vpH);
        }
        catch (Exception ex)
        {
            _logger.Warning("ViewSheetGenerator.MedirComposicion: {0}", ex.Message);
            return default;
        }
    }

    private static void AplicarTipoViewport(Viewport vp, long typeIdValue)
    {
        if (typeIdValue <= 0) return;
        try
        {
            var id = new ElementId(typeIdValue);
            if (vp.GetTypeId() != id && vp.GetValidTypes().Contains(id))
                vp.ChangeTypeId(id);
        }
        catch { /* tipo incompatible: se deja el por defecto */ }
    }

    private static double Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // =====================================================================
    // Export PNG de una hoja existente
    // =====================================================================

    public string? ExportarHojaPng(Document doc, long sheetId, string carpetaDestino)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        if (doc.GetElement(new ElementId(sheetId)) is not ViewSheet sheet) return null;

        try
        {
            Directory.CreateDirectory(carpetaDestino);
            // Carpeta limpia para identificar sin ambigüedad el archivo producido.
            string sub = Path.Combine(carpetaDestino, $"vsm_{sheetId}");
            if (Directory.Exists(sub)) Directory.Delete(sub, true);
            Directory.CreateDirectory(sub);

            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                ZoomType = ZoomFitType.FitToPage,
                FitDirection = FitDirectionType.Horizontal,
                PixelSize = 1400,
                ImageResolution = ImageResolution.DPI_150,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                FilePath = Path.Combine(sub, "preview"),
            };
            options.SetViewsAndSheets(new List<ElementId> { sheet.Id });

            doc.ExportImage(options);

            return Directory.GetFiles(sub, "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Warning("ViewSheetGenerator.ExportarHojaPng: {0}", ex.Message);
            return null;
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string? AplicarScopeBox(View view, ElementId scopeBoxId)
    {
        try
        {
            var prm = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (prm is null || prm.IsReadOnly) return "no se pudo asignar el Scope Box a la vista";

            // Asignar el Scope Box fija la región de recorte a su extensión.
            prm.Set(scopeBoxId);

            // El recorte debe estar ACTIVO para que limite el contenido. Si la
            // plantilla de vista controla "Crop View", el setter se ignora/lanza:
            // lo avisamos para que el usuario sepa por qué el plano puede salirse.
            bool recorteControladoPorPlantilla =
                view.ViewTemplateId != ElementId.InvalidElementId &&
                view.Document.GetElement(view.ViewTemplateId) is View tpl &&
                !tpl.GetNonControlledTemplateParameterIds()
                     .Contains(new ElementId(BuiltInParameter.VIEWER_CROP_REGION));

            try
            {
                view.CropBoxActive = true;
                view.CropBoxVisible = true;
            }
            catch
            {
                if (recorteControladoPorPlantilla)
                    return "el recorte lo controla la plantilla (desactiva 'Crop View' en la plantilla o usa otra) — el plano puede salirse del cajetín";
                throw;
            }

            if (!view.CropBoxActive && recorteControladoPorPlantilla)
                return "la plantilla mantiene el recorte desactivado — el contenido puede salirse del cajetín";

            return null;
        }
        catch (Exception ex)
        {
            return "Scope Box no aplicado: " + ex.Message;
        }
    }

    private static void TrySetScale(View view, int escala, out string? aviso)
    {
        aviso = null;
        if (escala <= 0) return;

        try
        {
            if (view.ViewTemplateId != ElementId.InvalidElementId &&
                view.Document.GetElement(view.ViewTemplateId) is View tpl)
            {
                var noControlados = tpl.GetNonControlledTemplateParameterIds();
                var scaleParam = new ElementId(BuiltInParameter.VIEW_SCALE);
                if (!noControlados.Contains(scaleParam))
                {
                    aviso = "escala controlada por la plantilla; no se sobrescribe";
                    return;
                }
            }

            view.Scale = escala;
        }
        catch (Exception ex)
        {
            aviso = "no se pudo fijar la escala: " + ex.Message;
        }
    }

    private static void RellenarRotulo(
        ViewSheet sheet, IDictionary<string, string>? parametros, PlanItem item, int padding)
    {
        if (parametros is null || parametros.Count == 0) return;

        foreach (var kv in parametros)
        {
            var prm = sheet.LookupParameter(kv.Key);
            if (prm is null || prm.IsReadOnly || prm.StorageType != StorageType.String) continue;
            string valor = ReplaceTokens(kv.Value, item.Prefijo, item.Nivel, item.Instalacion, item.SectoresTexto,
                0, padding);
            try { prm.Set(valor); } catch { /* parámetro incompatible: se ignora */ }
        }
    }

    /// <summary>Rellena los parámetros de clasificación (Capítulo/Subcapítulo) en una vista u hoja.</summary>
    private static void RellenarClasificacion(
        Element el, IDictionary<string, string>? clasif, PlanItem item, string sector, int padding)
    {
        if (clasif is null || clasif.Count == 0) return;
        foreach (var kv in clasif)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            var prm = el.LookupParameter(kv.Key);
            if (prm is null || prm.IsReadOnly || prm.StorageType != StorageType.String) continue;
            string valor = ReplaceTokens(kv.Value, item.Prefijo, item.Nivel, item.Instalacion, sector, 0, padding);
            try { prm.Set(valor); } catch { /* parámetro incompatible: se ignora */ }
        }
    }

    private static string BuildUniqueNumber(
        string patron, string prefijo, string nivel, string instalacion, string sector,
        int indice, int padding, HashSet<string> usados, ref string aviso)
    {
        string numero = ReplaceTokens(patron, prefijo, nivel, instalacion, sector, indice, padding);
        if (!usados.Contains(numero)) return numero;

        int extra = indice;
        string ajustado = numero;
        while (usados.Contains(ajustado))
        {
            extra++;
            ajustado = patron.Contains("{contador}")
                ? ReplaceTokens(patron, prefijo, nivel, instalacion, sector, extra, padding)
                : $"{numero}-{extra.ToString().PadLeft(padding, '0')}";
        }
        aviso = JoinAvisos(aviso, $"número en conflicto, ajustado a '{ajustado}'");
        return ajustado;
    }

    private static string EnsureUnique(string name, HashSet<string> usados)
    {
        string candidato = name;
        int i = 2;
        while (usados.Contains(candidato)) candidato = $"{name} ({i++})";
        usados.Add(candidato);
        return candidato;
    }

    private static string ReplaceTokens(
        string patron, string prefijo, string nivel, string instalacion, string sector, int contador, int padding)
    {
        if (string.IsNullOrEmpty(patron)) return string.Empty;
        return patron
            .Replace("{prefijo}", prefijo ?? string.Empty)
            .Replace("{nivel}", nivel ?? string.Empty)
            .Replace("{instalacion}", instalacion ?? string.Empty)
            .Replace("{sector}", sector ?? string.Empty)
            .Replace("{contador}", contador.ToString().PadLeft(Math.Max(1, padding), '0'));
    }

    private static string JoinAvisos(params string?[] avisos)
    {
        var validos = avisos.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        return validos.Length == 0 ? string.Empty : string.Join("; ", validos);
    }

    private static ResultadoGeneracion Error(PlanItem item, string motivo) => new()
    {
        Instalacion = item.Instalacion,
        Nivel = string.IsNullOrEmpty(item.SectoresTexto) ? item.Nivel : $"{item.Nivel} · {item.SectoresTexto}",
        NombreVista = item.Viewports.Count > 0 ? item.Viewports[0].NombreVista : string.Empty,
        NumeroHoja = item.NumeroHoja,
        Motivo = motivo,
        Exito = false,
    };

    private static string NombreElemento(Document doc, long idValue)
        => doc.GetElement(new ElementId(idValue))?.Name ?? $"#{idValue}";

    private static IEnumerable<string> CollectSheetNumbers(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber);

    private static IEnumerable<string> CollectViewNames(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate).Select(v => v.Name);

    private sealed class Combinacion
    {
        public PlanItem Item = null!;
        public List<VpCreado> Vps = new();
        public ViewSheet? Sheet;
        public string Numero = string.Empty;
        public string Aviso = string.Empty;
    }

    private sealed class VpCreado
    {
        public PlanViewport Spec = null!;
        public View View = null!;
        public string NombreVista = string.Empty;
        public string Aviso = string.Empty;
        public Viewport? Viewport;
        public XYZ? Target;
    }
}
