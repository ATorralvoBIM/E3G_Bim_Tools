# Saneamiento HS5 · Checklist de pruebas en Revit

Lista para validar en un proyecto real que la extensión HS5 funciona como debe.
Marca cada punto y anota el **ID** del elemento cuando algo no cuadre.

**Prioridad:** 🔴 crítico (si falla, los resultados no son fiables) · 🟠 alto · 🟢 conveniente

---

## 0. Preparación del modelo de prueba

- [ ] Las tuberías de **residuales** tienen el parámetro de UD acumuladas relleno (por defecto `MC Discharge Unit Sum`).
- [ ] En **pluviales**, el mismo parámetro de UD lleva la **superficie de recogida (m²)**: se asigna en cada sumidero y se acumula en las tuberías.
- [ ] Las familias de **aparatos sanitarios** tienen su UD en un parámetro (`MC Discharge Unit`; si no existe, se busca el de tubería).
- [ ] Los tipos de sistema contienen las palabras clave de *Aparatos y ajustes › Sistemas* (por defecto `10_10`, `10_30`, `Fecal`, `Gris` → residual · `10_20`, `Pluvial` → pluvial · `Mixt` → mixta · `Ventil` → ventilación).
- [ ] Los niveles de planta tienen marcada la opción **Planta de edificio** (se usa para contar plantas en las bajantes).
- [ ] La red está **conectada**: aparatos → derivaciones → ramales → bajantes → colectores → arquetas.

## 1. Lectura de la red y clasificación 🔴

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 1.1 | Seleccionar solo un tramo de colector y *Iniciar análisis* | El aviso "Red: …" cuenta bajantes, derivaciones y aparatos de toda la red conectada, no solo de lo seleccionado | ☐ |
| 1.2 | Revisar la columna **Tramo** de varias bajantes de 3+ plantas | "Bajante" en todos sus tramos, incluida la ventilación primaria si es del mismo sistema | ☐ |
| 1.3 | Bajante de **una sola planta** (< 3 m) | "Bajante" si su recorrido vertical es ≥ 1,5 m; si sale "Ramal", anotar el ID | ☐ |
| 1.4 | Bajada vertical corta de un aparato | "Derivación" (si sale de un aparato) o "Ramal" (caída) | ☐ |
| 1.5 | Colector suspendido que recibe bajantes | "Colector" | ☐ |
| 1.6 | Colector enterrado de planta baja que **solo** recibe aparatos | Hoy sale "Ramal" (4.3). Confirmar si debe tratarse como colector (4.5) | ☐ |
| 1.7 | Bajante con desviación > 45° | Tramo horizontal = "Desviación" (4.5 al 4 %); la bajante inferior hereda su DN | ☐ |
| 1.8 | Tubería **plana** (pendiente 0 %) | Sentido hacia la salida de la red; aviso "pendiente < mín." | ☐ |
| 1.9 | Red con tubos desconectados (huecos) | La clasificación cambia en la parte aislada: comprobar que no hay huecos | ☐ |
| 1.10 | Plantas de la bajante en el criterio ("4.4 · N pl.") | N = plantas desde las que vierten ramales | ☐ |
| 1.11 | Tramo 20310700 (pluvial, recoge 2 bajantes) | "Colector" aunque los tubos de enlace tengan el parámetro a 0 o casi sin pendiente | ☐ |
| 1.12 | Tubo con **contrapendiente** (sube en el sentido del agua; p. ej. 20426117) | KO "pend. < mín." con aviso "contrapendiente" | ☐ |
| 1.13 | Tubos redibujados sin recalcular MagiCAD (parámetro a 0) | Se dimensionan con lo que les llega aguas arriba y avisan "¿recalcular MagiCAD?" | ☐ |

## 2. Aparatos sanitarios y derivaciones (tabla 4.1) 🔴

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 2.1 | *Aparatos y ajustes › Leer aparatos del modelo* | Aparecen todos los tipos de aparato con su cantidad y Ø de desagüe | ☐ |
| 2.2 | Revisar la propuesta automática de cada tipo (ver §8) | Cada tipo mapeado a la fila correcta de la 4.1; corregir los que no lo estén | ☐ |
| 2.3 | Inodoro conectado con tubo ø90 | Derivación **KO → ø110** (el ø100 de la norma se exige como ø110 comercial) | ☐ |
| 2.4 | Derivación que pasa por un codo antes de unirse al ramal | **Todos** los tramos hasta la unión salen como "Derivación" con el Ø del aparato | ☐ |
| 2.5 | Tramo justo después de la unión con otro aparato o ramal | Ya no es derivación: es "Ramal" (4.3) | ☐ |
| 2.6 | Derivación con UD del modelo menor que la del aparato | KO con aviso "UD del modelo X < UD del aparato Y" | ☐ |
| 2.7 | Derivación de más de 1,5 m | Aviso "requiere cálculo pormenorizado (4.1.1.1.3)" | ☐ |
| 2.8 | Lavabo con UD 2 en la familia (valor de uso público) | Se deduce uso público → Ø mín. ø40; con UD 1 → privado → ø32 | ☐ |
| 2.8b | Aparato cuya UD de familia no está en la 4.1 | Se respeta su UD; Ø del uso elegido en ajustes; aviso | ☐ |
| 2.9 | Aparato sin mapear | Aviso en el resumen; se aplica la tabla 4.2 por el Ø del desagüe | ☐ |
| 2.9b | Termo o equipo de clima ("Otro") | Solo se exige el Ø de la tabla 4.2; la UD no se comprueba | ☐ |
| 2.9c | Inodoro cuya familia contiene "FLUX" | Fila "Inodoro · Con fluxómetro" | ☐ |
| 2.9d | Cambiar un mapeo, cerrar y abrir Revit | El mapeo se conserva (`Hs5Mapeo.json` junto a la DLL) y un redespliegue no lo borra | ☐ |
| 2.10 | Aparato mapeado como "Ignorar" | Su tubería se comprueba como ramal normal | ☐ |
| 2.11 | Sumidero modelado como **accesorio** en residuales | Cuenta como aparato "Sumidero sifónico" y su tubería es derivación | ☐ |

## 3. Residuales: ramales, bajantes y colectores 🔴

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 3.1 | Ramal al 2 % con 10 UD y ø50 | 4.3 al 2 % → ø63 (KO) | ☐ |
| 3.2 | Pendiente leída del modelo (columna **Pend.**) | Coincide con la pendiente de Revit | ☐ |
| 3.3 | Bajante de > 3 plantas | Criterio "4.4 · N pl." con la columna "> 3 pl." | ☐ |
| 3.4 | Bajante con un ramal muy cargado | El criterio muestra "ramal UD X → øY" y manda el mayor DN | ☐ |
| 3.5 | Forzar ≤ 3 / > 3 plantas desde el selector | Cambia la columna aplicada | ☐ |
| 3.6 | Colector a pie de bajante | DN ≥ DN de la bajante (no disminuye aguas abajo) | ☐ |
| 3.7 | Tubo más estrecho que el anterior (reducción en sentido del flujo) | KO "reduce de øX aguas arriba a øY" | ☐ |
| 3.8 | Desactivar "El diámetro no disminuye aguas abajo" | Desaparecen la herencia de DN y los KO por reducción | ☐ |
| 3.9 | Tramo con más UD que el máximo de la tabla | "fuera de tabla", KO | ☐ |
| 3.10 | Modo manual (elegir 4.3 / 4.4 / 4.5) | Solo se evalúan los tramos de esa orientación; el resto se omite con aviso | ☐ |
| 3.11 | Manguito corto (< 0,5 m) entre accesorios, p. ej. 20304377 | Toma la pendiente del tramo largo contiguo (criterio "pend. … del tramo X"); no da KO por pendiente | ☐ |

## 4. Pluviales y mixtos (4.6 – 4.9, Anejo B) 🟠

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 4.1 | Elegir **zona e isoyeta** en *Aparatos y ajustes › Pluviometría del proyecto* | Se guardan en el .rvt; se muestra "zona X · isoyeta Y → i = … mm/h · f = …" | ☐ |
| 4.1b | Proyecto **sin** pluviometría definida | Aviso en el resumen y se aplica 100 mm/h (f = 1): no reduce diámetros | ☐ |
| 4.1d | Cerrar y abrir el proyecto, o abrir otro proyecto | Cada .rvt conserva su propio régimen | ☐ |
| 4.1e | *Iniciar análisis* con pluviales | El resumen dice "Pluviales aplicadas con zona … → i = … · f = …" y cada criterio pluvial muestra "× f …" | ☐ |
| 4.1c | Sumideros con superficie en el parámetro de UD | Las tuberías pluviales leen ese valor como m² (columna UD muestra "… m²") | ☐ |
| 4.2 | Bajante pluvial | 4.8 con superficie × f | ☐ |
| 4.3 | Colector pluvial | 4.9 según pendiente con superficie × f | ☐ |
| 4.4 | Colector mixto | 4.9 con (superficie + superficie equivalente de las UD) × f | ☐ |
| 4.5 | Desmarcar "Pluviales: el parámetro de UD contiene la superficie" | Se lee el parámetro de superficie aparte (Área o Número, en m²) | ☐ |
| 4.6 | Calculadora de cubiertas | Nº de sumideros (4.6) y Ø de canalón (4.7) para la superficie introducida | ☐ |

## 5. Ventilación y arquetas (4.10 – 4.13) 🟠

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 5.1 | Ventilación primaria (prolongación de la bajante) | "Vent. primaria", DN = DN de la bajante | ☐ |
| 5.2 | Columna secundaria unida en cada planta | 4.11 según DN de la bajante | ☐ |
| 5.3 | Columna secundaria unida en algunas plantas | 4.10 (bajante, UD y longitud); ≥ ½ DN de la bajante | ☐ |
| 5.4 | Ventilación terciaria de un ramal | 4.12 según DN y pendiente del ramal y la longitud | ☐ |
| 5.5 | Columna con diámetros distintos | Aviso "no tiene un diámetro uniforme (4.4.2.1)" | ☐ |
| 5.6 | Arqueta conectada a un colector seleccionado | Fila "Arqueta" con "≥ L×A cm" según el colector de salida | ☐ |
| 5.7 | Parámetros de largo y ancho de la familia de arqueta | Se leen (ajustar nombres en *Parámetros del modelo* si no) | ☐ |

## 6. Dimensionado e informe (Fase 5) 🔴

| # | Prueba | Resultado esperado | ✓ |
|---|---|---|---|
| 6.1 | *Aplicar DN HS5* con KO de diámetro | Pide confirmación con el resumen øX → øY y cambia los diámetros | ☐ |
| 6.2 | Tras aplicar | Se reanaliza solo; los KO de diámetro pasan a OK | ☐ |
| 6.3 | DN que no existe en el tipo de tubería | Se informa como "no se pudo cambiar"; no rompe la transacción | ☐ |
| 6.4 | Accesorios (codos, tes) tras el cambio | Revit los adapta al nuevo DN; revisar que no quedan desconexiones | ☐ |
| 6.5 | Deshacer (Ctrl+Z) tras aplicar | Vuelve todo en un solo paso ("E3G HS5 — Aplicar DN") | ☐ |
| 6.6 | *Exportar informe* | El CSV se abre en Excel con acentos correctos y una fila por tramo | ☐ |
| 6.7 | *Restaurar colores* | Se quitan las sobrescrituras: las tuberías vuelven a su color real | ☐ |
| 6.8 | Tuberías correctas tras analizar | Sin sobrescritura (color real del sistema o filtro); solo las KO se pintan | ☐ |
| 6.9 | Clic en una fila de resultados (Ctrl/Mayús para varias) | El tramo queda seleccionado y resaltado en Revit; doble clic lo encuadra | ☐ |
| 6.10 | Seleccionar tramos en Revit | Sus filas se marcan en la tabla y se despliega su grupo (correctos / KO) | ☐ |

## 7. Rendimiento y estabilidad 🟢

- [ ] Análisis de un edificio completo en menos de unos 10 s.
- [ ] Red de más de 30 000 tuberías: aviso de recorrido parcial, sin bloquear Revit.
- [ ] Cambiar de tabla o de opciones y pulsar *Actualizar* sin volver a seleccionar.
- [ ] Los ajustes se conservan al cerrar y abrir Revit (`%APPDATA%\E3G\Saneamiento\hs5.json`).

---

## 8. Aparatos sanitarios que se analizan (tabla 4.1)

| Aparato (fila de la tabla 4.1) | UD privado | UD público | Ø privado | Ø público | Detección automática (familia o tipo contiene…) |
|---|---|---|---|---|---|
| Lavabo | 1 | 2 | 32 | 40 | LAVABO, LAVAMANOS, RENTAMANS |
| Bidé | 2 | 3 | 32 | 40 | BIDE |
| Ducha | 2 | 3 | 40 | 50 | DUCHA, DUTXA, PLATO, SHOWER |
| Bañera (con o sin ducha) | 3 | 4 | 40 | 50 | BAÑERA, BANYERA, BATHTUB |
| Inodoro · Con cisterna | 4 | 5 | 110 | 110 | INODOR, WC, VATER (sin "FLUX" en el nombre) |
| Inodoro · Con fluxómetro | 8 | 10 | 110 | 110 | INODOR/WC + FLUX |
| Urinario · Pedestal | – | 4 | – | 50 | URINAR + PEDEST |
| Urinario · Suspendido | – | 2 | – | 40 | URINAR (por defecto) |
| Urinario · En batería | – | 3,5 | – | – | URINAR + BATER |
| Fregadero · De cocina | 3 | 6 | 40 | 50 | FREGADERO, AIGÜERA, PICA, SINK |
| Fregadero · De laboratorio, restaurante, etc. | – | 2 | – | 40 | FREGADERO + LABOR / RESTAUR / INDUSTR |
| Lavadero | 3 | – | 40 | – | LAVADERO, SAFAREIG |
| Vertedero | – | 8 | – | 110 | VERTEDERO, ABOCADOR, SLOP |
| Fuente para beber | – | 0,5 | – | 25 | BEBEDERO, FUENTE, FONT DE BEURE |
| Sumidero sifónico | 1 | 3 | 40 | 50 | SUMIDERO, BUNERA, INTERCEPTOR |
| Lavavajillas | 3 | 6 | 40 | 50 | LAVAVAJILLAS, RENTAVAIXELLES |
| Lavadora | 3 | 6 | 40 | 50 | LAVADORA, RENTADORA |
| Cuarto de baño · Inodoro con cisterna / fluxómetro | 7 / 8 | – | 110 | – | solo mapeo manual |
| Cuarto de aseo · Inodoro con cisterna / fluxómetro | 6 / 8 | – | 110 | – | solo mapeo manual |
| **Otro** · tabla 4.2 por Ø de desagüe (sin comprobar UD) | 32→1 · 40→2 · 50→3 · 60→4 · 80→5 · 100→6 | | = Ø desagüe | | TERMO, CALENTADOR, CONDENS, FANCOIL, SPLIT, CLIMA |
| **Ignorar** (no evacúa) | — | | — | | solo mapeo manual |

El ø100 de la tabla 4.1 no es comercial en PVC: se exige **ø110**. La UD que se usa es la del parámetro de la familia. Si coincide con la de un uso de la tabla, se toma el Ø de ese uso. Cuando la tabla no da valor para el uso (p. ej. un urinario en uso privado), se toma el del otro uso y se avisa.

## 9. Mapeo de aparatos · decisiones (21/10/2026)

El mapeo se hace por **familia + tipo** en *Aparatos y ajustes* y se guarda **con la app** (`Hs5Mapeo.json` junto a la DLL del add-in; versionado en el repo en `Hs5/Hs5Mapeo.json`). Vale para todos los proyectos y el despliegue no sobrescribe el instalado.

1. **Inodoros con fluxómetro:** se distinguen porque el nombre de la familia contiene `FLUX`.
2. **Termos y climatización:** van por la tabla 4.2 según el Ø del desagüe. No se aplican UD (ni la regla de 0,03 dm³/s).
3. **Sumideros:**
   - en **pluviales**, el parámetro de UD lleva la superficie de recogida (m²), y las tablas 4.8 y 4.9 se aplican con ese valor × f;
   - en **residuales** pueden ser aparato o accesorio, y ambos se detectan.
   - El régimen pluviométrico del proyecto se introduce en una casilla (o se obtiene del Anejo B).
4. **Uso:** cada familia lleva su UD. Esa UD manda, y de ella se deduce el uso para el Ø de la derivación. El uso de ajustes solo se aplica si la familia no tiene UD o su valor no está en la tabla.
5. **Cuartos de baño o aseo completos:** se modelan aparatos sueltos. Las filas por recinto quedan solo para mapeo manual.
6. **Dónde se guarda el mapeo:** en la propia app (ver arriba).

Parámetro de UD de los aparatos: **`MC Discharge Unit`** (confirmado), editable en *Parámetros del modelo*.

## 10. Limitaciones conocidas

- Longitud efectiva de la ventilación secundaria (4.10): se aproxima por la longitud real de la columna.
- Umbral de bajante: un recorrido vertical de al menos 1,5 m (fijo).
- Canalones (4.7) y número de sumideros (4.6): solo en la calculadora, no se leen del modelo.
- Bombeo y elevación (4.6.1 / 4.6.2): fuera del alcance.
