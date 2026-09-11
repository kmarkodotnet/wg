# 2026-09-11 — ND-71 élő regresszió, ND-72 visszavonás és diagnosztika

A felhasználó az ND-71-et élőben elutasította: nincs érdemi felbontásnyereség,
viszont nagyon lelassult a kalkuláció. A friss 17:28:50-es PerfLog 6,7–9,6 s
cut-időt, 8–11,8 s requestAge-et és 190–270 ezer bounds/kérés költséget mutatott.
A drága terepkiértékelés a kvadfa belső ciklusába került, render/óceáni
szűrés előtt. A korábbi külön L14→L17 mélyzoom-próba nem igazolta a felhasználó
tényleges kameráinak látványjavulását. A hibás tradeoffot kifejezetten elismertük.

A diagnózis és a lépések ismertetése után az aktív viewerből kikerült az
ND-71 bounds-callback/cache és morph-integráció. Az ND-70 gömbös kiválasztása,
cullingja és morphja visszaállt; az ND-69/70 jó zoom/fedés/varratjavításai
megmaradtak. A tiszta kísérleti API csak offline használatban marad.
Nincs új zaj, Core-/seed-változás, budgetemelés, commit vagy push.

Azonos bemenetű offline ellenpróba a három logkamerával: ND-71 6056 / 7248 /
7237 ms; helyreállított út 75,6 / 97,3 / 92,2 ms. Nem teljes Unity/FPS mérés,
nem bitpontos live replay (fél-FOV nem volt a régi logban); részletek és
nyitott kapuk a [regressziós jelentésben](../docs/reviews/lod-zoom-regression-nd72-2026-09-11.md).

Új, kérésenként egy pontot mérő, külön időzített renderdiagnosztika:
tényleges terrain-nadírLOD, ocean-block, víz/land, radiális felszíntávolság,
morph és a feloldott quad kéréskori pixelátmérője. Új logjelek:
`[async apply ND-72]`, `[ND-72 render]`; FOV/viewport is naplózott.

Ellenőrzés: Core 381, CLI 7, LOD 83; LOD Release 83/83. Solution build
0 hiba, Unity assembly fordítás 0 hiba / 83 figyelmeztetés. Python nincs;
KAT/regenerálás nem futott, Core nem módosult. Élő ND-72 visszajelzés még kell.

M9 becslés visszakorrigálva 60–70%-ra; jelen munka durván 2–4 óra,
hátralévő zoommunka 16–32 óra (nem mért idő, km-lépték UI nélkül).
A felbontási plafont nem jelentettük megoldottnak.
