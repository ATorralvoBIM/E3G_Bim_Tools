# Guía de integración — Módulo Presupuesto / BC3

Este folder contiene los 16 archivos del módulo `E3G.Saneamiento.Presupuesto`
diseñado en `DISENO_P2_Mapping_Partidas_y_Export_BC3.docx`.

## 0. Requisitos previos

- El módulo `Classificacio de sistemes` (propuesta anterior) integrado y
  funcionando — este código depende de:
  - `CategoriaSistema` (enum)
  - `ClasificacionSistemasService.ObtenerCategoria(ElementId)`
- `HexToColorConverter` y `BoolToVisibilityConverter` declarados en
  `Page.Resources` de `AjustesWindow.xaml`.

## 1. Estructura de archivos

```
E3G_Saneamiento/Presupuesto/
├── Modelo/
│   ├── Partida.cs               (incluye CatalogoPartidas)
│   ├── ISubFiltro.cs            (3 implementaciones P2)
│   ├── ICriterio.cs             (2 implementaciones P2)
│   ├── Regla.cs
│   ├── Plantilla.cs
│   └── MedicionPorPartida.cs    (incluye LineaMedicion + MedicionAcumulador)
├── Storage/
│   ├── PartidasStorage.cs       (ExtensibleStorage + JSON polimórfico)
│   └── PlantillaPorDefecto.cs
├── Bc3/
│   ├── Bc3Encoding.cs
│   ├── Bc3Reader.cs
│   └── Bc3Writer.cs
├── Motor/
│   └── EvaluadorReglas.cs
├── Ui/
│   ├── PartidasViewModel.cs
│   ├── PartidasSection.xaml         (snippet — copiar a AjustesWindow.xaml)
│   ├── ReglaEditorDialog.xaml
│   └── ReglaEditorDialog.xaml.cs
└── Commands/
    └── ExportarBc3Command.cs
```

## 2. Pasos de integración

### 2.1. Generar GUID propio del Schema

En `PartidasStorage.cs`, sustituir el `SchemaGuid` ilustrativo por uno
**propio**. En Visual Studio: `Tools → Create GUID → Format Registry`.
Una vez fijado, NO se cambia jamás.

### 2.2. Registrar codepage 1252 en arranque

En el `OnStartup` del `IExternalApplication` (o el equivalente que ya exista):

```csharp
E3G.Saneamiento.Presupuesto.Bc3.Bc3Encoding.RegistrarCodepageProvider();
```

Sin esto, el lector y el escritor BC3 fallan en .NET 5+ con
`NotSupportedException` al pedir `Encoding.GetEncoding(1252)`.

### 2.3. Insertar el bloque XAML en AjustesWindow.xaml

`PartidasSection.xaml` contiene dos bloques:

- **Bloque A**: botón sidebar. Insertar dentro del `<StackPanel>` del grupo
  GENERAL del sidebar de Ajustes, después del botón "Classificació de sistemes".
- **Bloque B**: el `<Border x:Name="SecPartides">`. Insertar en el
  `<StackPanel>` de la columna derecha, después de `SecClassificacio`.

### 2.4. Code-behind de AjustesWindow.xaml.cs

Añadir los handlers correspondientes:

```csharp
private PartidasViewModel _vmPartides;

// Inicialización
private void InicializarPartides()
{
    _vmPartides = new PartidasViewModel(_doc);
    SecPartides.DataContext = _vmPartides;
    ActualizarUiCatalogo();
}

private void ActualizarUiCatalogo()
{
    LblCatalogoNombre.Text = _vmPartides.CatalogoNombre;
    LblCatalogoStats.Text  = _vmPartides.CatalogoCargado
        ? $"{_vmPartides.CatalogoCantidad:#,##0} partides · importat el {_vmPartides.CatalogoFechaImport}"
        : "Importa un BC3 per començar (BEDEC, Precio Centro, custom)…";
    LblContadorReglas.Text = $"{_vmPartides.TotalReglas} regles · {_vmPartides.CategoriasConReglas} categories actives";
}

// Botón sidebar
private void BtnNavPartides_Click(object sender, RoutedEventArgs e)
{
    OcultarTodasLasSecciones();
    SecPartides.Visibility = Visibility.Visible;
}

// Importar BC3
private void BtnImportarBc3_Click(object sender, RoutedEventArgs e)
{
    var ofd = new Microsoft.Win32.OpenFileDialog
    {
        Filter = "Arxiu BC3 (*.bc3)|*.bc3|Tots (*.*)|*.*",
        Title = "Importar catàleg BC3"
    };
    if (ofd.ShowDialog(this) != true) return;

    try
    {
        _vmPartides.ImportarBc3(ofd.FileName);
        ActualizarUiCatalogo();
    }
    catch (Exception ex)
    {
        MessageBox.Show(this, $"No s'ha pogut llegir el BC3:\n\n{ex.Message}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void BtnReimportarBc3_Click(object s, RoutedEventArgs e) => BtnImportarBc3_Click(s, e);

private void BtnLimpiarCatalogo_Click(object s, RoutedEventArgs e)
{
    var resp = MessageBox.Show(this,
        "Vols buidar el catàleg actual? Les regles existents es mantindran però " +
        "quedaran sense partida vàlida fins que importis un nou BC3.",
        "Buidar catàleg", MessageBoxButton.YesNo, MessageBoxImage.Question);
    if (resp != MessageBoxResult.Yes) return;
    _vmPartides.LimpiarCatalogo();
    ActualizarUiCatalogo();
}

// Añadir regla
private void BtnAnadirRegla_Click(object sender, RoutedEventArgs e)
{
    var btn = sender as Button;
    if (btn?.Tag is not CategoriaSistema cat) return;

    var dlg = new ReglaEditorDialog(_vmPartides.ObtenerPlantillaActual().Catalogo, null, cat);
    dlg.Owner = this;
    if (dlg.ShowDialog() == true)
        _vmPartides.AnadirRegla(dlg.ReglaEditada);
}

// Editar regla
private void BtnEditarRegla_Click(object sender, RoutedEventArgs e)
{
    if ((sender as Button)?.Tag is not Guid id) return;
    var regla = _vmPartides.ObtenerPlantillaActual()
                           .Reglas.FirstOrDefault(r => r.Id == id);
    if (regla == null) return;

    var dlg = new ReglaEditorDialog(_vmPartides.ObtenerPlantillaActual().Catalogo,
                                     regla, regla.Categoria);
    dlg.Owner = this;
    if (dlg.ShowDialog() == true)
        _vmPartides.ActualizarRegla(dlg.ReglaEditada);
}

// Eliminar regla
private void BtnEliminarRegla_Click(object sender, RoutedEventArgs e)
{
    if ((sender as Button)?.Tag is not Guid id) return;
    var resp = MessageBox.Show(this, "Eliminar aquesta regla?",
        "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
    if (resp != MessageBoxResult.Yes) return;
    _vmPartides.EliminarRegla(id);
}

private void BtnAplicarSugeridas_Click(object s, RoutedEventArgs e) =>
    _vmPartides.AplicarReglasSugeridasSaneamiento();
```

### 2.5. Persistir al cerrar Ajustes

En el handler de `BtnAceptar_Click` / `BtnGuardar_Click`:

```csharp
using (var tx = new Transaction(_doc, "Guardar configuració Partides BC3"))
{
    tx.Start();
    _vmPartides?.GuardarEnDocumento();
    tx.Commit();
}
```

### 2.6. Botón Exportar BC3 en SaneamientoWindow

En el XAML del panel Saneamiento, añadir el botón junto al `BtnCalcular`:

```xml
<Button x:Name="BtnExportarBc3"
        Content="Exportar BC3"
        Style="{StaticResource AccentButton}"
        Click="BtnExportarBc3_Click"
        Margin="8,0,0,0"/>
```

Y el handler:

```csharp
private void BtnExportarBc3_Click(object sender, RoutedEventArgs e)
{
    var cmd = new ExportarBc3Command(_uiApp, _clasifService);
    cmd.Ejecutar(this);
}
```

## 3. Test del Sprint 1 (sin UI, en consola)

Antes de tocar UI, validar el ciclo I/O en LINQPad o en un Test project:

```csharp
var catalogo = Bc3Reader.LeerCatalogo(@"C:\downloads\BEDEC_2025.bc3");
Console.WriteLine($"Llegides {catalogo.Partidas.Count} partides");

var partida = catalogo.Buscar("ED15B871");
var medicion = new MedicionPorPartida
{
    Partida = partida,
    CapituloDestino = "02SAN",
    CantidadTotal = 62.40m,
    Lineas = new List<LineaMedicion>
    {
        new() { Comentario = "Bajante B-01", Unidades = 1, Longitud = 14.20m },
        new() { Comentario = "Bajante B-02", Unidades = 1, Longitud = 28.50m },
        new() { Comentario = "Bajante B-03", Unidades = 1, Longitud = 19.70m }
    }
};

var writer = new Bc3Writer();
writer.Escribir(@"C:\temp\test.bc3",
    Plantilla.Vacia().ConCatalogo(catalogo),
    new[] { medicion },
    new DatosCabecera { ProyectoNombre = "TEST" });
```

Abrir `test.bc3` en Presto/Arquímedes. Si abre limpio, el ciclo I/O está
validado.

## 4. Cosas que aún NO hace este código (por diseño)

- Descomposición rica `~D` (mano obra + materiales) — eso es P3/P4.
- Editor de partidas — esto es lectura/escritura, no editor de BBDD.
- Sincronización con Presto/Arquímedes en vivo — eso es P5.
- Export `.e3gtpl` portable — eso es P3.
- Editor de reglas tipo query builder con AND/OR — sólo los 3 sub-filtros
  simples. Los stubs P3 quedan comentados en `ISubFiltro.cs` y `ICriterio.cs`.

## 5. Riesgos conocidos para Sprint 1

- **Codepage 1252**: hay que llamar a `Bc3Encoding.RegistrarCodepageProvider()`
  en el arranque (paso 2.2).
- **BEDEC con caracteres no estándar**: si un BC3 no abre bien, el problema
  suele estar en el subcampo 7 del `~V`. `Bc3Encoding.Detectar` cubre los
  casos comunes pero puede haber sorpresas.
- **Tamaño del JSON con un BEDEC entero (≈25k partidas)**: el ExtensibleStorage
  soporta strings grandes pero el .rvt crece. Si pasa de 10MB consultamos si
  vale la pena externalizar el catálogo.
