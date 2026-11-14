using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using E3G.BimTools.DiTools.Commands;

namespace E3G.BimTools.DiTools.Ribbon;

/// <summary>
/// Construye los paneles del ribbon en la pestania "e3gbimtools".
/// Reutiliza la pestania si ya existe (creada por Saneamiento/Fontaneria) o
/// la crea si todavia no esta.
/// </summary>
internal static class RibbonBuilder
{
    public const string TabName = "e3gbimtools";
    public const string PanelSaneamientoName = "MEP";
    public const string PanelInicioProyectoName = "Inicio Proyecto";
    public const string PanelGrafismoPlanosName = "Grafismo y planos";
    public const string PanelElectricidadName = "Electricidad";
    public const string PanelDiToolsName = "DiTools";
    public const string PanelMedicionesBC3Name = "Mediciones BC3";
    public const string PanelCalcMepName = "Cálculo MEP";

    /// <summary>
    /// true si DIALux → Revit ya esta en la barra lateral del panel MEP; entonces
    /// no se crea el panel «Electricidad».  Lo fija DiToolsApplication antes de Build.
    /// </summary>
    public static bool DialuxEnPanelMep { get; set; }

    /// <summary>Segunda linea del boton de las herramientas pendientes de revisar.</summary>
    private const string PorVerificar = "\n(por verificar)";

    public static void Build(UIControlledApplication app)
    {
        EnsureTab(app);

        // MEP (antes «Saneamiento»): PRIMERA app de la pestania (los paneles se
        // muestran en el orden de creacion).  El modulo se carga en
        // DiToolsApplication.OnStartup (SaneamientoBridge); si no cargo, el boton
        // explica el motivo.
        var panelSaneamiento = GetOrCreatePanel(app, PanelSaneamientoName);
        AddSaneamientoButtons(panelSaneamiento);

        var panelGrafismo = GetOrCreatePanel(app, PanelGrafismoPlanosName);
        AddFilterGraphicsDockButton(panelGrafismo);
        AddDxfLegendButton(panelGrafismo);
        AddWorksetDockButton(panelGrafismo);

        // DIALux → Revit se abre desde la barra lateral del panel MEP (2026-11-11).
        // El panel «Electricidad» solo queda si el modulo no cargo.
        if (!DialuxEnPanelMep)
        {
            var panelElectricidad = GetOrCreatePanel(app, PanelElectricidadName);
            AddDialuxImportButton(panelElectricidad);
        }

        var panelDiTools = GetOrCreatePanel(app, PanelDiToolsName);
        var panelBC3 = GetOrCreatePanel(app, PanelMedicionesBC3Name);
        var panelCalc = GetOrCreatePanel(app, PanelCalcMepName);
        // Ultimo panel de la pestania: solo contiene el About.
        var panelInicio = GetOrCreatePanel(app, PanelInicioProyectoName);

        // Primero las herramientas ya revisadas; detras, las marcadas «(por verificar)».
        // TableGen retirado de la cinta (2026-11-07): lo sustituye «TableLink».
        // El codigo se conserva (TableGenCommand / AddTableGenButton).
        AddScheduleExcelExportButton(panelDiTools);
        AddCopyLevelsButton(panelDiTools);
        AddOneFilterButton(panelDiTools);
        AddParaManagerButton(panelDiTools);
        AddViewManagerButton(panelDiTools);
        AddViewSheetManagerButton(panelDiTools);
        AddSectionBoxerButton(panelDiTools);
        AddSheetGenButton(panelDiTools);
        AddFamilyReviserButton(panelDiTools);
        AddOneParameterButton(panelDiTools);
        // SheetLink retirado de la cinta (2026-11-08): lo cubre TableLink.
        // El codigo se conserva (SheetLinkCommand / AddSheetLinkButton).

        AddRevitBC3Button(panelBC3);

        AddCalcPipingButton(panelCalc);
        AddCalcDuctButton(panelCalc);
        AddCalcWindowButton(panelCalc);
        AddCalcSelfTestButton(panelCalc);

        AddAboutButton(
            panelInicio,
            buttonName: "AboutInicioProyecto",
            text: "About",
            commandType: typeof(AboutInicioProyectoCommand),
            tooltip: "Informacion del modulo (Sprint 0 — temporal)");
    }

    /// <summary>
    /// Panel "MEP" (antes "Saneamiento", migrado del monolito E3G_Saneamiento el
    /// 2026-11-07): abre el panel acoplable E3G (Saneamiento HS5, Aislamiento y
    /// DIALux).  El calculo MEP del monolito vive ya en el panel "Calculo MEP"
    /// (e3gbimtools.Calc), por eso no se duplica aqui.  El nombre interno del
    /// boton («SaneamientoPanel») se mantiene para no perder su icono.
    /// </summary>
    private static void AddSaneamientoButtons(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        if (panel.AddItem(new PushButtonData(
                name: "SaneamientoPanel",
                text: "MEP",
                assemblyName: assemblyPath,
                className: typeof(SaneamientoPanelCommand).FullName)) is PushButton main)
        {
            main.ToolTip = "Abre el panel E3G MEP: saneamiento HS5, aislamiento de tuberías y DIALux → Revit.";
            main.LongDescription =
                "Panel acoplable junto al Navegador de proyectos. Saneamiento: verificación " +
                "CTE DB-HS5 (UD -> DN) de la red seleccionada, coloreado de tramos KO, " +
                "aplicación de DN, esquema de principio e informes. Aislamiento: espesores del " +
                "RITE y colocación del aislamiento en las tuberías seleccionadas. DIALux: coloca " +
                "las luminarias de un DWG exportado de DIALux.";
            SetIcon(main);
        }

        // Boton «Ajustes» retirado de la cinta (2026-11-08): los ajustes ya estan
        // dentro del panel de Saneamiento.  Se conserva SaneamientoAjustesCommand.
    }

    /// <summary>
    /// Boton "Copiar niveles" del panel "DiTools" (Sprint 1).  Abre la ventana
    /// modal CopyLevelsWindow para traer niveles desde un vinculo al documento
    /// anfitrion.
    /// </summary>
    private static void AddCopyLevelsButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "CopyLevels",
            text: "Copiar niveles" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(CopyLevelsCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Copia niveles desde un vinculo cargado al documento anfitrion.";
        btn.LongDescription =
            "Selecciona un vinculo cargado, marca los niveles que quieras traer al " +
            "documento anfitrion y la herramienta los crea respetando la elevacion " +
            "del vinculo. Permite añadir prefijo/sufijo al nombre y, opcionalmente, " +
            "generar una vista de planta por cada nivel copiado.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "OneFilter" del panel "DiTools" (Sprint 2).  Abre la ventana modal
    /// OneFilterWindow para filtrar elementos del modelo por categoria +
    /// condiciones AND/OR y aplicar acciones (seleccionar, aislar, ocultar,
    /// override color, edicion en lote, exportar CSV).
    /// </summary>
    private static void AddOneFilterButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "OneFilter",
            text: "OneFilter" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(OneFilterCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Filtrar elementos del modelo por categoría y parámetros (AND/OR).";
        btn.LongDescription =
            "Selecciona una o varias categorías y construye un árbol de condiciones " +
            "AND/OR anidadas sobre cualquier parámetro de instancia o tipo.  Sobre " +
            "el resultado puedes seleccionar, aislar/ocultar en la vista, aplicar " +
            "override de color, editar un parámetro en lote o exportar a CSV.  " +
            "Las plantillas se guardan en JSON dentro de %APPDATA%\\e3g\\DiTools\\FilterTemplates.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "ParaManager" del panel "DiTools" (Sprint 3).  Abre la ventana
    /// modal ParaManagerWindow para listar/crear/eliminar parametros de
    /// proyecto y compartidos, vincularlos a categorias y editar valores en
    /// lote por categoria.
    /// </summary>
    private static void AddParaManagerButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "ParaManager",
            text: "ParaManager" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(ParaManagerCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Gestor de parametros: listar, crear, eliminar, recategorizar y editar valores en lote.";
        btn.LongDescription =
            "ParaManager replica las funciones esenciales de DiRootsOne ParaManager: " +
            "lista los parametros de proyecto y compartidos vinculados al documento, " +
            "permite crear parametros nuevos (compartidos en el .txt configurado), " +
            "vincularlos a las categorias seleccionadas, recategorizar, eliminar y " +
            "editar valores en lote sobre toda una categoria. Una sola transaccion " +
            "por accion para que un Ctrl-Z deshaga todo.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "ViewManager" del panel "DiTools" (Sprint 4).  Abre la ventana
    /// modal ViewManagerWindow para gestionar View Templates y View Ranges:
    /// listar, aplicar a vistas en lote, comparar plantillas, editar View
    /// Range multi-vista y detectar plantillas huerfanas.
    /// </summary>
    private static void AddViewManagerButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "ViewManager",
            text: "ViewManager" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(ViewManagerCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Gestor de View Templates y View Ranges: aplicar a vistas en lote, comparar, detectar huerfanas.";
        btn.LongDescription =
            "ViewManager replica las funciones esenciales de DiRootsOne ViewManager: " +
            "inventario completo de View Templates, aplicacion masiva a N vistas en " +
            "una sola transaccion, edicion de View Range multi-vista, comparacion " +
            "(diff) entre dos plantillas, deteccion de plantillas huerfanas y " +
            "export de la matriz Vista x Template a Excel.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "View & Sheet Manager" del panel "DiTools".  Abre la ventana modal
    /// que automatiza la creacion de planos de instalaciones MEP: recorre el
    /// doble bucle instalaciones × niveles y, por cada combinacion, crea la vista
    /// de planta (con plantilla y escala), la hoja con su rotulo y el viewport
    /// centrado, todo en una sola transaccion.
    /// </summary>
    private static void AddViewSheetManagerButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "ViewSheetManager",
            text: "View & Sheet" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(ViewSheetManagerCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Crea planos de instalaciones MEP en lote: vista + plantilla + hoja + viewport centrado.";
        btn.LongDescription =
            "View & Sheet Manager automatiza la creacion de planos de instalaciones MEP. " +
            "Elige niveles e instalaciones (cada una con su plantilla de vista, escala y " +
            "prefijo) y la herramienta recorre el doble bucle instalaciones × niveles: crea " +
            "una vista de planta por combinacion, le aplica la plantilla y la escala, crea la " +
            "hoja con su rotulo, coloca la vista como viewport centrado y numera/nombra hoja y " +
            "vista con patrones de tokens. Todo en una sola transaccion, con resumen de exitos " +
            "y errores al final.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "SectionBoxer" del panel "DiTools" (Sprint 5).  Abre la ventana
    /// modal SectionBoxerWindow para generar Section Boxes 3D a partir de
    /// seleccion / habitacion / nivel / filtro guardado, con padding
    /// configurable y soporte de modelos vinculados.
    /// </summary>
    private static void AddSectionBoxerButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "SectionBoxer",
            text: "SectionBoxer" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(SectionBoxerCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Genera Section Boxes 3D desde seleccion, habitacion, nivel o filtro guardado.";
        btn.LongDescription =
            "SectionBoxer replica las funciones esenciales de DiRootsOne SectionBoxer: " +
            "calcula el bounding box minimo de un conjunto de elementos (origen " +
            "configurable: seleccion / room / nivel / filtro guardado), aplica el " +
            "padding indicado y lo asigna a la vista 3D activa o a una vista nueva " +
            "dedicada.  Soporta inclusion opcional de modelos vinculados y reset " +
            "al estado previo de la vista.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "SheetGen" del panel "DiTools" (Sprint 6).  Abre la ventana
    /// tabbed SheetGenWindow para crear sheets en bulk, importar/exportar
    /// desde Excel, definir layouts, gestionar revisiones y auditar el
    /// juego de planos.
    /// </summary>
    private static void AddSheetGenButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "SheetGen",
            text: "SheetGen" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(SheetGenCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Generacion masiva de Drawing Sheets, layouts y gestion de revisiones.";
        btn.LongDescription =
            "SheetGen replica las funciones esenciales de DiRootsOne SheetGen: " +
            "crea sheets en bulk desde una grilla o un Excel, aplica layouts de " +
            "colocacion de vistas persistidos en JSON, gestiona revisiones en lote " +
            "y audita el juego de planos (huerfanas, duplicados, renumeracion con patron).";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "FamilyReviser" del panel "DiTools" (Sprint 7).  Abre la ventana
    /// tabbed FamilyReviserWindow con las pestañas Renombrar, Exportar a disco
    /// y Worksets.
    /// </summary>
    private static void AddFamilyReviserButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "FamilyReviser",
            text: "FamilyReviser" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(FamilyReviserCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Renombrado masivo de familias, export organizado a disco y gestion de worksets.";
        btn.LongDescription =
            "FamilyReviser replica las funciones esenciales de DiRootsOne FamilyReviser: " +
            "renombra familias en bulk con prefijo/sufijo/find-replace, exporta los .rfa " +
            "a disco organizados por reglas configurables ({Categoria}/{Disciplina}/{Familia}.rfa), " +
            "y gestiona worksets (crear, renombrar, eliminar, asignar elementos por categoria).";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Worksets visibles" del panel "DiTools" (Bloque E — Worksets).
    /// Abre el dockable pane de visibilidad de worksets, que se puede fijar en
    /// el UI de Revit y sigue la vista activa (o su plantilla) en vivo.
    /// </summary>
    private static void AddWorksetDockButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "WorksetDock",
            text:         "Worksets visibles",
            assemblyName: assemblyPath,
            className:    typeof(WorksetVisibilityDockCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Panel acoplable para encender/apagar worksets en la vista activa al vuelo.";
        btn.LongDescription =
            "Abre un panel acoplable (dockable pane) que puedes fijar en cualquier borde del " +
            "UI de Revit. Sigue la vista activa en vivo y, si ésta tiene una plantilla de vista " +
            "asignada, actúa sobre la plantilla. Cada workset aparece con un interruptor que " +
            "refleja su visibilidad real y la cambia al instante.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Grafismo filtros" del panel "DiTools".  Abre el dockable pane que
    /// lista los filtros de la vista activa (o su plantilla) y exporta/importa
    /// su grafismo de V/G (lineas, patrones, transparencia) a JSON.
    /// </summary>
    private static void AddFilterGraphicsDockButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "FilterGraphicsDock",
            text:         "Grafismo filtros",
            assemblyName: assemblyPath,
            className:    typeof(FilterGraphicsDockCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Panel de filtros: exporta e importa el grafismo de V/G (lineas, patrones, transparencia) sin plantillas de vista.";
        btn.LongDescription =
            "Abre un panel acoplable que sigue la vista activa (o su plantilla de vista) y " +
            "muestra sus filtros con el resumen de Proyeccion/Superficie. Permite activar o " +
            "ocultar filtros al vuelo, exportar su grafismo a un JSON y reaplicarlo en " +
            "cualquier vista o proyecto casando los filtros por nombre. Los patrones se " +
            "guardan por nombre para que funcionen entre proyectos.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "TableLink" del panel "DiTools".  Abre la ventana de
    /// exportacion (como en Revit / editable, varias tablas en pestañas) e
    /// importacion de datos desde Excel.
    /// </summary>
    private static void AddScheduleExcelExportButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "ScheduleExcelExport",
            text:         "TableLink",
            assemblyName: assemblyPath,
            className:    typeof(ScheduleExcelExportCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Exporta tablas de planificación a Excel con el formato E3G e importa datos rellenados en Excel.";
        btn.LongDescription =
            "Lista todas las tablas del modelo (la activa ya marcada). Exporta una o varias — varias van al mismo " +
            "Excel, una pestaña por tabla — «como en Revit» (título, agrupaciones, colores; números como números) " +
            "o en formato editable: una fila por elemento con su ElementId para rellenar parámetros en Excel. " +
            "«Importar datos» compara ese Excel con el modelo, enseña los cambios y aplica los marcados en una " +
            "sola operación (un Ctrl+Z). Recuerda la carpeta de exportación de cada proyecto.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Leyenda DXF" del panel "DiTools".  Abre la ventana que recrea una
    /// leyenda de AutoCAD (DXF/DWG) con dibujo de detalle nativo en la leyenda o
    /// vista de dibujo elegida, con las mismas medidas.
    /// </summary>
    private static void AddDxfLegendButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "DxfLegend",
            text:         "Leyenda DXF",
            assemblyName: assemblyPath,
            className:    typeof(DxfLegendCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Crea una leyenda de Revit a partir de una leyenda de AutoCAD (DXF/DWG), con las mismas medidas.";
        btn.LongDescription =
            "Carga una leyenda dibujada en AutoCAD (espacio modelo, preparada para una ventana gráfica a " +
            "escala 1:N) y la recrea en la leyenda o vista de dibujo que elijas con líneas de detalle, " +
            "regiones rellenadas y textos nativos. Respeta colores, tipos de línea, grosores de polilínea, " +
            "sombreados, bloques y la maquetación de los textos. Crea estilos de línea, rellenos y tipos de " +
            "texto con el prefijo indicado. Vista previa con filtro de capas; todo se deshace con un Ctrl+Z.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "DIALux → Revit" del panel "DiTools".  Abre la ventana que coloca
    /// las luminarias de un DWG de DIALux asociando cada bloque a un tipo de
    /// luminaria del modelo (sustituye a la rutina Dynamo DIALux_a_Revit.dyn).
    /// </summary>
    private static void AddDialuxImportButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "DialuxImport",
            text:         "DIALux → Revit",
            assemblyName: assemblyPath,
            className:    typeof(DialuxImportCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Coloca en el modelo las luminarias de un DWG exportado de DIALux.";
        btn.LongDescription =
            "Carga el DWG/DXF de luminarias de DIALux, lista sus bloques y te deja asociar cada uno con un " +
            "tipo de familia de luminaria del modelo (con sugerencia por nombre), su altura de montaje y un giro " +
            "extra. Simular muestra qué se haría sin tocar el modelo; Crear coloca las nuevas; Rehacer borra las " +
            "importadas de ese DWG y las recoloca; Auditar compara DWG y modelo (nuevas, movidas, tipo distinto, " +
            "sobrantes) y permite sincronizar. La configuración se guarda y carga como .json.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "TableGen" del panel "DiTools" (Sprint 8).  Abre la ventana
    /// de importacion de tabla Excel como Drafting View en Revit.
    /// </summary>
    private static void AddTableGenButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "TableGen",
            text: "TableGen",
            assemblyName: assemblyPath,
            className: typeof(TableGenCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Importa una tabla Excel como Drafting View en Revit.";
        btn.LongDescription =
            "TableGen (parte 1) lee un rango de un .xlsx — preservando fuentes, colores " +
            "de fondo, bordes y celdas combinadas — y genera una Drafting View fiel " +
            "usando TextNotes, DetailLines y FilledRegions.  Incluye preview antes de generar.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "OneParameter" del panel "DiTools" (Sprint 10).  Abre la ventana
    /// modal OneParameterWindow para buscar todos los elementos que tienen un
    /// parametro dado, ver y editar sus valores de forma individual o masiva,
    /// y ejecutar Find &amp; Replace sobre valores de texto.
    /// </summary>
    private static void AddOneParameterButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "OneParameter",
            text:         "OneParameter" + PorVerificar,
            assemblyName: assemblyPath,
            className:    typeof(OneParameterCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Buscar, ver y editar un parametro en todos los elementos del modelo.";
        btn.LongDescription =
            "OneParameter replica las funciones esenciales de DiRootsOne OneParameter: " +
            "busca todos los elementos que tienen un parametro concreto, muestra sus " +
            "valores en una tabla editable, permite editarlos de forma individual o " +
            "en lote, y ejecutar Find & Replace sobre valores de texto.  " +
            "Toda la escritura va en una unica transaccion para que Ctrl-Z deshaga todo.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "SheetLink" del panel "DiTools" (Sprint 11).  Abre la ventana
    /// modal SheetLinkWindow para exportar datos de planos a CSV y reimportarlos
    /// tras edicion en Excel, actualizando numeros, nombres y revision.
    /// </summary>
    private static void AddSheetLinkButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "SheetLink",
            text:         "SheetLink",
            assemblyName: assemblyPath,
            className:    typeof(SheetLinkCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Exportar e importar datos de planos (numeros, nombres, revision) via CSV editable en Excel.";
        btn.LongDescription =
            "SheetLink replica las funciones esenciales de DiRootsOne SheetLink: " +
            "exporta todos los planos del modelo a un CSV que puedes editar en Excel " +
            "(numeros, nombres, firmantes, fechas de revision) y luego reimporta el CSV " +
            "actualizado para aplicar los cambios al modelo en una unica transaccion.  " +
            "Un solo Ctrl-Z deshace toda la importacion.";

        SetIcon(btn);
    }

    // ====================================================================
    //  Panel "Cálculo MEP" — Sprint Calc (Fase 5)
    // ====================================================================

    /// <summary>
    /// Boton "Hidráulica" del panel "Cálculo MEP".  Lanza CalcPipingNetworkCommand
    /// contra el PipingSystem que el usuario elija.
    /// </summary>
    private static void AddCalcPipingButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "CalcPiping",
            text:         "Hidráulica" + PorVerificar,
            assemblyName: assemblyPath,
            className:    typeof(CalcPipingNetworkCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Calcula la red hidráulica (PipingSystem) y escribe resultados MEP_CALC_*.";
        btn.LongDescription =
            "Selecciona un PipingSystem, elige el preset normativo (CTE/RITE) y ejecuta " +
            "el motor hidráulico: calcula velocidades, pérdidas de carga y DN recomendado. " +
            "Los resultados se graban como parámetros compartidos MEP_CALC_* en cada tramo.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Aeráulica" del panel "Cálculo MEP".  Lanza CalcDuctNetworkCommand
    /// contra el MechanicalSystem que el usuario elija.
    /// </summary>
    private static void AddCalcDuctButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "CalcDuct",
            text:         "Aeráulica" + PorVerificar,
            assemblyName: assemblyPath,
            className:    typeof(CalcDuctNetworkCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Calcula la red aeráulica (MechanicalSystem) y escribe resultados MEP_CALC_*.";
        btn.LongDescription =
            "Selecciona un MechanicalSystem, elige el preset normativo (RITE) y ejecuta " +
            "el motor aeráulico: calcula velocidades, pérdidas y tamaño de conducto recomendado.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Ventana cálculo" del panel "Cálculo MEP".  Abre la CalcWindow
    /// con sus 5 pestañas: Hidráulica, Aeráulica, Camino crítico, Esquema, Self-test.
    /// </summary>
    private static void AddCalcWindowButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "CalcWindow",
            text:         "Ventana cálculo" + PorVerificar,
            assemblyName: assemblyPath,
            className:    typeof(CalcShowWindowCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Abre la ventana completa de cálculo MEP (hidráulica, aeráulica, esquema…).";
        btn.LongDescription =
            "Ventana 1100×720 con 5 pestañas: Hidráulica, Aeráulica, Camino crítico, " +
            "Esquema y Self-test. Permite calcular, visualizar y exportar a CSV sin salir " +
            "del modelo.  La ventana es no modal: puedes seguir trabajando en Revit.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Self-test" del panel "Cálculo MEP".  Ejecuta CalcSelfTest y
    /// muestra el informe en un TaskDialog.
    /// </summary>
    private static void AddCalcSelfTestButton(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name:         "CalcSelfTest",
            text:         "Self-test" + PorVerificar,
            assemblyName: assemblyPath,
            className:    typeof(CalcSelfTestCommand).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Ejecuta los smoke-tests del motor MEP y muestra el resultado.";
        btn.LongDescription =
            "Lanza CalcSelfTest: una batería de pruebas sin NUnit que verifica Units, " +
            "FluidProperties, FrictionFactor, HydraulicCalculator y AeraulicCalculator " +
            "con redes sintéticas.  Resultado en un TaskDialog.  Útil tras cada sprint.";

        SetIcon(btn);
    }

    /// <summary>
    /// Boton "Revit → BC3" del panel "Mediciones BC3".  Abre la ventana
    /// independiente del modulo e3gbimtools.RevitBC3.
    /// </summary>
    private static void AddRevitBC3Button(RibbonPanel panel)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: "OpenRevitBC3",
            text: "Revit → BC3" + PorVerificar,
            assemblyName: assemblyPath,
            className: typeof(OpenRevitBC3Command).FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = "Mediciones y presupuestos: importa BC3, mapea elementos Revit a partidas y exporta el BC3 final.";
        btn.LongDescription =
            "Modulo BC3: relaciona elementos del modelo BIM con partidas de un archivo BC3 " +
            "(FIEBDC-3), calcula mediciones automaticamente y exporta un BC3 importable en " +
            "Presto o Arquimedes.  Andamiaje creado en Sprint 0bis; funcionalidad real en Sprints A1-A6.";

        SetIcon(btn);
    }

    // ====================================================================

    private static void EnsureTab(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe (creada por otro modulo de e3gbimtools).  OK.
        }
    }

    private static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string panelName)
    {
        foreach (var p in app.GetRibbonPanels(TabName))
            if (string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase))
                return p;

        return app.CreateRibbonPanel(TabName, panelName);
    }

    private static void AddAboutButton(
        RibbonPanel panel, string buttonName, string text, Type commandType, string tooltip)
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var data = new PushButtonData(
            name: buttonName,
            text: text,
            assemblyName: assemblyPath,
            className: commandType.FullName);

        if (panel.AddItem(data) is not PushButton btn) return;

        btn.ToolTip = tooltip;
        btn.LongDescription =
            "Sprint 0 — fundamentos. Este boton es temporal y se reemplazara por " +
            "las herramientas reales en proximos sprints.";

        SetIcon(btn);
    }

    /// <summary>
    /// Asigna el icono del boton: usa el vectorial Lucide mapeado por nombre de
    /// boton (<see cref="LucideIcons"/>) y, si no hay mapeo, cae al PNG heredado.
    /// </summary>
    private static void SetIcon(PushButton btn)
    {
        var img = LucideIcons.TryGet(IconKeyFor(btn.Name));
        if (img is null) return;
        btn.LargeImage = img;
        btn.Image      = img;
    }

    private static string IconKeyFor(string name) => name switch
    {
        "SaneamientoPanel"   => "showerhead",
        "SaneamientoAjustes" => "settings",
        "CopyLevels"   => "layers",
        "OneFilter"    => "funnel",
        "ParaManager"  => "sliders",
        "ViewManager"  => "layout",
        "ViewSheetManager" => "fileplus",
        "SectionBoxer" => "box",
        "SheetGen"     => "fileplus",
        "FamilyReviser"=> "boxes",
        "WorksetDock"  => "layers",
        "FilterGraphicsDock" => "palette",
        "TableGen"     => "table",
        "ScheduleExcelExport" => "sheetexport",
        "DxfLegend"    => "legend",
        "DialuxImport" => "lamp",
        "OneParameter" => "tag",
        "SheetLink"    => "link",
        "OpenRevitBC3" => "receipt",
        "CalcPiping"   => "droplets",
        "CalcDuct"     => "wind",
        "CalcWindow"   => "calculator",
        "CalcSelfTest" => "flask",
        _              => "info",
    };

    private static BitmapImage? TryLoadIcon(string fileName)
    {
        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir is null) return null;

            // Buscamos en 3 ubicaciones por robustez:
            //   1. bin/Resources/                                  (post-build copy)
            //   2. ..\..\..\assets\icons\                          (dev tree)
            //   3. ..\..\..\Resources\                             (monolito heredado)
            string[] candidates =
            {
                Path.Combine(dir, "Resources", fileName),
                Path.Combine(dir, "..", "..", "..", "assets", "icons", fileName),
                Path.Combine(dir, "..", "..", "..", "..", "..", "Resources", fileName),
            };

            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (!File.Exists(full)) continue;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(full, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { /* fallback: sin icono */ }
        return null;
    }
}
