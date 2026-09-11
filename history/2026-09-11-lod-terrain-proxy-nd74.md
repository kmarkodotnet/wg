# 2026-09-11 — Tereptávolság-proxy, ND-74

A felhasználó a zoom/tile-finomítás hátralévő feladatait sorrendben kéri,
minden végrehajtás után saját ellenőrzéssel. Az első tétel ellenőrizhető
részét adtuk át; a láthatósági/parti következő tétel nem indult el.

Előzetes döntés és architektúra: ND-74 / §3.11. A kész statikus sarokadatból
immutábilis radiális proxy és maximum-piramis épül. A cut és a morph közös
kéréskori snapshotot olvas; nincs új Core-mintavétel a kiválasztási ciklusban.
Build invalidálja a proxyt. Külön Inspector-kapcsoló, álló kameránál is
újrakérés, GPU/base > 8/base > max esetén explicit gömbös fallback.
Az ND-73 beállításait ez a lépés nem hangolta tovább; a scene új változása
csak `useTerrainLodProxy: 1`. Az ND-73 vizuális elutasítása dokumentálva;
18:39:49-es logban még 0,600 morph szerepelt a fájl 0,35 értéke helyett.

[Mérés, korlátok és élő ellenőrzési lista](../docs/reviews/lod-terrain-proxy-nd74-2026-09-11.md).
Azonos kamerás offline próbában két irány 103,663 távolságánál L12→L13;
cutméret kb. +70%, a nagyobb mesh-munka költségkockázata explicit.
Proxyépítés 9,0 ms, tömbadat 7 364 640 byte. A két nadír sugárhibája
−0,0550 / −0,0452 Unity-egység: a proxy nem szigorú terrain-error bound.
Kamera, culling, óceánszűrés, új részletmodell és streaming most változatlan.

Ellenőrzés:

- Core 381/381 és CLI 7/7 a solution tesztfutásában.
- Végleges LOD 110/110 Debug és 110/110 Release: 13 új eset, összesen
  498 külön teszteset a három projektben. A korábbi teljes solution-futás
  még 108 LOD-esetet tartalmazott; a két utolsó teszt után mindkét LOD-konfiguráció újrafutott.
- Solution build: 0 hiba / 0 figyelmeztetés.
- Unity Assembly-CSharp és hivatkozott LOD assembly fordítás: 0 hiba /
  83 figyelmeztetés. Az új forrás a Unity asmdef könyvtárában, saját meta
  mellett él; a parancssori generált projekthez az ignorált validation targets
  egészíti ki a Compile-listát. Nincs másolt Core.
- `git diff --check` tiszta. Python nincs telepítve (`py -0p`), KAT és
  referencia-regenerálás nem futott; Core/referencia/verziózott vektor nem változott.
- Élő Editor-import, képi és FPS-validáció nincs; ezt a felhasználó végzi.
  Commit/push nem történt, a korábbi munkapéldány-változások megmaradtak.

Durva tartalmi M9-becslés változatlanul 60–70%. E lépés fejlesztői
ráfordítás-egyenértéke kb. 3–6 óra, további zoommunka kb. 12–28 óra,
km-UI és új Core-részletmodell nélkül. Nem mért idők; a minőségi elfogadásig
nem tekintjük lezártnak az első tételt.
