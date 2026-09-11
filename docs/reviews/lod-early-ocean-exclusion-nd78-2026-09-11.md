# ND-78 — ND-77 logelemzés és a késleltetés célzott részjavítása

2026-09-11. **Implementált, élő próbára vár. Az 1. lépés teljes
minőségi célja még nincs megoldva.** Nem állítjuk, hogy a nagyra maradó
quadok mind finomabbak lesznek; a bizonyított osztáskeret-pazarlást javítjuk.

## Élő bizonyíték

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260911_212059.txt`.
Az ND-77 aktív, `terrainIdentity=ND77`. Seed `184482873278464`, tengerszint
`1648.0487450466471`, sugár 100, elevációskála 0,001, relief 1,5.

| Idő | Tile | Kirajzolt teljes clipped quad | Request-proxy | Megállás |
|---|---|---:|---:|---|
| 21:21:17–19 | `6800000000006270`, F3/L8/u140/v84 | 28,816 px | 9,194 px | below-threshold, 10 px |
| 21:21:21 | `69000000000014B0`, F3/L9/u100/v12 | 33,060 px | 6,087 px | below-threshold, 12 px |
| 21:21:53–55 | `68000000000049D4`, F3/L8/u158/v40 | 31,678 px | 7,176 px | below-threshold, 10 px |

Mindhárom állapotnál nulla alkalmazott kamerakülönbség, nincs függő kérés
és nincs halasztott folytatás. Az első és harmadik tile statikus:
nem morpholt és nem a közösél-resolver alakította nagyra.

A két statikus tile négy feltöltött sarkának független vetítése:

- Első: nyers pontokból **28,816176 px**, vízszintre emelt mély sarkokból
  **9,194226 px**. Nyers saroksugarak kb. 93,9855 / 102,0486 / 101,8509 /
  93,9309; a vízfelszín sugara **101,648049**.
- Második: nyersen **31,677595 px**, vízszintpadlóval **7,176460 px**.
  Az egyik sarok sugara 93,9526, a többi 101,982–102,146.

Ez pontosan reprodukálja a logban szereplő eltérést. A proxy clampingja
eltünteti a mély partfal kiterjedését, a mesh nem. Ugyanakkor az ND-75
teljes quad-átmérője **nem a takaratlan fragmentumok kiterjedése**:
a viewporton kívüli részt levágja, de a más felszín által takart részt nem.
A sugárkülönbség bizonyítja, hogy a quad víz alá nyúlik; a log nem
bizonyítja, pontosan mekkora részét takarja a víz végső GPU-képen.

Külön késési bizonyíték: 21:21:33–37 között ugyanaz a L10-es
`6A00000000061235` tereptile 16,525 px-es, 12,192 px-es request-becsléssel,
**`split-quota`** megállással. Álló kameránál több egymást követő adagban
sem jut osztási kerethez. Nem max LOD vagy leaf-budget a megállás oka.

## Az elvetett javítás

Először a nyers terepsugarak megőrzését és a quad/bounds metrikában való
használatát próbáltuk. A két statikus mérési hibát helyrehozta, de a
bolygóméretű próba aránytalan túlosztást mutatott: 300-as távolságnál már
~17 ezer, közepes zoomnál ~60–67 ezer tereplevél maradt, a régi 0 / ~1 ezer
helyett. Mélyebben 116–129 ezerig nőtt a renderelendő terep. Ez már a
korai óceáni kizárással együtt mért változat volt.

Ez nem elfogadható megoldás a „ne lassuljon, legyen részletes” kérésre.
**A runtime-kísérlet visszavonva**, a `TerrainLodProxy` változatlanul az
ND-76-os működésű. A regressziós bizonyító teszt és az offline próba
megőrzi a kísérlet reprodukcióját, de nincs aktív nyersmélység-kapcsoló.

## A tényleges részjavítás

A kiválasztó eddig felosztotta a tengerfeneket is, felhasználva a kérésenkénti
1024-es osztáskeretet. Ezután a renderer az `IsBaseAncestorOceanic` alapján
a teljes base-terület alatti finomítást eldobta. A látható terep így olyan
osztások mögött várt, amelyekből soha nem lett dinamikus mesh.

Most pontosan ugyanez a kizárás **a statikus alapszinten, még a heap és az
osztási kvóta előtt** történik. A feltétel nem szigorodott: víz alatti
középpont és mind a négy base-sarok a víz alatt. Vegyes part továbbra is
finomítható. A meglévő renderoldali ellenőrzés is megmaradt.

A CPU/proxy kiválasztás kap callbacket; az kizárólag a base-szinten fut,
a kész statikus besorolást/sarkakat olvassa. Nincs új Core-kiértékelés,
nincs vízgeometria-változás, nincs pixelcél-, budget-, morph-, scene- vagy
seed-változás. Ez nem az általános upload-optimalizálás megkezdése.

Új napló: `earlyOceanExclusion=ND78`, `skippedSelectionBases`; a döntési
trace-ben `renderer-base-exclusion`. A számláló kizárt **base-területek**
száma, nem megtakarított osztások száma. Az ND-77 többi mezője marad.

## Offline eredmény

Futtatás:

```text
dotnet run --project docs/reviews/ViewerLodDiagnostic.csproj -c Release --no-restore -p:_EnableDefaultWindowsPlatform=false -- --shoreline
```

A `mode=0` az ND-76, a `mode=1` az elvetett nyersmélység-kísérlet,
a `mode=2` az átadott korai kizárás. Két logbeli nézetirány, 7–7 állás,
a régi/új állapotsor saját előzményével. A szűrés utáni renderelt
terep-**TileId-halmaz mind a 14 állásnál azonos** a régi és az átadott új
út között (`sameRenderedSet=True`); nem csak a levélszám egyezik.

| Irány / középponttávolság | Régi kiválasztott levelek | Új kiválasztott levelek | Mindkét út renderelt tereplevelei |
|---|---:|---:|---:|
| 1 / 122,160638 | 31 312 | 8 338 | 8 338 |
| 1 / 109,957422 | 34 324 | 4 486 | 4 486 |
| 2 / 122,160638 | 36 233 | 24 793 | 24 793 |
| 2 / 109,957422 | 32 362 | 29 719 | 29 719 |

Állókamerás, előzmény nélküli 1024-es adagolás a 2. irányban:

| Távolság | Régi → új hullámok | Régi → új osztások |
|---|---:|---:|
| 122,160638 | 11 → 8 | 10 731 → 7 546 |
| 112,162027 | 14 → 13 | 14 183 → 12 903 |

A tiszta kiválasztás összideje több futásban közepesen ~0,8–1,05 s-ról
~0,54–0,55 s-ra, mélyebben ~1,22–1,32 s-ról ~0,90–1,09 s-ra változott.
Nem kontrollált FPS-benchmark: JIT és párhuzamos tesztfutás is befolyásolja
az időket. Nincs benne Unity emit/upload, a cache-elt kizárás próbában
előkészített maszk. A modell-előkészítés külön fut, nem a cut idejébe rejtve.
A munkaszámok és az azonos TileId-halmaz erősebb bizonyítékok az időknél.

## Ellenőrzés és nyitott munka

- LOD Debug/Release 170/170: 9 új eset, köztük a két logméret reprodukciója,
  korai kizárás kvóta nélkül, változatlan vegyes-parti kiválasztás, teljes
  fedés, visszazoom, törölt kérés, budget és megőrzött korábbi cut.
- Solution build: 0 hiba/0 figyelmeztetés. Unity-forrásfordítás:
  0 hiba/83 korábbi figyelmeztetés. Teljes teszteredmény a historyban.
- Python nincs telepítve; Core/referencia/tesztvektor nem változott.
- Élő Unity-próba még nincs. Nem jelentjük vizuálisan késznek.

**Próba:** új Play, azonos zoomút, közepesen/mélyen 10–15 s megállással.
A kérdés most: hamarabb kapnak-e részletet a várakozó tereprészek, rövidebb-e
a `split-quota` miatti megállás? A 28–32 px-es teljes-quad eltéréstől nem
várunk automatikus megszűnést. A vízfelszín finomítása, a takaratlan terep
helyes hibamértéke és a mély proxy/morph maradék hibái külön nyitottak.

M9 tartalmi becslés 60–70%. E lépés durva, nem mért fejlesztői ráfordítás-
egyenértéke 3–6 óra; hátralévő zoomminőség/validáció 8–20 óra,
km-UI és új Core-részletmodell nélkül.
