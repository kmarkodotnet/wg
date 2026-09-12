# ND-83 — Vízfelszín-LOD renderbekötés, próbára átadás

## Mi változik a futó viewerben?

Az ND-82 külön kiválasztója most ténylegesen rajzol vízgeometriát. A CPU,
perspektivikus, terrain-proxys adaptív útban a víz akkor is finomodhat,
ha az alatta lévő mély óceáni terepet az ND-78 kizárja a finomításból.
Nem változott a terep 12/10 px célja, a korai élesedés vagy a kamera.

- Saját vízkeret: 8192 dinamikus levél, kérésenként 256 új osztás.
- A statikus emitter tényleges vízazonosítói és a feltöltött submesh-indexek
  adják a vízmaszkot. Csak teljesen kiváltott base rejthető el.
- Nyers, morph nélküli modellmagasságból jön a mélységszín. Az overlay-ágak
  megmaradnak. A víz nem számol terep-normálokat vagy tile-biome-ot.
- A nyers színek 65 536 elemig gyorsítótárazottak. A geometria és RGB a
  meglévő közösél-resolver azonos topológiájával illeszkedik.
- A víz saját két váltott objektumot kap. A kész mesh publikálásakor vált
  a statikus maszk és az aktív buffer. Sikertelen feltöltésnél teljes
  statikus víz a tartalék; megszakított worker nem publikál.
- Azonos víz-cutnál nincs új vízemisszió/feltöltés. Új Build érvénytelenít;
  a korábbi index-layoutot nem írjuk vissza egy már újraépített mesh-re.

Nincs Inspector-/scene-átírás. A támogatott CPU-út automatikusan használja.
A GPU, ortografikus nézet, kikapcsolt terrain-proxy vagy nem támogatott
base a régi vízútra esik vissza. A tavak saját vízszintje és objektuma
változatlan; száraz base alatti finom parti víz a meglévő terrain-útban marad.

## Mit kell a logban nézni?

- `[ND-83 water source]`: base, tényleges vízgyökérszám, float tengerszintsugár.
- `[ND-83 water apply]`: leaves, replacedBase, deepestLevel, new/deferredSplits,
  selection/balance, emit, colorSamples/cache, upload, maskIndices, reusedMesh.
- `[ND-75 drawn]`: `independentWater`, `waterLeaves`, `hiddenStaticWaterQuads`;
  a meglévő statikus/dinamikus vízquad-statisztika most a maszkolt statikus
  vízquadokat kihagyja. Ez a feltöltött pozíciókat méri, nem a LOD célértékét.

A víz számítása a meglévő workerhez adódik, tehát nem ingyenes. Külön log
mutatja, mennyivel növeli a kérés és a főszálas upload költségét. A 256-os
splitkvóta nem milliszekundumos garancia. A geomorph nélküli vízváltás és a
régi parti vízzel találkozó határ külön vizuális ellenőrzést igényel.

## Kérlek ezt próbáld ki

1. Új Play-indítás; mély óceán fölött távoli → közepes → mély zoom.
   Néhány ponton állj meg, hogy a külön vízfinomítás felzárkózhasson.
2. Part mentén ugyanez, majd teljes visszazoom: ne legyen lyuk, dupla,
   villogó vízréteg vagy feltűnő színvarrat.
3. Egy tó környéke: maradjon a saját vízszintje.
4. Deep-time/seed vagy vízmennyiség változtatása után ne maradjon régi vízfolt.
5. Ha használsz szél-/csapadék-overlayt, kapcsolj át és vissza.

Utána PerfLog és rövid visszajelzés szükséges. A részletesebb vízmesh nem
jelent új, a világmodellben nem létező részleteket. Az első zoomok korábban
halasztott panasza nincs megoldva, és nem ez a lépés célozza.

## Ellenőrzési státusz

Hét új parancssori teszteset: ritka/fordított index-layout, változatlan
hiányzó quadok, hibás kérés előtti teljes validáció, üres maszk, teljes
statikus+dinamikus fedés három költségkerettel, megszakítás, visszazoom,
durva színél és geometria sarokgazdájának egyezése. Egy új Unity Editor
teszt valódi Mesh API-val ellenőrzi a ritka vízmaszkot; futtatása még nyitott.
A fordítási és regressziós eredmények a [munkanaplóban](../../history/2026-09-11-water-lod-render-nd83.md).

**Élő Unity-próba még nem történt: vizuális vagy sebességi készjelentés nincs.**
M9 tartalmilag továbbra is durván 60–70%. E lépés ráfordítás-egyenértéke
durván 3–5 óra, víz-validáció/korrekció még 1–3 óra; a fennmaradó zoom-
és megjelenítési munka 8–20 óra. Ezek nem mért időadatok.
