# Dev setup — e3gbimtools

## Requisitos

- Windows 10/11 x64
- Visual Studio 2022 17.10+ o Rider 2024.2+
- .NET 8 SDK
- .NET Framework 4.8 Developer Pack (para Revit 2022-2024)
- Al menos una version de Revit instalada (idealmente 2025+).

## Build

```powershell
# Default (Revit 2025)
dotnet restore
dotnet build -c Release

# Otra version
dotnet build -c Release /p:RevitVersion=2024
```

## Tests

```powershell
dotnet test -c Release
```

## Despliegue local (modo dev — sin instalador)

Visual Studio publica el build en:
```
src\e3gbimtools.DiTools\bin\Release\Revit2025\net8.0-windows\
```
El target `GenerateAddinManifest` produce `e3gbimtools.DiTools.addin` junto al DLL.
Copia (o crea un symlink con `mklink`) ese .addin a:
```
%APPDATA%\Autodesk\Revit\Addins\2025\
```
Al iniciar Revit, debe aparecer la pestania `e3gbimtools` con los paneles `Inicio Proyecto` y `DiTools`.

## Logs

```
%APPDATA%\e3g\DiTools\logs\
```

## Generar el instalador para distribuir a otros ordenadores

Ver [INSTALADOR.md](INSTALADOR.md) — guía paso a paso desde cero.
