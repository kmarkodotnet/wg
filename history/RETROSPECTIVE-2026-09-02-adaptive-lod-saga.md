# Retrospektív — adaptív LOD / M13 munkafolyamat, `519e80f` óta

**Miért ez a dokumentum:** a felhasználó kérésére, mielőtt a `screen-space-lod`
ág (vagy egy jelentős szelete) egy korábbi állapotból újraindul. Célja:
tömören, őszintén rögzíteni MI történt és MIÉRT `519e80f`
("docs: M9 adaptiv LOD terv + ND-40") óta, és mit érdemes MÁSKÉNT csinálni,
ha ez a munka újrakezdődik. 45 commit érintett (`519e80f..889ed4f`).

**A jelenlegi, megoldatlan probléma, ami ezt a leállást kiváltotta:** a
felhasználó `useGpuGeometry`-t kikapcsolva, a CPU-s renderelési úttal is
`adaptiv ujraepites 2341.7ms`-et mért (cut mérete 479 661) — ez a jelen
dokumentum írásakor **nyitva maradt regresszió**, amit az ND-46
nézési-szög-korrekció (ld. lent) okozott: a javítás helyesen mélyebbre
finomítja a súroló szögű területet, de ez a rendszer más részét (a
CPU-s renderelési útvonal per-frame költségét) nem méretezte át hozzá —
**a javítás CSAK a finomítási DÖNTÉST nézte, a lerenderelés KÖLTSÉGÉT nem**.

---

## 1. Kiindulási állapot (`519e80f`)

M9 adaptív LOD terv dokumentálva (`docs/05-milestones.md` §9), ND-40
(kvadfa a korábbi, fix-szintű egészgömb-építés helyett). Ez volt a
tervezési alap, még kód nélkül.

## 2. M9 adaptív kvadfa-LOD implementáció (`4a61fbd`..`d6f1406`)

- `4a61fbd`: `AdaptiveQuadTree` első implementációja + 5 Python-referencia
  (ND-41..45).
- `33fc186`, `d6f1406`: párhuzamosítás (`Parallel.For` a gyökér-bejáráshoz
  és a kiegyensúlyozáshoz) + látókúp-alapú finomítás bevezetése.

**Jellemző minta, ami a KÉSŐBBI munkára is igaz maradt:** a
párhuzamosítás korán, jó ösztönnel bekerült — ez NEM volt hibás döntés,
de a "gyors" jelző mindig relatív volt az AKTUÁLIS tile-számhoz képest,
ami folyamatosan nőtt a munka során.

## 3. Kamera-sebesség finomhangolás (`a498aff`..`b1ad99e`)

Rövid oldalág: `PlanetOrbitCamera` forgási sebesség kísérletezés
(magasság-arányos → állandó → vissza magasság-arányosra, explicit
felhasználói kérésre). **Tanulság:** a felhasználó explicit kérése
("állandó sebesség") és a végső, VISSZAÁLLÍTOTT állapot ("magasság-arányos")
között ELTÉRÉS volt — ez azt jelzi, hogy egy köztes döntést a felhasználó
felülbírált, de ez NEM lett külön dokumentálva, miért. Ha egy explicit
kérést később visszavonunk, érdemes 1 mondatban rögzíteni, miért.

## 4. Statikus/dinamikus réteg szétválasztás (`48e3fbb`, `incremental-mesh-buffers`)

A teljes bolygó `adaptiveBaseLevel`-en épül fel EGYSZER (statikus alap),
a dinamikus réteg csak a `level > adaptiveBaseLevel` finomított
tile-okat dolgozza fel újra mozgásonként. **Ez volt a legfontosabb,
maradandó értékű architekturális döntés** — mindvégig ez tette lehetővé,
hogy a per-frame költség FÜGGETLEN legyen `adaptiveBaseLevel`
nagyságától. Ez a döntés NEM okozott problémát később.

## 5. GPU compute pipeline — klasszifikáció (`0f907f5`)

`TileClassification.compute` + `GpuTileClassifier` — a WorldGen.Core
determinisztikus lánc (Threefry, FractalNoise, PlateGeneration stb.)
float32 HLSL-portja, KIZÁRÓLAG megjelenítési célra (nem érinti a
`src/WorldGen.Core`-t, I1/I2-t). Ez a pipeline alapja lett a KÉSŐBBI
(és problémássá váló) M13 Fázis 3 GPU-geometriának is.

## 6. Screen-space LOD metrika (`d7590fb`)

A régi távolság/méret-arány metrikát felváltotta a szögsugár-alapú
(`atan2(rTile,distance)`) screen-space metrika — ez volt a metrika,
amit a mai napig (ND-46-tal kiegészítve) használunk. **Ez a metrika
volt a gyökere SZÁMOS későbbi problémának** (a "GYÖKÉROK" katasztrófa,
majd az ND-46 nézési-szög-vakság) — retrospektíve ez egy nehezebb
probléma volt, mint amilyennek elsőre tűnt.

## 7. M13 Fázis 1 — folytonos árnyalás sagája (`62cf9be`..`748e0bb`, kb. 12 commit)

Ez volt az ELSŐ nagy "diagnózis-kör": a folytonos (nem kategória-alapú)
vertex-szín bevezetése (`62cf9be`) néhány lépésben SÚLYOS, egymást
átfedő regressziókat okozott:

1. `213f43f` — a domborzat "eltűnt" (Lambert-árnyékolás hiánya a
   vertex-színbe égetve javítva).
2. `9b38294` — cache-méret a statikus alapréteg lábnyoma ELLEN
   ellenőrizve (cache-thrashing egérmozgásra).
3. `34f2ec9` — Mesh-objektumok ÚJRAHASZNOSÍTÁSA `new Mesh()` helyett
   (GPU-oldali akadás javítása) — **ez volt a session egyik
   legfontosabb, általánosan hasznos teljesítmény-javítása**.
4. `a6e5a1a`, `868d1a8` — sarkonkénti (nem tile-egészre-egyenletes)
   folytonos szín, majd a SAJÁT REGRESSZIÓ (a sarok-szín számítás
   egyszálú költsége) párhuzamosítása.

**Tanulság ebből a körből:** minden egyes "javítás" új, saját
regressziót hozott be, amit csak a KÖVETKEZŐ felhasználói teszt fedett
fel. Ez NEM volt elkerülhetetlen — mindegyik saját magában logikus
lépés volt, de A VÉGSŐ, ÖSSZESÍTETT TELJESÍTMÉNY-HATÁST egyik lépésnél
sem mértük élőben, amíg a felhasználó nem jelezte.

## 8. A "6,3 milliós cut" katasztrófa — négy diagnózis-kör (`e329ca2`..`8480cbe`)

Ez volt a session leghosszabb, legtöbb kört igénylő hibavadászata:

1. **Első kör** (`e329ca2`): levélszám-korlát a kvadfában — DE csak a
   VÉGSŐ levélszámot korlátozta, a bejárás MUNKÁJÁT nem.
2. **Második kör** (`df0a657`): a korlát MINDEN meglátogatott
   csomópontot számol, nem csak a leveleket.
3. **Harmadik kör** (`9365ca4`): MÁSODIK biztonsági korlát az
   `EnforceRestrictedBalance`-ban (a 2:1 kiegyensúlyozás saját
   kaszkád-robbanása, amit az első két korlát NEM védett).
4. **Negyedik, MINŐSÉGILEG MÁS kör** (`8480cbe`): a VALÓDI gyökérok —
   `IsWithinViewCone` degenerációja (az `angularRadius` felső korlát
   nélkül `π`-hez közelíthetett, a nézőkúp-szűrés ténylegesen
   megszűnt). **Ezt a felhasználó SAJÁT technikai megérzése találta
   meg** ("csak a látható tile-okat kellene kiszámolni"), nem a kód-
   elemzésem — ez KÉTSZER is bebizonyosodott ebben a sessionben.

**A felhasználó ITT jelezte elégedettségét** ("nagyszerű, megoldottad,
a zoom és a rotáció is szuper") — ez volt az utolsó MEGERŐSÍTETT
jó állapot, mielőtt a Fázis 3 munka elkezdődött.

**Tanulság:** az első HÁROM kör mind VÉDŐHÁLÓ volt (korlátozta a kárt),
nem oldotta meg, MIÉRT próbált a rendszer egyáltalán a teljes gömböt
finomítani. Csak a NEGYEDIK kör kereste meg a tényleges okot. Ha
hasonló "a rendszer minden határon túl próbál valamit csinálni" hiba
jön elő legközelebb, ÉRDEMES ELŐBB a gyökérokot keresni (miért akarja
egyáltalán), és csak utána/mellette a védőhálót — ne fordítva.

## 9. M13 Fázis 3 — GPU-vezérelt geometria (`f8183f7`..`df7abab`) — VAKVÁGÁNY

A drága per-tile Core-láncot (fraktál-zaj, hőmérséklet, szín) GPU-ra
vitte át a dinamikus réteghez, a MEGLÉVŐ CPU-s mesh-építő csővezetékbe
visszaolvasva az eredményt.

- `f8183f7`: implementáció.
- `28a07b4`: egy VALÓDI HLSL-fordítási hiba javítása (dinamikus
  vektor-komponens-írás — `p[valtozo] = ...` — amit több fordító
  (FXC) nem enged). Ez a hiba a `.compute` fájlok élő Unity nélküli
  fordítás-ellenőrizhetetlensége miatt csak ÉLESBEN derült ki.
- `df7abab`: a kernel LEFORDULT, DE ~50 másodperc/újraépítés lett —
  mert a GPU kernel MINDEN tile-hoz KÜLÖN számolja mind a 4 sarkát,
  a CPU-s út `_persistentCornerColorCache`-es sarok-MEGOSZTÁSÁVAL
  szemben. **Ez egy ELŐRE LÁTHATÓ tervezési hiba volt** — a CPU-s út
  gyorsasága NAGYRÉSZT pont ebből a sarok-cache-ből jött (4 szomszédos
  tile osztozik egy sarkon → 4x kevesebb számítás), és a GPU-port ezt
  az invariánst NEM vitte át. Retrospektíve ezt a kockázatot A
  TERVEZÉS SORÁN kellett volna azonosítani, nem az implementáció UTÁN.

**Végeredmény: az EGÉSZ Fázis 3 munka (kb. fél munkamenetnyi idő) NEM
használható a jelenlegi formájában** — a `useGpuGeometry` kapcsoló
alapból (és ajánlottan) KIKAPCSOLVA marad. A HLSL-fordítási hiba
javítása önmagában hasznos maradt (jó gyakorlat), de a teljes
funkció nem ért el nettó előnyt.

**Fő tanulság:** GPU-portolás előtt explicit ellenőrizni kell, hogy a
CPU-s út gyorsasága miből ered (itt: sarok-cache), és a GPU-tervnek
ELŐRE reflektálnia kell erre — nem utólag, teljesítmény-mérésből
kiderítve.

## 10. ND-46 — nézési-szög-vakság diagnózisa és (részleges) javítása (`b8682b7`..`b545463`)

Egy MÁSIK, régóta dokumentált (de csak most élesben észlelt) hiányosság:
a szögsugár-metrika nem veszi figyelembe a nézési szöget, ezért a
horizonthoz közeli (súroló rálátású) terep alul-finomodik, annak
ellenére, hogy jelentős képernyő-területet foglal el.

- `b8682b7`: átlós-FOV nézőkúp-javítás (valós, de kicsi hatású —
  a felhasználó megerősítette, hogy önmagában NEM oldotta meg a
  problémát) + ideiglenes DIAG-log a valódi okhoz.
- Több kör AskUserQuestion-alapú kísérleti kizárás (hiszterézis,
  ablak-méret, olcsó előszűrés) — ez a RÉSZ jól működött: minden
  hipotézist MÉRÉSSEL/standalone teszttel zártunk ki, nem
  találgatással.
- `b545463`: a VÉGSŐ javítás — `1/cosGrazing` szorzó az
  `angularRadius`-on, `MinUsefulCosGrazing=0.02` alsó korláttal.

**EZ A JAVÍTÁS OKOZTA A JELENLEGI, MEGOLDATLAN REGRESSZIÓT.** A
standalone `lod-verify` harness-ben VERIFIKÁLTAM, hogy a `BuildCut`
mélyebbre finomodik (cut.Count nő, legmélyebb szint 9→15) — ez
IGAZ és HELYES eredmény volt. **DE**: csak azt ellenőriztem, hogy a
`AdaptiveQuadTree.BuildCut` (a KIVÁLASZTÁS) helyesen működik-e —
SOHA nem mértem meg élőben, mekkora a LERENDERELÉS (`RebuildAdaptiveMesh`)
költsége a MEGNÖVEKEDETT tile-számra. A felhasználó élesen mérte:
`cut merete=479661` (a korábbi ~400-410K helyett), és a CPU-s
renderelési út erre `2341.7ms`-et vett igénybe — **elfogadhatatlanul
lassú**, még úgy is, hogy a `useGpuGeometry` KI volt kapcsolva (tehát
ez NEM a Fázis 3 GPU-probléma, hanem egy ÚJ, az ND-46 fix által
okozott terhelés-növekedés a CPU-s renderelési oldalon).

**EZ A LEGFONTOSABB TANULSÁG EBBŐL A DOKUMENTUMBÓL:**

> **Egy LOD-döntési (kiválasztási) javítás verifikálása NEM elég, ha
> csak a KIVÁLASZTÁS logikáját teszteli (cut.Count, szint-mélység) egy
> standalone/izolált tesztben. A VALÓDI kérdés mindig az, hogy a
> megnövekedett/megváltozott tile-halmazt a RENDERELÉSI út (CPU vagy
> GPU) el tudja-e készíteni a frame-költségvetésen belül. Ezt CSAK élő
> Unity Editor-mérés tudja megválaszolni — sosem szabad "megoldottnak"
> nyilvánítani egy LOD-változtatást pusztán a kiválasztási teszt
> alapján.**

Egy másodlagos tanulság: a `useGpuGeometry` Inspector-kapcsoló
ÁLLAPOTA (be/ki) többször összezavarta a diagnózist ebben a
sessionben (a felhasználó bekapcsolva hagyta korábbi kísérletezésből,
majd ez adta az "megint katasztrófa" jelentést, holott a friss
kódváltoztatásoknak semmi köze nem volt hozzá). **Élő teljesítmény-
hiba diagnózisánál MINDIG ELSŐKÉNT ellenőrizni kell az ÖSSZES
releváns Inspector/scene-állapotot** (nem csak feltételezni, hogy
"alapértelmezett"), mielőtt a kódban keresünk okot.

---

## Összefoglaló tanulságlista (ha újrakezdjük)

1. **Minden LOD/teljesítmény-változtatást ÉLŐ Unity Editorban kell
   mérni, a TELJES pipeline-on (kiválasztás + renderelés), mielőtt
   "kész"-nek nyilvánítjuk.** Standalone teszt-harness kiváló a LOGIKAI
   helyesség (determinizmus, hisztérezis, biztonsági korlátok)
   verifikálására, de NEM helyettesíti az élő teljesítmény-mérést.
2. **GPU-portolás előtt azonosítani kell, MIBŐL ered a CPU-s út
   gyorsasága** (itt: sarok-cache/dedup), és a GPU-tervnek ezt meg
   kell tartania — különben a port lassabb lesz, nem gyorsabb.
3. **A "miért próbál a rendszer valami extrémet csinálni" gyökérokot
   ELSŐKÉNT kell keresni**, a védőhálót (biztonsági korlát) csak
   MELLETTE/UTÁNA — a védőháló elfedi a tünetet, de nem old meg semmit,
   és több kört is elvehet, mire kiderül, hogy nem elég.
4. **Élő teljesítmény-hiba diagnózisánál elsőként az Inspector/scene
   ÁLLAPOTOT kell ellenőrizni** (milyen kapcsolók vannak be/ki-kapcsolva
   ÉPPEN MOST), nem feltételezni az alapértelmezést.
5. **A felhasználó saját technikai megérzése** (a "csak a látható
   tile-okat kellene számolni" és a "nem bírja el ezzel a sok
   objektummal" észrevételek) **kétszer is pontosabb volt, mint az
   én kód-elemzésem** — érdemes komolyan venni és azonnal
   megvizsgálni, nem csak "megjegyezni".
6. **Egy metrika-korrekció (mint az ND-46 `1/cosGrazing` szorzó), ami
   NÖVELI a finomítandó tile-ok számát, MINDIG együtt jár egy
   terhelés-becsléssel is** — nem elég a "helyesebb" geometriai
   döntést bizonyítani, a megnövekedett munkamennyiség
   KÖLTSÉGVETÉSÉT is elemezni kell ELŐRE, nem utólag a felhasználói
   jelentésből.
7. **A `history/` munkamenet-napló gyakorlat jól működött** — ez tette
   lehetővé, hogy ez az összefoglaló egyáltalán megírható legyen
   pontosan, kommit-hivatkozásokkal. Érdemes folytatni.

## Utolsó ismert, felhasználó által megerősített JÓ állapot

`8480cbe` ("fix(M9 adaptiv LOD): a VALODI gyokerok - IsWithinViewCone
degeneracioja kozeli zoomnal") — a felhasználó explicit visszajelzése:
*"nagyszerű, megoldottad, a zoom és a rotáció is szuper. köszönöm!"*.
Ez volt az UTOLSÓ pont, ahol mind a teljesítmény, mind a vizuális
eredmény megerősítetten elfogadható volt, MIELŐTT a Fázis 3
(GPU-geometria) és az ND-46 munka elkezdődött.
