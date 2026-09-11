# 2026-09-11 — ND-77: túl nagy tereptile célzott azonosítása

A felhasználó az ND-76 próba kiértékelése után engedélyezte az 1. lépést:
a túl nagy tereptile-ok döntésének azonosítását és javítását. A meglévő log
álló kameránál, befejezett finomítás mellett 88,64 px-es dinamikus terepet
mutat, de nem tartalmazza a konkrét TileId-t. Emiatt **a javítási rész nem
teljesült**: célzott diagnosztika készült, az új élő próba a következő kapu.
Ezt munka közben is jeleztük; a régi log nem bizonyít konkrét gyökérokot.

ND-77 és architektúra §3.14 megelőzte az implementációt.

- Explicit CPU-terep quad → TileId hozzárendelés a konkatenált bucketek
  emissziós sorrendjében, cache- és pozíciófrissítési megőrzéssel.
- A kiválasztás tényleges megállási trace-e: méretküszöb, látótér, max LOD,
  tile-budget és új osztási kvóta külön. Opcionális, a cutot nem módosítja.
- Sikeres alkalmazáshoz kötött immutable snapshot. Az ND-75 találat mesh-
  azonosítást is kap; snapshotonként legfeljebb 8 részletes terepsor.
- Feltöltött quad mérete és négy sarka, TileId, megállási őstile/küszöb,
  request-proxy méret/morph, az éles resolver közvetlen sarokgazdái.
- `refinementPending` jelzi az adagok közti folytatást; a puszta
  `lodPending=False` nem jelent többé félreolvasható kész állapotot.
- Nincs Core-mintavétel a bővítésben. A pontos ID-map többletmemória,
  a trace többletmunka; az élő többletköltség még nem mért.

Ellenőrzés:

- Solution build: 0 hiba, 0 figyelmeztetés.
- Core 381/381, CLI 7/7; végleges LOD Debug és Release 161/161.
  Összesen **549 külön teszteset**, ebből 17 új ND-77-es eset.
- Unity Assembly-CSharp és függőségei: 0 hiba, 83 meglévő figyelmeztetés.
  Generált csproj + korábbi ignorált validation targets. Nincs élő
  Unity Editor-/vizuális-/performance validáció.
- `git diff --check` tiszta. Core/reference/Core-teszt diff üres.
- `py --list`: nincs telepített Python; KAT/vektorregenerálás nem futott.
- Nincs új scene-/Inspector-beállítás, sem commit/push. A korábbi dirty
  módosítások és a `.claude` tartalma megmaradt.

[Mezők, korlátok és próbamenet](../docs/reviews/lod-terrain-decision-trace-nd77-2026-09-11.md).
Következő: új Play → közepes/mély zoomnál 10–15 s megállás → friss ND-77
logból konkrét gyökérok és annak javítása. Vízfelszín és általános
gyorsítás nem változott. M9 tartalmi becslése továbbra is 60–70%.
Durva, nem mért ráfordítás-egyenérték: e csomag 2–4 óra;
hátralévő zoomjavítás/validáció 8–20 óra, km-UI és új Core-modell nélkül.
