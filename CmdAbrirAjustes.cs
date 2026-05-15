// ============================================================================
//  CmdAbrirAjustes.cs
//  Comando Revit que obre la finestra d'ajustos E3G BIM Tools.
//
//  El botó apareix al ribbon de Revit, al tab "E3G BIM Tools",
//  juntament amb la resta de botons del panell.
//
//  AjustesWindow és un Page — l'hostatgem en un Window lleuger.
//  El canvi d'idioma és immediat (LanguageService + ResourceDictionary swap).
// ============================================================================
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Localization;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Interop;
using WpfFrame = System.Windows.Controls.Frame;

namespace E3G.Saneamiento
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdAbrirAjustes : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;

                // Inicialitzar LanguageService (idempotent si ja s'ha cridat).
                LanguageService.Init();

                var ajustesPage = new AjustesWindow(uiApp);

                var dlg = new Window
                {
                    Title             = GetTitle(),
                    Width             = 860,
                    Height            = 700,
                    MinWidth          = 700,
                    MinHeight         = 520,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar     = false,
                    ResizeMode        = ResizeMode.CanResizeWithGrip,
                    Background        = System.Windows.Media.Brushes.White,
                };

                // Actualitzar el títol si l'usuari canvia l'idioma des d'Ajustos.
                LanguageService.LanguageChanged += () =>
                {
                    dlg.Dispatcher.Invoke(() => dlg.Title = GetTitle());
                };

                // Parentar al handle principal de Revit.
                IntPtr revitHandle = uiApp.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                    new WindowInteropHelper(dlg) { Owner = revitHandle };

                var frame = new WpfFrame
                {
                    NavigationUIVisibility = NavigationUIVisibility.Hidden,
                    Content = ajustesPage,
                };
                dlg.Content = frame;
                dlg.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("E3G Ajustos", "Error en obrir Ajustos:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static string GetTitle()
        {
            try
            {
                return System.Windows.Application.Current?.Resources["Str.Dlg.AjustesTitle"] as string
                       ?? "Ajustos — E3G BIM Tools";
            }
            catch { return "Ajustos — E3G BIM Tools"; }
        }
    }
}
