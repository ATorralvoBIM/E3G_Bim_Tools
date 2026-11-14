// ============================================================================
//  PartidasViewModel.cs
//  ViewModel de Ajustes › Partides BC3 (multi-catálogo).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using E3G.Saneamiento.Presupuesto.Bc3;
using E3G.Saneamiento.Presupuesto.Modelo;
using E3G.Saneamiento.Presupuesto.Storage;

namespace E3G.Saneamiento.Presupuesto.Ui
{
    public sealed class PartidasViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private Plantilla _plantilla;

        public PartidasViewModel(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            // 1. Si ja hi ha una plantilla a la sessió (cacheada de canvis anteriors), agafa-la.
            // 2. Si no, intenta llegir-la del .rvt (pot estar buida si és primer cop).
            _plantilla = PartidasSession.PlantillaActual
                         ?? PartidasStorage.Cargar(_doc)
                         ?? PlantillaPorDefecto.Crear();
            PartidasSession.PlantillaActual = _plantilla;
            ReconstruirGrupos();
            ReconstruirCatalogosUi();
        }

        // ─── Lista de catálogos cargados (para Card 1) ──────────────────────
        public ObservableCollection<CatalogoUiItem> CatalogosUi { get; } =
            new ObservableCollection<CatalogoUiItem>();

        public bool HayCatalogos => _plantilla.Catalogos.Count > 0;
        public int TotalPartidasCargadas => _plantilla.TotalPartidasCargadas;

        // ─── Reglas agrupadas (Card 2) ─────────────────────────────────────
        public ObservableCollection<GrupoReglas> GruposReglas { get; } =
            new ObservableCollection<GrupoReglas>();

        public int TotalReglas => _plantilla.Reglas.Count;
        public int CategoriasConReglas =>
            _plantilla.Reglas.Where(r => r.Activa).Select(r => r.Categoria).Distinct().Count();

        // ─── Catálogos: import/eliminar/marcar ──────────────────────────────
        public void ImportarBc3(string rutaArchivo, string etiquetaCustom = null)
        {
            var nuevoCat = Bc3Reader.LeerCatalogo(rutaArchivo, etiquetaCustom);
            _plantilla = _plantilla.AnadirCatalogo(nuevoCat);
            AutoGuardar();
            ReconstruirCatalogosUi();
            ReconstruirGrupos();
            NotificarTodo();
        }

        public void EliminarCatalogo(Guid catalogoId)
        {
            _plantilla = _plantilla.EliminarCatalogo(catalogoId);
            AutoGuardar();
            ReconstruirCatalogosUi();
            ReconstruirGrupos();
            NotificarTodo();
        }

        public void MarcarPrimario(Guid catalogoId)
        {
            _plantilla = _plantilla.MarcarPrimario(catalogoId);
            AutoGuardar();
            ReconstruirCatalogosUi();
            NotificarTodo();
        }

        // ─── Reglas: CRUD ───────────────────────────────────────────────────
        public void AnadirRegla(Regla nueva)
        {
            var nuevasReglas = _plantilla.Reglas.Append(nueva).ToList();
            _plantilla = _plantilla.ConReglas(nuevasReglas);
            AutoGuardar();
            ReconstruirGrupos();
            NotificarTodo();
        }

        public void ActualizarRegla(Regla actualizada)
        {
            var nuevasReglas = _plantilla.Reglas
                .Select(r => r.Id == actualizada.Id ? actualizada : r)
                .ToList();
            _plantilla = _plantilla.ConReglas(nuevasReglas);
            AutoGuardar();
            ReconstruirGrupos();
            NotificarTodo();
        }

        public void EliminarRegla(Guid reglaId)
        {
            var nuevasReglas = _plantilla.Reglas.Where(r => r.Id != reglaId).ToList();
            _plantilla = _plantilla.ConReglas(nuevasReglas);
            AutoGuardar();
            ReconstruirGrupos();
            NotificarTodo();
        }

        public void AplicarReglasSugeridasSaneamiento()
        {
            var sugeridas = PlantillaPorDefecto.ReglasSugeridasSaneamiento();
            var nuevasReglas = _plantilla.Reglas.Concat(sugeridas).ToList();
            _plantilla = _plantilla.ConReglas(nuevasReglas);
            AutoGuardar();
            ReconstruirGrupos();
            NotificarTodo();
        }

        /// <summary>DEBE invocarse dentro de una Transaction abierta por el caller.</summary>
        public void GuardarEnDocumento() => PartidasStorage.Guardar(_doc, _plantilla);

        /// <summary>
        /// Persisteix l'estat actual a la caché estàtica en memòria.
        /// Així ExportarBc3Command pot llegir la plantilla actualitzada
        /// sense passar per ExtensibleStorage (que requereix Transaction
        /// i no funciona des d'una Page modeless).
        /// </summary>
        private void AutoGuardar()
        {
            PartidasSession.PlantillaActual = _plantilla;
        }

        public Plantilla ObtenerPlantillaActual() => _plantilla;

        // ─── Reconstrucción ─────────────────────────────────────────────────
        private void ReconstruirCatalogosUi()
        {
            CatalogosUi.Clear();
            foreach (var c in _plantilla.Catalogos)
            {
                CatalogosUi.Add(new CatalogoUiItem
                {
                    Id = c.Id,
                    Etiqueta = c.Etiqueta,
                    Detalle = $"{c.Partidas.Count:#,##0} partidas · {c.FechaImportacion:dd/MM/yyyy}",
                    EsPrimario = c.Id == _plantilla.CatalogoPrimarioId
                });
            }
        }

        private void ReconstruirGrupos()
        {
            GruposReglas.Clear();
            foreach (var cat in CategoriasMostradas())
            {
                var reglas = _plantilla.Reglas
                    .Where(r => r.Categoria == cat)
                    .OrderBy(r => r.Nombre)
                    .ToList();
                GruposReglas.Add(new GrupoReglas(cat, reglas, _plantilla));
            }
        }

        private static IEnumerable<CategoriaSistema> CategoriasMostradas()
        {
            yield return CategoriaSistema.SaneamientoFecales;
            yield return CategoriaSistema.SaneamientoPluviales;
            yield return CategoriaSistema.SaneamientoGrises;
            yield return CategoriaSistema.FontaneriaAFS;
            yield return CategoriaSistema.FontaneriaACS;
            yield return CategoriaSistema.FontaneriaAR;
            yield return CategoriaSistema.PCIRociadores;
            yield return CategoriaSistema.PCIBIES;
        }

        // ─── INotifyPropertyChanged ────────────────────────────────────────
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void NotificarTodo()
        {
            OnPropertyChanged(nameof(HayCatalogos));
            OnPropertyChanged(nameof(TotalPartidasCargadas));
            OnPropertyChanged(nameof(TotalReglas));
            OnPropertyChanged(nameof(CategoriasConReglas));
        }
    }

    /// <summary>Item visible de la lista de catálogos cargados (Card 1).</summary>
    public sealed class CatalogoUiItem
    {
        public Guid Id { get; set; }
        public string Etiqueta { get; set; }
        public string Detalle { get; set; }
        public bool EsPrimario { get; set; }
    }

    /// <summary>Una categoría con sus reglas y metainfo para la UI.</summary>
    public sealed class GrupoReglas : INotifyPropertyChanged
    {
        public CategoriaSistema Categoria { get; }
        public string NombreCategoria { get; }
        public string ColorHex { get; }
        public ObservableCollection<Regla> Reglas { get; } = new ObservableCollection<Regla>();
        public Plantilla Plantilla { get; }

        public GrupoReglas(CategoriaSistema cat, IEnumerable<Regla> reglas, Plantilla plantilla)
        {
            Categoria = cat;
            NombreCategoria = NombrarCategoria(cat);
            ColorHex = ColorParaCategoria(cat);
            Plantilla = plantilla;
            foreach (var r in reglas) Reglas.Add(r);
        }

        public int NReglas => Reglas.Count;
        public string PillContador =>
            NReglas == 0 ? "0 reglas" : $"{NReglas} regla{(NReglas == 1 ? "" : "s")}";

        public string PillEstado
        {
            get
            {
                if (NReglas == 0) return "No se exportará";
                var hayInvalidas = Reglas.Any(r =>
                    Plantilla.BuscarPartida(r.PartidaCodigo, r.PartidaCatalogoId) == null);
                if (hayInvalidas) return "⚠ Partidas sin enlazar";
                return "✓ cobertura definida";
            }
        }

        public string PillEstadoTipo
        {
            get
            {
                if (NReglas == 0) return "muted";
                var hayInvalidas = Reglas.Any(r =>
                    Plantilla.BuscarPartida(r.PartidaCodigo, r.PartidaCatalogoId) == null);
                return hayInvalidas ? "warn" : "good";
            }
        }

        private static string NombrarCategoria(CategoriaSistema c) => c switch
        {
            CategoriaSistema.SaneamientoFecales   => "Saneamiento — Fecales",
            CategoriaSistema.SaneamientoPluviales => "Saneamiento — Pluviales",
            CategoriaSistema.SaneamientoGrises    => "Saneamiento — Grises",
            CategoriaSistema.FontaneriaAFS        => "Fontanería — AFS",
            CategoriaSistema.FontaneriaACS        => "Fontanería — ACS",
            CategoriaSistema.FontaneriaAR         => "Fontanería — AR",
            CategoriaSistema.PCIRociadores        => "PCI — Ruixadors",
            CategoriaSistema.PCIBIES              => "PCI — BIES",
            CategoriaSistema.Ignorar              => "Ignorar",
            _                                     => c.ToString()
        };

        private static string ColorParaCategoria(CategoriaSistema c) => c switch
        {
            CategoriaSistema.SaneamientoFecales   => "#6B4423",
            CategoriaSistema.SaneamientoPluviales => "#5DA3C7",
            CategoriaSistema.SaneamientoGrises    => "#8FB4D0",
            CategoriaSistema.FontaneriaAFS        => "#1D9E75",
            CategoriaSistema.FontaneriaACS        => "#C0392B",
            CategoriaSistema.FontaneriaAR         => "#E67E22",
            CategoriaSistema.PCIRociadores        => "#7D3C98",
            CategoriaSistema.PCIBIES              => "#5B2C6F",
            _                                     => "#94A3B8"
        };

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
