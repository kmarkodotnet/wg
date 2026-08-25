# unity/WorldGenViewer

A viewer/render-projekt (ND-01: Unity 6 + HDRP). A szimulációs mag
(`src/WorldGen.Core`) **nincs ide másolva** — a `Packages/manifest.json`
forrás szerint hivatkozza (`"com.worldgen.core": "file:../../../src/WorldGen.Core"`),
tehát egyetlen forrás létezik, amit a `dotnet build`/`dotnet test` ÉS a Unity
compiler is ugyanonnan fordít. Az asmdef `noEngineReferences: true` flagje
kikényszeríti, hogy ez az assembly Unity alól sem hivatkozhat
`UnityEngine`/`UnityEditor`-ra (ND-22).

## Amit előkészítettem

- `ProjectSettings/` — a Unity 6000.0.77f1-hez bundle-elt hivatalos HDRP
  projekt-sablon (`com.unity.template.3d-high-end`) beállításai, a
  demó-jelenetre és a felhő-fiókra mutató részek nélkül (`EditorBuildSettings`,
  `UnityConnectSettings` kihagyva — ezeket Unity magától újragenerálja).
- `Assets/Settings/` — kész HDRP Render Pipeline Asset (3 minőségi szint:
  Performant/Balanced/High Fidelity), Volume Profile, Sky & Fog beállítás,
  ugyanabból a sablonból. Ez azt jelenti, hogy a HDRP **már be van
  drótozva** — nem kell a Wizardot lépésről lépésre végigkattintani.
- `Packages/manifest.json` — HDRP 17.0.1, `com.unity.burst` 1.8.29,
  `com.unity.mathematics` 1.3.2 (ND-20 előkészítéshez), a `com.worldgen.core`
  helyi package.

## Amit neked kell csinálnod (itt kezdődik a te részed)

1. **Nyisd meg Unity Hub-ból** ezt a mappát (`unity/WorldGenViewer`) mint
   projektet. Az első megnyitás percekig tarthat — Unity ilyenkor felold
   minden csomagot és `.meta` fájlokat generál a `src/WorldGen.Core` alá is
   (ez a helyi package tartalma). **Ezeket a `.meta` fájlokat commitold be**
   — stabil GUID-eket hordoznak, nem generált zaj.
2. **Ellenőrizd, hogy a `com.worldgen.core` package betöltődött-e** (Package
   Manager → In Project). Ha hibát dob, legvalószínűbb ok: a relatív útvonal
   (`file:../../../src/WorldGen.Core`) nem stimmel a te lemez-elrendezésedhez
   — ilyenkor szólj, igazítom.
3. **Hozz létre egy `PlanetView` scene-t** `Assets/Scenes/` alatt — ez lesz
   az M2 "nyers, forgatható gömb-render" otthona.
4. **Vizuálisan ítéld meg a HDRP-t**: fut-e az ég/atmoszféra a
   `SkyandFogSettingsProfile`-lal, nem hiányzik-e semmi a Graphics
   Settingsből. Ez pontosan az a lépés, amit én nem látok — a KICKOFF.md is
   ezt mondja ki.

## Hátralevő nyitott döntések (ND-01 miatt aktívak)

`docs/04-decisions.md`: **ND-19** (floating origin, M2-ben eldöntendő),
**ND-20** (Burst `FloatMode.Strict` CI-kikényszerítés), **ND-21** (HDRP
felhő űrből — prototípussal ellenőrizendő). Egyik sem blokkolja a projekt
megnyitását, de mielőtt szimulációs kód kerül Burst alá, ND-20-at le kell
zárni.
