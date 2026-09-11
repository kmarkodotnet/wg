# Zoom-LOD javítás — második csomag, 2026-09-11

A felhasználó engedélyezte a következő lépést. A helyi ND-69 módosításokra
épültünk, `codex-handoff`, `195875e` után; az első csomag megmaradt, commit
és push nem történt. Új döntés: ND-70; architektúra §3.7.

Elkészült a kiváltandó base-tile alatti teljes renderpartíció, a statikus
terrain részleges, visszaállítható indexmaszkja és a közös geometriai sarkok
legdurvább szomszédhoz igazítása. A morph coarse-felülete most a valódi
háromszögpár, nem bilineáris nyereg. A változatlan topológiájú chunk változó
csúcsait pozíció-only feltöltés követi. A szinkron CPU-út közös kódra került,
a nyilvános Build-kérés megvárja a futó workert, radius/base-level módosítása
új alapmesht kér. A meglévő exact deep-time gyorsítások megmaradtak.

Az L20-as kockalapél-regresszió fejlesztés közben elbukott: az átlós
minták a vetítés nyírása miatt határra eshettek. Két eltérő meredekségű
mintasor oldotta meg; a teszttolerancia változatlan. A lapon belül egész
rácsindexek váltják ki a vetítéses szomszédkeresést.

Ellenőrzés: solution Debug build 0 warning/0 error; 381 Core + 57 LOD + 7 CLI
= 445/445 sikeres. LOD Release 57/57. Unity viewer C# build 0 error/83 warning,
EditMode tesztprojekt 0 error/4 warning. Két új valódi Mesh API-teszt csak
lefordult, Editorban még nem futott. `git diff --check` tiszta. Python-
orákulum nem futott (nincs telepített Python); Core/numerika nem változott.

A diagnosztika négy közeli nézetében a teljes fedés nem növelte a tile-számot:
3998/24998/50000/164627 maradt, egyaránt 50 base-tile alatt. Ez ideális
gömbös kiválasztási próba, nem általános költségkorlát és nem FPS-bizonyíték.

Új élő Unity-kép/PerfLog szükséges. A víz/debug-border és az attribútumok
interpolációja nem lett áttervezve; a frame-időkeret, teljes inkrementális
emisszió és displacement-tudatos kiválasztás nyitott. Részletek és tesztmenet:
[második javítási dokumentum](../docs/reviews/lod-zoom-fixes-phase2-2026-09-11.md).
Durva, nem mért becslés: M9 60–70%, e csomag 6–12 mérnöki óra,
hátralévő zoommunka és validáció 16–32 óra.
