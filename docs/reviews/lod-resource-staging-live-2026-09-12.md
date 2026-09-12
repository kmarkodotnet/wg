# ND-93/94 — Élő visszamérés, 14:02-es próba

Forrás: `unity/WorldGenViewer/Logs/PerfLog_20260912_140221.txt`,
14:02:21–14:05:45. Branch: `codex-handoff`, HEAD: `0f9cd4b`, további
nem commitolt változtatásokkal. Ez logelemzés, nem új implementáció.
Kvantilis: rendezett sorozat, `floor((n-1)*p)` index.

## A két átadott részfeladat eredménye

**ND-93: az inaktív cache ürítése ténylegesen működött.** 151 cache-minta,
végig 128-as limit; az ablakonkénti `evicted` összege 1568 kulcs.
Az egyetlen naplózott túllépés 337 inaktív kulcs, a következő mintában
128. Ez megfelel a fokozatos ürítésnek, nem beragadt túllépés.
Csúcsok: 634 célobjektum, 1152 saját mesh, 518 spare; ezek nem mind
inaktívak, ezért nem hasonlíthatók közvetlenül a 128-as limithez.
Világváltás után nulla cél/saját mesh/spare is naplózott, majd újraépülnek.
Az utolsó minta: 219 cél, 219 saját mesh, nulla spare/inaktív kulcs.

A cache-kezelés logablakokra összesített ideje összesen 15,919 ms,
legnagyobb ablak 1,832 ms. Ez **nem egy frame tüskéje**, nem GPU-byte-
mérés, és nem tartalmazza a később végrehajtott natív Destroy teljes árát.
A 8-as stresszlimit nem szerepel, de a 128-as korlát ténylegesen terhelődött.

**ND-94: a kétlépcsős staging aktív.** Mind a 104 publikáció és 256
staging-szelet az új utat használja.

| Mért idő | Medián | p90 | Maximum |
|---|---:|---:|---:|
| Feltöltési szelet | 1,34 ms | 2,01 ms | 3,18 ms |
| Végső commit | 0,89 ms | 1,93 ms | 4,55 ms |
| Teljes kérés (`requestAge`) | 385,2 ms | 671,1 ms | 9905,6 ms |

Kérésenként 1–10 staging-frame, medián 2. A legrosszabb szelet
egyetlen `terrainMesh` job (frame 641, 3,181 ms). A legnagyobb naplózott
`terrainTarget` job 2,485 ms, `terrainMask` 2,238 ms. Ezek a szelet
részei, nem hozzáadandó idők. A 2 ms továbbra is puha keret.

A korábbi 12:51-es próba szeletmaximuma 7,10 ms volt, most 3,18 ms;
a medián viszont 0,85-ről 1,34 ms-ra, a p90 1,67-ről 2,01 ms-ra változott.
A korábbi commit 0,92 / 2,18 / 4,25 ms volt. **Nem állítható általános
gyorsulás vagy regresszió**: eltérő útvonal, 39 helyett 104 publikáció,
most tíz deep-time újraépítés is szerepel. A viewport mindkét próbában
957×583, relief 111×; ezek a mostani logban végig változatlanok.

## Teljes késés: mit jelent, és mit nem?

Az első Build 11,614 s, a tíz későbbi Build 2,757–3,043 s.
A későbbi Buildek utáni első publikációk `requestAge` értéke 3,580–4,893 s.
Ezek nem tekinthetők tiszta zoom- vagy upload-időnek: a kezdőérték és a
végső érték `Time.unscaledTime` alapú, amely frame-enként rögzített.
A Build vége felé indított kérés is a Build-frame korábbi időbélyegét
kapja; a 14:03:40.940-es kickoff után 14:03:41.030-kor már 2847 ms
`pendingMs` szerepel. A Build ideje így belekerülhet a kérésszámlálóba.
Későbbi pontos reakcióidő-mérésnél külön monotón faliórás kickoff/
worker-ready/commit időbélyeg indokolt; a meglévő érték nem CPU-idő.

A Build-enként első commitokat kizárva 93 kérés marad: medián 370,1 ms,
p90 508,3 ms, maximum továbbra is 9905,6 ms. Ez az egy kiugrás még az
első világon történt, 14:03:25.438–14:03:35.358 között. Staging összesen
5,06 ms, commit 1,48 ms; tehát **nem a mért feltöltési munka magyarázza**.
Editor-szünet, fókuszvesztés vagy más főszálas várakozás elkülönítésére
nincs elég adat. Nem állítjuk sem azt, hogy biztosan alkalmazáshiba,
sem azt, hogy biztosan felhasználói szünet. Ehhez visszajelzés kell.

Az összes kérésben a selection medián/p90/max 74,68 / 162,51 / 346,74 ms,
a sarok-előkészítésé 33,04 / 120,27 / 838,82 ms. A több száz ms-os
teljes reakcióidő továbbra is nyitott, nem a néhány ms-os commit lezárása.

## A minőségi hiba továbbra is konkrétan kimutatható

Az utolsó **nyugalmi**, már elkészült állapot (14:05:40.291, frame 19990):
`lodPending=False`, `uploadPending=False`, `refinementPending=False`,
kameraeltérés nulla; p50 8,54 / p90 14,41 / max 26,97 px, 153/153 találat.

Az ND-77 azonosítja a legnagyobb terepquadot:

- Tile: `8900000000015424`, face 4, level 9, u=482, v=4.
- Tényleges kirajzolt átló: **26,975 px**.
- Kiválasztási proxy: **9,635 px**, küszöb 12 px.
- Megállási ok: `below-threshold`; morph alpha 1.

Tehát a kijelzett geometria közel 2,8-szer nagyobb a kiválasztás
becslésénél, mégsem osztódik tovább. Ez a korábban halasztott
proxy/geometria minőségi hiba, nem cache-ürítés vagy folyamatban lévő
feltöltés. A 111× relief fontos reprodukciós feltétel. A legutolsó
44,65 px-es minta viszont új világ utáni átmeneti statikus kép, nem a
végleges minőség mérőszáma. A víz 8192 levelet is elér, de ebből egyedül
nem következik a teljes vízfelület minőségi elfogadása.

## Hibák és ellenőrzési korlátok

- 81 ND-75 méretminta, egyikben sincs invalid/malformed quad; ez nem
  teljes képi lyukmentességi bizonyíték a ritka mintavétel miatt.
- A PerfLogban nincs ND-92 recovery vagy kivételsor. Az Editor.log
  olvasását a környezet hozzáférése nem engedte, ezért teljes Console-
  hibamentesség nem állítható.
- ND-91: 151 minta, ebből 149 modellforrású és két fallback. A két
  fallback deep-time konfigurációváltás mellett jelentkezik (14:04:53
  és a későbbi újraépítés előtt), ami összhangban van a még el nem
  készült világ esetén megőrzött korábbi sugárral; nem ND-92 uploadhiba.
  Minimum felszín feletti magasság 2,875534 egység, naplózott kifelé
  korrekció végig nulla. A 0,33-as kamerakorlátot ez a próba sem érte el.
- Tíz deep-time rebuild igazolt. Külön Play-leállítás/újraindítás és
  natív Editor-tesztfutás ebből az egy logból nem igazolható.

## Következő kapu és státusz

A cache ürítése és a kétlépcsős feltöltés most már **élő loggal is
igazolt részfunkció**, nem csak lefordított implementáció. Teljes memória-
és vizuális elfogadás továbbra is hátra. Nem indokolt újabb upload-
átalakítást automatikusan elkezdeni e log alapján. Következő aktív kapu:
víz/kamera/lépték célzott ellenőrzése, az Editor-regressziós esetek futtatása.
A halasztott fő minőségi tételhez a fenti tile jó reprodukciós eset;
újraaktiválásakor előbb a becslés eltérését kell kezelni, nem vakon
lejjebb venni a globális pixelküszöböt.

Az M9-audit reakcióidő/erőforrás csoportja részleges marad: az ürítéshez
új bizonyíték érkezett, de teljes reakcióidő és memória/FPS-kapu nincs
lezárva. Emiatt a súlyozott állapot kb. 55%, a tételes durva maradék
12–24 munkaóra marad; ez e körben ellenőrzött változatlanság, nem új
előrelépésként jelentett szám. Kódot, beállítást nem módosítottunk,
tesztet nem futtattunk újra pusztán a logelemzéshez.
