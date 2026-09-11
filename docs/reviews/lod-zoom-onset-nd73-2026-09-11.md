# Korábbi látható finomodás — ND-73 (2026-09-11)

**Utólagos státusz:** a felhasználó az eredményt elutasította. Az újabb,
18:39:49-es logban még 0,600 a morph-range, a scene fájlban 0,35;
a részleges érvényesülés miatt az alábbi teljes profil nem tekinthető
élőben igazoltnak. Ez nem cáfolja a minőségi panaszt. Folytatás: ND-74.

## Bizonyíték és cél

A felhasználó szerint az ND-72 után valamivel jobb, de zoomra túl későn
kezd részletesedni a felszín. Elsődleges mérés:
`unity/WorldGenViewer/Logs/PerfLog_20260911_182258.txt`.

Szárazföldi nadír, `nadirOceanBlocked=False`, `nadirUnderWater=False`:

| Kameratávolság a középponttól | Renderlevél | Vetített quad | Morph-alfa |
|---|---:|---:|---:|
| 149,319 | L8 | 11,17 px | 1 (statikus base) |
| 140,379 | L9 | 6,84 px | 0,105 |
| 122,161 | L9 | 12,98 px | 0,809 |
| 114,855 | L10 | 10,15 px | 0,517 |
| 103,663 | L12 | 18,89 px | 0,533 |

Két külön késleltetés van: a 12 px osztási cél mellett az első sor még nem
finomodik; az új L9 csúcsai pedig a második sorban csak 10,5%-ban távolodnak
el a coarse háromszögektől. A finom háló előállítása és a finom pozíció
teljes megjelenése nem ugyanaz. A közeli 18,89 px az alapgömbös metrika
megmaradt korlátját is mutatja: a 12 px nem valódi terrain-hibagarancia.

Az ND-72 visszaállítása után a log cut-idői ismét tizedmásodpercesek,
a diagnosztika tipikusan 0,06–0,10 ms. Az 1–2 s körüli teljes frissítések
miatti időbeli késés továbbra is létezik; e hangolás azt nem oldja meg.

## Elvetett szélesebb változtatás

A minden LOD-szintre érvényes 12→10 px cél a kontrollált be-/visszazoom
csúcslevélszámát 107 739-ről 195 118-ra növelte. Ezt nem aktiváltuk.
Az új kód nem kapcsolja vissza az ND-71 domborzati mintavételét sem.

## Aktivált, szűkített hangolás

- **Első base-osztás:** 10 px, új `initialRefinementPixelSize` mező.
- **További szintek:** változatlan 12 px `targetTilePixelSize`.
- **Merge:** változatlan 8 px (12/1,5); a base effektív hiszterézise 1,25.
  Ha az új split nem fér a merge és a normál split közé, nincs előrehozás.
- **Morph-range:** 0,6→0,35. A teljes finom pozíció a split-távolság 65%-ánál
  elérhető, a korábbi 40% helyett. Születéskor az alfa továbbra is nulla,
  nincs a képletben szándékos pozícióugrás.
- **Budget/maxLOD:** változatlan 200 000 / L20.

A base-szülő morphja ugyanazt az előrehozott küszöböt kapja, mint az osztás.
A küszöbök és a morph-range a kérés elején rögzítettek, nem változó Inspector-
adatot olvas a worker. Az opcionális API-skála nélküli viselkedés megmarad;
érvénytelen API-skálára explicit hiba jár. GPU-n nincs előrehozott base-split.

A `PlanetView.unity` tényleges szerializált értékei is frissültek; a kód
régi 48 px alapértéke a scene már korábban használt 12 px értékéhez igazodott.
A world-/seed-/Core-adatok és a km-lépték backlogja nem változott.

## Kontrollált összehasonlítás

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -p:UseAppHost=false -- --onset
```

Testirány: a friss log `(31.581, -127.684, 49.049)` kamerájának normalizált
iránya. FOV 1,047197543 rad, fél-nézetkúp 1,132737064 rad, magasság 688 px.
Azonos 10 közelítési és 9 visszatérési távolság, végig explicit előzmény.
Nem teljes élő replay: kontrollált egyenes zoom, kerekített bemenetek,
Unity mesh-/képmérés nélkül. Az itt mért alfa a nyers morph; a közösél-
illesztés további korlátozásokat alkalmazhat.

| Közelítési távolság | Régi LOD / alfa | Új LOD / alfa | Régi / új cut darab |
|---|---|---|---|
| 149,319 | L8 / statikus | L9 / 0,132 | 0 / 5668 |
| 140,379 | L9 / 0,105 | L9 / 0,626 | 5688 / 26 284 |
| 133,060 | L9 / 0,388 | L9 / 1,000 | 20 348 / 43 140 |
| 114,855 | L10 / 0,517 | L10 / 0,887 | 75 492 / 103 180 |
| 108,152 | L11 / 0,405 | L11 / 0,695 | 84 267 / 84 267 |
| 103,663 | L12 / 0,533 | L12 / 0,914 | 74 184 / 74 184 |

Az első felosztás a vizsgált sorban egy görgetési lépéssel korábban indul.
A teljes be-/visszazoom csúcslevélszáma **107 739→135 427 (+25,7%)**.
A 108,152 / 104,474 / 103,663 közeli kamerák hideg cutjai halmazként is
egyeznek a régivel, nem csak darabszámban. A végső távoli nézetben mindkét
profil visszatér a statikus alaphoz, üres dinamikus cuttal.

**Költségkorlát:** ez nem ingyenes. A korai zoomnál új, korábban még nem
létező tile-ok készülnek; 140,379-nél a cut több mint négyszer akkora, mint
a régi kis kezdő cut. A csúcsérték +25,7%-a nem jelenti, hogy minden kérés
ideje csak ennyivel nőhet. A parancssori cut-idő nagyságrendileg tizedmásodperc
maradt, de ebből nem következik Unity-emisszió/upload-idő vagy FPS. A korábbi
globális 10 px változatot pontosan a nagyobb költség miatt vetettük el.

## Ellenőrzés és nyitott kapu

- Core 381/381, CLI 7/7, LOD 97/97 Debug és Release; összesen 485 teszt.
  Új tesztek: korábbi első split, változatlan közeli
  cutok, be-/visszazoom budget és csúcslevélszám, explicit előzmény,
  base-morph küszöbegyezés, folytonos/monoton morph és érvénytelen skálák.
- Solution build: 0 hiba / 0 figyelmeztetés.
- Unity assembly fordítás: 0 hiba / 83 figyelmeztetés.
- A scene-bekötés szerializált mezők szintjén ellenőrzött, élő Editor-
  importot és látványt a parancssori fordítás nem igazol.
- `git diff --check` tiszta. Python nincs, KAT/regenerálás nem futott;
  Core és verziózott tesztvektor nem változott.

Kért élő próba: újraindított Play-módban ugyanazon szárazföldi nézet
be-/visszazoomja, a mesh-frissítések kivárásával. Az új fő logjel
`[async apply ND-73]`; a `[ND-72 render]` diagnosztikai sor marad.
A kickoff-log `baseTarget` és `morphRange` mezője igazolja az aktív profilt.
Korábbi részletmegjelenést, esetleges átmeneti pattogást és frissítési időt
együtt kell ellenőrizni. Élő ND-73 visszajelzés még nincs.

A közelről túl nagy tile-ok, valódi felszínhez igazított kamera/LOD és a
teljesen inkrementális emisszió továbbra is külön nyitott feladatok.

Durva becslés: M9 **60–70%**, jelen hangolás és ellenőrzés **2–4 óra**,
hátralévő zoommunka **14–30 óra**, a külön km-lépték UI nélkül. Nem mért idők;
élő elfogadás nélkül nem emeljük a készültséget pusztán a kód elkészülése miatt.
