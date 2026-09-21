# 2026-09-21 — ND-125: a „szabályos lemezek" overlay-hiba volt, nem világmodell-hiba

**Feladat:** #4 — lemez-szabálytalanság. A visszajelzés: „a lemezek rendkívül
szabályosak, szinte mindegyik egy négyszög vagy háromszög, némelyik pedig
brutál nagy."

A tétel **seed-törőnek volt beütemezve** (verzió-emelés + ND-09 ordinális
kalibráció újrafuttatása), és a munkarend szerint előbb tervet kellett volna
írnom. A vizsgálat kiderítette, hogy a panasz nagyobbik feléhez **egyik sem
kell**.

## A gyökérok

`PlanetGridMesh.TectonicPlateColorAt` a **NYERS** pozícióval hívta a
`PlateGeneration.AssignPlate`-et. A világmodell viszont — `SeaLevelCalibration`,
`RiverPathTracing`, a teljes domborzat-lánc — a **WARPOLT** pozícióval kérdez
(ND-36). Az overlay tehát egy másik lemez-felosztást rajzolt, mint amit a
domborzat használ.

Mérve (level 7, 98 304 tile, három seed):

| | nyers (az overlay) | warpolt (a világmodell) |
|---|---|---|
| eltérő hozzárendelés | — | **19,5–26,4%** a tile-okból |
| kerület/√terület | 4,9–5,2 | **7,2–7,9** (1,40–1,59×) |

A kerület/√terület a döntő: a nyers felosztás értéke a konvex sokszögekére
jellemző — mert az is, definíció szerint (gömbi Voronoi). A felhasználó
pontosan ezt látta.

Ez ugyanaz a hibaosztály, mint az **ND-119** (a szél-overlay nem a szimuláció
szelét mutatta) — két nappal korábbról. Érdemes lett volna előbb megnézni,
hogy van-e még ilyen.

## A javítás

Új kanonikus belépési pont: `PlateGeneration.AssignPlateWarped` (warp + assign
egy helyen), dokumentálva, hogy lemez-hovatartozást MEGJELENÍTENI csak ezzel
szabad. Az overlay ezt hívja. **Nem seed-törő** — a világmodell egyetlen bitje
sem változik.

Költség: 0,022 → 8,3 µs/sarok. Megmértem, mert a szél-overlaynél épp ez volt a
hiba (238 µs/sarok); ez a nagyságrend a már elfogadott, javított szél-overlayé
(6,3 µs), és a sarok-szín-előszámítás párhuzamos.

## A méret-eloszlás: a mérés ELLENTMOND a benyomásnak

A „némelyik brutál nagy" valódi modell-tulajdonság. Megmérve (lemez-terület a
bolygófelszín %-ában):

| Seed | Legnagyobb | Legkisebb | Top 3 együtt |
|---|---|---|---|
| `0xA7C944210000` | 12,1% | 1,6% | 31,1% |
| `0x1234` | 8,7% | 1,5% | 25,0% |
| `0xDEADBEEF` | 10,4% | 0,6% | 27,1% |
| **Föld** | **20,4%** | **0,05%** | **42,4%** |

Az eloszlásunk tehát **egyenletesebb**, mint a Földé, nem szélsőségesebb: a
legnagyobb lemezünk feleakkora, mint a Csendes-óceáni, és apró lemezünk
nincs. Ha ezen változtatni kell, az több szórást jelent (súlyozott Voronoi),
nem kevesebbet — és **az** már seed-törő. Döntést kér, de csak a javított
overlay megnézése után: különben egy seed-törő változást hoznánk meg egy
megjelenítési hiba okozta benyomás alapján.

## Tanulság

**A „seed-törő, tehát előbb terv" besorolás maga is ellenőrzésre szorul.**
A feladatot két napja soroltam seed-törőnek, a felhasználói szöveg alapján.
Ha a tervírással kezdem, egy olyan változás opcióit írtam volna le, amire
nem volt szükség. Az első lépés nem a terv volt, hanem a **mérés**: mit lát a
felhasználó, és mit használ a modell.

## Állapot

- Core tesztek: **516 ✓** (511 → +5), offline kapuk 0 hiba, Unity Editor
  fordítás hibátlan.
- Vizuális ellenőrzés hátra: a `todo.md` 2. táblájának 0. sora.
