# 2026-09-13 — Régi Canvas/TMP kontinens- és régiólista eltávolítása

## Mi és miért

Felhasználói kérés: az új IMGUI-navigáció (`PlanetGridMesh.DrawNavigationPanel`:
breadcrumb, Vissza gomb, lista) mellett a régi, Canvas/TextMeshPro alapú
kattintható lista ("Continents" + "Regions (top 10)") redundánssá vált, ezért
el kell tüntetni. Ezt már a `2026-09-13-navigation-menu-area-level.md` is
javasolta, élő ellenőrzés utánra.

## Változások

`unity/WorldGenViewer/Assets/Scripts/Viewer/WorldGenPanelUI.cs`:
- Törölt mezők: `continentPanelText`, `regionPanelText`,
  `flyToDurationSeconds`, `flyToAltitude`.
- Törölt metódusok: `TryGetClickedLinkIndex` (a `[diag]` naplózással
  együtt), `TryGetPaddedLinkBounds`, `FlyToRegion`, és a `LinkClickPaddingX/Y`
  konstansok.
- `Update()`: a kattintáskezelés kikerült, csak az `orbitCamera`
  öngyógyítása és az `UpdateViewLevelDisplay()` maradt. Ezzel megszűnt a
  minden bal kattintásra megjelenő "nem talált linket" Console-napló is.
- `RefreshPanels()`: csak a World-összefoglalót írja, és a `_lastData`-t
  tölti.
- Nem használt `using`-ok (`System.Text`, `UnityEngine.UI`) törölve, az
  osztály-doksi frissítve.

`unity/WorldGenViewer/Assets/PlanetView.unity`:
- A `ContinentPanelText` (GameObject `&2056601215`) és a `RegionPanelText`
  (`&2140367920`) `m_IsActive: 1 → 0`. Ez azért kell, mert a TMP
  komponenseken a régi lista szövege serializálva van (`m_text`), ezért a
  kód eltávolítása után is látszana a lista.
- A `PanelController` WorldGenPanelUI komponenséből (`&2133670176`) törölve a
  4 megszűnt mező serializált kulcsa. A megmaradt hivatkozások
  (`planetGridMesh`, `orbitCamera`, `worldPanelText`, `viewLevelText`)
  változatlanok. Semmi más nem hivatkozik a két inaktivált objektumra vagy
  TMP komponensükre, csak a Canvas `m_Children` listája a RectTransformjukra,
  és ez érintetlen. Törött hivatkozás tehát nem keletkezett.

Nem változott: `PlanetGridMesh*.cs`, `PlanetOrbitCamera.cs` (a
`FlyToDirection` továbbra is az új navigáció használja), `WorldGenPanelData`,
Core, tesztek.

## Ami a régi panelből megmaradt

- `WorldPanelText`: bolygónév, seed, óceán-lefedettség, habitability
  (a `RefreshPanels` írja).
- `ViewLevelText`: "Nézet: Bolygó/Kontinens/Régió — legközelebbi név",
  minden képkockán frissül.
- Az inaktív `ContinentPanelText` / `RegionPanelText` GameObjectek a
  Canvas alatt. Unityben nyugodtan törölhetők.

## Bizonyíték

- Offline Unity-fordítás (`Assembly-CSharp.csproj`, perjeles targets-útvonal):
  előtte **0 Error, 96 Warning**, utána **0 Error, 94 Warning**. A két
  megszűnt warning a törölt `FlyToRegion`-ből jött (CS8600, CS8602). Más
  fájl warninglistája nem változott (egyedi warningok diffje üres).
- A `WorldGen.Viewer.LodChunking.Tests` nem fordítja a `WorldGenPanelUI.cs`-t,
  ezért a tesztfuttatás nem releváns.
- `git diff --check` tiszta a két módosított fájlon.

## Élőben ellenőrizendő

- Play módban a régi kontinens-/régiólista nem látszik. A World-összefoglaló
  és a nézetszint-szöveg továbbra is látszik.
- Az új navigáció (breadcrumb, Vissza, lista, kamera-repülés) és a
  Deep time / Rétegek / Kamera állása / Kiválasztott elem panelek működnek.
- Nincs Console-hiba, és a PanelController Inspectorában nincs Missing mező.

## Elavult dokumentációs hivatkozások (nem módosítva)

- `docs/backlog.md` 701. és 747. sor: a `WorldGenPanelUI` link-kattintási
  mintájára hivatkozik.
- `docs/06-user-verification-checklist.md` 166. sor.
- `PlanetOrbitCamera.cs` 154. és 269. sor, `PlanetGridMesh.cs` 1752. sor:
  kommentek. A két `.cs` fájlban másik munkamenet dolgozik, ezért nem
  nyúltam hozzájuk.
