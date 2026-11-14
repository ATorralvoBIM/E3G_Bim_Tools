# Formato de Vinculación BC3-IFC para TCQ / Presto

> Documento entregable del **Sprint A2** — Investigación formato TCQ-BIM  
> E3G Enginyeria · e3gbimtools.RevitBC3 · Mayo 2026

---

## 1. Resumen ejecutivo

Tras la investigación mediante ingeniería inversa, consulta de documentación pública (ITeC,
RIB-Software/Presto, BuildingSMART) y análisis del repositorio, las conclusiones son:

| Pregunta | Resultado |
|----------|-----------|
| ¿Tiene TCQ un archivo externo de vinculación importable? | **No documentado públicamente.** La vinculación se almacena en la BD interna de TCQ. |
| ¿Lee TCQ los IfcGUIDs del campo comentario del `~M`? | **No.** TCQ no parsea el `~M` para auto-vincular. |
| ¿Define FIEBDC-3/2020 un campo específico para IfcGUID en `~M`? | **No.** El IfcGUID viaja como texto libre en el campo comentario (campo 2 del bloque de línea de desglose). |
| ¿Tiene Presto un mecanismo de vinculación importable? | **Sí, mediante JSON de configuración** (ver §4). |
| ¿Cuál es el mecanismo de auto-vinculación más viable para TCQ? | **PSETs en el IFC** (ver §5): TCQ lee parámetros del IFC y los asigna a fórmulas de partida. |

---

## 2. Cómo vincula TCQ internamente (ingeniería inversa)

### 2.1 Flujo manual dentro de TCQ

El módulo TCQ-BIM sigue estos pasos (fuente: manual TCQ-BIM Viewer 5.1 + blog MSI Digital Builders):

1. **BIM → Vinculación de objetos BIM con partidas de obra** → se indica la ruta del `.ifc`.
2. El árbol de la izquierda muestra todas las categorías IFC del modelo (en rojo = sin vincular).
3. Se selecciona un tipo IFC, se arrastra una partida del presupuesto a la ventana de vinculación.
4. Se asocia cada parámetro IFC (longitud, área, volumen…) al factor de la fórmula C#, D#, E#, F#.
5. **BIM → Importar mediciones multiplataforma** → TCQ calcula cantidades y las vuelca al presupuesto.

### 2.2 Dónde guarda TCQ la vinculación

La vinculación no se exporta a ningún archivo separado: se almacena en la base de datos
interna del proyecto TCQ (archivo `.tcq`, que es una BD propietaria). No existe ningún formato
público `.tcqbim`, `.bimlink` ni `.xml` documentado por ITeC para esta vinculación.

### 2.3 "Importar mediciones multiplataforma" — clave del flujo automatizable

TCQ lee propiedades IFC de cada elemento para calcular cantidades. Si el IFC contiene un
Property Set con el **código de partida BC3** como propiedad, TCQ puede asignarlo
automáticamente a la fórmula `C#` (código de unidad de obra). Esto es la puerta de entrada
para la automatización desde el add-in.

---

## 3. Situación del ~M en e3gbimtools (FIEBDC-3/2020 — corregido Sprint A2)

El `BC3WriterService.cs` emite registros `~M` con el siguiente formato extendido:

```
~M|CAP\SUBCAP\PARTIDA\||CantTotal||1\[Sistema · Nivel]#IfcGUID\1\cant\\|
```

> **Cambio Sprint A2:** el separador antes del IfcGUID es ahora `#` (antes era espacio).
> Esto sigue el estándar FIEBDC-3/2020 documentado por Presto/RIB Software:
> *"El valor del campo Mediciones.GuidElem se incluye en el comentario, separado por una '#',
> en las líneas de medición con origen Revit o IFC."*

Desglose del bloque de línea de desglose (6 campos separados por `\`):

| Campo | Valor | Descripción |
|-------|-------|-------------|
| Tipo  | `1`   | Línea normal |
| Comentario | `[Sistema · Nivel]#IfcGUID` | Texto libre + `#` + IfcGUID de 22 chars (estándar FIEBDC-3/2020) |
| Unidades | `1` | Nº de unidades del elemento |
| Longitud | `5.500` | Cantidad aportada por este elemento |
| Latitud | _(vacío)_ | No usado |
| Altitud | _(vacío)_ | No usado |

Cuando no hay prefijo de sistema/nivel, el comentario es simplemente `#IfcGUID`.

**Compatibilidad confirmada:**
- **Presto 21+**: parsea el campo `Mediciones.GuidElem` reconociendo el `#`; permite "Ir al elemento IFC" desde una línea de medición.
- **Arquimedes**: lee comentarios de `~M` estándar FIEBDC-3.
- **TCQ**: no parsea el `#IfcGUID` automáticamente, pero lo muestra en el desglose de medición. La vinculación TCQ se realiza vía IFC directo (ver §5).
- **Visor BC3 interno** (`Visor_BC3.html`): muestra el comentario completo incluyendo el GUID.

---

## 4. Presto: mecanismo de vinculación y JSON de configuración

### 4.1 Flujo en Presto

Presto IFC (módulo `IFCost`) lee directamente el archivo `.ifc` y:

1. Agrupa elementos por PSETs/propiedades elegidas por el usuario.
2. Asigna códigos de unidad de obra (partidas) a cada grupo.
3. Genera líneas de medición una por elemento (con dimensiones).
4. La vinculación "de vuelta" (del presupuesto al modelo) inserta propiedades en el IFC.

### 4.2 Archivo de configuración JSON de Presto

La configuración de criterios de medición se puede **exportar como JSON** y **reutilizar**
para medir versiones sucesivas del mismo modelo. Este es el mecanismo más cercano a un
"archivo de vinculación importable" en el ecosistema de software español.

Estructura simplificada del JSON de configuración de Presto:

```json
{
  "version": "1.0",
  "criterios": [
    {
      "clase_ifc": "IfcPipeSegment",
      "codigo_unidad_obra": "ED15B873",
      "filtros": [
        { "propiedad": "Pset_MEPServiceProperties.SystemType", "valor": "DomesticHotWater" }
      ],
      "dimensiones": {
        "C": "Qto_PipeSegmentBaseQuantities.Length"
      }
    }
  ]
}
```

Este JSON **no** mapea element-to-partida directamente; mapea *tipo IFC + filtros → código de partida*.
Para el sprint A3, el formato propuesto (§6) va más allá: mapea elemento a elemento con IfcGUID.

### 4.3 Compatibilidad BC3 en Presto

Presto genera mediciones compatibles con FIEBDC-3: importa el `.ifc`, genera el BC3 con
los códigos asignados, y los `~M` incluyen las propiedades IFC como dimensiones.
Presto **sí puede** leer el comentario `~M` para trazar de vuelta al elemento IFC si el
comentario contiene el IfcGUID y el modelo IFC está abierto en el visor de Presto.

---

## 5. Mecanismo de vinculación automática: PSETs en el IFC

### 5.1 Estrategia propuesta

La vía más interoperable (TCQ + Presto + herramientas openBIM) es **emitir en el IFC**
un Property Set específico con la información de presupuesto. Cuando el IFC ya contiene
el código de partida de cada elemento, TCQ puede usar "importar mediciones multiplataforma"
asignando esa propiedad al campo de código de unidad de obra, y la vinculación es automática.

### 5.2 Property Set propuesto: `E3G_Pset_BC3`

Añadir al IFC, en cada elemento medible, el siguiente PSET:

```
PSET: E3G_Pset_BC3
  BC3_Code        (STRING)  → Código de partida BC3 (ej. "ED15B873.001")
  BC3_Chapter     (STRING)  → Código de capítulo (ej. "02SAN")
  BC3_SubChapter  (STRING)  → Código de subcapítulo (ej. "02SAN.01")
  BC3_Quantity    (REAL)    → Cantidad aportada por este elemento (ej. 5.500)
  BC3_Unit        (STRING)  → Unidad de la partida (ej. "m")
  BC3_RuleId      (STRING)  → ID de la regla de mapeo en e3gbimtools (UUID)
```

### 5.3 Uso en TCQ

1. Exportar IFC desde Revit (con el add-in o manualmente).
2. El add-in escribe `E3G_Pset_BC3` en cada `IfcPipeSegment`, `IfcDuctSegment`, etc.
3. En TCQ → BIM → Importar mediciones multiplataforma:
   - Seleccionar el IFC.
   - Asignar la propiedad `E3G_Pset_BC3.BC3_Code` al campo de código de unidad de obra.
   - Asignar `E3G_Pset_BC3.BC3_Quantity` al campo `C#`.
   - TCQ genera automáticamente las mediciones por partida.

**Limitación:** Este flujo requiere que el usuario haga el paso de asignación de propiedades
a fórmulas en TCQ una sola vez (configuración guardada en el proyecto TCQ). Las
exportaciones sucesivas se actualizan automáticamente.

---

## 6. Formato JSON propuesto para el archivo de vinculación de e3gbimtools

Dado que TCQ no dispone de un formato externo importable, proponemos un archivo
propio con extensión **`.bimlink.json`** que:
- Pueda ser consumido por el add-in en exportaciones sucesivas.
- Sirva de trazabilidad y auditoría del proceso.
- Sea la base para generar el PSET en el IFC (Sprint A3/A4).

### 6.1 Esquema del archivo

```json
{
  "version": "1.0",
  "generator": "e3gbimtools.RevitBC3",
  "generated": "2026-05-23T10:30:00Z",
  "bc3File": "LFA-E3G-ZZZ-ZZZ-M3D-INS-001_20260523.bc3",
  "ifcModel": "LFA-E3G-ZZZ-ZZZ-M3D-INS-001.ifc",
  "partidas": [
    {
      "codigo": "ED15B873.001",
      "codigoCatalogo": "ED15B873",
      "resumen": "Tubería PP-RCT Ø40 mm - AFS (Edificio A)",
      "unidad": "m",
      "cantidad": 45.320,
      "capitulo": "02SAN",
      "subcapitulo": "02SAN.01",
      "elementos": [
        {
          "ifcGuid": "3evGS0iVf78wLzyovWD4b9",
          "elementId": 17916301,
          "cantidad": 5.500,
          "systemName": "10_10 AFS",
          "levelName": "P00.B1"
        },
        {
          "ifcGuid": "1Kh8nP2vDf4rQmBxTy6uWc",
          "elementId": 17916325,
          "cantidad": 0.703,
          "systemName": "10_10 AFS",
          "levelName": "P00.B1"
        }
      ]
    }
  ],
  "estadisticasGuid": {
    "totalElementos": 312,
    "desdeParametro": 0,
    "desdeCalculo": 312,
    "sinGuid": 0,
    "coberturaPorc": 100.0
  }
}
```

### 6.2 Campos del esquema

| Campo | Tipo | Obligatorio | Descripción |
|-------|------|-------------|-------------|
| `version` | string | ✅ | Versión del esquema (actualmente `"1.0"`) |
| `generator` | string | ✅ | Nombre del generador |
| `generated` | ISO 8601 | ✅ | Fecha y hora de generación |
| `bc3File` | string | ✅ | Nombre del archivo BC3 asociado (solo nombre, sin ruta) |
| `ifcModel` | string | ⚠️ opcional | Nombre del archivo IFC de referencia |
| `partidas[].codigo` | string | ✅ | Código final en el BC3 exportado (puede tener sufijo `.001`) |
| `partidas[].codigoCatalogo` | string | ✅ | Código original del catálogo BC3 |
| `partidas[].cantidad` | decimal | ✅ | Cantidad total de la partida |
| `partidas[].elementos[].ifcGuid` | string(22) | ✅ | IfcGUID de 22 caracteres (Base64 IFC) |
| `partidas[].elementos[].elementId` | int | ✅ | ElementId de Revit (para diagnóstico) |
| `partidas[].elementos[].cantidad` | decimal | ✅ | Cantidad aportada por este elemento |
| `partidas[].elementos[].systemName` | string | ⚠️ opcional | Nombre del sistema MEP en Revit |
| `partidas[].elementos[].levelName` | string | ⚠️ opcional | Nombre del nivel en Revit |
| `estadisticasGuid` | object | ⚠️ opcional | Cobertura de GUIDs del proceso de extracción |

### 6.3 Nombre del archivo

```
<nombre-bc3-sin-extension>.bimlink.json
```

**Ejemplo:** si el BC3 se llama `LFA-E3G-ZZZ-ZZZ-M3D-INS-001_20260523.bc3`,
el archivo de vinculación es `LFA-E3G-ZZZ-ZZZ-M3D-INS-001_20260523.bimlink.json`.

---

## 7. Verificación de compatibilidad: ejemplo mínimo funcional

El siguiente ejemplo cubre una partida con 2 elementos — suficiente para
implementar y probar el Sprint A3:

```json
{
  "version": "1.0",
  "generator": "e3gbimtools.RevitBC3",
  "generated": "2026-05-23T00:00:00Z",
  "bc3File": "TEST.bc3",
  "ifcModel": "TEST.ifc",
  "partidas": [
    {
      "codigo": "ED15B873",
      "codigoCatalogo": "ED15B873",
      "resumen": "Tubería NIRON Ø40",
      "unidad": "m",
      "cantidad": 6.203,
      "capitulo": "02SAN",
      "subcapitulo": "",
      "elementos": [
        { "ifcGuid": "3evGS0iVf78wLzyovWD4b9", "elementId": 17916301, "cantidad": 5.500, "systemName": "AFS", "levelName": "PB" },
        { "ifcGuid": "1Kh8nP2vDf4rQmBxTy6uWc", "elementId": 17916325, "cantidad": 0.703, "systemName": "AFS", "levelName": "PB" }
      ]
    }
  ]
}
```

### Validaciones mínimas (para Sprint A5)

- `ifcGuid` tiene exactamente 22 caracteres y solo contiene caracteres del alfabeto
  `0-9A-Za-z_$`.
- La suma de `elementos[].cantidad` es igual a `partidas[].cantidad` (salvo override manual).
- `bc3File` existe en la misma carpeta que el archivo `.bimlink.json`.

---

## 8. Diferencias respecto a Presto/Arquimedes

| Característica | TCQ | Presto IFC | e3gbimtools (propuesta) |
|----------------|-----|-----------|------------------------|
| Archivo externo de vinculación | ❌ No | ✅ JSON de criterios | ✅ `.bimlink.json` |
| Lee IfcGUID del `~M` BC3 | ❌ No | ⚠️ Parcial (visor) | N/A (generador) |
| PSETs en IFC con código BC3 | ✅ Sí (importación manual) | ✅ Sí (lectura y escritura) | Sprint A4 (generación) |
| Granularidad | Por tipo IFC | Por tipo IFC | **Por elemento** (IfcGUID) |
| Actualización automática | Manual (re-import) | Automática (re-import IFC) | Automática (re-export BC3) |
| Mapeo bidireccional | Sí (con visor TCQ) | Sí (visor Presto) | Futuro (Sprint A5) |

---

## 9. Conclusiones para el Sprint A3

1. **No hay formato TCQ externo importable**: la vinculación TCQ se configura una vez
   manualmente dentro de TCQ. El add-in no puede "importar" la vinculación en TCQ.

2. **El mecanismo más automatizable es el PSET en el IFC**: si el IFC contiene
   `E3G_Pset_BC3.BC3_Code` y `E3G_Pset_BC3.BC3_Quantity` por elemento, TCQ puede
   auto-importar las mediciones con un único paso de configuración reutilizable.

3. **El archivo `.bimlink.json` cumple dos funciones**:
   - Trazabilidad: registro permanente de qué elemento aportó qué a cada partida.
   - Insumo para generar el PSET en el IFC (Sprint A4).

4. **Presto es compatible con este enfoque**: Presto lee PSETs del IFC y los puede
   mapear a partidas BC3 de forma equivalente a TCQ.

5. **El `~M` con IfcGUID sigue siendo valioso**: aunque TCQ no lo use para vincular,
   el Visor BC3 interno y Presto (modo visor) sí lo usan para trazar al modelo.

---

## 10. Referencias

- [TCQ-BIM Viewer Manual de uso (ITeC, Ene 2017)](https://itec.es/docs/pdf/tcq-51-manual-bim-viewer-esp.pdf)
- [Flujo de trabajo Revit–TCQ, archivo IFC (MSI Digital Builders)](https://msidigitalbuilders.com/blog/flujo-de-trabajo-autodesk-revit-tcq-archivo-ifc-parte-ii/)
- [Presto IFC — RIB Software](https://www.rib-software.es/pdf/Enlace-con-BIM/Presto-IFC.pdf)
- [FIEBDC-3/2020 Especificación](https://www.fiebdc.es/web/es/documentacion/)
- [BuildingSMART IfcGloballyUniqueId](https://standards.buildingsmart.org/IFC/RELEASE/IFC4/ADD2/HTML/link/ifcgloballyuniqueid.htm)
- [Wiki ITeC — Visualizar y convertir BC3 a TCQ](https://wiki.itec.cat/doku.php?id=faqs-preguntas-frecuentes:como-visualizar-archivo-bc3-convertirlo-formato-tcq)

---

*E3G Enginyeria · e3gbimtools.RevitBC3 · Sprint A2 · Mayo 2026*
