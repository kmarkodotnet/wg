# A8 — lépcsős előnézet helyett valódi finom ágak (ND-177)

## Javítás

A felhasználó felhatalmazására a durva négyirányú előnézetet kivettük.
A meglévő finom követő spekulatív ágszámítása párhuzamos marad; a kész
eredmények forrásindex szerinti commitja már a többi ág számításával
átfedve is futhat. A claimed írás és callback egyetlen kapu alatt van.
A callback teljes, kanonikusan csonkolt ágat ad át; a pontokat utána
nem módosítjuk. A viewer 1/4/16/48 kész ágnál publikál listapillanatképet,
majd a teljes hálózatot. Minden publikáció generációvédett és szeletelt.

A Rivers mesh a közös víz-shadert és kék vertexszínt kapja akkor is,
ha a GameObject már a jelenetben létezett. A korábbi külön HDRP/Lit
anyagot lecseréljük. A fél-szélesség 0,08 → 0,008 (scene + kód):
kb. 11,9 km helyett 1,19 km legkisebb teljes megjelenítési szélesség,
a korábbi gyökös vízhozam-súlyozással. Ez továbbra is szimbolikus
vizualizáció, nem hidrológiai mederszélesség. A panel megkülönbözteti
a finom ágak számítását, kirajzolását és a kész állapotot.

ND-176 korábbi klíma-/regolit-javítás megmaradt. Core-numerika,
random és generátorverzió nem változott; Python-vektorfrissítés nincs.

## Bizonyíték

- Új regressziók: progresszív callback sorrend, a befogadó korábbi ág
  létezése és pontos közös pont, valamint callbackből történő leállítás.
- Teljes Release solution: **1999/1999 PASS**: Core 859, viewer-LOD 664,
  app 452, CLI 24. Solution build 0 hiba/0 warning. Python KAT 9/9.
- Teljes 96 ágú aktuális szekvenciális és progresszív lenyomat egyezik:
  `65cb11f22ff6ad204ec325bc474e30bb4429b22d5105fd6b3dde749184cd07af`.
  Ez a forrásokat, végállapotokat, összefolyásokat, vízhozamsúlyokat és
  minden XYZ double-bitmintát tartalmazza. 415 293 pont.
- .NET kontroll: szekvenciális 135,246 s, progresszív 47,347 s;
  az első 4 kanonikus ág 3,325 s. Csúcs processz-munkakészlet 131,469 MB;
  ez nem Unity-memóriaprofil. A menetek nem teljesen izolált benchmarkok.
- Alapvilág: 66 Ocean, 19 Merged, 11 Pit. **Mind a 11 Pit végpont
  megjelenített L8 tóra esik** (minLakeTiles=6, minLakeDepthMeters=40).
  A mért átlagos ágút 509 km, max. 2193 km. Ez nem alátámasztás minden
  seedhez, és nem endorheikus modell általános igazolása.
- Két tényleges Unity Play-menet külön projektmásolaton, aktuális
  hőmodell-klíma bekapcsolva: 96 ág, 60 Ocean, 25 Merged, 11 Pit,
  0 MaxSteps. Közös víz-shader, részleges finom publikáció megjelent.
  A más forráslista miatt a CLI számaival nem keverhetők.
- Első Play: teljes finom mesh 152,159 s; max slice 7,9 ms, upload 23,5 ms.
  Második Play (regressziós terhelés is futott): 188,748 s, max slice
  7,7 ms, upload 22,1 ms. Ezek nem kontrollált gyorsulásmérések.
- A nappali HDRP-képen kék finom ágak és összefolyás látszik; a fehér,
  vastag, lépcsős durva előnézet nincs. A képrögzítés kamera-ugrás után
  történt; az új nézet terep-LOD-jának teljes felzárkózását nem igazolja.

Helyi bizonyíték: `artifacts/a8-progressive-current/`,
`artifacts/a8-sequential-current/`, `artifacts/a8-play-result.txt`,
`artifacts/a8-play.log`, `artifacts/a8-play-daylight.log`,
`artifacts/a8-partial.png`, `artifacts/a8-fine.png`.

## Állapot és következő kapu

A8/B3 nem felhasználó által elfogadott. Az aktuális hibajavítás és
műszaki ellenőrzés kész, a kép új megítélése következik. A teljes A8
tartalmilag súlyozott készültsége kb. **80%**: a bitazonos Core és a
viewer adatút kész, a kontrollált teljesítmény-/memória-/váltási és
felhasználói vizuális kapuk hiányoznak. E kör ráfordítása durván
**3–5 emberóra**, a fennmaradó átvétel/profil **4–8 óra durva becslés**,
az új visszajelzésből következő hibajavítások nélkül. Commit/push nincs.
