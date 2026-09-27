# A14 / ND-21 — HDRP volumetrikus felhő űrből: prototípus és elutasítás

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Kiváltó:** „todo2.md-ből olvasd fel A14et, és implementáld”

## Mit kért a feladat

Az A14 nem implementációs, hanem **döntés-előkészítő** tétel: az ND-21 (az
ND-01 lezárásakor nyitott négy feltétel egyike) azt kérte, hogy prototípussal
ellenőrizzük, működik-e a HDRP volumetrikus felhőrendszere bolygó-léptékben,
űrből nézve. A `todo2.md` állítása („mindhárom HDRP-asset
`supportVolumetricClouds: 0`, a prototípus nem történt meg”) ellenőrizve és
igaz volt — valójában **négy** asset van, a `HDRPDefaultResources`-beli is
kikapcsolt volt. Az aktív minőségi szint: `HDRP High Fidelity`.

## A prototípus menete (élő Editor)

Unity 6000.0.77f1, HDRP 17.0.1, `PlanetView` jelenet, Play mód.

1. `supportVolumetricClouds` IDEIGLENESEN `true` a `HDRP High Fidelity`
   assetben (SerializedObject → `m_RenderPipelineSettings.…`).
2. Eldobható, `HideFlags.DontSave` `__A14_CloudProbe` GameObject: globális
   `Volume` (priority 1000) memóriabeli `VolumeProfile`-lal —
   `VisualEnvironment` (`planetRadius = 100`, `centerMode = Manual`,
   `planetCenter = (0,0,0)`, `renderingSpace = World`) + `VolumetricClouds`.
3. A kamerát a `PlanetOrbitCamera.FlyToDirection`-nel a Nap felőli oldalra és
   több magasságra vittem (felszín feletti 500 / 200 / 50 egység), plusz az
   északi és déli pólus fölé.
4. Változatok: `Simple`/Cloudy preset, `Advanced` (felhőtérkép),
   `PhysicallyBasedSky` hozzáadása, a bolygó-renderer ideiglenes kikapcsolása
   (tiszta héj-kép).
5. **Minden visszaállítva:** probe törölve, renderer vissza, asset-flag `0`,
   az importált `Assets/Temp` törölve. `git status` a menet után **tiszta**,
   a Unity konzolon 0 futásidejű hiba (az egyetlen error a saját, projekt-
   gyökéren kívüli mentési útvonalam volt a capture-nél).

Képek (`artifacts/`, gitignore alatt):

| fájl | mit mutat |
|---|---|
| `a14/01-planetview-nocloud.png` | alapvonal: a bolygó űrből, a MOSTANI mesh-alapú felhő-MVP-vel |
| `a14/02-hdrp-simple-clamped.png` | HDRP felhő bekapcsolva, bolygónézet — a felszínen SEMMI, sötét foltok a csillagtérben |
| `a14/03-hdrp-simple-far.png` | ugyanaz nagyobb távolságból |
| `a14/04-hdrp-with-pbsky.png` | `PhysicallyBasedSky` hozzáadva — a mi M3-as megvilágításunk átrendeződik |
| `a14/06-shell-only.png` | bolygó elrejtve, `Advanced` mód: üres kép |
| `a14/07-simple-inside-shell.png` | a héjon BELÜLRŐL, `Simple` preset: fekete pettyek az egész felszínen |
| `a14/08/09-advanced-inside-north/south.png` | `Advanced` mód mindkét féltekéről: nulla felhő |

## Mit mért a prototípus

**1. A rétegvastagság alsó korlátja a bolygónk sugara.** Az `altitudeRange`
`MinFloatParameter(2000, 100)`, a setter keményen vág. MÉRVE: `kért = 0,05 →
tényleges = 100`. Nálunk `radius = 100` egység = 7420 km, tehát 1 egység =
74,2 km: a legvékonyabb lehetséges héj `r = 100,02 … 200,02`, azaz **7420 km
vastag felhő** — bolygónyi burok, nem felszíni réteg. A valós lépték nem kiút,
azt az ND-19/A12 (float32) zárja.

**2. A sűrűség-normalizálás a Föld sugarát drótozza be.**
`ComputeNormalizationFactor`-ban `const float k_EarthRadius = 6378100.0f`, és
ez osztja a felhőtérkép UV-jét (`GetCloudCoverageData`). A mi 200 egységnyi
bolygónk egyetlen texelre képződik. MÉRVE: `Advanced` módban **semmi nem
renderelődött** — kívülről, belülről, északról, délről sem.

**3. A felhőtérképes út kizárja a fél bolygót — a shader maga mondja ki.**
`VolumetricCloudsUtilities.hlsl`, `EvaluateCloudProperties`:

```hlsl
#ifndef CLOUDS_SIMPLE_PRESET
    // When using a cloud map, we cannot support the full planet due to UV issues
    if (positionPS.y < 0.0f)
        return;
#endif
```

Ez a döntő: a felhőtérkép az EGYETLEN út, amin a mi csapadék-mezőnk hajthatná
a felhőt — és az a út sík, XZ-vetületű UV-t használ, a déli féltekét pedig
elvileg is eldobja.

**4. Ami renderel, az sérti az I3-at.** Egyedül a `Simple` preset ad képet, ott
viszont a lefedettség a shaderben `float4(0.9f, 0.0f, 0.25f, 1.0f)` konstans +
procedurális zaj: nulla köze a világmodellhez. MÉRVE (`07`): a héjon belülről
fekete pettyek, mert a méter-alapú `shapeScale`/`erosionScale` a mi
skálánkon ~1 egység (≈74 km) szemcsét ad.

**Járulékos:** a felhő behúzza a HDRP bolygó-/ég-modelljét, ami a mi M3-as
megvilágításunkkal ütközik (`04`: a terminátor és a nappali oldal
átrendeződött, pedig csak az ég került be).

**GPU-költség: nem mérhető a rendelkezésre álló műszerrel.** A
`get_performance_stats` képkocka-időzítése ebben a jelenetben 3,4–6,1 ms között
szórt (terep-LOD háttérmunka), egy érvénytelen 0,03 ms-os mintával. Egyetlen
tiszta bolygónézeti pár adódott (2,71 → 3,16 ms) — **tájékoztató, nem
bizonyíték**. A döntés nem ezen múlik.

## Döntés

**ND-21 lezárva: ELUTASÍTVA.** A `docs/03-unity-hdrp-evaluation.md`
fallback-ága lép életbe: a Planet nézet felhői saját úton maradnak (ma a
csapadék-mezőből épülő mesh-MVP, `WorldGen/CloudUnlit`; a volumetrikus
folytatás az A16/M13 saját, gömbi raymarch shadere).

Az ND-01 fidelity-érvének az a tétele, hogy „a HDRP kész felhő-rendszere
renderelési munkát spórol”, **a felhőre nézve nem teljesül**. Az atmoszférára
(`PhysicallyBasedSky`) és a vízre ez az ND nem dönt — azok külön vizsgálandók,
és a 4-es megfigyelés (a HDRP ég-modellje felülírja a mi megvilágításunkat)
arra is figyelmeztet.

## Amit a menet tanulságként hagy

A kérdést **nem kellett volna** kizárólag képről eldönteni: a négy blokkoló ok
közül három (méret-clamp, Föld-sugár konstans, féltekére vágó `#ifndef`) a
package forrásából egyértelműen kiolvasható. A kép viszont megmutatta azt,
amit a forrás nem: hogy a `Simple` mód mit ad ténylegesen (fekete pettyek), és
hogy az ég-modell mellékhatásként átrajzolja a saját megvilágításunkat. A
kettő együtt zárja le a döntést — külön-külön egyik sem lett volna elég.
