# Zoom-LOD javítás — első csomag, 2026-09-11

A felhasználó a külön diagnózis után kérte a javítások megkezdését.
Kiindulás: tiszta `codex-handoff`, `195875e`. A közben elkészült ND-63–68
exact deep-time gyorsításait megtartottuk. Új döntés: ND-69.

Javítva a prioritásos sor budget miatti eldobása, a kis kameramozgások
végleges elvesztése, az irány/FOV/aspect/pixelméret érvénytelenítése,
valamint az adaptív CPU-geometria és GPU-bázisbesorolás modellkülönbsége.
Az óceáni ős szűrője most a négy base-sarkot is figyelembe veszi, cache-elve.
A pixelképlet perspektivikus fókusztávolságot használ; az async PerfLog
fázisonként bontja a worker költségét és jelzi a kérés korát.

Azonos kiválasztási próbában a 4000/25000/50000-es budget mellett korábban
943/752/468 képernyőminta esett vissza L8-ra a 943-ból; most mindháromnál 0.
Ez ideális gömbön, a régi pixelküszöbbel végzett izolált kontroll, nem
vizuális vagy FPS-bizonyíték. A 9 új budget-tesztből a régi kvadfával 8 hibás,
a javítottal mind sikeres; a teljes új LOD-csomag 43/43.

Solution build: 0 warning/0 error. Tesztek: 381 Core + 43 LOD + 7 CLI,
431/431 sikeres. Unity offline C# build: 0 error, 85 warning. Python nincs
telepítve, KAT/vektor-újragenerálás nem futott. Core és scene nem változott,
commit/push nem történt. Új élő Unity-látvány/PerfLog szükséges.

Részletes változások, reprodukció, korlátok és élő tesztmenet:
[javítási dokumentum](../docs/reviews/lod-zoom-fixes-2026-09-11.md).
A durva/fine fedéscsere és a beragadt chunk-geomorph a következő csomag;
a teljes zoomélmény még nem kész. M9 durván 55–65%; első csomag becsült
ráfordítása 4–8 óra, hátralévő zoommunka/validáció 20–40 óra, nem időnapló.
