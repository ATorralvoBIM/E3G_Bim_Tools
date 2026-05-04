// ============================================================================
//  ExportarBc3Command.cs
//  Pegamento entre el panel Saneamiento, el motor y el writer BC3.
//
//  Multi-catálogo: la plantilla se carga con todos sus catálogos. Las reglas
//  ya saben de qué catálogo viene su partida (PartidaCatalogoId), así que el
//  motor y el writer no necesitan tocarse.
//
//  El comando ahora EXPONE públicamente el método CalcularMediciones()
//  separado de Exportar(), para que el panel Saneamiento pueda invocar el
//  cálculo, mostrar el drill-down al usuario, y luego exportar (con las
//  exclusiones puntuales aplicadas).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using E3G.Saneamiento.Presupuesto.Bc3;
using E3G.Saneamiento.Presupuesto.Modelo;
using E3G.Saneamiento.Presupuesto.Motor;
using E3G.Saneamiento.Presupuesto.Storage;
using Microsoft.Win32;

namespace E3G.Saneamiento.Presupuesto.Commands
{
    public sealed class ExportarBc3Command
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private readonly ClasificacionSistemasService _clasif;

        public ExportarBc3Command(UIApplication uiApp, ClasificacionSistemasService clasif)
        {
            _uiDoc = uiApp.ActiveUIDocument
                  ?? throw new InvalidOperationException("Cap projecte actiu a Revit.");
            _doc = _uiDoc.Document;
            _clasif = clasif ?? throw new ArgumentNullException(nameof(clasif));
        }

        /// <summary>
        /// Helper: muestra un MessageBox tolerante a window=null. Las Page hosted
        /// en DockablePane suelen devolver null en Window.GetWindow(this).
        /// </summary>
        private static MessageBoxResult Msg(Window owner, string text, string caption,
            MessageBoxButton btn, MessageBoxImage icon)
        {
            return owner != null
                ? MessageBox.Show(owner, text, caption, btn, icon)
                : MessageBox.Show(text, caption, btn, icon);
        }

        /// <summary>
        /// Calcula las mediciones aplicando reglas + drill-down. NO escribe BC3.
        /// Útil para que el panel Saneamiento muestre la previa antes de exportar.
        /// Devuelve null si hay error de configuración (catálogo o reglas vacías).
        /// </summary>
        public ResultadoCalculo CalcularMediciones(Window ventanaPadre = null)
        {
            // Prioridad: caché en memoria (si l'usuari ha modificat coses a Ajustes
            // durant aquesta sessió) → fallback al .rvt → fallback plantilla buida.
            var plantilla = PartidasSession.PlantillaActual
                            ?? PartidasStorage.Cargar(_doc);

            if (plantilla.Reglas.Count == 0)
            {
                Msg(ventanaPadre,
                    "Encara no hi ha cap regla configurada.\n\n" +
                    "Vés a Ajustes › Partides BC3 i defineix com a mínim una regla per categoria.",
                    "Cap regla configurada",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            if (!plantilla.TieneCatalogos)
            {
                Msg(ventanaPadre,
                    "Cap catàleg de partides carregat.\n\n" +
                    "Vés a Ajustes › Partides BC3 i importa com a mínim un BC3.",
                    "Cap catàleg carregat",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var warnings = new List<string>();
            try
            {
                var motor = new EvaluadorReglas(_clasif);
                var mediciones = motor.Evaluar(plantilla, _doc, warnings);
                return new ResultadoCalculo
                {
                    Plantilla = plantilla,
                    Mediciones = mediciones.ToList(),
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExportarBc3Command] Motor falló: {ex}");
                Msg(ventanaPadre,
                    $"Error en evaluar les regles:\n\n{ex.Message}",
                    "Error d'execució", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Escribe el BC3 a partir de las mediciones ya calculadas (con sus
        /// exclusiones aplicadas vía LineaMedicion.Excluida). El usuario puede
        /// haber pasado por la UI antes y excluido líneas concretas.
        /// </summary>
        public bool Exportar(
            ResultadoCalculo resultado,
            string rutaSalida = null,
            Window ventanaPadre = null)
        {
            if (resultado == null) return false;

            // Si no se pasa ruta, pedirla.
            if (string.IsNullOrWhiteSpace(rutaSalida))
            {
                var sfd = new SaveFileDialog
                {
                    Filter = "Arxiu BC3 FIEBDC-3 (*.bc3)|*.bc3|Tots els arxius (*.*)|*.*",
                    FileName = ConstruirNombreSugerido(_doc),
                    DefaultExt = ".bc3",
                    AddExtension = true
                };
                bool? sfdOk = ventanaPadre != null ? sfd.ShowDialog(ventanaPadre) : sfd.ShowDialog();
                if (sfdOk != true) return false;
                rutaSalida = sfd.FileName;
            }

            // Ya hay drill-down → si todas las líneas están excluidas, abortar.
            var mediciones = resultado.Mediciones
                .Where(m => m.LineasParaExportar.Any())
                .ToList();
            if (mediciones.Count == 0)
            {
                Msg(ventanaPadre,
                    "Totes les línies han estat excloses. No es generarà cap arxiu.",
                    "Sense mediciones", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            try
            {
                var writer = new Bc3Writer();
                var cabecera = new DatosCabecera
                {
                    NombrePrograma = "E3G_BIM_TOOLS",
                    Empresa = "E3G",
                    Version = "1.0",
                    Fecha = DateTime.Now,
                    ProyectoNombre = ProyectoNombre(_doc),
                    CodigoRaiz = "##"
                };
                writer.Escribir(rutaSalida, resultado.Plantilla, mediciones, cabecera);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExportarBc3Command] Writer falló: {ex}");
                Msg(ventanaPadre,
                    $"Error en escriure el BC3:\n\n{ex.Message}",
                    "Error d'escriptura", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // Resumen.
            var totalImporte = mediciones.Sum(m =>
                m.CantidadParaExportar * (m.Partida?.Precio ?? 0m));
            var lineasExportadas = mediciones.Sum(m => m.LineasParaExportar.Count());
            var lineasExcluidas = resultado.Mediciones.Sum(m => m.Lineas.Count(l => l.Excluida));

            var detalleWarnings = resultado.Warnings.Count == 0 ? "" :
                "\n\nAvisos durant l'execució:\n" +
                string.Join("\n", resultado.Warnings.Take(5).Select(w => "  · " + w)) +
                (resultado.Warnings.Count > 5 ? $"\n  … i {resultado.Warnings.Count - 5} més" : "");

            var detalleExclusions = lineasExcluidas == 0 ? "" :
                $"\n  Línies excloses: {lineasExcluidas} (per drill-down de l'usuari)";

            Msg(ventanaPadre,
                $"BC3 generat correctament:\n\n" +
                $"  Arxiu:        {Path.GetFileName(rutaSalida)}\n" +
                $"  Partides:     {mediciones.Count}\n" +
                $"  Línies medic.: {lineasExportadas}" + detalleExclusions + "\n" +
                $"  Import total: {totalImporte:0.00} €" +
                detalleWarnings,
                "Exportació completada",
                MessageBoxButton.OK,
                resultado.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return true;
        }

        /// <summary>
        /// Flux estàndard: calcular → previsualitzar → confirmar → escriure BC3.
        /// </summary>
        public bool Ejecutar(Window ventanaPadre = null)
        {
            var resultado = CalcularMediciones(ventanaPadre);
            if (resultado == null) return false;

            // Pas nou: mostrar previsualització abans d'escriure res al disc.
            // L'usuari veu què es generarà i confirma o cancel·la.
            var preview = new E3G.Saneamiento.Presupuesto.Ui.PreviewExportacionDialog(resultado);
            if (ventanaPadre != null) preview.Owner = ventanaPadre;
            var confirmat = preview.ShowDialog();
            if (confirmat != true) return false;

            // Si l'usuari ha confirmat, llavors sí obrim el SaveFileDialog i escrivim.
            return Exportar(resultado, null, ventanaPadre);
        }

        // ─── Helpers ────────────────────────────────────────────────────────
        private static string ConstruirNombreSugerido(Document doc)
        {
            var nombre = ProyectoNombre(doc);
            foreach (var c in Path.GetInvalidFileNameChars())
                nombre = nombre.Replace(c, '_');
            return $"{nombre}_{DateTime.Now:yyyyMMdd}.bc3";
        }

        private static string ProyectoNombre(Document doc)
        {
            try
            {
                var info = doc.ProjectInformation;
                var n = info?.Name;
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
            catch { }
            var title = doc.Title;
            return string.IsNullOrEmpty(title) ? "PRESUPUESTO" : title;
        }
    }

    /// <summary>
    /// Resultado del cálculo, listo para drill-down y exportación posterior.
    /// </summary>
    public sealed class ResultadoCalculo
    {
        public Plantilla Plantilla { get; init; }
        public List<MedicionPorPartida> Mediciones { get; init; }
        public List<string> Warnings { get; init; }
    }
}
