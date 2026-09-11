# Hőmérséklet-overlay tervezése (2026-09-11)

## Kérés

A felszíni hőmérséklet-overlay backlog-állapotának ellenőrzése, majd a feladat
implementáció előtti pontos kidolgozása.

## Feltárt állapot

- Az aktuális `codex-handoff` ág backlogjában nem szerepelt a tétel.
- Az `experiment/full-temperature-model` testvérág backlogjában már létezett
  egy rövid változat, implementáció nélkül.
- Az aktuális viewerben a szél- és csapadék-overlay boolean kapcsolókkal,
  egymásra épülő prioritással működik; módváltásuk a teljes
  `WorldConfigChangedSinceBuild()` útvonalat és a mért kb. 24 s-os `Build()`-et
  indítja.
- A színcache csak a szél-overlay állapotát követi. A statikus terep, a
  dinamikus/chunkolt terep, a nyílt víz és a tó külön mesh-/anyagútvonal.
- A jelenlegi ág központi viewer-adaptere a Simple
  `Temperature.TemperatureKelvin` modellt hívja. A testvérág a Full modell
  pozíciófüggetlen tagjait Build-szinten cache-eli, ezért az overlaynek nem
  szabad saját Simple/Full választást vagy HLSL hőképletet bevezetnie.
- A Core külön óceán/szárazföld boolean típust ismer, de külön édesvízi
  felszínhőmérséklet-modellt nem; a tó színezése valódi nyitott döntés.

## Dokumentált terv

A `docs/backlog.md` új sora és részletes szakasza rögzíti:

- a napi átlagos felszíni hőmérséklet pontos jelentését és a permanens jég
  éves átlagmezőjétől való eltérést;
- az egyetlen `SurfaceOverlayMode` állapotot;
- a fix, méréssel kalibrálandó °C-skálát, a 0 °C jelölést és a
  világításfüggetlen diagnosztikai megjelenítés javaslatát;
- a Core-adapterből származó nyers Kelvin-cache és a külön prezentációs
  színcache adatfolyamát;
- az ND-50-re épülő, teljes `Build()` nélküli szín-only frissítést;
- a statikus/dinamikus/chunkolt terep, `WaterSurface`, `DynamicWater` és a
  döntés szerinti `LakeSurface` érintettségét;
- az automata teszteket, az élő Unity acceptance-et és a PerfLog-kritériumot.

Új ND-szám most nem készült: az áganként duplikált két ND-62-t előbb fel kell
oldani. A hőskála konkrét végpontjai sem lettek találomra rögzítve; a tervezett
referencia-LOD/évszak eloszlásméréshez ebben a környezetben nem volt telepített
Python interpreter.

## Ellenőrzés

- `git diff --check`: sikeres.
- Build és teszt nem futott, mert kizárólag dokumentáció változott.
- Core- és Unity-implementáció nem történt.

## Hatókör-pontosítás: pillanatnyi napszak és szélhő-szállítás

A felhasználó ugyanebben a tervezési körben pontosította, hogy nem napi átlagos
színnézetet kér. A mezőnek az aktuálisan napsütötte és éjszakai oldalt is meg
kell különböztetnie, valamint a szeleknek hideg/meleg levegőt kell tudniuk
szállítani. Ezért a korábbi tervet lecseréltük egy két részből álló feladatra:

1. új, Core-oldali, időben fejlődő hőmodell `SurfaceTemperatureK` és
   `NearSurfaceAirTemperatureK` állapottal;
2. ennek teljes bolygós, `Build()` nélküli, gyors diagnosztikai megjelenítése.

A vizsgálat két konkrét integrációs problémát talált. A `SunController` saját
`currentTimeDays` mezője képkockánként halad, a `PlanetGridMesh.climateDayT`
ettől független és teljes rebuildhez kötött, ezért közös Core-oldali
`SimulationTime` szükséges. A meglévő `WindPrecipitation.WindVector` napi
átlaghőmérsékletből és még nem bitdeterminisztikus transzcendens útból készül;
első körben csak determinisztikussá tett, egyirányú advekciós bemenet lehet.
A pillanatnyi hőmezőből visszacsatolt szél külön, későbbi ND.

A kibővített backlogterv rögzíti a lassú klímabázis körüli két gyors
hőanomáliát (felszín és felszínközeli levegő), így a napi átlagos napsugárzás
nem számolódik kétszer. Rögzíti továbbá a fix level-6 (`24 576` cellás)
kétpufferes solvert, a fizikai cubed-sphere rácsmetrikát, a CFL alapján
választandó egész tick-et, a felszín–levegő hőcserét, a
land/ocean/lake/ice hőtehetetlenséget, a spin-up/checkpoint kérdést, valamint
a GPU kizárólag megjelenítési szerepét. A javasolt Viewer-adatút hat szeletes,
CPU-n determinisztikusan fixpontosra kvantált scalar texture/buffer, így a
prezentációs bemenet byte-reprodukálható, és minden termikus ticknél nem kell
több százezer magas-LOD vertex színét CPU-ról újratölteni.

Ez továbbra is csak dokumentációs tervezés: Core-, Viewer-, scene- és
tesztimplementáció nem történt.

## Felhasználói döntéssor lezárása

Ugyanezen a napon a felhasználóval 29 pontban, egyenként végigvettük a
hőmodell, időkezelés, szélkapcsolat, felszíntípusok, megjelenítés, cache,
teljesítmény és validáció nyitott kérdéseit. Minden pont lezárult; a részletes,
számozott választási jegyzék a `docs/backlog.md` „Jóváhagyott döntések —
tételes jegyzék” szakaszában van.

A legfontosabb lezárt irányok: kétállapotú `Ts/Ta` hőanomália-modell; fix
level-6 egészgömbös rács; egész tickes közös `SimulationTime`; determinisztikus
spin-up/checkpoint; négy termikus felszíntípus; egyirányú, konzervatív upwind
széladvekció; külön `WindTick`; háttérben folyamatos solver; hatlapos fixpontos
GPU-megjelenítés; világításfüggetlen fix °C-paletta; ugyanaz a közös
vezérlőpanel, ahol a deep-time és kameramódok találhatók; részletes
kurzorpont-debug; valamint Python + automata + élő Unity validáció.

Öt külön, egymásra hivatkozó ND készítése lett jóváhagyva egyetlen ernyő-ND
helyett. Az ND-k számozása és a `docs/04-decisions.md` módosítása tudatosan
elmaradt: előbb fel kell oldani a kameraág és az
`experiment/full-temperature-model` testvérág két eltérő ND-62 bejegyzését.
Ez nem implementációs blokk, hanem a döntésnapló integritásának előfeltétele.
