# Tektonikus lemez overlay (2026-09-13)

## Kérés

"Legyen egy tektonikus tile overlay is ami deep time során már mutatja a
tektonikus lemezek kalkulált mozgását. Minden lemeznek saját színe legyen,
és legyen nevük. Részletes domborzat nem kell egyelőre, elég ha a
lemezeket szépen kirajzolod."

## Koordináció

Több párhuzamos munkamenet dolgozott a repóban (App-shell architektúra,
hőmérséklet-overlay, scale-bar diagnosztika). Egyeztetés után kiderült:
egyik sem érinti a `DrawLayersPanel`-t vagy a `ContinuousCornerColor`-t -
ezeket szabadon lehetett módosítani. A hőmérséklet-overlay munkaszál (ND-104)
tanácsa alapján a tektonikus overlay is a MÁR MEGLÉVŐ, kölcsönösen kizáró
overlay-mintát követi (szél/csapadék), nem önálló, párhuzamos rendszerként.

## Megvalósítás

**Core** (nincs új szimuláció - minden szükséges adat MÁR létezett):
- `PlateGeneration.AssignPlate` (legközelebbi lemez-mag) + `PlateMotion.MovedSeeds`
  (deep-time-mozgatott magpontok, amit a `Build()` már kiszámol `_adaptiveSeeds`-be) -
  ezekből a lemez-hovatartozás bármely pontra lekérdezhető, plusz munka nélkül.
- Új `PlatePresentation.cs`: lemezenkénti SZÍN (arany-arány léptékű
  hue-elosztás, `RandomDomain.Decorative`/`RandomProperty.PlateColorHue=42` -
  tisztán renderelési tulajdonság, nem a világmodell fizikai állapota) és
  NÉV (a MEGLÉVŐ `NameGeneration.GenerateName` újrahasználva, `OceanicCrust`/
  `ContinentalCrust` "álbiome" kulcsokkal a `Craton`/`Trench` jellegű
  utótagokért - 2 új bejegyzés a `BiomeSuffixes` táblában, nem seed-törő).
  FeatureId-tartomány: `800000+plateId`, disjunkt a kontinens/régió/terület
  tartományoktól.
- Python referencia (`plate_presentation_ref.py`) + C# port bitpontos
  egyezéssel (`PlatePresentationTests.cs`, 9 teszt).

**Viewer** (`PlanetGridMesh.TectonicOverlay.cs`, új partial fájl + minimális
hook a fő fájlban):
- `tectonicPlateOverlay` bool - ugyanaz a minta, mint `windSpeedOverlay`/
  `precipitationOverlay`: `ContinuousCornerColor`/`ContinuousWaterCornerColor`
  elején egy `if`, ami a normál biome-színezés helyett
  `TectonicPlateColorAt`-ot hív. Kölcsönösen kizárja a szél-/csapadék-/
  hőmérséklet-overlay-t MINDKÉT irányban (a korábbi wind/precip pár csak
  egyirányú kizárást csinált - ezt a hézagot is bezártam, hogy a három/négy
  overlay között ne maradhasson zavaró, egyszerre-bekapcsolt állapot).
- `DrawLayersPanel`-ben új checkbox ("Tektonikus lemezek").
- `WorldConfigChangedSinceBuild()`/`InvalidateColorCacheIfModeChanged`
  kiegészítve `tectonicPlateOverlay`-jel - enélkül a kapcsoló bekapcsolása
  nem indított volna újraszínezést (ugyanaz a mechanizmus, mint a szél-/
  csapadék-overlay-nél, a meglévő, dokumentált "teljes Build() szükséges a
  fő terep-szín-váltáshoz" korláttal együtt).
- Jelmagyarázat-doboz (`DrawTectonicPlateLegend`, jobb oldali oszlop,
  Kiválasztott elem alatt) - csak overlay aktív állapotban jelenik meg,
  gördíthető lista, színminta (`GUI.color` tint + `Texture2D.whiteTexture`)
  + név + óceáni/kontinentális jelzés minden lemezhez.

**Nem módosítva (tudatosan, kis hézag)**: ha a felhasználó a HŐMÉRSÉKLET-
overlay-t kapcsolja be közvetlenül (nem az enyémet), az nem kapcsolja ki a
tektonikus overlay-t (a fordított irány - tektonikus bekapcsolása kikapcsolja
a hőt - MŰKÖDIK). A hőmérséklet-overlay saját fájlját (`PlanetGridMesh.
ThermalOverlay.cs`) direkt nem módosítottam, mert nem az én munkám és nem
akartam belenyúlni egy másik, félig ismeretlen rendszerbe kockázat nélkül -
ha éles gondot okoz, könnyen pótolható egy sorral `DrawThermalOverlayRows`-ban.

## Ellenőrzés

- Python referencia: determinizmus + különböző plateId különböző
  szín/nevet ad + minden RGB [0,1]-ben - mind PASS.
- `dotnet test tests/WorldGen.Core.Tests`: **441/441 PASS** (9 új).
- Unity build: a helyi, korábban elavult `Assembly-CSharp.csproj`/
  `WorldGen.Core.csproj` (gitignore-olt, Unity-generált fájlok) időközben
  egy ÉLŐ Unity Editor-munkamenet frissítette - ez adta az EDDIGI
  legmegbízhatóbb offline szignált: a friss projekt ellen fordítva
  **0 hiba az én fájljaimban** (a maradék 2 hiba egy másik munkaszál
  App/UnityBinding fájljában van, nem az enyém).
- **Élő Unity Play-teszt még hátra**: a checkbox tényleges bekapcsolása,
  a lemez-színek/nevek vizuális megjelenése, a deep-time csúszka
  mozgatásakor a lemezek láthatóan mozgó határai.
