# Play-menet ellenőrzőlista — 2026-09-20

Ez a lap a mai munka **élő visszamérését** írja le. Minden ponthoz oda van
írva, **mit várok** — ha az eltér attól, amit látsz, az önmagában információ.

A Unity Editor a session végén leállt válaszolni, ezért az alábbiakból semmi
nem futott Play módban. **Indítsd újra az Editort**, mielőtt nekikezdesz.

---

## 0. Elsőként: nulla konzol-hiba

Play → kapcsolgasd az overlayeket (szél / csapadék / tektonikus) oda-vissza
kétszer-háromszor.

**Várt:** a konzolban **nincs** hiba. Konkrétan nem szabad megjelennie ennek:

```
ArgumentException: Új víz-snapshothoz új kiválasztás kell.
```

**Miért:** ez a hiba az ND-50 overlay-út bevezetésekor keletkezett (regresszió,
amit én okoztam), és a következménye az volt, hogy a nézegető **tartósan
visszaállt a szinkron, fő szálú újraépítésre**. Javítva (`09cde49`), de
futásidőben nem igazolva.

**Ha mégis megjelenik:** azonnal jelezd — akkor a javítás nem a teljes okot
fedte le.

---

## 1. Csapadék-cache (PerfLog: `cacheHit=`)

Play → várd meg az első Build-et → mozgasd a **deep-time** csúszkát.

**Várt:** az első Build-ben `precipitation(enabled=True, cacheHit=False)=320-410 ms`,
**minden további** Build-ben `cacheHit=True` és a sor **~0 ms**.

A cache akkor és csak akkor ürül, ha a nyolc kulcs-elem valamelyike változik:
`worldSeed`, `plateCount`, `level`, `climateDayT`, `climateOrbitalPeriodDays`,
`climateRotationPeriodDays`, `climateAxialTiltDegrees`, `targetWaterFraction`.
A `deepTimeMyr` **nincs** köztük — ez a lényeg.

**Próbáld ki azt is:** állíts a `climateAxialTiltDegrees`-en → ott `cacheHit=False`-t
kell látni. Ha ott is `True`, a kulcs hibás.

---

## 2. Sűrű tó-pipeline (PerfLog: `lakes=`, `conversion=`, `dense=`)

**Várt** a hidrológia-sorban:

| mező | eddig | most |
|---|---|---|
| `lakes=` | 180–190 ms | **~10 ms alatt** |
| `conversion=` | 12–14 ms | **0,0 ms** |
| `dense=` | (nem volt kiírva) | **True** |

A tavaknak vizuálisan **ugyanott, ugyanakkorának** kell lenniük — a kimenet
bitre azonos, ezt 12 orákulum-teszt igazolja. Ha bármelyik tó eltűnik vagy
elmozdul, az komoly jelzés.

---

## 3. Split-kvóta 1024 → 8192 (a #1 visszajelzésed)

Zoomolj végig a skálán, közben forgass is. Figyeld, mennyi idő alatt
„élesedik ki" a kép egy mozdulat után.

**Várt:** a konvergencia 13–17 kérésről **3**-ra csökken, az összes munka
326–600 ms-ról 58–115 ms-ra. A **végső** részletesség **változatlan** — a
kvóta csak késleltetés volt, ezt teszt rögzíti.

**Amit tudnod kell:** a tile-méret panaszod másik fele NEM ettől oldódik meg.
A `MaximumRenderBudget = 48 000` plafon ma **köt**: 1920×1080-on a képlet
97 200 levelet kérne. Ha ki akarod próbálni, **nem kell kódot módosítani** —
az `adaptiveRenderBudget` mezőt Play közben írd át 96 000-re (0 = automatikus).
Ez az **ND-121** döntés.

---

## 3b. Hideg Build: terrain-bázis lemez-gyorsítótár (ND-122)

**Az ELSŐ Play** (üres gyorsítótárral): a PerfLogban a `terrainBasis=` sor
változatlanul **7,4–8,5 s**, utána viszont megjelenik egy új sor:

```
[terrain-basis cache] KIIRVA basis_....bin (54.4 MiB, ~150 ms)
```

**A MÁSODIK Play** (vagy `worldSeed`/`adaptiveBaseLevel` visszaállítás után):

```
[terrain-basis cache] TALALAT basis_....bin (54.4 MiB, ~150 ms)
```

és a `terrainBasis=` sornak **~0,2 s** körülinek kell lennie 7,4–8,5 s helyett.
Az első Build így nagyjából 13–14 s-ról **6–7 s-ra** csökken.

**A fájlok helye:** `Application.persistentDataPath/terrainBasisCache/`.
Nyugodtan törölheted bármikor — a gyorsítótár **nem** világállapot, a törlése
csak lassít.

**Amit ellenőrizni érdemes:** a domborzatnak **pontosan ugyanolyannak** kell
lennie, mint gyorsítótár nélkül. Ha kétséged van, kapcsold ki az
Inspectorban a `useTerrainBasisDiskCache` mezőt, és hasonlítsd össze. Ha
`ELUTASITVA` sort látsz, az nem hiba — azt jelenti, hogy a mentett fájl nem a
mostani algoritmussal készült, és a rendszer helyesen újraszámolt.

---

## 3c. ELSŐ DOLOG a következő futásnál: az emit ön-ellenőrzése (ND-123)

A statikus alapréteg emitje mostantól **párhuzamosan** fut (a legnagyobb
egyetlen tétel volt: 584–688 ms). Mivel az Editor a session alatt nem
válaszolt, **beépítettem egy ön-ellenőrzést**, ami alapból BE van.

**Amit a PerfLogban látnod kell:**

```
[ND-123 emit verify] EGYEZIK (parhuzamos == egyszalu), ellenorzes=...ms
```

Plusz a `BuildStaticBaseLayer reszletek` sorban `parallelEmit=True`, és az
`emit=` értéknek érdemben kisebbnek kell lennie 584–688 ms-nál.

**Ha ehelyett `[ND-123 emit verify] ELTERES: ...` jelenik meg** (és egy
konzol-hiba), az azt jelenti, hogy a párhuzamos út mást ad — akkor kapcsold
ki az Inspectorban a párhuzamos emitet nem lehet külön kikapcsolni, de a
`showBorders` bekapcsolása visszaviszi az egyszálú útra, és jelezd nekem az
üzenetet (benne van, melyik bucket melyik eleme tér el).

**Amíg az ellenőrzés be van kapcsolva, a Build LASSABB** (kétszer emitel).
Ha az `EGYEZIK` megjelent, a `verifyParallelStaticEmit` mezőt kapcsold ki —
vagy szólj, és én kapcsolom ki a kódban.

**Fontos, hogy mit NEM mér:** a várható gyorsulás nem a magszám, hanem
nagyjából **2–3×**, mert a párhuzamosságot a legnagyobb bucket (az óceáni
terep-slot) határolja. Ha az `emit=` csak ennyit javul, az az elvárt
viselkedés, nem hiba.

---

## 4. Mire számíts a nagyobb budgetnél — offline előrejelzés

Ez a 6. feladat eredménye: a chunk-csomagolás és a feltöltés **offline
mérhető**, tehát nem kell vakon próbálgatni.

**A modell hitelesítve:** ugyanezen a nézeten, 32 000-es budgettel a modell
52 változott chunkot és 4269 újraépítendő levelet ad; a te ÉLES naplódban
(`cutBudget=30138`) `chunks=51..81` és `emittedLeaves=4422..5144` szerepel.

**Egy kis kamera-mozdulat (0,25°) után újraépítendő munka:**

| magasság | budget 8 000 | budget 48 000 |
|---|---|---|
| 30 (távoli) | 24 chunk / 812 levél (10,2%) | **46 chunk / 1424 levél (3,0%)** |
| 10 (közepes) | 16 chunk / 488 levél (6,1%) | **67 chunk / 6053 levél (12,6%)** |
| 3 (felszín-közel) | 36 chunk / 4608 levél (57,6%) | **84 chunk / 13011 levél (27,1%)** |

**Amit ebből tudni érdemes:**

1. **A chunk-szám szublineárisan nő**: hatszoros budget mellett 1,9× / 4,2× /
   2,3× a három sávban. Ez azért fontos, mert a frame-enkénti feltöltési korlát
   `TerrainUploadsPerFrame = 128` **job**, és a naplóid szerint
   `jobs ≈ 2 × chunk + 3`. Vagyis **~62 változott chunk fölött** a feltöltés
   átcsúszik a következő frame-re — ez a `TerrainUploadSliceMs = 2` szeletelés
   szándékos működése, **nem akadás**.
2. 48 000-nél a közepes és a felszín-közeli sáv 67 és 84 chunkot ad, tehát
   ott a feltöltés rendszeresen **2 frame**. Ha akadást érzel, ezt keresd a
   naplóban (`[ND-85 upload begin] chunks=... jobs=...`), ne a szelekciót.
3. **A relatív újraépítés a nagyobb budgetnél CSÖKKEN** a távoli és a
   felszín-közeli sávban (10,2%→3,0% és 57,6%→27,1%) — a nagyobb vágás
   *stabilabb* egy kis mozdulatra. A közepes sáv a kivétel (6,1%→12,6%), és
   éppen ez az a sáv, amire panaszkodtál.
4. A csomagolás maga olcsó: 0,6–8,4 ms.

---

## Amit ezek után tőlem várhatsz

Ha az Editor újraindul és a 0–3b. pont rendben van, a maradék nyitott
kérdésekre (**ND-119**, **ND-120**, **ND-121**) a döntésed kell. A
`todo.md` első táblájából **2 tétel** marad: a 8. (statikus mesh
direct-array emit) és a blokkolt 10. (maradék regolit-mezők).
