// Marker reemplazado por ProjectInfo.cs en el Bloque 2.
// Se conserva un tipo dummy para no romper SanityTests.cs hasta que migremos
// los tests reales en Sprint 2.

// Permite que el proyecto de tests acceda a los tipos internal del Core
// (necesario para TableGenReaderTests y otros tests de caja blanca).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("e3gbimtools.DiTools.Tests")]

namespace E3G.BimTools.DiTools.Core;

internal static class AssemblyMarker
{
    public const string Name = "e3gbimtools.DiTools.Core";
}
