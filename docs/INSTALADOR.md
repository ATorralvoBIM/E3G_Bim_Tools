# Generar el instalador `.msi` de e3gbimtools

Guía paso a paso, escrita pensando en alguien que no es programador.

---

## 1. Qué es el instalador y para qué sirve

El **instalador** (`.msi`) es un archivo que cualquier persona puede ejecutar
en su ordenador con doble click para **instalar automáticamente** el add-in
`e3gbimtools` en su Revit, sin tener que copiar archivos a mano.

Cuando alguien lo ejecuta:

1. Le pregunta dónde quiere instalarlo (puede aceptar el sitio por defecto).
2. Copia las DLLs a `%LOCALAPPDATA%\Programs\e3g\BimTools\Revit2025\`.
3. Copia el archivo `.addin` a `%APPDATA%\Autodesk\Revit\Addins\2025\`.
4. La próxima vez que esa persona abra Revit 2025, le aparecerá la pestaña
   **`e3gbimtools`** en el ribbon, con tus paneles **Inicio Proyecto** y
   **DiTools** listos para usar.

No requiere permisos de administrador (es per-user). No requiere reiniciar
Windows. No toca el registro del sistema.

---

## 2. Instalar WiX (UNA SOLA VEZ EN TU VIDA)

WiX Toolset es la herramienta gratuita que sabe construir archivos `.msi`.
**Solo necesitas instalarla la primera vez.** Después se queda en tu máquina
para siempre.

### Pasos

1. Abre el menú **Inicio** de Windows.
2. Escribe `PowerShell` y dale a Enter.
3. Se abre una ventana negra. Pega esto exactamente y pulsa Enter:

   ```
   dotnet tool install -g wix
   ```

4. Espera 20-40 segundos. Verás un mensaje del tipo *"You can invoke the
   tool using the following command: wix"*. Eso significa que ha ido bien.
5. Cierra PowerShell. Ya no la necesitas más.

### ¿Cómo sé que está instalado?

Abre PowerShell de nuevo y escribe:

```
wix --version
```

Te debería responder con un número de versión, por ejemplo `5.0.2`. Si te
sale un error tipo *"wix no se reconoce"*, repite los pasos 1-4.

---

## 3. Generar el instalador

Cada vez que quieras generar un `.msi` nuevo (porque has hecho cambios y
quieres distribuir una versión actualizada), haz esto:

### Paso 3.1 — Subir el número de versión (recomendado)

Abre con Visual Studio (o Bloc de notas) el archivo:

```
C:\Dev\E3GBIMTools\installer\Product.wxs
```

Busca la línea que dice:

```
Version="0.1.0"
```

Cámbiala al número siguiente. Convenciones:

- Cambios pequeños o corrección de un bug: sube el último número
  (`0.1.0` → `0.1.1`).
- Cambios visibles para el usuario: sube el del medio (`0.1.0` → `0.2.0`).
- Cambios grandes o re-arquitectura: sube el primero (`0.1.0` → `1.0.0`).

Guarda el archivo (Ctrl+S).

### Paso 3.2 — Recompilar la solución principal

En Visual Studio:

1. Asegúrate de que la configuración activa es **Release** (no Debug). En la
   barra superior verás un desplegable cerca del botón verde de "Iniciar"
   donde pone "Debug" o "Release". Cámbialo a **Release**.
2. Menú **"Compilación"** → **"Recompilar solución"**.
3. Espera a que termine sin errores rojos.

Esto produce las DLLs nuevas en sus carpetas `bin\Release\`.

### Paso 3.3 — Construir el instalador

1. En el Explorador de soluciones, busca el proyecto **`e3gbimtools.Installer`**
   (dentro de la carpeta `installer`).
2. **Click derecho** sobre él → **"Compilar"** (Build).
3. Espera 30-60 segundos.
4. Cuando termine, el `.msi` está en:

   ```
   C:\Dev\E3GBIMTools\installer\bin\Release\e3gbimtools-setup.msi
   ```

¡Listo! Ese archivo es lo que puedes distribuir.

---

## 4. Probar el instalador en otro ordenador

### Lo que necesita el otro ordenador

- Windows 10 o Windows 11.
- **Revit 2025 instalado**. Si no tiene Revit 2025, el instalador funcionará
  pero el add-in no servirá para nada.
- **.NET 8 Desktop Runtime**. Si no lo tienen instalado, lo pueden bajar
  gratis de [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
  (sección "Desktop Runtime", x64).

### Cómo se instala

1. Copia el archivo `e3gbimtools-setup.msi` al otro ordenador (por
   correo, USB, OneDrive, lo que sea).
2. En ese ordenador, haz **doble click** sobre `e3gbimtools-setup.msi`.
3. Aparece un asistente sencillo. Acepta los pasos por defecto (Next, Next,
   Install).
4. Cuando termine, abre Revit 2025.
5. Comprueba que aparece la pestaña **`e3gbimtools`** en el ribbon con
   sus paneles.

### Si algo va mal

- **El instalador da un error al iniciar:** confirma que el otro
  ordenador tiene .NET 8 Desktop Runtime instalado.
- **Revit no muestra la pestaña:** revisa el log en
  `%APPDATA%\e3g\DiTools\logs\` — Revit ha intentado cargar el add-in
  pero algo ha fallado. Pásame el log y lo diagnostico.
- **Aparece un mensaje de Revit preguntando si cargar el add-in:** la
  primera vez es normal. Que elija **"Cargar siempre"** (Always Load).

---

## 5. Desinstalar

Si quieres quitar el add-in de un ordenador donde estaba instalado:

1. **Panel de Control** → **Programas y características** (o en Windows 11,
   **Configuración** → **Apps** → **Aplicaciones instaladas**).
2. Busca **"e3gbimtools"** en la lista.
3. Click sobre él → **Desinstalar**.

Eso elimina las DLLs y el archivo `.addin`. La próxima vez que abras Revit
ya no aparecerá la pestaña.

(Los logs en `%APPDATA%\e3g\DiTools\logs\` no se borran automáticamente
por si quieres conservarlos.)

---

## 6. Actualizar la versión instalada

Si un usuario ya tiene una versión del add-in instalada y le pasas un `.msi`
nuevo (con el número de versión más alto), simplemente que haga **doble click**
sobre el `.msi` nuevo:

- El instalador detecta automáticamente que ya hay una versión vieja.
- Desinstala la vieja.
- Instala la nueva.
- Todo sin tener que desinstalar a mano primero.

Esto funciona gracias a la regla `MajorUpgrade` que tengo configurada en el
archivo `Product.wxs`. **No cambies el `UpgradeCode`** (el GUID
`A12B3C4D-5E60-4F71-8A92-1B2C3D4E5F60`), porque es la "huella digital"
que permite a Windows reconocer que es el mismo producto.

---

## 7. Resolución de problemas comunes al generar el instalador

### "Could not find a part of the path... e3gbimtools.DiTools.dll"

Tienes que recompilar la solución principal **en modo Release** antes de
construir el instalador. El instalador empaqueta lo que hay en
`bin\Release\`, no en `bin\Debug\`.

### "wix.exe no se reconoce como comando"

WiX no está instalado. Vuelve al apartado 2 de este documento.

### "WixToolset.UI.wixext package not found"

NuGet no ha conseguido descargar el paquete. Confirma que tienes
conexión a internet y vuelve a hacer click derecho sobre el proyecto →
"Compilar".

### Visual Studio no recompila el instalador aunque le doy a Compilar

Asegúrate de hacer click derecho sobre **`e3gbimtools.Installer`** (no sobre
la solución entera) y elegir **"Compilar"** o **"Recompilar"**. El proyecto
está configurado para NO incluirse en "Compilación → Recompilar solución",
porque si no, te molestaría en cada build diaria.

---

## 8. Resumen para tener a mano

| Acción | Cómo |
|---|---|
| Instalar WiX (una vez) | PowerShell → `dotnet tool install -g wix` |
| Subir versión | Editar `Version="..."` en `installer\Product.wxs` |
| Compilar add-in | Visual Studio → Release → "Recompilar solución" |
| Generar `.msi` | Click derecho en `e3gbimtools.Installer` → "Compilar" |
| Instalador resultante | `installer\bin\Release\e3gbimtools-setup.msi` |
| Distribuir | Copia el `.msi` y pásalo (USB, email, OneDrive...) |
| Actualizar versión instalada | Doble click sobre el `.msi` nuevo (reemplaza la vieja sola) |
| Desinstalar | Panel de Control → Programas → "e3gbimtools" → Desinstalar |

---

## 9. Enlaces útiles

- **Documentación oficial de WiX Toolset 5:**
  [https://docs.firegiant.com/wix/](https://docs.firegiant.com/wix/)
- **.NET 8 Desktop Runtime (para los usuarios finales):**
  [https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio Community (gratis para uso individual y pequeñas empresas):**
  [https://visualstudio.microsoft.com/vs/community/](https://visualstudio.microsoft.com/vs/community/)
