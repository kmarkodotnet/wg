# 2026-09-12 — ND-87 visszamérés, ND-89 terepmaszk-előkészítés

## Kérés és mérés

A felhasználó futtatott, majd engedélyezte a folytatást. Az ND-87 log
`PerfLog_20260912_023354.txt`: 257 commit, 511 staging-szelet. Terep-
publikálás max 1,98 ms (előző 6,30), teljes commit max 9,85 ms, ebből
7,95 ms a maszk. A terhelés/kameraút eltér, nem kontrollált A/B benchmark.
Egy staging-szelet 25,76 ms, a kapcsolódó stageTarget összidő 28,40 ms:
ez külön nyitott tüske, pontos natív/GC oka nem bizonyított.

## Implementáció

Kód előtti döntés: az előkészített maszk saját gyökérhalmazt, visszaállítási/
elrejtési offseteket és azonos range-képzést használ. A prepare nem módosít
élő indexet/fedést; az apply előbb ellenőrzi a tulajdonost és a revíziót.
Idegen, elavult vagy ismételt terv nem írhat. A legacy SetHidden megmarad
prepare+apply kompozícióként. A változó indexek elvárt értékei nem változtak.

A viewer staging-sor végén terv és megváltozott fedéshez diagnosztikai
snapshot készül. A közös commit CPU-indexet ír, a meglévő natív index-
feltöltést végzi és publikálja a snapshotot; változatlan fedésnél nincs
új snapshot vagy natív hívás. Nincs teljes statikus buffer-/GPU-mesh-másolat.
Új log: pipeline=ND89, terrainMaskMode=ND89, maskPlan/maskApply/maskUpload/
maskSnapshot/maskRanges. A terrainPipeline=ND87 és auxPipeline=ND86 marad.
A natív maszkfeltöltés és a tervkészítő staging-job is monolitikus maradt.

## Párhuzamos munka és megszakítás

A másik szál közben ND-88 fizikai relief-skálázást és scene-változást hozott.
Ezeket megőriztük; a saját maszkdöntés kezdeti ND-88 azonosítóját ND-89-re
cseréltük, így nincs dupla döntés. A másik változás PlanetConstants nevéhez
hiányzó WorldGen.Core importot pótoltuk a sikeres fordításhoz. A fizikai
skálázást, scene-t és új docs/base_models tartalmat nem mi alakítottuk.

A felhasználó megszakította, majd folytatást kért. A korábbi teljes teszt-
folyamat eredménye már nem volt lekérhető; a teljes suite-ot újrafuttattuk,
nem számítottuk a megszakadt Core-futást sikeresnek.

## Ellenőrzés és átadás

- Solution build: 0 hiba/0 warning.
- Új célzott .NET-esetek: **7/7 PASS**, előkészítés/alkalmazás/restore,
  idegen/elavult/ismételt terv, bemenet- és snapshot-másolás, hibás/missing/
  null bemenet, 20 lépéses független elvárt indexállapot két sorrendben.
- Teljes solution: **666/666 PASS** = 381 Core + 278 viewer-LOD + 7 CLI.
- Viewer-LOD Release: **278/278 PASS**.
- Aktuális Unity Assembly-CSharp offline fordítás: 0 hiba/83 korábbi warning.
- Editor LOD-tesztassembly: 0 hiba/4 korábbi warning. Új két-submesh-es natív
  indexbuffer-teszt: prepare nem ír, commit maszkol, restore visszaállít,
  pozíciók/bounds megmaradnak. **Editorban nem futott**, csak fordított.
- `git diff --check`: tiszta. Core/Python orákulum változatlan; KAT és
  vektorregenerálás nem futott (a korábban hiányzó Python-környezet miatt).

[Részletes átadás és próbamenet](../docs/reviews/lod-prepared-mask-nd89-2026-09-12.md).
Architektúra 3.22, backlog és M9 frissítve. Élő ND-89 teljesítmény-/vizuális
elfogadás nincs. A következő logban a maszkterv, CPU-írás és natív upload
ideje együtt értékelendő; a fizikai relief másik szálon változott, ezt a
régi és új mérés összehasonlításánál figyelembe kell venni. A teljes kérés-
késés, korai élesség, proxyhiba és 25,76 ms staging-tüske nem lezárt.

Branch/HEAD változatlan: codex-handoff / 0f9cd4b. Nem volt commit/push,
saját scene-módosítás vagy új szimulációs algoritmus. M9 tartalmilag durván
60–70%; e lépés ráfordítás-egyenértéke 2–4 óra, élő validáció/korrekció
1–3 óra, fennmaradó zoom/render munka 8–20 óra: durva, nem mért becslések.
