# 2026-09-20 — Offline fordítási kapu a viewer-réteghez

## Mi hiányzott

Az `Assets/Scripts/Viewer/` (`PlanetGridMesh` és társai, 20 fájl + `Lod/` +
`Gpu/`) **offline sehogy nem fordult le**. Minden viewer-módosítást csak az
élő Editor `recompile`-jával lehetett ellenőrizni. Ez aznap konkrétan meg is
fogott: a Unity MCP-kapcsolat kiesett, és a `NewSplitsPerRequest` emelése plusz
néhány komment fordítatlanul került be a commitba.

A todo.md ezt a `Directory.Build.props`-szivárgás felől fogalmazta meg
(`Nullable=enable` + `TreatWarningsAsErrors` beszivárog a Unity által GENERÁLT
csproj-ba → 180 „hiba"). A generált csproj viszont IDE-artefaktum, amit a Unity
újragenerál — nem az fordít. A repóban már volt jó minta a valódi megoldásra:
`tests/WorldGen.App.UnityBinding.Compile`. Csak épp a legnagyobb részre,
a viewerre nem létezett.

## Mi készült

`tests/WorldGen.Viewer.Compile/WorldGen.Viewer.Compile.csproj`:

- a forrást **linkként** veszi fel (egyetlen igazságforrás, nincs másolat):
  `Viewer/Lod/**`, `Viewer/Gpu/**`, `Viewer/*.cs`;
- `netstandard2.1` + `LangVersion 9.0` — ugyanaz, amit a Unity használ;
- a helyi Unity Editor `Managed/UnityEngine` DLL-jei + a projekt
  `Library/ScriptAssemblies` csomag-assembly-i (TextMeshPro, HDRP,
  UnityEngine.UI) ellen fordít;
- érthető hibaüzenettel áll meg, ha a Unity vagy a `ScriptAssemblies`
  hiányzik;
- **nincs a `WorldGen.sln`-ben** — a CI-gépeken nincs Unity.

Parancs (bekerült a CLAUDE.md Unity-szakaszába):

```
dotnet build tests/WorldGen.Viewer.Compile
```

## Három beállítás, ami szándékos

**`Nullable=disable` + `TreatWarningsAsErrors=false`.** A repo gyökerének
`Directory.Build.props`-a mindkettőt élesíti. Ha itt is érvényesülne, a
viewer-kód `?` annotációi (CS8632) SZÁZ „hibát" adnának, miközben a Unity
alatt a kód hibátlanul fordul. A kapu feladata az, hogy azt mérje: lefordul-e
ÚGY, ahogy a Unity fordítja — nem az, hogy szigorúbb legyen nála.

**`NoWarn CS0649`.** Mind a 28 találat `[SerializeField]` mező (tételesen
ellenőrizve: `SunController.planetTransform`, `PlanetGridMesh.borderMaterial`,
stb.), amiket a Unity szerializálója tölt fel a jelenetből — a Unity maga sem
jelzi őket. Elnémítva, hogy a valódi figyelmeztetések látszódjanak.

## Igazolás

- Tiszta állapot: **0 hiba**, 115 figyelmeztetés (CS8632 + CS0618) — ugyanaz
  az osztály és nagyságrend, amit a Unity Console ír.
- **A kapu tényleg kapu:** beírtam egy szándékos hibát
  (`int y = "nem szam";`) a `BodyFrameConversion.cs`-be, a build elbukott
  (`error CS0029`), majd visszaállítottam. Enélkül nem lett volna bizonyítva,
  hogy nem csak „mindig zöld".
- A mai, Editor nélkül bekerült változások utólag mindkét úton igazolva:
  offline kapu 0 hiba, Unity `recompile` → `up_to_date`, `editor_status` →
  `ready`, hiba nélkül.

## Tanulság

**Ha egy réteget csak élő eszközzel tudsz ellenőrizni, az az eszköz egyetlen
hibapont.** Ma ez ki is esett. A kapu felépítése ~30 percbe telt, és minden
további viewer-munkát olcsóbbá tesz. Ezt hamarabb kellett volna megcsinálni,
mint bármelyik teljesítmény-javítást.
