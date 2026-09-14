# Negyedik hierarchiaszint (Terület) + teljes navigációs menü (2026-09-13)

## Kérés

A [Planet Navigator mockup](../docs/backlog.md) jóváhagyása és a deep-time/
kamera-panel kiegészítés után a felhasználó kérte: "előbb vedd fel a
negyedik szintet, vezesd be a kódba. ha ez megvan, akkor alakíts át mindent
úgy ahogy kértem az előbb, a képernyőképnek megfelelően" - azaz a teljes
bolygó/kontinens/régió/terület navigációs menü éles megvalósítását,
breadcrumb-bal, vissza-gombbal, a jóváhagyott panel-elrendezéssel (bal
oldalon Navigáció/Deep time/Rétegek, jobb oldalon Kamera állása/
Kiválasztott elem).

## 1. Negyedik szint - Core ("Terület"/Area)

Részletes terv: `docs/01-architecture.md` §12. Röviden:

- `FeatureSegmentation.PartitionRegionIntoAreas` - tiszta gráf-algoritmus
  (I1/I2-kompatibilis, nincs random, nincs transzcendens): előbb
  összefüggő komponensekre bont (a vízgyűjtő-régió nem garantáltan
  összefüggő), majd minden komponenst többforrású BFS-sel ("legtávolabbi
  pont" magválasztás + legközelebbi-mag hozzárendelés, kanonikus
  `TileId.Value` döntetlen-eldöntéssel) oszt fel kb. 40 tile-os darabokra.
- Python referencia (`tools/reference/features_ref.py`:
  `partition_region_into_areas` + segédfüggvények) - invariáns-tesztekkel
  (teljes/átfedésmentes lefedés, kontiguitás, determinizmus, élesetek)
  futtatva a valódi TestEarth001 világon: 421 régióból az 5 legnagyobbra
  2-3 területet ad (pl. 108 tile-os régió → 3×33/22/53 tile).
- C#-port a Python-vektorok (`features_vectors.json` `areasByRegion`)
  ellen BITPONTOSAN egyezik. `tests/WorldGen.Core.Tests/Features/
  AreaPartitioningTests.cs` - vektor-egyezés + tisztaság + paraméter-
  érzékenység + szétkapcsolt-komponens + élesetek + MINDEN (≥5 tile-os,
  421 db) régióra teljes lefedés/kontiguitás invariáns. **9 új teszt,
  393/393 Core-teszt zöld.**
- Névadás: ugyanaz a `NameGeneration.GenerateName`, nincs új
  `RandomProperty` → NEM seed-törő. FeatureId-tartomány:
  `20000 + regionIndex*1000 + areaIndexWithinRegion`.

## 2. Viewer - panel-adat és lusta kiértékelés

- `AreaPanelData` (`WorldGenPanelData.cs`) - `RegionPanelData` mintájára.
- `PlanetGridMesh.ComputeAreaPanelData(globalRegionIndex)` - CSAK a
  ténylegesen kiválasztott régióhoz számol területeket, nem minden
  régióhoz előre (a `ComputePanelData()` költsége nem nő).
- `ComputeRegionPanelDataForContinent(continentIndex, out globalIndices)`
  - egy kontinens ÖSSZES régiója (nem csak a világ-panel top 10-e), a
  navigációs lista teljességéhez. Kontinens↔régió hozzárendelés
  HEURISZTIKA (dokumentált egyszerűsítés, mint a többi FeatureMetrics-
  közelítés): a régió tile-jainak első eleme alapján dönt.
- `PlanetGridMesh.Built` eseményre `RefreshNavigationCache()` - minden
  sikeres Build() után újraszámolja a navigációs gyorsítótárat és
  visszaáll bolygó-szintre (egy új világon a régi index érvénytelen lenne).

## 3. Teljes navigációs OnGUI panel

- `NavigationLevel` enum (Planet/Continent/Region/Area) + állapotgép
  (`NavigateToPlanet/Continent/Region/Area`, `NavigateBack`) - minden
  navigáció a MÁR MEGLÉVŐ `PlanetOrbitCamera.FlyToDirection`-t hívja.
- Új `PlanetOrbitCamera.SuggestedAltitude(ViewLevel)` - a
  `continentViewAltitude`/`regionViewAltitude` küszöbökből származtatott
  célmagasság, hogy a "Vissza"/lista-kattintás pontosan abba a
  nézetszintbe repüljön, aminek a `CurrentViewLevel` IS minősíti.
- Breadcrumb: minden szegmens valódi `GUI.Button` (nem a Canvas/TMP
  padded-link kompromisszum - OnGUI-ban nincs EventSystem-függés).
- Gördíthető lista (`GUI.BeginScrollView`) a jelenlegi szint gyerekeiről.
- Jobb oldali "Kiválasztott: <szint>" doboz - a Core-forrású adatokat
  mutatja (World/Continent/Region/Area, a szintnek megfelelően).

## 4. Panel-elrendezés (a jóváhagyott mockup szerint)

- **Bal oldal, fentről le**: Navigáció → Deep time → Rétegek (a korábbi
  Deep time dobozból kiemelt overlay-/kamera-mód kapcsolók KÜLÖN dobozban
  - "fenntartott hely" a jövőbeli hőmérséklet-overlay blokknak, ld.
  a hőmodell-terv 11./22. jóváhagyott döntése).
- **Jobb oldal, fentről le**: Kamera állása → Kiválasztott elem infója.
- A Deep time doboz szélessége 675→460px (a bal oldali oszlopszélesség
  szerint arányosítva; a 9 léptetőgomb ~51px/gomb - élő teszttel
  ellenőrizendő, nem csonkolódik-e a felirat).

**Tudatosan NEM módosítva ebben a lépésben**: a régi Canvas/TMP-alapú
`WorldGenPanelUI` (World/Continent/Region szöveges listák, a képernyő
bal-közép részén) VÁLTOZATLAN maradt - most redundáns az új navigációs
dobozzal, de a scene GameObject-jeinek inaktiválása élő Unity-ellenőrzés
nélkül kockázatos lett volna (nem látom, mi hivatkozik rájuk). Javaslat a
következő élő körre: ha a navigációs doboz működik, a régi panelek
`m_IsActive: 0`-ra állíthatók a scene-ben.

## Ellenőrzés

- Python referencia: minden invariáns-assert PASS (lefedés, kontiguitás,
  determinizmus, élesetek) a valódi TestEarth001 világon.
- `dotnet test tests/WorldGen.Core.Tests`: **393/393 PASS** (9 új).
- `dotnet test tests/WorldGen.Viewer.LodChunking.Tests`: **373/373 PASS**
  (nem érintett, de a megosztott Lod-fájlok épsége is igazolva).
- `dotnet build unity/WorldGenViewer/Assembly-CSharp.csproj -p:LangVersion=9`:
  **a `-p:LangVersion=9` felülbírálás ÚJ, ebben a körben felfedezett
  diagnosztikai lépés** - a korábbi `-p:Nullable=disable` egyedül NEM adott
  tiszta jelet ebben a dotnet SDK-verzióban (CS8632-vel elakadt egy, a
  MÁSIK (hőmodell) munkaszál új, nullable-annotációs fájljainál is). A
  `-p:LangVersion=9` (a src/ tényleges C#9 célja) önmagában 0 hibával
  fordítja a WorldGen.Core-t. Az Assembly-CSharp.csproj-on mérve: baseline
  (az én változtatásaim NÉLKÜL) 154 hiba, utána 180 - **+26, mind a MÁR
  ismert 8 hibaosztályba tartozik (CS0618 elavult `FindObjectOfType`,
  CS8600-8604/8618/8625 nullable) - nincs új hibaosztály.** A CS0618-as
  többlet két új `FindObjectOfType<PlanetOrbitCamera>()` hívásból jön,
  UGYANAZZAL az API-val, amit a kódbázis már máshol is használ.
- **Élő Unity Game view ellenőrzés MÉG HÁTRA**: a négy szint közti
  navigáció ténylegesen kattintható-e, a breadcrumb/vissza-gomb helyesen
  repül-e, a lista görgethető-e, és a bal/jobb oszlop nem lóg-e ki/fedi-e
  egymást keskeny Game View-nál. A `docs/06-user-verification-checklist.md`
  szerint ez a projekt szabálya - kódból nem nyilvánítható késznek.
