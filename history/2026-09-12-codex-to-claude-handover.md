# 2026-09-12 — Codex → Claude tudásátadás

A felhasználó részletes Sonnet/Opus átadást és a korábbi átadás átnevezését
kérte. Elkészült a gyökér [kt_2_co2cl.md](../kt_2_co2cl.md): az átvétel óta
végzett exact rebuild-, tile/zoom-, víz-, upload-, cache-, kamera-/lépték-
munka, a kód/tulajdonviszonyok, bizonyítékok, nyitott hibák és folytatási terv.

Külön kt nevű régi fájl nem volt. A tényleges eredeti Claude → Codex átadás
`docs/07-handover-2026-09-10.md` volt; ez a felhasználónak jelzett értelmezés
szerint [kt_1_cl2co.md](../kt_1_cl2co.md) névre, a gyökérbe került.
Szövege sorvég-normalizálás után pontosan azonos. Az AGENTS/CLAUDE/startup
dokumentumok aktuális hivatkozást kaptak; a `.claude/` tartalma változatlan.

Az ellenőrzés során új logot találtunk: `PerfLog_20260912_202309.txt`.
ND-96–98 aktív, 64 kérésből 27 ND-98 víz-reuse. A végén a terep még
finomodik, 64220 levél / 3770 halasztott split; teljes request p50/p90
746/1073 ms. Az átadás §8 rögzíti a részidőket és a korlátokat, a backlog/
milestone/audit az új tényhez frissült. Nem új kódjavítás vagy vizuális elfogadás.

Ellenőrzés: commitgráf/munkafa, releváns döntések/kód/scene/tesztprojekt,
új log, átnevezett szöveg egyezése, átadási Markdown-linkek létezése,
`git diff --check`. Build/teszt most nem futott újra, mert csak dokumentáció
és fájlnév változott. Az utolsó ND-98 ellenőrzés 765/765 .NET és 373/373
viewer Release; a forrásfordítás és natív/vizuális hiány külön jelölve.
Nincs új commit vagy push, nincs alkalmazáskód-módosítás.

M9 61,25%, durva maradék 5–11 óra, a teljesítménykorrekciók bizonytalanok;
új architektúra esetén újrabecslés szükséges. A teljes Codex-időszak tényleges
munkaórája nem rekonstruálható, a dokumentum nem talál ki összesített időt.

## Claude-átvétel — ellenőrizetlen háttérmunkák listája

A felhasználó kérésére a `kt_2_co2cl.md` új, 15. fejezetet kapott: 19
tételben sorolja fel a háttérben elkészült, de a felhasználó által még nem
ellenőrzött fejlesztéseket. Mindegyiknél szerepel, mi készült, mi a
bizonyítéki szint („élő log látta” vagy „semmi élő bizonyíték”), hogyan kell
ellenőrizni és mi az elvárt eredmény. A zoom-tételek az ND-96 végső lista
feltételeire hivatkoznak, nem új feltételrendszert adnak.

Források: history-naplók, review-dokumentumok, `06-user-verification-checklist.md`,
valamint kódbeli ellenőrzés (EditMode tesztfájlok, `WorldPackage` v1-hibaüzenet,
„Pálya mentén (hamarosan)” vezérlő, `StarField.KeepFixedInWorldSpace`).
Csak dokumentáció változott; build/teszt nem futott, commit/push nincs.
