# A8 — „nincsenek folyók": a kész hálózat sosem jutott ki a képre (2026-10-03)

Felhasználói visszajelzés az ND-186 (A8) kör után: *„a
06-user-verification-checklist.md-ben ellenőriztem volna a folyókat, de nem
látszik semmi, egyáltalán nincsenek folyók, semmit nem tudok ellenőrizni."*

Ez elutasítás volt, és helyes. Az ok NEM a folyómodellben van — a hálózat
mindkét esetben elkészült.

## Amit az ÉLŐ Editorban mértem (nem feltételezés)

A Play-menet a visszajelzés pillanatában még futott, így a jelenet állapotát
reflexióval közvetlenül kiolvastam:

| Mérés | Érték |
|---|---|
| `_pendingRiverNetwork` (háttérszál kimenete) | **96 kész ág, 114 352 pont** |
| `_adaptiveRefinedRiverPaths` (ami a képre jut) | **6 ág** (áttekintés) |
| `_riverRefinementTask` | `RanToCompletion`, nem faulted |
| `Time.time` (játékidő) | **50,9 s** |
| folyó-stopper (valós idő) | **306,8 s** |
| `Application.runInBackground` | **False** |
| `Rivers` mesh | 1 chunk, 11 124 vertex |
| a 6 kirajzolt ág iránya vs kamerairány (dot) | **−0,50 … −0,74** (mind a túloldalon) |
| a 96 ágból a látható féltekén | 47 |

A két szám közti szakadék (50,9 s játékidő vs 306,8 s valós idő) a diagnózis:
a fő szál a Play első 51 másodpercén ragadt, mert a Unity ablak elvesztette a
fókuszt, és `runInBackground` ki volt kapcsolva. A háttérszál (dedikált
LongRunning Task) ettől függetlenül végig dolgozott, de a
`TryApplyCompletedRiverRefinement()` a fő szálon van: **frame nélkül nincs
átvétel**.

**Közvetlen bizonyíték.** Futás közben beállítottam
`Application.runInBackground = true`-t, és 9 másodperc alatt:
`refined = 6 → 96`, `preview = True → False`, `fineCompleted = 0 → 96`, majd
felépült a teljes, 919 946 vertexes (57 chunk) finom folyómesh. Semmi mást nem
változtattam. A közeli nézet natív Unity-képén a vonalak, elágazások és
összefolyások tisztán látszottak. (A képet akkor csak a beszélgetésbe
kaptam vissza, fájlba nem íródott ki — a `capture_game_view` az
`outputPath`-t nem menti lemezre. A későbbi köröknél ezért a kamerát
RenderTexture-be rendereltem és magam írtam ki a PNG-t; az így mentett
képek a `pics/a8-final-*.png` fájlok.)

## A második ok: a fix ágszám-küszöbök

A `BuildContinuousRiverNetworkFromSourcesParallel` a kész ágakat SZIGORÚAN
forrás-sorrendben commitálja (a `claimed` szemantika reprodukálása —
determinizmus-követelmény, nem változtatható). A commit-időpontok ezért
nagyon egyenetlenek. `RiverBaseline`-nal mérve (seed 0xA7C944210000, level 5,
4 worker, 1 km-es áttekintés, `artifacts/a8-overview-timing/step1000b.log`):

| ág | idő |
|---|---|
| 1. | 0,67 s |
| 4. | 1,01 s |
| 6. | **1,57 s** |
| 16. | **21,15 s** |
| 96. | 36,53 s |

A régi küszöbök (1/4/6/16/48) tehát **1,6 és 21,1 s között egyetlen frissítést
sem adtak** — a felhasználó 6 ágon ragadt, és azok ráadásul a túloldalra
estek. Egy másik futásban az utolsó 18 ág ugyanazon a 37,67 s-on commitált.

## Amit módosítottam

1. `unity/WorldGenViewer/ProjectSettings/ProjectSettings.asset`:
   `runInBackground: 0 → 1`.
2. `PlanetGridMesh.cs`: a preview-publikálás fix ágszám-küszöbök helyett
   IDŐALAPÚ (`RiverPreviewPublishIntervalSeconds` = 1,0 s; az első és az
   utolsó ág mindig publikál) — az áttekintő és a finom menetben egyaránt.
   A hálózat kimenete BITRE változatlan, ez kizárólag megjelenítési
   ütemezés: nincs generátorverzió-emelés.
3. `docs/04-decisions.md`: ND-181 kiegészítés (a fenti mérésekkel) és új,
   nyitott **ND-188**.
4. `docs/06-user-verification-checklist.md`: a lista elejére került, mit és
   milyen időzítéssel tud a felhasználó ellenőrizni.

## Új nyitott döntés: ND-188

A mérés egy következményt is kimutatott: az „áttekintés" alig gyorsabb a
véglegesnél. 1 km-es lépésközzel **36,5 s / 123 926 pont**, az 50 m-es finom
menettel 60,8 s / 504 060 pont — a 20-szoros lépésköz csak **1,7-szeres**
időnyereség. Oka mért: a hossz 58,77%-át adó escape-szakaszokat fix 250 m-es
sűrűséggel mintavételezzük (ND-186), a lépésköztől függetlenül, és a lokális
priority-flood sem a lépésközzel skálázódik. Opciók (lépésköz-arányos
emisszió / escape nélküli áttekintés / marad a progresszív publikálás) az
ND-188-ban, döntés előtt méréssel. Nem oldottam meg csendben.

## Ellenőrzés

- `dotnet build tests/WorldGen.Viewer.Compile`: 0 hiba (131 figyelmeztetés,
  a korábbi szint).
- Élő Unity Editor `recompile`: `completed`, `compilationFailed: false`,
  **0 konzolhiba**.
- Teljes megoldás `dotnet test -c Release`: lásd lent.
- Újraindított Play-menet az új kóddal: az áttekintés 96 ága megjelent,
  majd `preview = False` / `fineCompleted = 96` — a lánc végig lefutott,
  piros kivétel nélkül. (Az ebben a menetben mért időket a párhuzamosan
  futó tesztmenet terhelte, ezért azok NEM tiszta teljesítményszámok.)

## Ami hátravan

- **A felhasználói látvány-átvétel (B3)** — a folyók ALAKJA. Ezt én nem
  tudom eldönteni.
- A folyómesh felépítése a finom hálózatra 2-3 perc (4 ms/frame szelet,
  919 946 vertex). Nem hiba, de a budget emelése külön mérést érdemel.
- ND-188 (áttekintés költsége), ND-187 (medence = folyó vagy tó).

## Durva becslés

E kör munkája durván **2-3 emberóra** (nincs valós időnaplózás, ez becslés,
nem mért tény). Az A8 tartalmilag súlyozott állapota **~88% → ~90%**: a
megjelenítési lánc most végig lefut, de a látvány-elfogadás továbbra is
nyitott (**4-8 óra**).
