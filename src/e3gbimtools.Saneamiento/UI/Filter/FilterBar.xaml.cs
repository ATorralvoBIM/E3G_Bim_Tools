// ============================================================================
//  FilterBar.xaml.cs  —  Code-behind mínimo del UserControl.
//
//  El UserControl es stateless: toda la lógica vive en FilterBarViewModel.
//  Este code-behind solo inicializa el DataContext cuando el ViewModel se
//  asigna desde fuera (via la propiedad ViewModel).
// ============================================================================
using System.Windows.Controls;

namespace E3G.Saneamiento.UI.Filter
{
    public partial class FilterBar : UserControl
    {
        public FilterBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Asigna el ViewModel y conecta el DataContext.
        /// Llamar desde el constructor de CalcWindow justo después de
        /// crear el FilterBarViewModel.
        /// </summary>
        public void SetViewModel(FilterBarViewModel vm)
        {
            DataContext = vm;
        }
    }
}
