# Parche para AjustesWindow.xaml.cs

> **No aplicado automáticamente.** Este archivo describe los cambios que hay que hacer a mano sobre `C:\Dev\E3G_Saneamiento\AjustesWindow.xaml.cs` y `AjustesWindow.xaml`. Antes de tocar el original, **haz copia con sufijo `.bak`**.

---

## Objetivo

Cablear la nueva sección **Ajustes › Classificació de sistemes** (definida en `AjustesClasificacionSistemas_section.xaml`) al servicio `ClasificacionSistemasService` (definido en `ClasificacionSistemas.cs`).

El comportamiento esperado:

1. Al **entrar** en la sección, leer el `.rvt` activo, mostrar lo guardado y rellenar los huecos con auto-sugerencias (silenciosamente, sin botón).
2. Al cambiar una categoría en el ComboBox, actualizar el contador "X de Y classificats" y dejar el cambio en memoria.
3. Al **guardar** Ajustes (botón Save global de la ventana), persistir la tabla en el `.rvt` mediante ExtensibleStorage, dentro de una `Transaction`.
4. Al **cancelar**, descartar los cambios en memoria.

---

## Paso 1 — XAML: insertar los dos bloques

Coger el contenido de `AjustesClasificacionSistemas_section.xaml` y aplicar:

### Bloque A (botón sidebar)

Localiza en `AjustesWindow.xaml` el grupo `GENERAL` del sidebar — busca el botón existente `Nomenclatura`. Justo después, inserta:

```xml
<Button x:Name="BtnNavClassificacio"
        Style="{StaticResource SettingsNavBtn}"
        Content="Classificació de sistemes"
        Click="BtnNavClassificacio_Click"/>
```

### Bloque B (sección de contenido)

Localiza en el StackPanel de la columna derecha el `<Border x:Name="SecSaneament" ...>`. Justo **antes** de él, pega el bloque `<Border x:Name="SecClassificacio" ...>` completo del archivo `AjustesClasificacionSistemas_section.xaml`.

### Page.Resources

Añadir dentro de `<Page.Resources>` (los dos converters):

```xml
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
<local:HexToColorConverter    x:Key="HexToColorConverter"/>
```

El primero es estándar de WPF y probablemente ya existe — si está, no duplicar.

El segundo es nuevo: se define en `HexToColorConverter.cs` (archivo nuevo, ya entregado en `codigo/`). Convierte el hex `"#RRGGBB"` que `FilaClasificacion.ColorRevit` rellena leyendo `PipingSystemType.LineColor` en un `System.Windows.Media.Color` para alimentar el `SolidColorBrush` del cuadrito de color del DataGrid.

Para que la sintaxis `local:HexToColorConverter` resuelva, en la etiqueta raíz `<Page>` debe estar el namespace:

```xml
xmlns:local="clr-namespace:E3G.Saneamiento"
```

(probablemente ya está; si no, añadirlo).

---

## Paso 2 — code-behind: añadir using y campos

En la cabecera de `AjustesWindow.xaml.cs`, asegurar:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
// (los using de E3G.Saneamiento ya deberían estar)
```

Dentro de la clase `AjustesWindow` (o equivalente), añadir campos:

```csharp
// === Clasificación de sistemas (nuevo) ============================
private readonly IClasificacionSistemasService _svcClassif = new ClasificacionSistemasService();
private List<FilaClasificacion> _filasClassif = new List<FilaClasificacion>();

/// <summary>
/// Document Revit activo. Inyectado desde fuera (ver Paso 3).
/// </summary>
public Document DocActivo { get; set; }

/// <summary>
/// Disparador de la transacción de guardado. Inyectado desde fuera.
/// Ej: () => new Transaction(DocActivo, "E3G — Guardar clasificación")
/// </summary>
public Func<Document, Transaction> CrearTransaccion { get; set; }

/// <summary>
/// Categorías que rellena el ComboBox de cada fila del DataGrid.
/// La binding del DataTemplate las busca en este property por reflection.
/// </summary>
public IReadOnlyList<CategoriaInfo> CategoriasDisponibles { get; }
    = CategoriasCatalogo.Todas;
```

> **Notas sobre el binding del ComboBox:** el `DataTemplate` del DataGrid usa
> `RelativeSource AncestorType=Page` para encontrar `CategoriasDisponibles`.
> Si tu `AjustesWindow` deriva de `Window` o `UserControl` en vez de `Page`,
> ajusta el `AncestorType` a `Window` o `UserControl` correspondientemente.

---

## Paso 3 — Inyección del Document desde el caller

Hoy `AjustesWindow` se abre desde `MainWindow` (probablemente con `new AjustesWindow()` o similar). Hay que pasarle el `Document` activo.

### Opción A — Constructor sobrecargado (recomendado)

Añadir constructor:

```csharp
public AjustesWindow(UIApplication uiApp) : this()
{
    DocActivo = uiApp?.ActiveUIDocument?.Document;
    CrearTransaccion = (doc) => new Transaction(doc, "E3G — Guardar clasificació de sistemes");
}
```

Y en el caller (probablemente `MainWindow.xaml.cs`), donde se abre Ajustes:

```csharp
// Antes:
var ajustes = new AjustesWindow();

// Después:
var ajustes = new AjustesWindow(_uiApp); // _uiApp = la UIApplication que recibe el plugin
```

### Opción B — Property después de construir

Si no quieres tocar constructores:

```csharp
var ajustes = new AjustesWindow();
ajustes.DocActivo = _uiApp.ActiveUIDocument?.Document;
ajustes.CrearTransaccion = (doc) => new Transaction(doc, "E3G — Guardar clasificació de sistemes");
ajustes.ShowDialog(); // o el método que se use
```

---

## Paso 4 — Handler del botón de navegación

```csharp
private void BtnNavClassificacio_Click(object sender, RoutedEventArgs e)
{
    // Ocultar las otras secciones (mismo patrón que el resto de
    // BtnNavXxx_Click ya existentes en este archivo — copiar el patrón).
    OcultarTodasLasSecciones();
    SecClassificacio.Visibility = Visibility.Visible;

    // Cargar la tabla
    CargarTablaClasificacion();
}

private void OcultarTodasLasSecciones()
{
    // Si en el archivo ya existe un método con este nombre, NO lo dupliques —
    // sólo añade la línea SecClassificacio.Visibility = Collapsed; al método
    // existente. Aquí lo dejo como referencia:
    SecIdioma.Visibility       = Visibility.Collapsed;
    SecUnitats.Visibility      = Visibility.Collapsed;
    SecConfirmacions.Visibility= Visibility.Collapsed;
    SecNomenclatura.Visibility = Visibility.Collapsed;
    SecClassificacio.Visibility= Visibility.Collapsed; // ← NUEVA LÍNEA
    SecSaneament.Visibility    = Visibility.Collapsed;
    SecFontaneria.Visibility   = Visibility.Collapsed;
}
```

---

## Paso 5 — Carga de la tabla

```csharp
private void CargarTablaClasificacion()
{
    // 1) Sin doc activo → mostrar aviso amarillo y vaciar grid
    if (!_svcClassif.IsAvailable(DocActivo))
    {
        PnlClassifAviso.Visibility = Visibility.Visible;
        LblClassifAviso.Text       = "Obre un projecte de Revit per editar la classificació de sistemes.";
        DgClassificacioSistemes.ItemsSource = null;
        LblContadorClassificacio.Text = "0 de 0 classificats";
        return;
    }

    // 2) Construir las filas (mezcla guardado + sugerencias)
    _filasClassif = FilaClasificacionBuilder.Construir(DocActivo, _svcClassif);

    // 3) Modelo vacío → aviso de "no hi ha tipus al projecte"
    if (_filasClassif.Count == 0)
    {
        PnlClassifAviso.Visibility = Visibility.Visible;
        LblClassifAviso.Text       = "Aquest projecte no té cap PipingSystemType.";
        DgClassificacioSistemes.ItemsSource = null;
        LblContadorClassificacio.Text = "0 de 0 classificats";
        return;
    }

    // 4) Caso normal
    PnlClassifAviso.Visibility = Visibility.Collapsed;
    DgClassificacioSistemes.ItemsSource = _filasClassif;
    ActualizarContadorClasificacion();
}

private void ActualizarContadorClasificacion()
{
    int total = _filasClassif?.Count ?? 0;
    int classif = _filasClassif?.Count(f =>
        f.CategoriaActual != CategoriaSistema.SinAsignar) ?? 0;
    LblContadorClassificacio.Text = $"{classif} de {total} classificats";
}
```

> Para que el contador se actualice en vivo al cambiar el ComboBox, hay dos
> opciones: (a) implementar `INotifyPropertyChanged` en `FilaClasificacion`
> y suscribirte al evento, o (b) refrescar el contador desde el handler
> `DgClassificacioSistemes.CellEditEnding`. La opción (b) es más simple:

```csharp
// En el XAML del DataGrid:
//     CellEditEnding="DgClassificacioSistemes_CellEditEnding"

private void DgClassificacioSistemes_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
{
    Dispatcher.BeginInvoke((Action)ActualizarContadorClasificacion);
}
```

---

## Paso 6 — Recalcular sugerencias

```csharp
private void BtnRecalcularSugerencias_Click(object sender, RoutedEventArgs e)
{
    if (!_svcClassif.IsAvailable(DocActivo)) return;

    // Para los tipos en SinAsignar, regenerar sugerencias usando la heurística
    var sugerencias = _svcClassif.AutoSugerir(DocActivo);
    foreach (var fila in _filasClassif)
    {
        if (fila.CategoriaActual == CategoriaSistema.SinAsignar
            && sugerencias.TryGetValue(fila.TipoSistemaId, out var sug))
        {
            fila.CategoriaActual = sug;
            fila.EsSugerencia = true;
        }
    }
    DgClassificacioSistemes.Items.Refresh();
    ActualizarContadorClasificacion();
}
```

---

## Paso 7 — Guardado

Localiza el método/handler del botón **Guardar** global de la ventana (probablemente `BtnGuardar_Click` o `OnSave`). Al final de ese handler, justo antes de cerrar la ventana, añadir:

```csharp
// === Guardar clasificación de sistemas ===========================
if (_svcClassif.IsAvailable(DocActivo) && _filasClassif != null && _filasClassif.Count > 0)
{
    var asignaciones = _filasClassif
        .Where(f => f.CategoriaActual != CategoriaSistema.SinAsignar) // SinAsignar no se persiste
        .ToDictionary(f => f.TipoSistemaId, f => f.CategoriaActual);

    var crearTx = CrearTransaccion ??
        ((doc) => new Transaction(doc, "E3G — Guardar clasificació de sistemes"));

    using (var tx = crearTx(DocActivo))
    {
        tx.Start();
        try
        {
            _svcClassif.Guardar(DocActivo, asignaciones);
            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.RollBack();
            // Loguear con el mecanismo del proyecto (ver Verbose en AjustesConfig)
            System.Diagnostics.Debug.WriteLine(
                "[E3G] Error guardando clasificación de sistemas: " + ex.Message);
            MessageBox.Show(
                "No s'ha pogut desar la classificació de sistemes:\n\n" + ex.Message,
                "E3G — Ajustes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
```

---

## Paso 8 — Cancelar (opcional pero recomendado)

En el handler del botón **Cancel·lar**, los cambios en `_filasClassif` se descartan automáticamente porque no se ha llamado a `Guardar`. No hay que hacer nada especial — pero si quieres ser explícito:

```csharp
// En BtnCancelar_Click (o equivalente):
_filasClassif = null; // limpieza, opcional
```

---

## Resumen de archivos tocados

```
AjustesWindow.xaml          ← + 1 botón sidebar, + 1 Border SecClassificacio,
                              + 1 BoolToVisibilityConverter en Page.Resources

AjustesWindow.xaml.cs       ← + using's, + 4 campos/properties,
                              + 1 ctor sobrecargado (Opción A),
                              + 5 métodos:
                                    BtnNavClassificacio_Click
                                    CargarTablaClasificacion
                                    ActualizarContadorClasificacion
                                    DgClassificacioSistemes_CellEditEnding
                                    BtnRecalcularSugerencias_Click
                              + bloque de guardado en BtnGuardar_Click

ClasificacionSistemas.cs    ← archivo nuevo (ya entregado)

MainWindow.xaml.cs          ← 1 línea: pasar UIApplication al ctor de AjustesWindow
```

Sin tocar `SaneamientoWindow.cs`, `FontaneriaWindow.cs`, `EsquemaPrincipi.cs` en este parche — esos cambian con el parche `E3G_Saneamiento_PARCHE.md`.
