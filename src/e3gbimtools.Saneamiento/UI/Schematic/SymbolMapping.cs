// ============================================================================
//  SymbolMapping.cs
//  Una fila del DataGrid de mapeo Categoría/Familia 3D → Símbolo 2D (.rfa).
//
//  Kind:
//    Identificador semántico del elemento en nuestro modelo de cálculo
//    (TEE, ELBOW, CROSS, TERMINAL, REDUCER, FIN, PIPE, FUENTE).  Vendrá
//    poblado al abrir el panel a partir de los tipos presentes en la red.
//
//  SymbolPath:
//    Ruta absoluta al archivo .rfa que se cargará al generar.  Si está
//    vacía, la generación cae a la primitiva interna (símbolo dibujado con
//    DetailCurve / FilledRegion).
// ============================================================================
using System.IO;
using E3G.Saneamiento.UI.Mvvm;

namespace E3G.Saneamiento.UI.Schematic
{
    public sealed class SymbolMapping : BindableBase
    {
        private string _kind;
        public string Kind
        {
            get => _kind;
            set => Set(ref _kind, value);
        }

        private string _description;
        public string Description
        {
            get => _description;
            set => Set(ref _description, value);
        }

        private string _symbolPath;
        public string SymbolPath
        {
            get => _symbolPath;
            set
            {
                if (Set(ref _symbolPath, value))
                    Raise(nameof(SymbolDisplay));
            }
        }

        /// <summary>Nombre legible para el DataGrid (filename o "—").</summary>
        public string SymbolDisplay =>
            string.IsNullOrWhiteSpace(_symbolPath)
                ? "— sin asignar —"
                : Path.GetFileNameWithoutExtension(_symbolPath);

        private bool _visible = true;
        public bool Visible
        {
            get => _visible;
            set => Set(ref _visible, value);
        }
    }
}
