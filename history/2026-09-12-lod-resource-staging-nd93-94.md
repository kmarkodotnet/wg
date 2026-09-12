# 2026-09-12 — Közös ND-93/94 csomag

A felhasználó a következő 2–3 feladatot egyben kérte, közös ellenőrzéssel.
A két választott tétel az aktív upload/cache erőforráskockázatot kezeli;
a korai élesség és mozgókamerás selection halasztása változatlan.

ND-93: O(1) inaktív kulcssor, 128-as megtartási célkorlát, puha 0,5 ms /
8 jelölt per Update takarítás. Aktív/publikált kulcsok védettek; staging
alatt nem takarít. A saját runtime mesh-ek létrehozástól nyilvántartottak,
eviction/Build/OnDestroy felszabadítja őket; külső sharedMesh nem saját asset.
Új darabszám-/eviction-log, nem byte-pontos GPU-memóriadiagnosztika.

ND-94: külön terepmesh- és rendercél-job, 2 ms puha / 128 részfeladat keret.
A közös fedéscsere továbbra is csak a teljes sor után, külön Update.
Megszakítás a fél pár után is biztonságos. A legdrágább job tényleges
típusa/ideje/kulcsa naplózott; nincs garantált teljesköltség-csökkenés.

[Átadás és egyesített próbamenet](../docs/reviews/lod-resource-staging-batch-nd93-94-2026-09-12.md).
Kilenc új .NET-eset, viewer Debug/Release 304/304; Core 384/384 és CLI 8/8,
teljes .NET 696/696. Solution build 0 hiba/0 warning. Unity-forrás
0 hiba/83 korábbi warning, Editor-assembly 0 hiba/4 korábbi warning.
Kilenc új natív integrációs eset és négy frissített korábbi staging-eset
csak fordított, élő Editor-futás/vizuális/performance elfogadás nincs.

Nincs új Core/seed/referencia/relief/scene-változás, commit vagy push.
M9 audit szerint kb. 55%: a teljes minőségi és reakcióidő-kapu nyitott.
E csomag durva ráfordítás-egyenértéke 2–4 óra, nem mért idő; a megmaradt
upload/cache munka már visszamérés/korrekció (1–2 óra), összes maradék
12–24 óra az audit azonos hatókörében.
