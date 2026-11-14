// ============================================================================
//  ISchematicLayoutService.cs
//  Envoltura fina sobre Core.Schematic.SchematicLayoutEngine. Construye el
//  layout 2D del esquema MEP para pintarlo en el Canvas de CalcWindow.
// ============================================================================

using System.Collections.Generic;
using e3gbimtools.Calc.Core.Common;
using e3gbimtools.Calc.Core.Schematic;

namespace e3gbimtools.Calc.Abstractions
{
    public interface ISchematicLayoutService
    {
        SchematicLayout Build(
            IList<ResultRow> rows,
            IReadOnlyDictionary<ResultRow, int> columnOf,
            SchematicParameters parameters,
            ISet<ResultRow> criticalPathRows = null,
            SchematicLegend legend = null);
    }
}
