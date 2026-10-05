# A8 / ND-193 — a folyószalag a RENDERELT felszínre vetül (2026-10-05)

Felhasználói visszajelzés képen (`pics/p.png`): pirossal „összevissza folyók",
lilával „a folyó nem éri el a tavat, vagy épp nagyon belenyúlik" — és egy
hipotézis: *„amikor a zoomolás miatt pontosabb kalkulációt kap a tó pereme, a
folyó végpontja a zoomoláskor nincs újraszámolva."*

A hipotézis igaz volt. A mérés viszont egy NAGYOBB okot is talált mögötte.

## 1. Mérés — a folyóvonal 37 km-rel a terep FÖLÖTT lebegett

Élő Editorban (seed 0xA7C944210000, t=0, 96 ág, 484 096 pont, kamera 988 km
magasan, 1275×809): minden folyópontra kiszámoltam a KIRAJZOLT vertex és az
ugyanazon a felületi ponton lévő RENDERELT terep-háromszög
(`LodCoverage.FindRenderedLeaf` + `SurfaceQuad.At`) távolságát.

| Mérőszám | Érték |
|---|---|
| sugár-irányú eltérés, átlag | **37,04 km** |
| képernyő-elcsúszás, átlag / max | **9,87 / 22,7 képpont** |
| a folytonos mező és a renderelt mesh eltérése, level 8 | átlag 21,5 m, max 147,9 m |
| ugyanez level 9 / 10 / 11 | 4,5 / 0,9 / 0,5 m |

A 37 km teljes egészében a `riverLineRadialBias = 0,5` Unity-egységből jött.
A mező doksija „~50 m ekvivalens"-et ígért — ez a RÉGI `elevationScale = 0,01`
mellett volt igaz. A mai lánccal (`elevationScale = 1,3477e-5`,
`terrainReliefExaggeration = 111`) ugyanaz a 0,5 egység **334 világ-méter**,
kirajzolva **37,1 km**. Ferde rálátásnál ez parallaxis: a vonal a völgyéből a
hegyoldalra, a tóba, vagy a parttól beljebb csúszik.

**Ugyanaz a hibaosztály, amit a hőmodellnél kétszer is elkaptunk: a konstans
maradt, a mező léptéke változott.** A táblázat utolsó két sora egyben azt is
megmutatja, miért javul zoomra: a renderelt mesh a folytonos mezőhöz
konvergál (21,5 m → 0,5 m level 8 → 11 között).

## 2. Mérés — a vágás tile-granularitású volt

Az ND-187 óta a szalagot a megjelenített tó-TILE-halmaz kapuzta (level 8 ≈
39 km-es tile), miközben a kirajzolt vízfelszín a tó-réteg és a terep
metszete. Ugyanabban az állapotban:

| Mérőszám | Érték |
|---|---|
| teljes hossz | 44 974 km (ebből rajzolva 25 179) |
| tó-tile miatt kivágva, de a renderelt terep a víz FÖLÖTT | **715,1 km** |
| kirajzolva, de a renderelt terep a víz ALATT | **32,9 km** |
| ágvégek a renderelt vízvonal fölött | **56 / 96** |

## 3. A javítás (ND-193, csak megjelenítés)

1. A szalag a **RENDERELT** felszínre vetül (ugyanaz a háromszögelés, amiből
   a terep-mesh készült), nem a folytonos elevációmezőre.
2. Ott szakad meg, ahol a renderelt felület a **kirajzolt** vízszint alá
   kerül (tónál a `BuildLakeSurface` gyűrűvel kiterjesztett feltöltési
   szintje, egyébként a tengerszint; a finomított réteg vizénél a
   `dynamicLayerRadialBias`-szal együtt).
3. **Minden LOD-alkalmazás után újravetül** (`AdoptRenderedSurface` →
   `MaintainRiverSurfaceProjection`, 0,4 s nyugalmi késleltetéssel, mert a
   LOD akár 0,1 s-onként is újraépül, a folyómesh viszont ~0,4 s alatt készül
   el) — ettől számolódik újra a végpont zoomoláskor.
4. A sugár-eltolás `riverLineBiasWorldMeters = 10` VILÁG-méter, a terep
   megjelenítési láncán átszámolva.

**Szálbiztonság.** A vetítés a fő szálon fut, miközben egy worker már a
következő LOD-ot építheti, ezért csak adatverseny nélkül elérhető sarkot
olvas: alap-szinten a statikus, csak-olvasott sarok-tömböt, finomított
szinten kizárólag a MÁR feloldott sarkot
(`LodCornerResolver.TryGetResolvedCorner`). Mérve: **projectionMisses = 0,
projectionFallbacks = 0** mind az áttekintő, mind a finom hálózaton.

Amit tudatosan NEM teszünk: nem hosszabbítjuk a vonalat a renderelt partig —
az toldás lenne (I3). A rés magától záródik a zoommal.

## 4. Mérés a javítás UTÁN (ugyanaz a kamera, ugyanaz a seed)

A kirajzolt mesh középvonal-vertexeiből (nem újraszámolt modellpontokból):

| Mérőszám | Előtte | Utána |
|---|---|---|
| sugár-irányú eltérés a renderelt mesh-től | 37,04 km (= 334 világ-m) | **10,00 világ-méter** (min 9,98, max 10,00) |
| képernyő-elcsúszás, átlag / max (988 km) | 9,87 / 22,7 px | **0,30 / 0,69 px** |
| kirajzolt vertex a renderelt víz alatt | 32,9 km hossz | **0 / 30 331** (áttekintés), **0 / 3 791** (finom minta) |

Finom hálózat: 96 ág, 296 310 kirajzolt pont, 187 786 pont víz alattiként
kivágva, 37 chunk, 592 692 vertex. Újravetítés költsége:
`activeMs = 383,8`, `maxSliceMs = 8,4`, `uploadMs = 35,6` — tehát egy
LOD-megállapodás után ~0,42 s, képkockákra osztva.

**Zoom-próba (a felhasználó hipotézise).** 988 km → 222 km: a LOD level
13-14-re finomodott, `surfaceRevision` 13 → 15, és a folyómesh
`riverMeshSurfaceRevision` is 15 lett, azaz **újravetült**. Ott mérve:
sugár-eltérés 9,99 m, víz alatti vertex 0, képernyő-elcsúszás 2,53 / 3,99 px
(ugyanaz a 10 m, csak közelebbről több képpont).

Próbaként 3 m-es eltolással is újraépítettem: a vonal ott is összefüggő
maradt (nincs z-fighting-szakadás), tehát a 10 m konzervatív választás, és a
mező szabadon lejjebb vehető, ha a felhasználó közelről is szorosabbat kér.

## 5. Vizuális ellenőrzés

`pics/nd193_before.png` ↔ `pics/nd193_after.png` (azonos kamera):
- a tó bal partján korábban ~25 px-szel a part ELŐTT véget érő folyó most
  pontosan a partvonalon áll meg;
- a tó belsejében lebegő, különálló folyó-darab eltűnt;
- a tó nyakán átvágó ág most a parton áll meg;
- az a folyó, ami korábban a tó BELSEJÉBEN kezdődött, most a parttól indul.

`pics/nd193_after_zoom.png`: 222 km-ről ugyanez, a finomabb tóperemhez
igazodva.

## 6. Ami NYITVA marad (a felhasználó PIROS jelölései)

A piros keretek („összevissza folyók") egy részét a parallaxis okozta — azt
ez a kör megszünteti. Ami a közeli képen továbbra is látszik: hosszú, EGYENES
folyószakaszok, amik nem követik a terep kis völgyeit. Ezek a medence-átvágó
„escape"-szakaszok (a hossz jelentős része, ld. ND-188), plusz a közeli
képen látható mikro-domborzat SHADER-oldali (nem modell), tehát a folyó
definíció szerint nem követheti. Ez külön, MÉRENDŐ kérdés — nem javítom
csendben.

## Ellenőrzés

- `dotnet build tests/WorldGen.Viewer.Compile`: 0 hiba.
- `dotnet build tests/WorldGen.App.UnityBinding.Compile`: 0 hiba.
- `python tools/ci/check_burst_strict.py`: OK (343 fájl).
- Élő Editor: újrafordítás 0 hiba, teljes Play-menet (világ + 96 ág finom
  hálózat 280 s-nál) **0 piros konzolüzenet**.
- Nincs Core-/seed-változás, nincs generátorverzió-emelés.

## 7. Elfogadás

**2026-10-05: a felhasználó elfogadta az A8-at** (*„A8-at elfogadom,
regisztráld"*). Regisztrálva: `todo2.md` A8 🟡 → ✅ és B3 👁 → ✅,
`docs/05-milestones.md` (súlyozott A8-becslés 95% → 100%),
`docs/06-user-verification-checklist.md` (az A8 záró átvétel szakasza
történetivé minősítve), `CLAUDE.md` Állapot.

Az elfogadás NEM terjed ki a hosszú, egyenes medence-átvágó („escape")
folyószakaszokra — az külön, mérendő modellkérdésként nyitva marad.
