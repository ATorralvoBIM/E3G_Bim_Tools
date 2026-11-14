using Autodesk.Revit.DB;
using E3G.BimTools.DiTools.Core.Models.Families;
using FluentAssertions;
using Xunit;

namespace E3G.BimTools.DiTools.Tests.Families;

/// <summary>
/// Tests de la validación de <see cref="WorksetAssignRequest"/>
/// sin tocar Revit (lógica pura).
/// </summary>
public sealed class WorksetAssignRequestTests
{
    [Fact]
    public void Request_invalido_sin_workset_destino()
    {
        var req = new WorksetAssignRequest
        {
            // TargetWorksetId no asignado → queda en InvalidWorksetId
        };

        req.IsValid(out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Request_valido_con_workset_destino()
    {
        var req = new WorksetAssignRequest
        {
            TargetWorksetId = new WorksetId(1),
        };

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void Request_con_elementids_y_workset_es_valido()
    {
        var req = new WorksetAssignRequest
        {
            TargetWorksetId = new WorksetId(2),
            ElementIds       = new[] { new ElementId(100), new ElementId(200) },
        };

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void Request_con_categorias_y_workset_es_valido()
    {
        var req = new WorksetAssignRequest
        {
            TargetWorksetId = new WorksetId(3),
            CategoryFilter   = new[] { BuiltInCategory.OST_Doors },
        };

        req.IsValid(out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void Request_include_imports_por_defecto_es_false()
    {
        var req = new WorksetAssignRequest { TargetWorksetId = new WorksetId(4) };

        req.IncludeImportInstances.Should().BeFalse();
        req.IsValid(out _).Should().BeTrue();
    }

    // ================================================================== //
    //  WorksetAuditResult — ToSummary                                      //
    // ================================================================== //

    [Fact]
    public void WorksetAuditResult_limpio_da_mensaje_correcto()
    {
        var result = new WorksetAuditResult();
        result.IsClean.Should().BeTrue();
        result.ToSummary().Should().Contain("limpio");
    }

    [Fact]
    public void WorksetAuditResult_con_vacios_no_es_limpio()
    {
        var result = new WorksetAuditResult();
        result.EmptyWorksets.Add("WS_Vacío");

        result.IsClean.Should().BeTrue();     // IsClean solo mira Inconsistencias
        result.ToSummary().Should().Contain("vacío");
    }

    [Fact]
    public void WorksetAuditResult_con_inconsistencias_no_es_limpio()
    {
        var result = new WorksetAuditResult();
        result.Inconsistencies.Add((999, "WS_Actual", "WS_Esperado"));

        result.IsClean.Should().BeFalse();
        result.ToSummary().Should().Contain("inconsistencia");
    }

    // ================================================================== //
    //  WorksetAssignResult — ToSummary                                     //
    // ================================================================== //

    [Fact]
    public void WorksetAssignResult_sin_errores()
    {
        var result = new WorksetAssignResult { AssignedCount = 5 };
        result.HasErrors.Should().BeFalse();
        result.ToSummary().Should().Contain("5");
    }

    [Fact]
    public void WorksetAssignResult_con_errores()
    {
        var result = new WorksetAssignResult { AssignedCount = 3 };
        result.Errors.Add((101, "Elemento no encontrado."));

        result.HasErrors.Should().BeTrue();
        result.ToSummary().Should().Contain("Error");
    }

    // ================================================================== //
    //  WorksetBatchCreateResult — Plan (bloque A)                          //
    // ================================================================== //

    [Fact]
    public void BatchPlan_normaliza_trim_y_descarta_vacios()
    {
        var result = new WorksetBatchCreateResult();

        var toCreate = result.Plan(
            new[] { "  Estructura ", "", "   ", "MEP" },
            existingNames: System.Array.Empty<string>());

        toCreate.Should().Equal("Estructura", "MEP");
    }

    [Fact]
    public void BatchPlan_detecta_duplicados_en_la_lista_sin_case()
    {
        var result = new WorksetBatchCreateResult();

        var toCreate = result.Plan(
            new[] { "Estructura", "ESTRUCTURA", "MEP" },
            existingNames: System.Array.Empty<string>());

        toCreate.Should().Equal("Estructura", "MEP");
        result.DuplicateInInput.Should().ContainSingle().Which.Should().Be("ESTRUCTURA");
    }

    [Fact]
    public void BatchPlan_descarta_los_que_ya_existen_sin_case()
    {
        var result = new WorksetBatchCreateResult();

        var toCreate = result.Plan(
            new[] { "Arquitectura", "estructura", "MEP" },
            existingNames: new[] { "Estructura" });

        toCreate.Should().Equal("Arquitectura", "MEP");
        result.AlreadyExisting.Should().ContainSingle().Which.Should().Be("estructura");
    }

    [Fact]
    public void BatchPlan_resumen_refleja_los_recuentos()
    {
        var result = new WorksetBatchCreateResult();
        result.Plan(
            new[] { "A", "A", "B" },
            existingNames: new[] { "B" });
        result.Created.Add("A"); // simula creación de los planificados

        var summary = result.ToSummary();
        summary.Should().Contain("Ya existían: 1");
        summary.Should().Contain("Duplicados en lista: 1");
    }

    // ================================================================== //
    //  WorksetRenameResult — Plan (bloque B)                               //
    // ================================================================== //

    [Fact]
    public void RenamePlan_descarta_nombre_propuesto_vacio()
    {
        var result = new WorksetRenameResult();

        var valid = result.Plan(
            new[] { ("Estructura", "   ") },
            allCurrentNames: new[] { "Estructura", "MEP" });

        valid.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("vacío");
    }

    [Fact]
    public void RenamePlan_descarta_colision_con_workset_que_se_queda()
    {
        var result = new WorksetRenameResult();

        var valid = result.Plan(
            new[] { ("Estructura", "MEP") },
            allCurrentNames: new[] { "Estructura", "MEP" });

        valid.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("Ya existe");
    }

    [Fact]
    public void RenamePlan_descarta_duplicados_dentro_del_lote()
    {
        var result = new WorksetRenameResult();

        var valid = result.Plan(
            new[] { ("A", "Comun"), ("B", "comun") },
            allCurrentNames: new[] { "A", "B" });

        valid.Should().ContainSingle().Which.Should().Be(("A", "Comun"));
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("duplicado");
    }

    [Fact]
    public void RenamePlan_permite_intercambio_cuando_ambos_se_renombran()
    {
        var result = new WorksetRenameResult();

        // A→B y B→A: ninguno de los dos nombres "se queda", así que no hay colisión.
        var valid = result.Plan(
            new[] { ("A", "B"), ("B", "A") },
            allCurrentNames: new[] { "A", "B" });

        valid.Should().HaveCount(2);
        result.Skipped.Should().BeEmpty();
    }

    // ================================================================== //
    //  WorksetLinkOrganizeResult — SanitizeWorksetName (bloque D)          //
    // ================================================================== //

    [Fact]
    public void Sanitize_quita_sufijo_de_posicion_de_instancia()
    {
        WorksetLinkOrganizeResult.SanitizeWorksetName("", "Edificio_A.rvt : 2")
            .Should().Be("Edificio_A.rvt");
    }

    [Fact]
    public void Sanitize_reemplaza_caracteres_invalidos_y_antepone_prefijo()
    {
        WorksetLinkOrganizeResult.SanitizeWorksetName("RVT_", "Plano{1}:rev")
            .Should().Be("RVT_Plano_1__rev");
    }

    [Fact]
    public void Sanitize_nombre_vacio_devuelve_Link()
    {
        WorksetLinkOrganizeResult.SanitizeWorksetName("", "   ")
            .Should().Be("Link");
    }

    [Fact]
    public void Sanitize_recorta_a_la_longitud_maxima()
    {
        var nombre = WorksetLinkOrganizeResult.SanitizeWorksetName("", new string('x', 200), maxLength: 50);
        nombre.Length.Should().Be(50);
    }

    // ================================================================== //
    //  WorksetVisibilityResult — ToSummary (bloque E)                      //
    // ================================================================== //

    [Fact]
    public void VisibilityResult_resume_vistas_y_cambios()
    {
        var result = new WorksetVisibilityResult { ViewsAffected = 4, ChangedCount = 7 };

        var summary = result.ToSummary();
        summary.Should().Contain("Vistas afectadas: 4");
        summary.Should().Contain("Cambios: 7");
    }

    [Fact]
    public void VisibilityResult_incluye_no_soportadas_y_errores()
    {
        var result = new WorksetVisibilityResult { SkippedViews = 2 };
        result.Errors.Add(("Plano 1", "fallo"));

        result.HasErrors.Should().BeTrue();
        var summary = result.ToSummary();
        summary.Should().Contain("Vistas no soportadas: 2");
        summary.Should().Contain("Errores: 1");
    }

    // ================================================================== //
    //  WorksetVisibilityReport — ToReportText (bloque F)                   //
    // ================================================================== //

    [Fact]
    public void Report_sin_vistas_da_mensaje_explicito()
    {
        var report = new WorksetVisibilityReport();
        report.ToReportText().Should().Contain("ninguna vista compatible");
    }

    [Fact]
    public void Report_lista_recuentos_por_workset()
    {
        var report = new WorksetVisibilityReport
        {
            ViewsInspected = 3,
            Rows = new[]
            {
                new WorksetVisibilityReportRow { WorksetName = "Estructura", VisibleCount = 2, HiddenCount = 1 },
            },
        };

        var text = report.ToReportText();
        text.Should().Contain("Vistas inspeccionadas: 3");
        text.Should().Contain("Estructura: visible 2, oculto 1, global 0");
    }

    // ================================================================== //
    //  WorksetViewCreateResult — MakeUniqueViewName (vistas 3D por workset) //
    // ================================================================== //

    [Fact]
    public void UniqueViewName_devuelve_el_base_si_no_existe()
    {
        var existing = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        WorksetViewCreateResult.MakeUniqueViewName("WS - Estructura", existing)
            .Should().Be("WS - Estructura");
        existing.Should().Contain("WS - Estructura");
    }

    [Fact]
    public void UniqueViewName_añade_sufijo_numerico_si_colisiona()
    {
        var existing = new System.Collections.Generic.HashSet<string>(
            new[] { "WS - MEP" }, System.StringComparer.OrdinalIgnoreCase);

        WorksetViewCreateResult.MakeUniqueViewName("WS - MEP", existing).Should().Be("WS - MEP (2)");
        WorksetViewCreateResult.MakeUniqueViewName("WS - MEP", existing).Should().Be("WS - MEP (3)");
    }

    [Fact]
    public void UniqueViewName_colision_es_insensible_a_mayusculas()
    {
        var existing = new System.Collections.Generic.HashSet<string>(
            new[] { "WS - mep" }, System.StringComparer.OrdinalIgnoreCase);

        WorksetViewCreateResult.MakeUniqueViewName("WS - MEP", existing).Should().Be("WS - MEP (2)");
    }

    [Fact]
    public void UniqueViewName_base_vacio_usa_Vista()
    {
        var existing = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        WorksetViewCreateResult.MakeUniqueViewName("   ", existing).Should().Be("Vista");
    }

    [Fact]
    public void ViewCreateResult_resumen_refleja_recuentos()
    {
        var result = new WorksetViewCreateResult();
        result.Created.Add(("Estructura", "WS - Estructura"));
        result.Errors.Add(("MEP", "fallo"));

        result.HasErrors.Should().BeTrue();
        var summary = result.ToSummary();
        summary.Should().Contain("Vistas 3D creadas: 1");
        summary.Should().Contain("Errores: 1");
    }
}
