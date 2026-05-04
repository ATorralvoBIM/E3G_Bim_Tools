### Frase per enviar a Claude després de cada tasca feta
Em podries explicar que es el que tenim ara mateix, que fan ara amb els canvis fets i tal per deixar-ho redactat i explicat tot.
el que tenim actualment és això, has d'afegir lels ultims canvis fets:
# DECISIONS.md — E3G BIM Tools
Registre de decisions d'arquitectura del projecte.

---

## [2026-04-13] Llenguatge de disseny visual — Neumorfisme 2.0

**Problema:** Calia un estil visual consistent, professional i modern per als panells dockables de Revit.

**Alternatives considerades:**
- Material 3 Expressive (descartat: orientat a mòbil)
- Glassmorfisme (descartat: WPF no gestiona bé fons dinàmics)
- Estètica Y2K (descartat: inapropiat per a eina d'enginyeria)
- Neumorfisme 2.0 ✓

**Solució adoptada:** Neumorfisme 2.0 amb fons `#E8ECF0`, sombres `#C8CDD2` / `#FFFFFF`, simulat amb `DropShadowEffect` en WPF (no hi ha `box-shadow`).

**Tokens del sistema:**
- Fons base: `#E8ECF0`
- Accent Saneament: `#4a7a96`
- Accent Fontaneria: `#1D9E75`
- Radi contenidor: 18px · cards: 14px · elements petits: 10-12px
- Tipografia: 200 (logo) / Light (mètriques) / Medium (cos) / SemiBold (botons)

**Conseqüències:** Els elements generats dinàmicament des del `.cs` (cards de grups de fontaneria, files de medicions) han d'adoptar el mateix fons `#E8ECF0` i `CornerRadius` per mantenir consistència.

---

## [2026-04-13] Arquitectura de panells — DockablePane amb Page WPF

**Problema:** Calia integrar UI pròpia a Revit sense bloquejar el flux de treball.

**Alternatives considerades:**
- Finestra flotant `Window` (descartat: es tanca en canviar de vista)
- `IExternalCommand` amb diàleg modal (descartat: bloqueja Revit)
- `IDockablePaneProvider` amb `Page` WPF ✓

**Solució adoptada:** Dos panells dockables independents registrats a `OnStartup`: `PaneId` (Saneament) i `PaneIdFontaneria` (Fontaneria), cadascun amb el seu `IDockablePaneProvider`.

**Conseqüències:** Les operacions que accedeixen al model (anàlisi, mesura) requereixen `IExternalEventHandler` + `ExternalEvent` per executar-se al thread de Revit. Cap operació de model es pot fer directament des d'un event WPF.

---

## [2026-04-13] Target framework — net8.0-windows amb x64

**Problema:** Revit 2025 requereix un target framework específic. Els primers intents amb `net8.0-windows` sense especificar plataforma causaven `TypeLoadException`.

**Solució adoptada:** `<TargetFramework>net8.0-windows</TargetFramework>` + `<PlatformTarget>x64</PlatformTarget>` al `.csproj`. Rutes a `RevitAPI.dll` i `RevitAPIUI.dll` a `E:\APPS\Autodesk\Revit 2025\`.

**Conseqüències:** El `.dll` compilat només funciona amb Revit 2025 x64. No és compatible amb versions anteriors sense recompilar.

---

## [2026-04-13] Estructura de fitxers — monòlit inicial

**Problema:** Tot el codi (models de dades, motors, providers, App) és a `E3G_Saneamiento.cs` (1354 línies).

**Estat:** Acceptat temporalment per velocitat de desenvolupament inicial.

**Deute tècnic identificat:** Separar en fitxers independents: `SaneamientoMotor.cs`, `FontaneriaMotor.cs`, `Models.cs`, `App.cs`. Prioritat P2.

# [15/04/2026]
## Què teniem fet i funcionant fins ara

El plugin es carrega a Revit amb un sol botó "E3G BIM Tools" al Ribbon. En prémer el botó s'obra un panell dockable que conté 
el MainWindow (una finestra amb un sidebar a l'esquerra i el contingut a la dreta)

El sidebar té 6 icones: Saneamiento i Fontaneria (funcionals) i Climatització, Ventilació, Electricitat i CI (desactivades, pròximament).
En clicar una icona del sidebar, el contingut de la dreta canvia entre SaneamientoWindow i FontaneriaWindow.

Tots dos panells fills tenen el disseny neumòrfic aplicat.

## Què no funcionava abans del dia 15/04/2026

El contingut del MainWindow apareixia com un rectangle blanc/negre buit. La causa era que estàvem utilitzant un ContentControl per mostrar
les pàgines WPF, però WPF necessita un Frame per navegar entre Page. 

## Un cop aplicats els canvis

Canvi - ContentControl -> Frame + Navigate()
Ara la navegació entre Saneamiento i Fontaneria funciona visualment, que el botó actiu del sidebar es marca correctament, i les funcionalitats de Saneamiento
i Fontaneria segueixen funcionant dins del nou contenidor.

# [17/04/2026]
## Estat actual - E3G BIM Tools
### Canvis realitzats

Creació de l'Angel del fitxer EsquemaPrincipi.cs per al panell Saneamiento. Aquesta pestanya llegeix les tuberíes seleccionades post-càlcul MagiCAD i genera un
esquema de principi de sanejament amb baixants, col·lectors, UD i DN.

- Afegir EsquemaPrincipi.cs al projecte

- Canvis a SaneamientoWindow.xaml:
Afegit el namespace xmlns:local="clr-namespace:E3G.Saneamiento" a l'element arrel <Page>.
Afegit un nou RadioButton x:Name="TabEsquema" al bloc de pestanyes, amb Checked="TabEsquema_Checked".
Afegit un nou StackPanel x:Name="PanelEsquema" al final del contingut, amb Visibility="Collapsed" i el control <local:EsquemaPrincipiControl x:Name="tabEsquema"/> a dins.

- Canvis a SaneamientoWindow.xaml.cs:
Afegits dos nous camps privats: _eventoEsquema i _handlerEsquema.
Al constructor, inicialitzats amb new EventoEsquemaPrincipi { Pagina = this } i ExternalEvent.Create(...), seguint el mateix patró que _eventoMedicion.
Afegit el handler TabEsquema_Checked que amaga els altres panells, mostra PanelEsquema i crida _eventoEsquema.Raise().
Afegit el mètode públic IniciarEsquema(Document doc, List<int> pipeIds) que rep el document i els IDs des del context de l'API i els passa a tabEsquema.IniciarAmbDoc(...).

- Canvis a E3g_Saneamiento.cs:
Afegida la nova classe EventoEsquemaPrincipi : IExternalEventHandler. Quan s'executa, llegeix la selecció activa de Revit (uidoc.Selection.GetElementIds()), extreu els IDs
i crida Pagina.IniciarEsquema(doc, pipeIds) via Dispatcher.Invoke. Això és necessari perquè les transaccions i l'accés al document de Revit només es poden fer des del 
context de l'API (un IExternalEventHandler), mai directament des d'un thread WPF.

- Correccions de compilació:
Propietats múltiples en una línia: public double X0, Y0, Z0 { get; set; } no és vàlid en C#. S'han separat en sis propietats individuals.
TextNote.Create amb signatura obsoleta: A Revit 2025, el mètode ja no accepta TextAlignFlags com a quart argument. S'ha substituït per TextNoteOptions amb HorizontalTextAlignment.
Ambigüitat de Grid: El compilador no sabia si Grid era System.Windows.Controls.Grid o Autodesk.Revit.DB.Grid. S'ha qualificat explícitament com System.Windows.Controls.Grid.

### Estat actual
La pestanya Esquema Principal apareix correctamanet al panell Saneamiento. En clicar "Generar Esquema" amb tuberíes seleccionades, el plugin llegeix els paràmetres MagiCAD del model 
i processa la xarxa. El botó de "Inserir a Revit" per ara dona error de transacció.

# [17/04/2026]
## Problemes detectats i solucions aplicades
	- Problema 1 - El botó "Inserir a Revit" donava error de transacció
Causa: El botó OnInserirClick cridava ExportadorDraftingView.CrearDraftingView directament des del thread WPF. Les transaccions de Revit (crear elements al model, obrir vistes) només 
es poden executar des del context de l'API de Revit, mai des d'un thread WPF.
Solució: Encapsular la lògica d'inserció en un nou ExternalEvent, seguint el mateix patró que la resta d'operacions del plugin.
Canvis aplicats:
	· A EsquemaPrincipi.cs: afegida la propietat pública EventoInserir i XarxaActual al control EsquemaPrincipiControl. El mètode OnInserirClick ara simplement crida EventoInserir.Raise() 
	en lloc d'executar la transacció directament. Afegit també el mètode NotificarInsercioCompletada perquè l'event pugui actualitzar la UI un cop acabada la inserció.
	· A E3G_Saneamiento.cs: afegida la nova classe EventoInserirEsquema : IExternalEventHandler que executa ExportadorDraftingView.CrearDraftingView des del context correcte de l'API, obre
	la vista creada i notifica el control WPF via Dispatcher.Invoke.
	· A SaneamientoWindow.xaml.cs: afegits els camps _eventoInserir i _handlerInserir, inicialitzats al constructor. Al mètode IniciarEsquema s'injecta _eventoInserir al control perquè pugui
	usar-lo. Afegit el mètode públic GetTabEsquema() per permetre l'accés al control des del handler.

	- Problema 2 - El WebBrowser mostrava el SVG com a text en brut
Causa: El control WebBrowser de WPF dins d'un panell dockable de Revit usa per defecte una versió molt antiga d'Internet Explorer que no renderitza SVG. El meta http-equiv='X-UA-Compatible'
content='IE=11' no és suficient quan s'usa NavigateToString.
Solució: En lloc de passar el HTML com a string, escriure'l a un fitxer temporal i navegar-hi amb Navigate(new Uri(...)). Quan el browser carrega des d'un fitxer local, respecta el mode de 
compatibilitat IE11 i renderitza l'SVG correctament.
Canvi aplicat a EsquemaPrincipi.cs, mètode MostrarSvg: substituït NavigateToString(html) per escriptura a Path.GetTempPath()/E3G_Esquema.html i navegació amb Navigate(new Uri(tmpPath)).

	- Problema 3 - Els botons apareixien apagats fins que es clicava una estona
Causa: IniciarAmbDoc s'executava i intentava actualitzar la UI (_btnGenerar.IsEnabled, _labelEstat.Text) abans que el control WPF estigués completament inicialitzat i visible al panell 
dockable.
Solució: Diferir l'actualització de la UI usant Dispatcher.BeginInvoke amb prioritat DispatcherPriority.Loaded, que garanteix que el codi s'executa només un cop la UI és completament 
activa.

	- Problema 4 - L'esquema apareixia a les altres pestanyes
Causa: El control WebBrowser és un control Win32 natiu (no WPF pur) i ignora la propietat Visibility de WPF. Quan el PanelEsquema es col·lapsava, el browser continuava renderitzant 
per sobre dels altres panells.
Solució: Quan l'usuari canvia a Verificación o Mediciones, a més d'amagar el PanelEsquema, es crida el nou mètode BuidarBrowser() que navega el browser a about:blank, buidant-lo 
efectivament.
Canvis aplicats:
	· A EsquemaPrincipi.cs: afegit el mètode públic BuidarBrowser().
	· A SaneamientoWindow.xaml.cs: als handlers TabVerificacion_Checked i TabMediciones_Checked, afegits PanelEsquema.Visibility = Visibility.Collapsed i tabEsquema?.BuidarBrowser().

	- Problema 5 - Icona del Ribbon perduda
Causa: Els fitxers .png de la carpeta Resources no tenien configurat Copy to Output Directory, de manera que en compilar no es copiaven a la carpeta de la DLL i Revit no els trobava.
Solució: Seleccionar tots els fitxers de la carpeta Resources al Solution Explorer i configurar Build Action = Content i Copy to Output Directory = Copy always. A partir d'aquí es 
copien automàticament en cada compilació.


## Implementació Pluvials i Grises — Motors de medició

	- Canvis realitzats
E3G_Saneamiento.cs
Afegits nous models de dades: FilaBajantPluvial, ResumenPluvials i ResumenGrises. Afegits dos nous camps a Sesion: UltimesPluvials i UltimesGrises. Afegits dos nous camps a 
SaneamientoSession: HandlerPluvials, EventoPluvials, HandlerGrises i EventoGrises.
Afegits dos nous motors de càlcul:

PluvialsMotor.Calcular(doc) — recull totes les canonades del sistema 10_20, les classifica com verticals (bajants) o horitzontals, identifica les plantes d'inici i fi de cada bajant, 
i compta les buneres E3G_Sumidero del model.
GrisesMotor.Calcular(doc) — recull totes les canonades del sistema 10_30 dins de grups de Revit, les agrupa per tipus de grup (igual que el motor de Fecals), i mesura per DN i grup.

Afegits dos nous event handlers: EventoMedicionPluvials i EventoMedicionGrises, que criden els motors respectius i actualitzen la UI via Dispatcher.Invoke.
A OnStartup, registrats els nous handlers i events just abans d'assignar SaneamientoSession.Handler.
SaneamientoWindow.xaml
El panel de Pluvials i el de Grises han passat de mostrar "pròximament" a tenir contingut real:

PanelSisPluviales ara conté un StackPanel x:Name="PanelPluvialsContenido" i un botó "Medir" actiu connectat a BtnMedirPluvials_Click.
PanelSisGrises ara conté un StackPanel x:Name="PanelGrisesContenido" i un botó "Medir" actiu connectat a BtnMedirGrises_Click.

SaneamientoWindow.xaml.cs
Afegits quatre nous mètodes:

ActualizarPluvials(ResumenPluvials res) — pobla PanelPluvialsContenido amb les seccions de total, DN per DN i bajants amb plantes d'inici i fi.
ActualizarGrises(ResumenGrises res) — pobla PanelGrisesContenido amb les seccions de total i detall per grup.
BtnMedirPluvials_Click — assigna this com a Pagina del handler i llança EventoPluvials.
BtnMedirGrises_Click — assigna this com a Pagina del handler i llança EventoGrises.


## [21/04/2026]
### Canvis visuals

	- Targetes de mètriques amb fons blanc
	- Botons Fecals/Pluvials/Grises 
En fer clic als botons de selecció de sistema (Fecals, Pluvials, Grises) al panell de Medicions, el text desapareixia.
Solució: Modificar ActualizarColorBotonSistema al SaneamientoWindow.xaml.cs per accedir directament al TextBlock interior del botó via btn.Content as TextBlock i assignar-hi el Foreground correcte:

Botó actiu → Background = color del sistema, Foreground del TextBlock = blanc
Botó inactiu → Background = blanc, Foreground del TextBlock = color del sistema
	
	- Correcció exportació CSV Fontaneria
El problema era que l'exportació CSV no incloïa les derivacions individuals per planta.
Solució: afegint un segon bloc, separat del primer. Per cada grup que tingui derivacions, s'escriu una fila per cada planta amb els metres per DN i el total. Les columnes de DN es generen dinàmicament
a partir dels DN presents a les dades, de manera que el format s'adapta automàticament si en el futur hi ha DNs nous.

	- Redisseny visual del Sidebar
Comportament visual per estat:

Normal (funcionals): pill elevada, ombra exterior #C8CDD2 a Direction=315, BlurRadius=6, ShadowDepth=3
Hover: fons lleugerament enfosquit a #DDE1E5, mantenint la pill
Actiu (seleccionat): pendent d'implementar via lògica .cs — requereix invertir les ombres per simular enfonsament (inner shadow emulat amb dos Border annidat)
Desactivats (pròximament): mateixa pill però amb Opacity=0.45 i ombra reduïda (BlurRadius=4, ShadowDepth=2)

Altres ajustos estructurals:

Padding="6,0" afegit al Border contenidor de la sidebar per donar respiració lateral a les pills
Margin="0,3" a cada botó per separació vertical uniforme
Margin="0,0,0,8" al bloc del logo per augmentar la separació visual abans dels botons
La direcció de l'ombra del contenidor sidebar canviada de Direction=90 a Direction=0 per coherència amb el nou disseny

Pendent
L'efecte d'enfonsament de la pill activa (inner shadow quan el botó és el seleccionat) requereix un segon pas: afegir lògica al MainWindow.xaml.cs per canviar dinàmicament les propietats de l'ombra 
o aplicar un segon Border interior quan el botó és l'actiu. Això es farà en la propera iteració un cop validat visualment el disseny de les pills en Revit.


## [30/04/2026]
### Implementació de la secció "Classificació de sistemes" als Ajustos

#### Context
Fins ara, el plugin identificava els sistemes de tuberia per contingut del nom
(p.ex. `sysType.Contains("10_10")`). Aquesta lògica hardcoded no funciona amb
projectes que usen nomenclatures diferents. La solució és una taula de
classificació explícita que l'usuari defineix una vegada per projecte.

#### Nou fitxer: `ClasificacionSistemas.cs`

Fitxer de servei transversal que implementa:

**`CategoriaSistema` (enum)** — 10 categories funcionals amb valors enters
fixos (no modificar per compatibilitat amb projectes existents):
`SinAsignar`, `SaneamientoFecales`, `SaneamientoPluviales`,
`SaneamientoGrises`, `FontaneriaAFS`, `FontaneriaACS`, `FontaneriaAR`,
`PCIRociadores`, `PCIBIES`, `Ignorar`.

**`CategoriasCatalogo`** — Catàleg estàtic de les 10 categories amb etiqueta
visible en català i grup d'agrupació. Únic punt de la veritat per a la UI.

**`ClasificacionSistemasSchema`** — Schema d'ExtensibleStorage amb GUID fix
`E3C0F1A2-1B3D-4E5F-A6B7-C8D9E0F1A2B3`. Persiteix un mapa
`ElementId → int` (CategoriaSistema) en un `DataStorage` dedicat al `.rvt`.
La configuració viatja amb el projecte. **No regenerar el GUID.**

**`ClasificacionSistemasService`** — Implementació de
`IClasificacionSistemasService` amb:
- `Leer(doc)` — Llegeix el mapa guardat, purgant huèrfans silenciosament.
- `Guardar(doc, mapa)` — Escriu al `.rvt`. **Requereix Transaction oberta.**
- `AutoSugerir(doc)` — Heurística en dos nivells: (1) paraules clau al nom
  del tipus (multi-idioma: català, castellà, anglès), (2) fallback per
  `MEPSystemClassification`. Cobreix Saneament/Fontaneria/PCI.
- `LeerConSugerencias(doc)` — Combina guardat + suggeriments per als no
  classificats. És el que mostra la UI d'Ajustos.
- `ExportarParaPreset` / `ImportarDesdePreset` — API preparada per a futur
  format `.e3gpreset` (fora d'abast actual).

**`FilaClasificacion`** — ViewModel per a cada fila del DataGrid (una per
`PipingSystemType`). Conté nom, classificació Revit, categoria actual, flags
`EsSugerencia`/`EsNuevo` i color `#RRGGBB` extret de `LineColor`.

**`FilaClasificacionBuilder.Construir()`** — Construeix la llista de files
combinant guardat + suggeriments, ordenada per nom.

**Nota sobre `MEPSystemClassification.StormDrain`:** Aquest valor no existeix
a la versió de `RevitAPI.dll` del projecte (Revit 2025). El `case` pluvials
del switch s'ha eliminat — la detecció de pluvials es fa per heurística de nom
(`PLUVIAL`, `STORM`, `RAIN`) que va abans del fallback.

---

	- Canvis a `AjustesWindow.xaml`

**Sidebar (grup GENERAL):** Afegit botó `BtnNavClassificacio` (🗂️ Classificació
sistemes) just després de Nomenclatura.

**Àrea de contingut:** Afegit `<Border x:Name="SecClassificacio">` amb:
- Capçalera + comptador `LblContadorClassificacio` ("X de Y classificats").
- Avís `PnlClassifAviso` (visible quan no hi ha document actiu).
- `DataGrid` `DgClassificacioSistemes` amb tres columnes: nom del tipus
  (text, read-only), classificació Revit (text, read-only), categoria E3G
  (`DataGridComboBoxColumn`, editable).
- Botó `BtnRecalcularSugerencias` al peu.

S'ha optat per `DataGridTextColumn` i `DataGridComboBoxColumn` estàndard
en lloc de `DataGridTemplateColumn` amb converters, per evitar dependències
de `HexToColorConverter` que no existia encara al projecte.

	- Canvis a `AjustesWindow.xaml.cs`

**Nous `using`:** Afegits àlies explícits per resoldre conflictes entre
`Autodesk.Revit.DB.Color` vs `System.Windows.Media.Color`,
`Autodesk.Revit.UI.ComboBox` vs `System.Windows.Controls.ComboBox`, i
`Visibility` (WPF vs Revit). Àlies usats: `MediaColor`, `MediaColors`,
`MediaBrush`, `MediaColorConverter`, `WpfVisibility`, `WpfComboBox`,
`WpfComboBoxItem`.

**Nou camp `_uiApp` (UIApplication):** El constructor ara accepta
`UIApplication uiApp = null` per poder accedir a
`_uiApp.ActiveUIDocument.Document` des de la UI sense ExternalEvent (lectura
pura, sense transacció).

**Nou camp `_clasificacioService`:** Instància de
`ClasificacionSistemasService`, creada al camp directament.

**`BtnNavClassificacio_Click`:** Navega a `SecClassificacio` i crida
`CarregarClassificacio()`.

**`CarregarClassificacio()`:** Mètode central que:
1. Comprova si hi ha document actiu. Si no, mostra `PnlClassifAviso`.
2. Crida `FilaClasificacionBuilder.Construir()` per obtenir les files.
3. Assigna `ItemsSource` al DataGrid.
4. Configura la `DataGridComboBoxColumn` amb `CategoriasCatalogo.Todas`
   (`DisplayMemberPath="Etiqueta"`, `SelectedValuePath="Categoria"`).
5. Actualitza el comptador (exclou `SinAsignar` i `Ignorar` del recompte
   de "classificats").

**`BtnRecalcularSugerencias_Click`:** Simplement crida `CarregarClassificacio()`
per regenerar la taula des del model.

**`OcultarTotesSeccions`:** Afegit `SecClassificacio` a la llista.



#### Deute tècnic generat

| Prioritat | Tasca |
|---|---|
| P1 | Implementar el botó "Desar" a `SecClassificacio` que cridi `Guardar()` dins d'una `Transaction` via ExternalEvent |
| P2 | Passar `UIApplication` al constructor d'`AjustesWindow` des de `MainWindow` / `E3G_Saneamiento.cs` per activar la càrrega real de dades |
| P3 | Substituir les columnes `DataGridTextColumn` per `DataGridTemplateColumn` amb color del sistema i badge "suggeriment" (requereix `HexToColorConverter`) |












