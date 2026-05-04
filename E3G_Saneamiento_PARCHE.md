# Parche para E3G_Saneamiento.cs

> **No aplicado automáticamente.** Antes de tocar el original, **haz copia con sufijo `.bak`**.

---

## Objetivo

Sustituir la lógica hardcodeada `sysType.Contains("10_10")` por consultas al servicio `ClasificacionSistemasService` (definido en `ClasificacionSistemas.cs`).

Hay **3 sitios** afectados en `E3G_Saneamiento.cs` y **1 más en `EsquemaPrincipi.cs`** (sólo un comentario, no funcional). Tras este parche, el comportamiento de las funciones de medición de Saneamiento queda gobernado por la tabla de Ajustes › General › Classificació de sistemes.

---

## Sitio 1 — Líneas 208-214 — `EsSistemaValido`

### Estado actual

```csharp
public static bool EsSistemaValido(string sysType)
{
    if (string.IsNullOrEmpty(sysType)) return false;
    return sysType.Contains("10_10") || sysType.Contains("Fecal")
        || sysType.Contains("10_30") || sysType.Contains("Gris")
        || sysType.Contains("Grey");
}
```

### Cambio propuesto

Conservar la firma original por compatibilidad (marcada como `Obsolete`), y añadir una nueva sobrecarga que consulta el servicio:

```csharp
/// <summary>
/// LEGACY — hace match por nombre. Mantener mientras haya código que aún
/// la llame, pero migrar a EsSistemaFecalesOGrises(doc, svc, tipoId).
/// </summary>
[System.Obsolete("Usar EsSistemaFecalesOGrises(doc, svc, tipoSistemaId). Esta sobrecarga " +
                 "se eliminará cuando se haya completado la migración a clasificación por ElementId.")]
public static bool EsSistemaValido(string sysType)
{
    if (string.IsNullOrEmpty(sysType)) return false;
    return sysType.Contains("10_10") || sysType.Contains("Fecal")
        || sysType.Contains("10_30") || sysType.Contains("Gris")
        || sysType.Contains("Grey");
}

/// <summary>
/// Devuelve true si el PipingSystemType cuyo Id es <paramref name="tipoSistemaId"/>
/// está clasificado en Sanejament — Fecals o Sanejament — Grises (los dos
/// que históricamente se medían bajo "saneamiento residual").
/// </summary>
public static bool EsSistemaFecalesOGrises(
    Document doc,
    IClasificacionSistemasService svcClassif,
    ElementId tipoSistemaId)
{
    if (svcClassif == null || tipoSistemaId == null) return false;
    return svcClassif.EsCategoria(doc, tipoSistemaId, CategoriaSistema.SaneamientoFecales)
        || svcClassif.EsCategoria(doc, tipoSistemaId, CategoriaSistema.SaneamientoGrises);
}
```

> Si los callers de `EsSistemaValido(string)` lo único que tienen a mano es el nombre del sistema, primero deben resolverlo a `ElementId`. Para no propagar el cambio en todos los callers de un golpe, deja la legacy y migra los callers uno por uno.

---

## Sitio 2 — Líneas 289-296 — pipes en grupo

### Estado actual

```csharp
var pipesEnGrupo = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_PipeCurves)
    .WhereElementIsNotElementType()
    .Cast<Element>()
    .Where(e => {
        var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
        return sys.Contains("10_10") && e.GroupId != ElementId.InvalidElementId;
    });
```

### Cambio propuesto

Resolver el set de IDs de tipos clasificados como Fecales **una sola vez** (fuera del lambda), y usar `BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM` para obtener el ElementId del tipo de sistema en cada pipe.

```csharp
// === Antes del FilteredElementCollector ==========================
// Resolver una sola vez los IDs de PipingSystemType clasificados como Fecales.
// _svcClassif debe ser un campo/instancia accesible (ver bloque "Inyección"
// más abajo).
var idsFecales = new HashSet<ElementId>(
    _svcClassif.ObtenerSistemas(doc, CategoriaSistema.SaneamientoFecales));

var pipesEnGrupo = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_PipeCurves)
    .WhereElementIsNotElementType()
    .Cast<Element>()
    .Where(e => {
        var tipoId = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
        return tipoId != null
            && tipoId != ElementId.InvalidElementId
            && idsFecales.Contains(tipoId)
            && e.GroupId != ElementId.InvalidElementId;
    });
```

---

## Sitio 3 — Líneas 437-444 — pipes sin grupo (servicio común)

### Estado actual

```csharp
var pipesServComun = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_PipeCurves)
    .WhereElementIsNotElementType()
    .Cast<Element>()
    .Where(e => {
        var sys = e.LookupParameter("Nombre de sistema")?.AsValueString() ?? "";
        return sys.Contains("10_10") && e.GroupId == ElementId.InvalidElementId;
    });
```

### Cambio propuesto

Reutilizar el mismo `idsFecales` calculado arriba (no recalcular):

```csharp
var pipesServComun = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_PipeCurves)
    .WhereElementIsNotElementType()
    .Cast<Element>()
    .Where(e => {
        var tipoId = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
        return tipoId != null
            && tipoId != ElementId.InvalidElementId
            && idsFecales.Contains(tipoId)
            && e.GroupId == ElementId.InvalidElementId;
    });
```

> Si los Sitios 2 y 3 viven en el mismo método, calcula `idsFecales` una vez al principio y reutiliza. Si están en métodos distintos, calcúlalo en cada uno (la consulta al servicio es barata: lee el `DataStorage`, filtra y devuelve).

---

## Inyección del servicio en el motor de medición

El motor que contiene los Sitios 2 y 3 (la clase que orquesta la medición de Saneamiento — métodos `MedirFecales`, `MedirPluviales`, etc.) hoy no tiene referencia al servicio. Hay dos formas de pasársela:

### Opción A — Campo de instancia (si el motor es una clase)

```csharp
public class MotorSaneamiento  // o el nombre real
{
    private readonly IClasificacionSistemasService _svcClassif;

    public MotorSaneamiento(IClasificacionSistemasService svcClassif = null)
    {
        _svcClassif = svcClassif ?? new ClasificacionSistemasService();
    }

    // ... resto de la clase
}
```

### Opción B — Parámetro de método (si los métodos son estáticos)

Si las funciones de medición son static, añadir un parámetro:

```csharp
public static ResultadoSaneamiento MedirFecales(
    Document doc,
    IClasificacionSistemasService svcClassif,
    /* resto de parámetros existentes */)
{
    // ...
}
```

Y en el caller (`SaneamientoWindow.xaml.cs`, donde se dispara la medición):

```csharp
var svcClassif = new ClasificacionSistemasService();
var resultado = MedirFecales(doc, svcClassif, /* ... */);
```

---

## Sitio 4 — `EsquemaPrincipi.cs` línea 644 (cosmético, no funcional)

Es un comentario:
```csharp
// "E3G — Esquema Principi — 10_10_Fecals — 20260415_1423_01"
```

Si lo dejas como ejemplo, vale. Si lo quieres genérico, sustituye por:
```csharp
// "E3G — Esquema Principi — <CodiSistema>_Fecals — 20260415_1423_01"
```

---

## Migración progresiva — orden recomendado

1. Añadir `ClasificacionSistemas.cs` al proyecto. Compilar. (No debe romper nada — es código nuevo.)
2. Aplicar el parche XAML/code-behind de Ajustes (`AjustesWindow_PARCHE.md`). Compilar y probar la UI con un proyecto cualquiera.
3. Aplicar el Sitio 1 de este parche. Compilar. (Aún no se usa la nueva sobrecarga — sólo está disponible.)
4. Aplicar Sitios 2 y 3 — el motor empieza a leer del servicio. **Probar a fondo:**
   - Proyecto SIN clasificación guardada → la medición no devuelve nada (esperado, pendiente que el usuario configure Ajustes).
   - Proyecto CON clasificación guardada → la medición da los mismos resultados que antes para los tipos clasificados como Fecales/Grises.
5. Una vez que todos los callers de `EsSistemaValido(string)` estén migrados, eliminar la versión legacy y el atributo `[Obsolete]`.
6. Aplicar el mismo patrón a Pluviales (`10_20`), AFS (`20_10`), ACS (`20_20`), AR (`20_30`) cuando los respectivos paneles lo necesiten — es repetir Sitios 2/3 con la categoría correspondiente.

---

## Pruebas mínimas a hacer antes de mergear

**Caso 1 — Proyecto sin tipos de sistema (vacío)**
- Abrir Ajustes › Classificació de sistemes → debe aparecer aviso "Aquest projecte no té cap PipingSystemType".
- Disparar medición de Fecales → 0 resultados, sin excepción.

**Caso 2 — Proyecto con tipos pero sin clasificar nada**
- Abrir Ajustes → la tabla muestra todos los tipos con sugerencias.
- Cerrar SIN guardar → al volver a abrir, las sugerencias se recalculan (no se persistieron).
- Disparar medición → 0 resultados (no hay nada guardado).

**Caso 3 — Proyecto con clasificación parcial**
- Marcar 1 tipo como Fecales y guardar.
- Disparar medición → cuenta sólo los pipes de ese tipo.

**Caso 4 — Tipo borrado del modelo después de guardar**
- Guardar 3 tipos en distintas categorías.
- Borrar uno de los tipos del Project Browser.
- Re-abrir Ajustes → el huérfano desaparece silenciosamente (sin error).
- Disparar medición → no falla.

**Caso 5 — Cambio de proyecto activo**
- Abrir proyecto A, configurar.
- Cambiar al proyecto B (otro .rvt).
- Abrir Ajustes › Classificació de sistemes → muestra la configuración de B (independiente de A).

**Caso 6 — Múltiples sesiones**
- Configurar y guardar en sesión 1.
- Cerrar Revit por completo.
- Abrir el mismo .rvt en sesión 2 → la clasificación persiste (vino con el .rvt).

---

## Resumen de archivos tocados

```
E3G_Saneamiento.cs    ← + sobrecarga EsSistemaFecalesOGrises (Sitio 1),
                          modificación de pipesEnGrupo (Sitio 2),
                          modificación de pipesServComun (Sitio 3),
                          + (opcional) campo _svcClassif en la clase del motor

EsquemaPrincipi.cs    ← (opcional) comentario actualizado en línea 644

SaneamientoWindow.xaml.cs ← (si motor estático) pasar svcClassif a las
                              llamadas de medición
```
