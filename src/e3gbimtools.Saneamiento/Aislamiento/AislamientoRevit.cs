// ============================================================================
//  AislamientoRevit.cs
//  Analiza las tuberias seleccionadas que llevan aislamiento (fontaneria,
//  calefaccion, refrigeracion y otras; no saneamiento ni contraincendios),
//  agrupadas por tipo de sistema, y coloca el aislamiento RITE en tuberias,
//  fittings y accesorios.
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace E3G.Saneamiento
{
    /// <summary>Tuberia, fitting o accesorio con el diametro que fija su espesor.</summary>
    public sealed class ElementoAislable
    {
        public ElementId Id { get; set; }
        public double DiametroMm { get; set; }
    }

    /// <summary>Un tipo de sistema analizado; la pagina edita fluido, temperatura y ubicacion.</summary>
    public sealed class FilaAislamiento : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Nombre { get; set; }
        public string Detectado { get; set; }
        public List<ElementoAislable> Tuberias { get; } = new List<ElementoAislable>();
        public List<ElementoAislable> Accesorios { get; } = new List<ElementoAislable>();

        public string Conteo => $"{Tuberias.Count:N0} tuberías · {Accesorios.Count:N0} accesorios";

        private bool _incluir = true;
        public bool Incluir
        {
            get => _incluir;
            set { _incluir = value; Avisar(nameof(Incluir)); }
        }

        private int _indiceFluido;
        public int IndiceFluido
        {
            get => _indiceFluido;
            set
            {
                if (_indiceFluido == value || value < 0) return;
                _indiceFluido = value;
                _temperatura = RiteAislamiento.TemperaturaPorDefecto(Fluido).ToString(CultureInfo.CurrentCulture);
                Avisar(nameof(IndiceFluido));
                Avisar(nameof(TemperaturaTexto));
                Avisar(nameof(Resumen));
            }
        }

        private string _temperatura = "";
        public string TemperaturaTexto
        {
            get => _temperatura;
            set { _temperatura = value; Avisar(nameof(TemperaturaTexto)); Avisar(nameof(Resumen)); }
        }

        private int _indiceUbicacion;
        public int IndiceUbicacion
        {
            get => _indiceUbicacion;
            set
            {
                if (value < 0) return;
                _indiceUbicacion = value;
                Avisar(nameof(IndiceUbicacion));
                Avisar(nameof(Resumen));
            }
        }

        public FluidoRite Fluido => (FluidoRite)_indiceFluido;
        public UbicacionRite Ubicacion => (UbicacionRite)_indiceUbicacion;

        private AmbienteCondensacion _ambiente = AmbienteCondensacion.PorDefecto;
        /// <summary>Aire para la comprobacion de condensacion del agua fria; null si la pagina tiene valores no validos.</summary>
        public AmbienteCondensacion Ambiente
        {
            get => _ambiente;
            set { _ambiente = value; Avisar(nameof(Resumen)); }
        }

        /// <summary>true si no se supo si es caliente o frio: sale desmarcada hasta que se revise.</summary>
        public bool PorRevisar { get; private set; }

        public void Inicializar(FluidoDetectado detectado)
        {
            _indiceFluido = (int)detectado.Fluido;
            _temperatura = detectado.Temperatura.ToString("0.#", CultureInfo.CurrentCulture);
            Detectado = detectado.Etiqueta;
            PorRevisar = !detectado.Seguro;
            _incluir = detectado.Seguro;
        }

        public bool TryTemperatura(out double valor)
            => double.TryParse((_temperatura ?? "").Trim().Replace(',', '.'),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out valor);

        /// <summary>
        /// Espesor en mm para un diametro; null si la temperatura o el ambiente no son
        /// aplicables. El diametro se redondea igual que en el resumen.
        /// </summary>
        public int? EspesorPara(double diametroMm)
        {
            if (!TryTemperatura(out double t)) return null;
            if (Fluido == FluidoRite.Afs && _ambiente == null) return null;
            return RiteAislamiento.Espesor(Fluido, Ubicacion, t, Math.Round(diametroMm, 1), _ambiente);
        }

        public bool EsAplicable
            => TryTemperatura(out double t)
               && RiteAislamiento.IndiceTemperatura(Fluido, t) >= 0
               && EspesoresPorDiametro().All(x => x.Espesor != null);

        private List<(int? Espesor, int Cantidad)> EspesoresPorDiametro()
            => Tuberias.Concat(Accesorios)
                .GroupBy(e => Math.Round(e.DiametroMm, 1))
                .Select(g => (EspesorPara(g.Key), g.Count()))
                .ToList();

        public string Resumen
        {
            get
            {
                if (!TryTemperatura(out double t)) return "Temperatura no válida";
                if (RiteAislamiento.IndiceTemperatura(Fluido, t) < 0)
                    return Fluido == FluidoRite.Afs
                        ? "El agua fría debe estar entre 0 y 40 °C: esta fila no se aislará"
                        : "Temperatura fuera de la tabla del RITE: esta fila no se aislará";
                if (Fluido == FluidoRite.Afs && _ambiente == null)
                    return "Aire no válido (temperatura de 0 a 50 °C, humedad entre 0 y 100 %): esta fila no se aislará";

                var espesores = EspesoresPorDiametro();
                if (espesores.Any(x => x.Espesor == null))
                    return $"Ningún espesor de hasta {RiteAislamiento.EspesoresComerciales.Last()} mm evita la condensación " +
                           "con este aire: esta fila no se aislará";

                var grupos = espesores
                    .GroupBy(x => x.Espesor.Value)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key} mm: {g.Sum(x => x.Cantidad):N0}");
                return string.Join(" · ", grupos) + "  —  " + Origen(espesores);
            }
        }

        private string Origen(List<(int? Espesor, int Cantidad)> espesores)
        {
            if (Fluido == FluidoRite.Refrigerante)
            {
                string frio = RiteAislamiento.Tabla(FluidoRite.Frio, Ubicacion).Codigo;
                string caliente = RiteAislamiento.Tabla(FluidoRite.Caliente, Ubicacion).Codigo;
                return $"Línea frigorífica reversible: la más exigente entre frío (tabla {frio}, evaporación) y " +
                       $"gas caliente a más de 60 °C (tabla {caliente}) · RITE IT 1.2.4.2.1.2 ap. 4. " +
                       "Si la máquina solo enfría, elige «Frío (tabla)»";
            }
            if (Fluido != FluidoRite.Afs)
                return $"Tabla {RiteAislamiento.Tabla(Fluido, Ubicacion).Codigo}"
                       + (Fluido == FluidoRite.Acs ? " (no depende de la temperatura)" : "");

            string Num(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);
            string texto =
                $"Anticondensación (UNE-EN ISO 12241) con aire a {Num(_ambiente.Temperatura)} °C y " +
                $"{Num(_ambiente.HumedadRelativa)} % HR, rocío {_ambiente.PuntoRocio.ToString("0.0", CultureInfo.CurrentCulture)} °C · " +
                "RITE IT 1.2.4.2.1.2 ap. 9 y CTE DB HS 4 ap. 5.1.1.3.2";
            if (espesores.Any(x => x.Espesor > RiteAislamiento.EspesorAguaFriaMm))
                texto = $"{RiteAislamiento.EspesorAguaFriaMm} mm no basta en algunos diámetros con este aire: " +
                        "se sube al espesor comercial que evita la condensación · " + texto;
            if (Ubicacion == UbicacionRite.Exterior)
                texto += " · En exterior: acabado contra la intemperie (RITE IT 1.2.4.2.1.1 ap. 2) " +
                         "y revisar las heladas (CTE DB HS 4 ap. 5.1.1.3.3)";
            return texto;
        }

        private void Avisar(string propiedad)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
    }

    public sealed class TipoAislamiento
    {
        public ElementId Id { get; set; }
        public string Nombre { get; set; }
        public override string ToString() => Nombre;
    }

    public sealed class ResultadoAnalisis
    {
        public string DocKey { get; set; }
        public List<FilaAislamiento> Filas { get; } = new List<FilaAislamiento>();
        public List<TipoAislamiento> Tipos { get; } = new List<TipoAislamiento>();
        public int TuberiasExcluidas { get; set; }
        public int AccesoriosSinTuberia { get; set; }
    }

    public sealed class ResultadoColocacion
    {
        public int Creados { get; set; }
        public int Actualizados { get; set; }
        public int SinCambios { get; set; }
        public int Bloqueados { get; set; }
        public int Fallidos { get; set; }
        public List<string> SistemasRevertidos { get; } = new List<string>();

        public void Sumar(ResultadoColocacion o)
        {
            Creados += o.Creados;
            Actualizados += o.Actualizados;
            SinCambios += o.SinCambios;
            Bloqueados += o.Bloqueados;
            Fallidos += o.Fallidos;
        }
    }

    public static class AislamientoRevit
    {
        private const double PiesAMm = 304.8;
        private const int MaxVisitados = 40;   // tope de la busqueda por cadenas de fittings
        private const int PasoProgreso = 200;  // elementos entre repintados de la barra

        public static string ClaveDocumento(Document doc) => doc.PathName + "|" + doc.Title;

        public static ResultadoAnalisis Analizar(Document doc, ICollection<ElementId> seleccion,
                                                 Action<double, string> progreso)
        {
            var resultado = new ResultadoAnalisis { DocKey = ClaveDocumento(doc) };

            resultado.Tipos.AddRange(new FilteredElementCollector(doc)
                .OfClass(typeof(PipeInsulationType))
                .Select(e => new TipoAislamiento { Id = e.Id, Nombre = e.Name })
                .OrderBy(t => t.Nombre));

            var clasificacion = new ClasificacionSistemasService().LeerConSugerencias(doc);
            var filasPorTipo = new Dictionary<ElementId, FilaAislamiento>();   // null = no se aisla

            FilaAislamiento FilaDe(Pipe tuberia)
            {
                var tipoId = tuberia.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
                var tipo = tipoId == null ? null : doc.GetElement(tipoId) as PipingSystemType;
                if (tipo == null) return null;
                if (filasPorTipo.TryGetValue(tipoId, out var existente)) return existente;

                FilaAislamiento fila = null;
                var detectado = Detectar(tipo, clasificacion);
                if (detectado != null)
                {
                    fila = new FilaAislamiento { Nombre = tipo.Name };
                    fila.Inicializar(detectado);
                }
                filasPorTipo[tipoId] = fila;
                return fila;
            }

            var elementos = seleccion.Select(doc.GetElement).Where(e => e != null).ToList();
            var tuberias = elementos.OfType<Pipe>().Where(EsTuberia).ToList();

            // Fittings y accesorios: los seleccionados y los conectados a las tuberias seleccionadas.
            var instancias = elementos.OfType<FamilyInstance>().Where(EsFittingOAccesorio)
                .ToDictionary(i => i.Id);

            int leidas = 0;
            foreach (var tuberia in tuberias)
            {
                if (++leidas % PasoProgreso == 0)
                    progreso(50.0 * leidas / tuberias.Count, "Leyendo tuberías");

                var fila = FilaDe(tuberia);
                if (fila == null) { resultado.TuberiasExcluidas++; continue; }
                fila.Tuberias.Add(new ElementoAislable { Id = tuberia.Id, DiametroMm = DiametroExteriorMm(tuberia) });

                var alcanzados = new List<FamilyInstance>();
                Recorrer(tuberia, new List<Pipe>(), alcanzados);
                foreach (var instancia in alcanzados) instancias[instancia.Id] = instancia;
            }

            int leidos = 0;
            foreach (var instancia in instancias.Values)
            {
                if (++leidos % PasoProgreso == 0)
                    progreso(50 + 50.0 * leidos / instancias.Count, "Leyendo fittings y accesorios");

                var conectadas = new List<Pipe>();
                Recorrer(instancia, conectadas, new List<FamilyInstance>());
                if (conectadas.Count == 0) { resultado.AccesoriosSinTuberia++; continue; }

                var propias = conectadas.Where(t => FilaDe(t) != null).ToList();
                if (propias.Count == 0) continue;   // pertenece a saneamiento u otra red

                // IT 1.2.4.2.1.2: el accesorio lleva el espesor de su tuberia;
                // en reducciones y tes manda la mayor.
                FilaDe(propias[0]).Accesorios.Add(new ElementoAislable
                {
                    Id = instancia.Id,
                    DiametroMm = propias.Max(DiametroExteriorMm),
                });
            }

            resultado.Filas.AddRange(filasPorTipo.Values
                .Where(f => f != null && f.Tuberias.Count + f.Accesorios.Count > 0)
                .OrderBy(f => f.Nombre));
            return resultado;
        }

        private static bool EsCategoria(Element e, BuiltInCategory categoria)
            => e.Category != null && e.Category.Id.Value == (long)categoria;

        private static bool EsTuberia(Element e) => EsCategoria(e, BuiltInCategory.OST_PipeCurves);

        private static bool EsFittingOAccesorio(Element e)
            => EsCategoria(e, BuiltInCategory.OST_PipeFitting) || EsCategoria(e, BuiltInCategory.OST_PipeAccessory);

        /// <summary>
        /// Fluido de un tipo de sistema, o null si no se aisla (saneamiento,
        /// contraincendios o «Ignorar» en Ajustes).
        /// - Fontaneria: manda la clasificacion de Revit; el codigo 20_30 de E3G es
        ///   agua reutilizada (fria), asi que la categoria «AR» no indica retorno de ACS.
        /// - Calefaccion, refrigeracion y otras: <see cref="DeteccionFluido"/>. En los
        ///   hidronicos no se usa la categoria de Ajustes, porque su sugerencia por
        ///   nombre toma «Retorno calefacción» por AR y «Agua fría clima» por AFS.
        /// </summary>
        private static FluidoDetectado Detectar(PipingSystemType tipo, Dictionary<ElementId, CategoriaSistema> clasificacion)
        {
            clasificacion.TryGetValue(tipo.Id, out var categoria);
            if (categoria == CategoriaSistema.Ignorar) return null;

            var clase = tipo.SystemClassification;
            if (clase == MEPSystemClassification.DomesticHotWater) return FluidoDetectado.Por(FluidoRite.Acs);
            if (clase == MEPSystemClassification.DomesticColdWater) return FluidoDetectado.Por(FluidoRite.Afs);
            if (clase == MEPSystemClassification.Sanitary || clase == MEPSystemClassification.Vent ||
                clase == MEPSystemClassification.Storm ||
                clase == MEPSystemClassification.FireProtectWet || clase == MEPSystemClassification.FireProtectDry ||
                clase == MEPSystemClassification.FireProtectPreaction || clase == MEPSystemClassification.FireProtectOther)
                return null;

            // Lineas frigorificas: por nombre, antes que la categoria de Ajustes, cuya
            // sugerencia tomaria «gas caliente» por ACS.
            if (DeteccionFluido.PorNombre(tipo.Name) == FluidoRite.Refrigerante)
                return DeteccionFluido.Detectar(tipo.Name, TemperaturaFluido(tipo));

            bool hidronico = clase == MEPSystemClassification.SupplyHydronic ||
                             clase == MEPSystemClassification.ReturnHydronic;
            if (!hidronico)
            {
                switch (categoria)
                {
                    case CategoriaSistema.FontaneriaACS:
                        return FluidoDetectado.Por(FluidoRite.Acs);
                    case CategoriaSistema.FontaneriaAFS:
                        return FluidoDetectado.Por(FluidoRite.Afs);
                    case CategoriaSistema.FontaneriaAR:
                        string nombre = (tipo.Name ?? "").ToUpperInvariant();
                        return FluidoDetectado.Por(nombre.Contains("RETORN") || nombre.Contains("RECIRC")
                            ? FluidoRite.Acs : FluidoRite.Afs);
                    case CategoriaSistema.SaneamientoFecales:
                    case CategoriaSistema.SaneamientoPluviales:
                    case CategoriaSistema.SaneamientoGrises:
                    case CategoriaSistema.PCIRociadores:
                    case CategoriaSistema.PCIBIES:
                        return null;
                }
            }

            return DeteccionFluido.Detectar(tipo.Name, TemperaturaFluido(tipo));
        }

        /// <summary>Temperatura de fluido del tipo de sistema en °C, o null si no la tiene.</summary>
        private static double? TemperaturaFluido(PipingSystemType tipo)
        {
            var parametro = tipo.get_Parameter(BuiltInParameter.RBS_PIPE_FLUID_TEMPERATURE_PARAM);
            if (parametro == null || !parametro.HasValue || parametro.StorageType != StorageType.Double) return null;
            return UnitUtils.ConvertFromInternalUnits(parametro.AsDouble(), UnitTypeId.Celsius);
        }

        private static double DiametroExteriorMm(Pipe tuberia)
        {
            var parametro = tuberia.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)
                         ?? tuberia.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            return (parametro?.AsDouble() ?? 0) * PiesAMm;
        }

        /// <summary>
        /// Recorre la red desde <paramref name="origen"/> atravesando solo fittings y
        /// accesorios: se detiene en cada tuberia y no cruza aparatos ni equipos.
        /// </summary>
        private static void Recorrer(Element origen, List<Pipe> tuberias, List<FamilyInstance> accesorios)
        {
            var visitados = new HashSet<ElementId> { origen.Id };
            var pendientes = new Queue<Element>();
            pendientes.Enqueue(origen);

            while (pendientes.Count > 0 && visitados.Count < MaxVisitados)
            {
                var actual = pendientes.Dequeue();
                var gestor = actual is MEPCurve curva ? curva.ConnectorManager
                                                      : (actual as FamilyInstance)?.MEPModel?.ConnectorManager;
                if (gestor == null) continue;

                foreach (Connector conector in gestor.Connectors)
                {
                    if (conector.Domain != Domain.DomainPiping || !conector.IsConnected) continue;
                    foreach (Connector otro in conector.AllRefs)
                    {
                        var dueno = otro.Owner;
                        if (dueno == null || !visitados.Add(dueno.Id)) continue;
                        if (dueno is Pipe tuberia && EsTuberia(tuberia))
                        {
                            tuberias.Add(tuberia);
                        }
                        else if (dueno is FamilyInstance instancia && EsFittingOAccesorio(instancia))
                        {
                            accesorios.Add(instancia);
                            pendientes.Enqueue(instancia);
                        }
                    }
                }
            }
        }

        public static ResultadoColocacion Colocar(Document doc, IList<FilaAislamiento> filas, ElementId tipoId,
                                                  Action<double, string> progreso)
        {
            var total = new ResultadoColocacion();
            int totalElementos = filas.Sum(f => f.Tuberias.Count + f.Accesorios.Count);
            int hechos = 0;

            // Una transaccion por sistema: si Revit rechaza uno, los demas se conservan.
            // El grupo los deja como un unico paso de deshacer.
            using (var grupo = new TransactionGroup(doc, "E3G Colocar aislamiento RITE"))
            {
                grupo.Start();
                foreach (var fila in filas)
                {
                    var parcial = new ResultadoColocacion();
                    using (var t = new Transaction(doc, "Aislamiento " + fila.Nombre))
                    {
                        t.Start();
                        var opciones = t.GetFailureHandlingOptions();
                        opciones.SetFailuresPreprocessor(new SinDialogos());
                        opciones.SetClearAfterRollback(true);
                        t.SetFailureHandlingOptions(opciones);

                        foreach (var elemento in fila.Tuberias.Concat(fila.Accesorios))
                        {
                            if (++hechos % PasoProgreso == 0)
                                progreso(100.0 * hechos / totalElementos, "Aislando " + fila.Nombre);

                            int? espesor = fila.EspesorPara(elemento.DiametroMm);
                            if (espesor == null) { parcial.Fallidos++; continue; }
                            Aplicar(doc, elemento.Id, tipoId, espesor.Value / PiesAMm, parcial);
                        }

                        progreso(100.0 * hechos / totalElementos, "Confirmando " + fila.Nombre + " en Revit");
                        if (t.Commit() == TransactionStatus.Committed) total.Sumar(parcial);
                        else total.SistemasRevertidos.Add(fila.Nombre);
                    }
                }
                grupo.Assimilate();
            }
            return total;
        }

        private static void Aplicar(Document doc, ElementId id, ElementId tipoId, double espesorPies, ResultadoColocacion r)
        {
            try
            {
                if (doc.GetElement(id) == null) { r.Fallidos++; return; }
                if (!EsEditable(doc, id)) { r.Bloqueados++; return; }

                var existentes = InsulationLiningBase.GetInsulationIds(doc, id);
                if (existentes.Count == 0)
                {
                    PipeInsulation.Create(doc, id, tipoId, espesorPies);
                    r.Creados++;
                    return;
                }

                foreach (var aislamientoId in existentes)
                {
                    if (!(doc.GetElement(aislamientoId) is PipeInsulation aislamiento)) continue;
                    if (!EsEditable(doc, aislamientoId)) { r.Bloqueados++; continue; }

                    bool cambiaTipo = aislamiento.GetTypeId() != tipoId;
                    bool cambiaEspesor = Math.Abs(aislamiento.Thickness - espesorPies) > 1e-6;
                    if (!cambiaTipo && !cambiaEspesor) { r.SinCambios++; continue; }

                    if (cambiaTipo) aislamiento.ChangeTypeId(tipoId);
                    if (cambiaEspesor) aislamiento.Thickness = espesorPies;
                    r.Actualizados++;
                }
            }
            catch (Exception)
            {
                r.Fallidos++;
            }
        }

        // En un modelo central, tocar un elemento de otro usuario o desactualizado
        // haria fallar el commit y revertiria el sistema entero.
        private static bool EsEditable(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared) return true;
            if (WorksharingUtils.GetCheckoutStatus(doc, id) == CheckoutStatus.OwnedByOtherUser) return false;
            var estado = WorksharingUtils.GetModelUpdatesStatus(doc, id);
            return estado != ModelUpdatesStatus.UpdatedInCentral && estado != ModelUpdatesStatus.DeletedInCentral;
        }

        /// <summary>Evita un dialogo por cada aviso en una operacion de miles de elementos.</summary>
        private sealed class SinDialogos : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accesor)
            {
                bool hayErrores = false;
                foreach (var fallo in accesor.GetFailureMessages())
                {
                    if (fallo.GetSeverity() == FailureSeverity.Warning) accesor.DeleteWarning(fallo);
                    else hayErrores = true;
                }
                return hayErrores ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }

    public enum AccionAislamiento { Analizar, Colocar }

    public sealed class EventoAislamiento : IExternalEventHandler
    {
        public AislamientoPage Pagina { get; set; }
        public AccionAislamiento Accion { get; set; }
        public string DocKey { get; set; }
        public List<FilaAislamiento> Filas { get; set; }
        public ElementId TipoId { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    Pagina.MostrarError("Abre un proyecto de Revit.");
                }
                else if (Accion == AccionAislamiento.Analizar)
                {
                    var seleccion = app.ActiveUIDocument.Selection.GetElementIds();
                    if (seleccion.Count == 0)
                        Pagina.MostrarError("Selecciona en Revit las tuberías que quieres analizar y vuelve a pulsar Analizar.");
                    else
                        Pagina.MostrarAnalisis(AislamientoRevit.Analizar(doc, seleccion, Pagina.ReportarProgreso));
                }
                else if (AislamientoRevit.ClaveDocumento(doc) != DocKey)
                {
                    Pagina.MostrarError("El proyecto activo ha cambiado. Vuelve a analizar.");
                }
                else
                {
                    Pagina.MostrarColocacion(AislamientoRevit.Colocar(doc, Filas, TipoId, Pagina.ReportarProgreso));
                }
            }
            catch (Exception ex)
            {
                Pagina.MostrarError(ex.Message);
            }
        }

        public string GetName() => "E3G Aislamiento RITE";
    }
}
