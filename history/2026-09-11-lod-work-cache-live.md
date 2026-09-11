# ND-81 élő próba — első zoomok késése

Felhasználói visszajelzés: az alapnézet utáni első zoomok beragadnak,
nem sima az átmenet. Elemzett log: `PerfLog_20260911_224948.txt`.
Branch `codex-handoff`, HEAD `195875e`, kód/scene változatlan.

Az ND-81 aktív, álló kameránál találatokat ad, de mozgáskor 128–130 ezer
új metrikaszámítás és 285–374 ms selection marad. A kezdeti példákban nincs
splitkvóta-miatti halasztás, az upload csak 1–6 ms. A későbbi állókamerás
láncok 3,01 / 3,95 / 4,18 másodpercesek; balance és közösél/pozícióellenőrzés
is jelentős. Kész állapotban 9,881 px-es statikus tile marad a 10 px-es
küszöb alatt: a korai élesedést a cache nem javítja.

Külön nyitott geometriai bizonyíték: befejezett visszazoomnál 34,969 px-es
feltöltött quadhoz 6,931 px-es proxy tartozik, vegyes saroktulajdonosokkal.
Nem állítjuk, hogy új regresszió vagy kizárólag resolverhiba.

[Részletes elemzés és javasolt sorrend](../docs/reviews/lod-work-cache-live-2026-09-11.md).
Most csak diagnózis/dokumentáció, nincs implementáció vagy tesztújrafuttatás,
commit/push. Következő javasolt fókusz a mozgókamerás selection költsége,
kamerafüggetlen geometriai cache-sel és a bejárás mérésével.

M9 60–70%. Durva, nem mért ráfordítás-egyenérték: 0,5–1,5 óra elemzés;
hátralévő zoom-/megjelenítés és validáció 8–20 óra.
