# WorldGen alapmodellek — áttekintés

**Állapotdokumentáció dátuma:** 2026-09-12

Ez a mappa a WorldGen jelenlegi fizikai és fizikai jellegű alapmodelljeit írja
le. A cél, hogy minden modellnél egyértelmű legyen:

- milyen bemenetekből dolgozik;
- milyen feltételek mellett hoz létre egy jelenséget;
- milyen képletet vagy döntési szabályt használ;
- mely konstansok határozzák meg;
- ténylegesen be van-e kötve a jelenlegi világépítésbe;
- milyen fizikai egyszerűsítései és hiányai vannak.

## Státuszjelölések

| Státusz | Jelentés |
|---|---|
| **Aktív** | A jelenlegi Unity Viewer vagy CLI tényleges világépítési útja használja. |
| **Részben aktív** | A modell egy része be van kötve, más részei csak külön függvényként léteznek. |
| **Elérhető, nincs bekötve** | Core-kód és teszt létezik, de a fő Viewer/CLI világállapot nem használja. |
| **Megjelenítési proxy** | Modelladatból származik, de nem teljes fizikai szimuláció. |
| **Hiányzik** | A specifikációban szerepelhet, de működő modell még nincs. |

## Dokumentumok

1. [Bolygótest, rács, pálya és besugárzás](01-planet-orbit-insolation.md)
2. [Tektonika, domborzat, tengerszint és deep-time](02-tectonics-terrain-deep-time.md)
3. [Hőmérséklet és klímaciklusok](03-temperature-climate.md)
4. [Szél, időjárás, nedvesség, csapadék és felhők](04-wind-weather-moisture.md)
5. [Hidrológia, folyók, tavak és erózió](05-hydrology-rivers-lakes.md)
6. [Jég, hó, biome-ok és tile-típusok](06-cryosphere-biomes-tiles.md)
7. [Becsapódások és vulkanizmus](07-geological-events.md)
8. [Fizikai eredetű megjelenítési rétegek](08-physical-presentation.md)
9. [Modellkapcsolatok, aktív adatút és hiányok](09-model-coupling-and-gaps.md)

## Fontos értelmezési szabály

A dokumentumok a tényleges kódot írják le, nem azt, amit egy teljes
bolygószimulátornak ideális esetben tudnia kellene. A „fizikai” szó itt nem
jelenti automatikusan azt, hogy a modell teljes, kalibrált vagy első elvekből
levezetett. Több jelenlegi részmodell tudatos, determinisztikus proxy.

A jövőbeli, nem Föld-szerű világok parametrizálási terve külön dokumentumban
található: [Fejlesztési ötlet #2 — Nem Föld-szerű bolygók](../08-development-idea-2-non-earthlike-planets.md).

## Globális invariánsok

- Azonos seed + modellverzió + bemenet + idő azonos világot kell adjon.
- A random réteg állapotmentes, címzett és kiértékelési sorrendtől független.
- A Core fizikai mennyiségei `double` típusúak.
- A renderelt fizikai jelenségnek modelladatból kell származnia.
- A panelen megjelenő értékhez forrás, egység és számítási lánc tartozik.
- Ahol a kód nyers `Math.Sin`, `Math.Cos`, `Math.Exp`, `Math.Tanh` vagy más
  nem garantáltan bitazonos transzcendens függvényt használ, ott a szigorú
  platformközi determinizmus még nyitott kockázat.

