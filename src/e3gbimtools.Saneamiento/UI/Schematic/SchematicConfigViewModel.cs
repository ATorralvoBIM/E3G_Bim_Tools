// ============================================================================
//  SchematicConfigViewModel.cs
//  ViewModel del panel de configuración del generador de esquemas.
//  Expone:
//    - Collection observable de mapeos categoría → .rfa
//    - Parámetros del layout (separación líneas, altura texto, tamaño símbolo)
//    - Flags de visibilidad por tipo
//    - Comandos para cargar familias, resetear, aplicar, generar
//
//  Eventos hacia el host (ventana que contiene este panel):
//    PreviewRequested  → la ventana host debe redibujar el preview
//    GenerateRequested → la ventana host debe lanzar la generación en Revit
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using E3G.Saneamiento.UI.Mvvm;
using ResultRow = E3G.Saneamiento.CalcWindow.ResultRow;

namespace E3G.Saneamiento.UI.Schematic
{
    public sealed class SchematicConfigViewModel : BindableBase
    {
        public ObservableCollection<SymbolMapping> Mappings { get; }
            = new ObservableCollection<SymbolMapping>();

        // ── Parámetros del layout (todo en mm de PAPEL) ────────────────────

        private double _paperMmLineSpacing = 8.0;
        public double PaperMmLineSpacing
        {
            get => _paperMmLineSpacing;
            set { if (Set(ref _paperMmLineSpacing, value)) NotifyPreview(); }
        }

        private double _paperMmColumnIndent = 25.0;
        public double PaperMmColumnIndent
        {
            get => _paperMmColumnIndent;
            set { if (Set(ref _paperMmColumnIndent, value)) NotifyPreview(); }
        }

        private double _paperMmTextHeight = 2.5;
        public double PaperMmTextHeight
        {
            get => _paperMmTextHeight;
            set { if (Set(ref _paperMmTextHeight, value)) NotifyPreview(); }
        }

        private double _paperMmSymbolSize = 3.5;
        public double PaperMmSymbolSize
        {
            get => _paperMmSymbolSize;
            set { if (Set(ref _paperMmSymbolSize, value)) NotifyPreview(); }
        }

        // ── Filtros de visibilidad ────────────────────────────────────────

        private bool _showPipeLabels = true;
        public bool ShowPipeLabels
        {
            get => _showPipeLabels;
            set { if (Set(ref _showPipeLabels, value)) NotifyPreview(); }
        }

        private bool _showFittingLabels = true;
        public bool ShowFittingLabels
        {
            get => _showFittingLabels;
            set { if (Set(ref _showFittingLabels, value)) NotifyPreview(); }
        }

        private bool _showValves = true;
        public bool ShowValves
        {
            get => _showValves;
            set { if (Set(ref _showValves, value)) NotifyPreview(); }
        }

        private bool _showMeters = true;
        public bool ShowMeters
        {
            get => _showMeters;
            set { if (Set(ref _showMeters, value)) NotifyPreview(); }
        }

        private bool _showSensors = true;
        public bool ShowSensors
        {
            get => _showSensors;
            set { if (Set(ref _showSensors, value)) NotifyPreview(); }
        }

        private bool _showLegend = true;
        public bool ShowLegend
        {
            get => _showLegend;
            set { if (Set(ref _showLegend, value)) NotifyPreview(); }
        }

        private bool _highlightCriticalPath = true;
        public bool HighlightCriticalPath
        {
            get => _highlightCriticalPath;
            set { if (Set(ref _highlightCriticalPath, value)) NotifyPreview(); }
        }

        // ── Comandos ──────────────────────────────────────────────────────

        public RelayCommand LoadFamilyForRowCommand { get; }
        public RelayCommand LoadLibraryFilesCommand { get; }
        public RelayCommand LoadLibraryFolderCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand PreviewCommand { get; }
        public RelayCommand GenerateCommand { get; }

        // ── Validator (opcional) ──────────────────────────────────────────

        private readonly IFamilyValidator _validator;

        // ── Eventos hacia el host ─────────────────────────────────────────

        /// <summary>El layout debe redibujarse (parámetro cambió).</summary>
        public event EventHandler PreviewRequested;

        /// <summary>El usuario pulsó "Generar en Revit".</summary>
        public event EventHandler GenerateRequested;

        /// <summary>Mensaje informativo o de aviso que el host debe mostrar
        /// (validaciones de familia, archivos descartados, etc.).</summary>
        public event EventHandler<FamilyWarningEventArgs> WarningRaised;

        public SchematicConfigViewModel() : this(null) { }

        public SchematicConfigViewModel(IFamilyValidator validator)
        {
            _validator = validator;

            LoadFamilyForRowCommand   = new RelayCommand(p => OpenFamilyDialog(p as SymbolMapping));
            LoadLibraryFilesCommand   = new RelayCommand(_ => LoadLibraryFromFiles());
            LoadLibraryFolderCommand  = new RelayCommand(_ => LoadLibraryFromFolder());
            ResetCommand              = new RelayCommand(_ => ResetToDefaults());
            PreviewCommand            = new RelayCommand(_ => NotifyPreview());
            GenerateCommand           = new RelayCommand(
                _ => GenerateRequested?.Invoke(this, EventArgs.Empty));
        }

        // ── Acciones internas ────────────────────────────────────────────

        private void OpenFamilyDialog(SymbolMapping row)
        {
            if (row == null) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Familias Revit (*.rfa)|*.rfa",
                Title = "Seleccionar símbolo 2D para " + row.Kind,
            };
            if (dlg.ShowDialog() != true) return;

            var v = ValidateOrNotify(dlg.FileName);
            if (!v.IsAcceptable) return;   // ya se notificó el problema

            row.SymbolPath = dlg.FileName;
            NotifyPreview();
        }

        private void LoadLibraryFromFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Familias Revit (*.rfa)|*.rfa",
                Title = "Seleccionar familias para la biblioteca",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true) return;
            ProcessLibraryFiles(dlg.FileNames);
        }

        private void LoadLibraryFromFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Seleccionar carpeta con familias .rfa",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var files = Directory.EnumerateFiles(dlg.FolderName, "*.rfa",
                    SearchOption.AllDirectories).ToArray();
                if (files.Length == 0)
                {
                    RaiseWarning("Aviso", "No se encontraron archivos .rfa en la carpeta seleccionada.", FamilyWarningSeverity.Info);
                    return;
                }
                ProcessLibraryFiles(files);
            }
            catch (Exception ex)
            {
                RaiseWarning("Error al leer la carpeta", ex.Message, FamilyWarningSeverity.Error);
            }
        }

        /// <summary>Procesa una lista de rutas: valida cada una y auto-asocia
        /// por filename a las filas de Mappings.  Acumula el balance y emite
        /// un único WarningRaised al final con el resumen + detalle por
        /// archivo (que el host puede pintar como dialog con tabla).</summary>
        private void ProcessLibraryFiles(IReadOnlyList<string> paths)
        {
            int assigned = 0, rejected = 0, deferred = 0, unknown = 0;
            var details = new List<(string Path, FamilyValidationResult Result)>();

            foreach (var path in paths)
            {
                var v = _validator != null
                    ? _validator.Validate(path)
                    : FamilyValidationResult.Deferred();
                details.Add((path, v));

                if (v.Kind == FamilyValidationKind.WrongCategory ||
                    v.Kind == FamilyValidationKind.CannotRead)
                {
                    rejected++;
                    continue;
                }
                if (v.Kind == FamilyValidationKind.Unknown) deferred++;

                var lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool matched = false;
                foreach (var m in Mappings)
                {
                    var kind = (m.Kind ?? "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(kind) && lower.Contains(kind))
                    { m.SymbolPath = path; matched = true; assigned++; break; }
                }
                if (!matched) unknown++;
            }

            NotifyPreview();

            var sev = rejected > 0 ? FamilyWarningSeverity.Warning : FamilyWarningSeverity.Info;
            var sb = new StringBuilder();
            sb.Append($"Asignadas: {assigned}");
            if (unknown > 0)  sb.Append($"  ·  Sin tipo coincidente: {unknown}");
            if (deferred > 0) sb.Append($"  ·  Validación diferida: {deferred}");
            if (rejected > 0) sb.Append($"  ·  Descartadas: {rejected}");

            WarningRaised?.Invoke(this, new FamilyWarningEventArgs(
                "Carga de biblioteca", sb.ToString(), sev, details));
        }

        private FamilyValidationResult ValidateOrNotify(string path)
        {
            var v = _validator != null
                ? _validator.Validate(path)
                : FamilyValidationResult.Deferred();
            if (!v.IsAcceptable)
                RaiseWarning("Familia rechazada", v.Message, FamilyWarningSeverity.Warning);
            return v;
        }

        private void RaiseWarning(string title, string body, FamilyWarningSeverity sev)
            => WarningRaised?.Invoke(this, new FamilyWarningEventArgs(title, body, sev));

        private void ResetToDefaults()
        {
            PaperMmLineSpacing = 8.0;
            PaperMmColumnIndent = 25.0;
            PaperMmTextHeight = 2.5;
            PaperMmSymbolSize = 3.5;
            ShowPipeLabels = true;
            ShowFittingLabels = true;
            ShowValves = true;
            ShowMeters = true;
            ShowSensors = true;
            ShowLegend = true;
            HighlightCriticalPath = true;
        }

        private void NotifyPreview()
            => PreviewRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Rellena la tabla con los tipos por defecto cuando no se ha
        /// detectado nada concreto desde Revit todavía.
        /// </summary>
        public void SeedDefaultMappings()
        {
            Mappings.Clear();
            foreach (var k in DefaultKinds)
                Mappings.Add(new SymbolMapping { Kind = k, Description = DescriptionFor(k) });
        }

        /// <summary>
        /// Pueble la tabla a partir de los tipos REALMENTE presentes en el
        /// cálculo.  Mantiene un orden estable (definido por DefaultKinds) y
        /// añade al final cualquier tipo desconocido que aparezca en las rows.
        /// Las filas no presentes en el cálculo no se añaden — así la tabla
        /// solo muestra lo que el usuario va a poder asignar.
        ///
        /// LIMITACIÓN — disciplina PIPE vs DUCT:
        ///   El motor actual (MakeRowFromPipe / MakeRowFromDuct en CalcWindow)
        ///   no marca la disciplina en ElementKind ni en EndNodeKind.  Los
        ///   accesorios topológicos (TEE, ELBOW, REDUCER, TERMINAL, FIN,
        ///   CROSS) se etiquetan igual para tubería y conducto.  Si un
        ///   cálculo mezcla ambas disciplinas (caso poco habitual), el .rfa
        ///   asignado a una fila se usará para los nodos de las dos.
        ///   Recomendación de uso: generar un esquema por disciplina.
        /// </summary>
        public void SeedFromRows(IEnumerable<ResultRow> rows)
        {
            if (rows == null) { SeedDefaultMappings(); return; }

            // Conjunto de tipos presentes: ElementKind (para PIPE / TEE / ELBOW…)
            // + EndNodeKind cuando el accesorio termine en TERMINAL/FIN/JUNCTION,
            // para que el usuario también pueda mapear esos terminadores.
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasSource = false;
            foreach (var r in rows)
            {
                if (r == null) continue;
                if (!string.IsNullOrWhiteSpace(r.ElementKind))
                    present.Add(r.ElementKind.Trim().ToUpperInvariant());
                if (!string.IsNullOrWhiteSpace(r.EndNodeKind))
                    present.Add(r.EndNodeKind.Trim().ToUpperInvariant());
                if (r.IsSource) hasSource = true;
            }
            if (hasSource) present.Add("FUENTE");

            // JUNCTION no se mapea — es un nodo lógico sin símbolo propio.
            present.Remove("JUNCTION");

            Mappings.Clear();
            // Primero, los conocidos en su orden canónico.
            foreach (var k in DefaultKinds)
                if (present.Contains(k))
                    Mappings.Add(new SymbolMapping { Kind = k, Description = DescriptionFor(k) });
            // Después, cualquier otro tipo desconocido detectado.
            foreach (var k in present.OrderBy(s => s, StringComparer.Ordinal))
                if (!DefaultKinds.Contains(k))
                    Mappings.Add(new SymbolMapping { Kind = k, Description = DescriptionFor(k) });

            // Salvaguarda: si por algún motivo no se detectó ningún tipo,
            // caemos al juego por defecto para que el panel no aparezca vacío.
            if (Mappings.Count == 0) SeedDefaultMappings();
        }

        /// <summary>Volcado de los mapeos actuales en un diccionario
        /// Kind→ruta-rfa, descartando los no asignados.  Lo usa el generador
        /// para resolver qué familia cargar para cada categoría.</summary>
        public IReadOnlyDictionary<string, string> CollectSymbolPaths()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Mappings)
                if (!string.IsNullOrWhiteSpace(m.SymbolPath) && !string.IsNullOrWhiteSpace(m.Kind))
                    dict[m.Kind] = m.SymbolPath;
            return dict;
        }

        // ── Catálogo interno ──────────────────────────────────────────────

        private static readonly string[] DefaultKinds =
        {
            "PIPE", "TEE", "CROSS", "ELBOW", "REDUCER",
            "TERMINAL", "FIN", "FUENTE",
            "VALVE", "METER", "SENSOR",
        };

        private static string DescriptionFor(string kind)
        {
            switch ((kind ?? "").ToUpperInvariant())
            {
                case "PIPE":     return "Tubería / Conducto";
                case "TEE":      return "Bifurcación a 3 vías";
                case "CROSS":    return "Bifurcación a 4 vías";
                case "ELBOW":    return "Codo";
                case "REDUCER":  return "Reductor de sección";
                case "TERMINAL": return "Aparato / Punto de consumo";
                case "FIN":      return "Tapón / Cap";
                case "FUENTE":   return "Acometida / Origen";
                case "VALVE":    return "Válvula";
                case "METER":    return "Contador";
                case "SENSOR":   return "Sensor";
                default:         return "(tipo detectado en el cálculo)";
            }
        }
    }
}
