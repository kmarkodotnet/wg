# Zoom-LOD — második javítási csomag: fedés és közös geometria

Dátum: 2026-09-11. Döntés: ND-70. Az ND-69 helyi módosításaira épül,
ugyanazon `codex-handoff` ágon, `195875e` után. Commit/push nem történt.

## Elkészült

- **A finom felszín kiváltja, nem csak lefedi az alap-terraint.** Minden
  érintett base-tile teljes renderpartíciót kap. A kiválasztásból kimaradt
  ágak durvább pótló levelek, ugyanabból a világmodellből. Csak a dinamikus
  mesh-ek sikeres feltöltése után kapcsoljuk ki a kiváltott alap-tile két
  háromszögét. Visszazoomkor az eredeti indexek visszaállnak.
- **Nincs teljes alapmesh-újraépítés zoomkor.** A base vertex-, normal-,
  color- és submesh-buffer megmarad. A `TerrainIndexMask` csak a változott
  tile-ok indexintervallumait írja, kis hézagok összevonásával. A kikapcsolt
  quad a saját első csúcsára degenerálódik; nincs shader-discard vagy nagy
  radiális eltolás. A tárolt eredeti/aktuális indexek és a dense offsetek
  L8-on nagyságrendileg 20 MiB állandó többletmemóriát jelentenek.
- **A közös geometriai pontot közösen döntjük el.** Gazdája a legdurvább
  érintkező renderlevél, azonos szintnél a legkisebb TileId. Finom–durva
  élen a finom csúcs a durva él két feloldott végpontjára illeszkedik.
  A statikus szomszéd is részt vesz ebben. A rekurzió szigorúan csökkenő
  LOD-szinteken halad; a feloldott sarkok kérésenként cache-eltek.
- **A coarse morph-felület a valódi két háromszög.** A bilineáris nyereg
  helyett az `AddQuad` 00–11 átlójú háromszögpárját interpoláljuk. A terrain
  korábbi radiális bias-a ezen a fedéscserés CPU-úton nincs alkalmazva.
- **A változatlan tile-halmaz nem jelenti a geometria változatlanságát.**
  A chunkok emit-sorrendje stabil; az új csúcsokat egzaktul összehasonlítjuk
  az utolsó feltöltéssel. Csak pozícióváltozásnál pozíció és bounds frissül,
  nem index/szín/normal/material. A teljes feltöltés submeshenként nem
  számolja újra a már a workeren elkészült bounds-ot.
- **Az async és szinkron CPU-út közös fedési kódot használ.** A nyilvános
  Build-kérés sem ürítheti a futó worker cache-eit: a single-flight után
  teljesül. A base-level vagy bolygósugár módosítása teljes alapmesh-buildet
  kér. Chunkolt/nem chunkolt módváltáskor a korábbi rendercélok kikapcsolnak.
  Sikertelen mesh-feltöltéskor az alkalmazási út megkísérli visszaállítani a
  statikus fedést, és kikapcsolja a félkész dinamikus rendercélokat.

Az indexbuffer részleges frissítésének és saját adatérvényesség-ellenőrzésének
Unity-s szerződését a [hivatalos API-leírás](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetIndexBufferData.html)
alapján követjük. A submesh indexStart/indexCount/baseVertex értékeit a
feltöltött mesh-ből ellenőrizzük; a primitívek száma és a bounds nem változik.

## Ellenőrzési eredmények

- Solution Debug build: **0 warning, 0 error**.
- Teljes solution-teszt: **381 Core + 57 LOD + 7 CLI = 445/445 sikeres**.
- A LOD-tesztek külön Release módban is **57/57** sikeresek.
- Az első csomaghoz képest 14 új parancssori teszt: teljes partíció és
  hiányzó ágak, nincs átfedés, visszaállítható indexmaszk, változatlan
  topológia melletti pozícióváltozás, valódi háromszög-interpoláció,
  finom/durva és statikus él, lapélek/sarkok, L20 és fordított kiértékelési
  sorrend, valamint a valódi 4000/25000-es cut minden sarkának feloldása.
- A lapélteszt fejlesztés közben ténylegesen talált L20-as hibát: a puszta
  átlós minták a face-váltás nyírása miatt cellahatárra eshettek. Két eltérő
  meredekségű mintasor javította; a lapon belül a keresés egész rácsindexeket
  használ, tan/atan nélkül. A hibát a teszt toleranciájának lazítása nélkül
  javítottuk.
- Unity viewer offline C# build, valódi Unity DLL-ekkel: **0 error, 83 warning**
  (a task nullable-életciklusának pontosítása két régi figyelmeztetést is
  megszüntetett). Unity EditMode tesztprojekt offline build:
  **0 error, 4 warning**.
- Két új, valódi `Mesh` API-t használó NUnit-teszt is elkészült:
  `TerrainIndexMaskMeshTests`. **Csak lefordítottuk, még nem futott az
  Editorban.** Nem része a 445 sikeres .NET-tesztnek.
- `git diff --check`: tiszta. Core, shader és scene nem módosult. Python
  KAT/vektor-regenerálás nem futott: az előző körben ellenőrzött környezetben
  nincs telepített Python; szimulációs numerika nem változott.

Az önálló diagnosztika új `COVERAGE` sorai azonos, ideális gömbös nézetnél
(R=100, kamera=100,6, H=1080, eredeti szögküszöb, óceánszűrés nélkül):

| Keret | Kiválasztott levél | Teljes fedés levelei | Pótló levél | Kiváltott base-tile |
|---|---:|---:|---:|---:|
| 4 000 | 3 998 | 3 998 | 0 | 50 |
| 25 000 | 24 998 | 24 998 | 0 | 50 |
| 50 000 | 50 000 | 50 000 | 0 | 50 |
| 200 000 | 164 627 | 164 627 | 0 | 50 |

Ez a négy eset **nem** bizonyít általános nulla többletmunkát: más nézetben,
különösen a horizonton, a pótlás növelheti a renderlevelek számát. Az élő
számlálót a PerfLog `fallbackLeaves` mezője mutatja.

## Amit még élőben kell ellenőrizni

Új kép- vagy FPS-bizonyíték nincs, ezért a csomag **implementált és
parancssorosan ellenőrzött, nem vizuálisan elfogadott**.

1. Állítsd le a Play módot, várd meg az Editor újrafordítását. Az EditMode
   Test Runnerben futtasd a `TerrainIndexMaskMeshTests` két tesztjét.
2. Indítsd újra a Play módot, változatlan seeddel/budgettel/chunk-szinttel.
   A scene meglévő `useGpuGeometry=false` beállítása maradjon.
3. Ugyanazon szárazföldi helyen lassú zoom, megállás, majd visszazoom.
   Figyeld a korábbi lapos takarófoltokat és az újonnan látható völgyeket;
   ne maradjon lyuk visszazoom vagy elfordulás után.
4. Partvonal, kockalapél és a kamera környékének LOD-határai is kerüljenek a
   tesztbe. A megmaradó szín-/világítási varratot is külön jelezd.
5. Küldj képet és új PerfLogot. Jelölő: `[async apply ND-70]`; új mezők:
   `positionOnlyChunks`, `fallbackLeaves`, `replacedBase`, `maskIndices`.
   A `requestAge`, `emit` és `mesh-feltoltes` értékeket együtt kell nézni.

## Nyitott korlátok és következő lépés

A worker továbbra is minden renderlevél geometriáját előállítja. A sarok-
feloldás és a pozíció-összehasonlítás többlet CPU/memória; a pozíció-only
feltöltés kisebb adatmennyiség, de **ebből még nem következik mért FPS-nyereség**.
Frame-időkeretes feltöltés és teljesen inkrementális emisszió még kell.

A normál/szín attributumok továbbra is a meglévő mezőmintákból készülnek;
a geometriai közösél-feloldás nem új árnyalási/attribútum-interpolációs rendszer.
A morph kérésenként, nem frame-enként frissül, és topológiaváltásra nem adunk
új, általános pop-mentességi garanciát. A külön statikus víz- és debug-border
rétegek megmaradnak. A GPU-geometria kísérleti ágát nem állítottuk át erre az
útra. Domborzati displacementet követő kiválasztási korlát, kamera–felszín
távolság, apró szigetek biztos finomítása és szigorú végső budget még nyitott.

Következő lépés: az élő visszajelzés alapján a megmaradó geometriai/vizuális
hibák ellenőrzése, majd célzott worker-/upload-időkeret és inkrementális
emisszió. Durva, tartalmilag súlyozott M9-becslés **60–70%**. E csomag
hagyományos mérnöki ráfordításának becslése **6–12 óra**, a hátralévő zoommunka
és validáció **16–32 óra**; ezek nem mért munkaidők.
