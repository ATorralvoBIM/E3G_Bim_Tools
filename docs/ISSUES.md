# Issues conocidos — e3gbimtools.RevitBC3

> Sprint A5 · Mayo 2026 · Actualizado automáticamente por el proceso de validación end-to-end

---

## Bugs activos

### [BUG-01] BC3 exportado vacío si ninguna regla captura elementos
**Gravedad:** Media  
**Estado:** Mitigado (aviso en UI desde ronda de bugs 2026-05-20)

Si todas las reglas activas tienen 0 elementos (filtros demasiado estrictos o categoría incorrecta), la guardia pre-export avisa con un mensaje claro que indica cuántas partidas tienen `FinalQuantity > 0`. Sin embargo, el usuario puede forzar la exportación de todos modos, generando un BC3 técnicamente válido pero sin mediciones útiles.

**Workaround:** revisar la columna **Elem.** en la tabla de mediciones antes de exportar. Si es 0 para una regla, ajustar la categoría o los filtros.

---

### [BUG-02] Cobertura IfcGUID <100% en modelos no exportados a IFC
**Gravedad:** Baja (funcional, pero impacta trazabilidad)  
**Estado:** Parcialmente resuelto (Sprint A1)

El algoritmo `RevitIfcGuidCalculator.FromUniqueId` calcula correctamente el IfcGUID desde el UniqueId de Revit para elementos estándar. Sin embargo, algunos tipos de elemento (familias anotativas, elementos de detalle, componentes de sistema sin geometría 3D) no tienen UniqueId con el formato esperado (`GUID-ElementIdHex`). Para estos, el cálculo devuelve `null` y se aplica el fallback `"E" + ElementId`, que NO es un IfcGUID válido.

**Workaround:** ejecutar "🔗 Preparar IFC..." y luego exportar el IFC desde Revit una vez. Esto puebla el parámetro `IFC_GUID` en todos los elementos y la cobertura pasa a 100%.

---

### [BUG-03] Subcapítulo sintético duplicado si dos reglas comparten mismo capítulo sin subcapítulo
**Gravedad:** Baja (cosmética en BC3)  
**Estado:** Pendiente de investigar

Cuando dos reglas tienen el mismo `ChapterCode` pero `SubChapterCode` vacío, ambas se agrupan en el subcapítulo sintético correcto. Sin embargo, si una tercera regla tiene el mismo `ChapterCode` con un subcapítulo real, el subcapítulo sintético puede aparecer antes de los reales en el `~D`, generando un orden inesperado en Presto.

**Workaround:** asignar subcapítulo explícito a todas las reglas de un mismo capítulo, o asegurarse de que no se mezclan reglas con y sin subcapítulo dentro del mismo capítulo.

---

### [BUG-04] Campo `IfcModel` del .bimlink.json siempre vacío
**Gravedad:** Baja  
**Estado:** ✅ Resuelto en Sprint A5

El campo `IfcModel` del archivo `.bimlink.json` siempre se escribe como cadena vacía. El Sprint A4 definió el campo en `BC3ExportOptions.IfcModelPath` y añadió el TextBox en la UI, pero `BimlinkExporter.Export` actualmente pone `IfcModel = string.Empty` de forma incondicional (comentario: "Sprint A4 rellenará esto").

**Workaround:** editar manualmente el `.bimlink.json` si se necesita el nombre del IFC para una integración externa. En TCQ la vinculación funciona por IfcGUID en el `~M`, no por este campo.

---

## Limitaciones de diseño

### [LIM-01] Solo Revit 2025
El add-in usa `ForgeTypeId` (SpecTypeId, GroupTypeId) directamente, que no existe en versiones anteriores. Adaptar a 2022–2024 requeriría un bloque `#if REVIT2022` con las APIs antiguas (`ParameterType`, `BuiltInParameterGroup`).

### [LIM-02] Sin NuGet — sustituciones activas
| Dependencia prevista | Sustitución actual | Impacto |
|---|---|---|
| Serilog | Logger casero a `%APPDATA%\e3g\DiTools\logs\` | Sin structured logging ni sinks remotos |
| Microsoft.Extensions.DependencyInjection | `DiToolsServiceContainer` casero | Sin scopes, sin decoradores |
| CommunityToolkit.Mvvm | Code-behind directo | Más código en xaml.cs, sin source generators |
| ClosedXML | Export CSV con UTF-8 BOM | Sin formato Excel real (celdas, colores, fórmulas) |
| Windows-1252 encoding | `Encoding.Latin1` | Caracteres €, –, … pueden aparecer incorrectos en el BC3 |

### [LIM-03] Override manual desactiva trazabilidad elemento a elemento
Si el usuario edita manualmente la cantidad de una partida en la tabla de mediciones, el BC3 exportado no incluye líneas de desglose `~M` con IfcGUIDs para esa partida. Esto es intencional (para no falsear la trazabilidad), pero implica que esa partida no tendrá vinculación en TCQ/Presto. El `.bimlink.json` lo indica con `EsOverrideManual: true`.

### [LIM-04] Algoritmo UniqueId→IfcGUID no verificado con todos los tipos de familia
El algoritmo `RevitIfcGuidCalculator` está verificado para:
- Tuberías (`Pipe`)
- Conductos (`Duct`)
- Accesorios de tubería (`PipeFitting`)
- Equipos mecánicos (`MechanicalEquipment`)

No está verificado para familias anotativas, familias de detalle 2D, ni elementos importados. En estos casos el cálculo puede producir un GUID que no coincide con el que Revit asignaría al exportar a IFC.

### [LIM-05] Archivo .bimlink.json no importado directamente por TCQ/Presto
El formato `.bimlink.json` es propio de e3gbimtools. TCQ y Presto leen los IfcGUIDs del campo comentario del `~M` en el BC3 directamente — no necesitan el `.bimlink.json` para la vinculación básica. El archivo `.bimlink.json` está pensado para:
- Auditoría y trazabilidad interna.
- Futuras integraciones directas (API TCQ si se publica).
- El validador `BimLinkingValidationTests`.

---

## Mejoras pendientes (backlog)

| ID | Descripción | Prioridad |
|----|-------------|-----------|
| MEJ-01 | Rellenar `IfcModel` en el bimlink desde `BC3ExportOptions.IfcModelPath` | Media |
| MEJ-02 | Panel de diagnóstico de cobertura GUID por categoría en la UI (Sprint A4 pendiente) | Media |
| MEJ-03 | Validar algoritmo UniqueId→IfcGUID con familias de acero estructural y carpintería | Baja |
| MEJ-04 | Exportar `.bimlink.json` con referencia al nombre del IFC exportado | Baja |
| MEJ-05 | Integración con Google Sheets (SheetLink P3 / Sprint 13) | Baja |
| MEJ-06 | Pipeline CI/CD GitHub Actions multi-Revit (Sprint 0 bloque 7) | Baja |

---

*Actualizado: 2026-05-24 — Sprint A5 BIM Linking BC3-IFC*
