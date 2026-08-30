# Backlog — hátralévő feladatok mérföldkövenként

Állapotfelmérés: 2026-08-30. Az M0-M3, M5, M7-M8 tartalmilag kész (halasztott
al-tételekkel), az M4 ebben a munkamenetben lezárult (ND-36/37/38). A tábla a
MÉG HÁTRALÉVŐ tételeket sorolja fel — a kész mérföldkövek nem szerepelnek
itt, ld. `docs/05-milestones.md` a teljes státuszért.

Oszlopok: **Prio** (Magas/Közepes/Alacsony), **Komplexitás** (Kicsi/Közepes/
Nagy — durva becslés, nem munkaóra), **Kellek?** (kell-e felhasználói
vizuális ellenőrzés/döntés a megvalósításhoz: Igen/Részben/Nem).

| Mérföldkő | Megnevezés | Leírás | Prio | Komplexitás | Kellek? |
|---|---|---|---|---|---|
| M9 | LOD-pixelesség közeli zoomnál | **Implementáció kész, felhasználói vizuális ellenőrzés hátra.** Kamera-vezérelt, hiszterézises kvadfa-LOD (`AdaptiveQuadTree`, tiszta/motorfüggetlen, `WorldGen.Viewer.Lod` assembly) 2:1 kiegyensúlyozással és geomorphinggal beépítve a `PlanetGridMesh`-be (`useAdaptiveLod` kapcsoló, alapból bekapcsolva — kikapcsolva a viselkedés bitre az M9 előtti). Perzisztens, LRU-korlátos sarok-cache a korábbi per-Build() cache helyett. A kamera `minDistance`-e csökkentve (120→100.1), mert a régi érték soha nem engedte elég közel a kamerát ahhoz, hogy a finomabb LOD-szintek egyáltalán aktiválódjanak. Unit-tesztek (`AdaptiveQuadTreeTests`, EditMode) lefedik a kötelező listát (2:1 egyensúly, hiszterézis/nincs oszcilláció, determinizmus, teljes lefedettség/nincs hézag) — önálló .NET futtatóval végrehajtva és zöldre igazolva, a live Unity Editor pedig hiba nélkül lefordította az egész projektet. Élő vizuális ellenőrzés (varratmentesség, pattogásmentesség, teljesítmény, K_split hangolás) még nem történt meg — ez a következő lépésed. | Magas | Nagy | Igen |
| M9 | Kontinens/régió kamera-átmenetek | Zoom közben folyamatos átmenet Planet → Continent → Region nézetek között (a spec referenciakép 1., 3., 4. szintje). Az orbit/zoom kamera-alap kész, de a nézetszint-váltás/fly-to logika nincs. | Közepes | Nagy | Igen |
| M6 | Atmoszféra-render | Rayleigh-szórás, felhőzet, ciklonok — HDRP-prototípus (ND-21) még el sem kezdődött; bizonytalan, hogy az űrből nézett HDRP felhőrendszer minősége megfelelő lesz-e. | Közepes | Nagy | Igen |
| M11 | Rift-zóna + lemez-hasadás/egyesülés | **Python-referencia kész** (ND-45, `tools/reference/plate_lifecycle_ref.py`, összevonva az M10 lemez-születés/-halál tétellel — ugyanaz a spec §16) — C#-port hátra; a `PlateId` `ulong`-ra vált (seed-törő), a split/merge-konstansok felhasználói megerősítést igényelnek. | Alacsony | Nagy | Részben |
| M11 | Kráter tartós hatásának ellenőrzése | A dokumentáció korábban "hátralévőnek" jelölte a kráterek magasság-mezőbe való tartós beépítését — a jelenlegi Unity-kód (`PlanetGridMesh.Build()`) már alkalmazza őket a renderben, de érdemes leellenőrizni, hogy ez a Core-oldali (nem csak viewer-oldali) láncban is konzisztens-e. | Közepes | Kicsi | Nem |
| M12 | Checkpoint-rendszer + `.worldpkg` formátum | Csak a `WorldStateHash` (a hash-mechanizmus maga) kész; a köré épülő szerializációs/checkpoint-réteg nincs. | Alacsony | Nagy | Nem |
| M12 | `worldgen verify` CLI | Parancssori eszköz a state-hash ellenőrzésére — nincs elkezdve. | Alacsony | Közepes | Nem |
| M13 | Volumetrikus felhő + AO | Végleges vizuális polish-tételek, semmi nincs elkezdve belőlük. | Alacsony | Nagy | Igen |
| M13 | Színkalibráció | A teljes látvány egységes színvilágának finomhangolása (spec §73 elfogadási kritérium). | Alacsony | Közepes | Igen |
| M8 | Habitability / Coastal complexity / Soil fertility panel-mezők | Más, még nem épített modulokra épülnek (talaj-modul, részletes klíma) — jelenleg blokkolva. | Alacsony | Közepes | Nem |
| M8 | Morfológiai típusfelismerés | Delta/hegylánc/medence mintafelismerés a régiónevekhez — jelenleg a biome-alapú utótag ennek egyszerűsített közelítése. | Alacsony | Közepes | Nem |
| M8 | Ordinális kvantálás kalibrálása (ND-09) | ~1000 generált világ referencia-eloszlása kellene a "Low/Moderate/High" jellegű mezőkhöz — nincs elkezdve. | Alacsony | Nagy | Nem |
| M5 | Szél, nedvesség, csapadék (§30-32) | **Python-referencia kész** (ND-41, `tools/reference/wind_precipitation_ref.py`) — C#-port + felhasználói megerősítés a nyitott konstansokra (szög/együttható-értékek) hátra. | Közepes | Nagy | Részben |
| M5 | Teljes hőmérséklet-modell (`T_greenhouse`, `T_ocean`, `T_weather`, `T_cycle`) | **Python-referencia kész** (ND-42, `tools/reference/temperature_ref.py::temperature_kelvin_full`, a régi `temperature_kelvin` érintetlen) — C#-port + felhasználói megerősítés (280ppm/3.0K, 0.3 óceán-együttható, ciklus-amplitúdók) hátra. | Alacsony | Közepes | Részben |
| M7 | Tavak, jég/hó, eróziós visszahatás | **Python-referencia kész** (ND-43, `tools/reference/lakes_ice_erosion_ref.py`) — C#-port + Unity vizuális ellenőrzés hátra. | Közepes | Közepes | Igen |
| M10 | Erózió idővel, eljegesedés-ciklusok | **Python-referencia kész** (ND-44, `tools/reference/erosion_glaciation_deep_time_ref.py`, zárt alakú relaxáció, timestep-invariancia félcsoport-teszttel bizonyítva) — C#-port + felhasználói megerősítés (relaxációs időállandó, jégkorszak-periódus/amplitúdó) hátra. | Alacsony | Nagy | Részben |
| M10 | Dinamikus (térfogat-alapú) tengerszint — Unity vizuális ellenőrzés | A Core-oldali mechanizmus (ND-38) kész és tesztelt, de Unityben (időcsúszka mozgatásával) még nincs kipróbálva/megerősítve, hogy a víz-arány ténylegesen elmozdul-e vizuálisan is. | Közepes | Kicsi | Igen |
| M10 | Lemez-születés/-halál (§16) | **Python-referencia kész, összevonva az M11 rift/split-merge tétellel** (ND-45, ugyanaz a spec-szakasz) — ld. az M11 sort lent. | Alacsony | Nagy | Részben |

## Már lezárt, ebben a munkamenetben megoldott tételek (referenciaként)

- ND-36: domain warping a lemez-Voronoi határaira (szabálytalan kontinensszélek)
- ND-37: `DefaultOceanicProbability` decorrelálva a tengerszint-céltól (part-magasság)
- ND-38: térfogat-megmaradás alapú tengerszint a deep-time lánchoz
- Óceán-fenék kőzetszínezés + mélységfüggő vízréteg (a felhasználó megerősítette, jó)
