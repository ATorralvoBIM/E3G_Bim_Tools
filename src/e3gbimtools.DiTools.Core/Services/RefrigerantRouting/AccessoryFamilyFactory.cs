using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.RefrigerantRouting;

namespace E3G.BimTools.DiTools.Core.Services.RefrigerantRouting;

/// <summary>
/// Fabrica una familia de accesorio de tuberia a partir de un bloque LATS
/// cuando no hay familia del fabricante (LG solo las da por su add-in):
///   • Derivador: un tubo por boca con conector «Global» en la cara de la boca y
///     diametro por parametro de ejemplar D_1…D_n (n = bocas del bloque).
///   • Caja / equipo: prisma con la planta del cuerpo del bloque y el alto de
///     las opciones, y muñones en las bocas SIN conectores (si una sola pieza
///     uniera liquido y gas, Revit fundiria los dos sistemas).
/// Plantilla: «Modelo generico metrico» cambiado a categoria Accesorios de tuberia.
/// Se guarda en %TEMP%\E3G_Clima y se carga en el proyecto (debe haber transaccion abierta).
/// </summary>
internal static class AccessoryFamilyFactory
{
    private const double Ft = 1 / 0.3048;
    private static readonly string[] TemplateNames =
    {
        "Modelo genérico métrico.rft", "Metric Generic Model.rft", "Generic Model.rft",
        "Modèle générique métrique.rft", "Modello generico metrico.rft",
    };

    /// <param name="unitToMeters">Metros por unidad del bloque.</param>
    public static FamilySymbol Create(Document doc, LatsBlockDef def, BlockRole role, double unitToMeters,
                                      double spacing, double unitHeight, List<string> warnings)
    {
        var app = doc.Application;
        string template = FindTemplate(app) ?? throw new InvalidOperationException(
            "No se encuentra la plantilla «Modelo genérico métrico» para crear la familia " + def.Name + ".");

        string dir = Path.Combine(Path.GetTempPath(), "E3G_Clima");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Sanitize(def.Name) + ".rfa");

        Document fd = app.NewFamilyDocument(template);
        try
        {
            using (var t = new Transaction(fd, "Crear " + def.Name))
            {
                t.Start();
                fd.OwnerFamily.FamilyCategory = Category.GetCategory(fd, BuiltInCategory.OST_PipeAccessory);
                var fm = fd.FamilyManager;
                if (fm.CurrentType is null) fm.NewType("Estándar");

                if (role == BlockRole.Branch) BuildBranch(fd, def, unitToMeters, warnings);
                else BuildUnit(fd, def, unitToMeters, spacing, unitHeight);
                t.Commit();
            }
            var so = new SaveAsOptions { OverwriteExistingFile = true };
            fd.SaveAs(path, so);
        }
        finally
        {
            fd.Close(false);
        }

        if (!doc.LoadFamily(path, new OverwriteLoadOptions(), out Family fam) || fam is null)
            fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                      .FirstOrDefault(f => f.Name.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException("No se pudo cargar la familia " + def.Name + ".");
        var symbol = fam.GetFamilySymbolIds().Select(doc.GetElement).OfType<FamilySymbol>().First();
        if (!symbol.IsActive) symbol.Activate();
        return symbol;
    }

    public static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // ============================================================
    // Geometria
    // ============================================================

    private static void BuildBranch(Document fd, LatsBlockDef def, double k, List<string> warnings)
    {
        var fm = fd.FamilyManager;
        var hub = new XYZ(0, 0, 0);
        var opt = new Options { ComputeReferences = true };
        foreach (var p in def.Ports)
        {
            var port = ToXyz(p.Local, k);
            var dir = new XYZ(p.Out.X, p.Out.Y, 0).Normalize();
            double reach = (port - hub).GetLength();
            double stub = Math.Min(0.055 * Ft, reach / 2);
            var stubStart = port - dir * stub;
            double r = (p.Index == 0 ? 9.0 : 7.0) / 304.8;
            if (stubStart.DistanceTo(hub) > 1e-4) Cylinder(fd, hub, stubStart, r * 0.9);
            var ext = Cylinder(fd, stubStart, port, r);
            fd.Regenerate();

            PlanarFace? face = null;
            foreach (var go in ext.get_Geometry(opt))
                if (go is Solid so)
                    foreach (Face f in so.Faces)
                        if (f is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(dir) && Math.Abs((pf.Origin - port).DotProduct(dir)) < 1e-5)
                            face = pf;
            if (face is null) { warnings.Add($"{def.Name}: no se pudo crear el conector de la boca {p.Index + 1}."); continue; }

            var ce = ConnectorElement.CreatePipeConnector(fd, Autodesk.Revit.DB.Plumbing.PipeSystemType.Global, face.Reference);
            var fp = fm.AddParameter("D_" + (p.Index + 1), GroupTypeId.Mechanical, SpecTypeId.PipeSize, true);
            fm.Set(fp, 12.7 / 304.8);
            var dp = ce.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER);
            if (dp is not null) fm.AssociateElementParameterToFamilyParameter(dp, fp);
        }
    }

    private static void BuildUnit(Document fd, LatsBlockDef def, double k, double spacing, double height)
    {
        double h = height * Ft;
        if (def.HasBody)
        {
            double x0 = def.MinX * k * Ft, x1 = def.MaxX * k * Ft, y0 = def.MinY * k * Ft, y1 = def.MaxY * k * Ft, z = -h / 2;
            var pl = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
            var sp = SketchPlane.Create(fd, pl);
            var ca = new CurveArray();
            ca.Append(Line.CreateBound(new XYZ(x0, y0, z), new XYZ(x1, y0, z)));
            ca.Append(Line.CreateBound(new XYZ(x1, y0, z), new XYZ(x1, y1, z)));
            ca.Append(Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x0, y1, z)));
            ca.Append(Line.CreateBound(new XYZ(x0, y1, z), new XYZ(x0, y0, z)));
            var caa = new CurveArrArray();
            caa.Append(ca);
            fd.FamilyCreate.NewExtrusion(true, caa, sp, h);
        }
        // Muñones de cada boca: liquido y gas, separados como las tuberias.
        foreach (var p in def.Ports)
        {
            var port = ToXyz(p.Local, k);
            var dir = new XYZ(p.Out.X, p.Out.Y, 0).Normalize();
            var canon = p.Out.X < -1e-9 || (Math.Abs(p.Out.X) <= 1e-9 && p.Out.Y < 0) ? -dir : dir;
            var side = new XYZ(-canon.Y, canon.X, 0);
            foreach (double s in new[] { -spacing / 2, spacing / 2 })
            {
                var end = port + side * (s * Ft);
                Cylinder(fd, end - dir * (0.04 * Ft), end, 6 / 304.8);
            }
        }
    }

    private static XYZ ToXyz(P2 p, double k) => new(p.X * k * Ft, p.Y * k * Ft, 0);

    private static Extrusion Cylinder(Document fd, XYZ a, XYZ b, double r)
    {
        var d = (b - a).Normalize();
        double len = (b - a).GetLength();
        var pl = Plane.CreateByNormalAndOrigin(d, a);
        var sp = SketchPlane.Create(fd, pl);
        var ca = new CurveArray();
        ca.Append(Arc.Create(pl, r, 0, Math.PI));
        ca.Append(Arc.Create(pl, r, Math.PI, 2 * Math.PI));
        var caa = new CurveArrArray();
        caa.Append(ca);
        return fd.FamilyCreate.NewExtrusion(true, caa, sp, len);
    }

    private static string? FindTemplate(Application app)
    {
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(app.FamilyTemplatePath) && Directory.Exists(app.FamilyTemplatePath))
        {
            roots.Add(app.FamilyTemplatePath);
            var parent = Directory.GetParent(app.FamilyTemplatePath)?.FullName;
            if (parent is not null) roots.Add(parent);
        }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "RVT " + app.VersionNumber, "Family Templates"));

        foreach (var root in roots.Where(Directory.Exists))
            foreach (var name in TemplateNames)
            {
                var hit = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null) return hit;
            }
        return null;
    }

    private sealed class OverwriteLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }
}
