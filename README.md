# e3gbimtools

Add-in interno de Revit para E3G. Modulos:

- **DiTools** — replica nativa de funcionalidades clave de DiRootsOne:
  - ✓ CopyLevels (Sprint 1)
  - ✓ OneFilter (Sprint 2)
  - ✓ ParaManager (Sprint 3)
  - ✓ ViewManager (Sprint 4)
  - ✓ SectionBoxer (Sprint 5)
  - ✓ SheetGen (Sprint 6)
  - ✓ FamilyReviser (Sprint 7)
  - ✓ TableGen (Sprints 8-9)
  - ✓ OneParameter (Sprint 10)
  - ✓ SheetLink P1 — Export/Import Excel (Sprint 11)
  - ✓ SheetLink P2 — Diff Before/After selectivo (Sprint 12)
  - ○ SheetLink P3 — Google Sheets + sync avanzado (Sprint 13)
- **Saneamiento** — herramientas internas de disciplina (BC3, clasificacion de sistemas, mediciones).
- **Fontaneria** — herramientas internas de disciplina.

Compatible con Revit 2022 a 2027. Stack: .NET Framework 4.8 (2022-2024) / .NET 8 (2025+), WPF + CommunityToolkit.Mvvm, Serilog, ClosedXML.

## Documentacion

- [Arquitectura](docs/ARCHITECTURE.md)
- [Dev setup](docs/DEV_SETUP.md)
- [Roadmap maestro](Roadmap_DiTools_e3gbimtools.html)

## Quick build

```powershell
dotnet build /p:RevitVersion=2025 -c Release
```

---

# Notas historicas de QA

## [2026-04-13] NOTES ATorralvoBIM 

Panell Saneamiento:
 - A la pestanya Verificación de colectores.La taula que fa el llistat i sobretot la capçalera d'aquesta taula, es pot assimilar estéticament al llenguatge visual que tenim a la resta de taula? ✔️
     - Pot ser hauriem de ficar fondo blanc a les targetetes per a que quedin més clares les dades. ​✔️
     - Els botons blaus d'execucció de les accions els hi falta volum.  ✔️​
- A la pestanya de Mediciones, el desplegable de Normativa hauria de desapareixer ✔️​
     - Quan Fas clic, als botons "Fecals""Pluvials""Grises" el texte desapareix per que la tarjeta quan fas clic es fica en el color que hem demanat pero tapa les lletres. ​✔️
 
 ## [2026-04-14] NOTES ATorralvoBIM

 Panell Saneamiento: ​❗
  - Proba a implementar la medició tant de pluvials com de Grises.
    - A pluvials li has de dir que mesuri totes les canonades del sistema "10_20_Pluvials". Ha de identificar, les tuberies entre que planta y que planta està. Si son verticals tractar-les 
        com a baixants i per últim el número de buneres connectades a aquest sistema, el nom de la familia de les buneres es "E3G_Sumidero".
    - A Grises. Que mesuri totes les canonades del sistema "10_30_Grises" .Primer vull identificar els grups de model en el que es troben les canonades de grises. Vull per una banda, saber 
       les canonades dins els grups igual que a residuals i considerar-les interior viviendas, també identifica els dispositius als que estan conectats (inodors). Per altra banda vull que 
       identifiqui les tuberies verticals com a bajantes i despres amb el mateix criteri de residuals que identifiqui els colectors com les canonades que pasen entre el nivell P00.A i el P01.A. 
       Per últim les canonades que passen entre els nivells del aparcament S01.A S02.A S03.A

    - Sidebar com un requadre amb relleu ​✔️

 ## [2026-04-21]

 Les medicions de fontaneria, no està exportant les derivacions individuals tot i que al panell del Revit les mostra ​✔️

 El botó de exportar a CSV de sanejament pluviales i Grises no funciona ❗
 
 El de medir de Grises no medeix res, no surt l'amidament, ja ho arreglares ❗
 



​❓​ - per comprovar 
❌ - no funciona
​❗​ - no implementat
​✔️​ - fet



holaaa funciona?

sii ja funciona