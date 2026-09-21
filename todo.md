# TODO — rövidített backlog

> **2026-09-21 óta TÖRTÉNETI.** Az élő, ellenőrzött feladatlista: [`todo2.md`](todo2.md).
> Ez a lap a 2026-09-21-i állapotot őrzi meg, a diagnózisok szövegével együtt.

A `docs/backlog.md` teljes, történeti; ez a kivonat csak azt mutatja, **mi van
hátra**, két részre osztva: amit én el tudok végezni önállóan, és amihez te
kellesz (élő Unity Play-menet, vizuális elfogadás).

Állapot: **2026-09-21** (az ND-127 lezárása után).

> A 2026-09-20-i lista mind a 10 saját tétele lezárult; az élő visszamérések is
> megtörténtek (`terrainBasis` 7258 → 332 ms, `lakes` 180–190 → 6,2–9,5 ms,
> `precipitation` 322–410 → 0,0 ms, teljes hideg Build 12 731 → 5271 ms).
> A lezárt tételek innen kikerültek — a `history/` naplókban maradnak meg.

---

## 1. Az én feladataim (önállóan elvégezhetők)

| # | Feladat | Leírás / mi hiányzik | Hivatkozás |
|---|---|---|---|
| 1 | **#4 — lemez-szabálytalanság** | ~~Seed-törő, előbb terv.~~ → **AZ ALAK JAVÍTVA** (2026-09-21, ND-125), és **nem volt seed-törő**: az overlay a NYERS pozícióval hívta az `AssignPlate`-et, a világmodell viszont a WARPOLTTAL — mérve a tile-ok **19,5–26,4%-án** más felosztást rajzolt, a határ-hullámzás (kerület/√terület) 4,9–5,2 helyett 7,2–7,9. A nyers gömbi Voronoi definíció szerint konvex sokszög — ezt látta. **Nyitva marad a MÉRET-eloszlás** (ND-125 2. rész): a mérés szerint a generált eloszlás *egyenletesebb*, mint a Földé. Döntést kér, vizuális ellenőrzés UTÁN. | ND-125, ND-36 |
| 2 | **Klíma-konstansok hangolása (ND-126b)** | ~~A biome-osztályozó nem látja a csapadékot.~~ → **MEGOLDVA** (2026-09-21, ND-126 (A)): kétdimenziós (hőmérséklet × csapadék) osztályozás, öt szárazföldi biome. Mérve: az Egyenlítő sávjában 1 helyett **4**, a 30–50° sávban 2 helyett **6** különböző biome. Ami MARADT, az valódi konstans-hangolás: (a) jég+tundra a szárazföld **40,7%-a** (Földön ~18%) — túl meredek sark–egyenlítő hőmérsékleti esés; (b) a termikus szél a sarkoknál **120–126 m/s** (Egyenlítőn 7,4) — a tag korlátlan, `tanh`-hal kellene lefogni; (c) a csapadék-percentilisek (20/45/75) aránya — ez vizuális ítélet. | ND-126b, ND-41 |
| 3 | **Régiók földrajzi összetartozása** | ~~A navigációs menü régiói nem „tartoznak össze".~~ → **MEGOLDVA** (2026-09-21, ND-127 (A)): a `FindWatershedRegions` a torkolat óceán-tile-ja szerint kulcsol, ami a lefolyás azonosítója, nem földrajzi egységé. A `MergeWatershedsIntoRegions` a vízgyűjtő-komponenseket a leghosszabb közös határ mentén vonja össze a szárazföld 3%-áig. Mérve level 5-ön: lefedett szárazföld **50,1% → 100%**, térben szétesett régió **35,7% → 0**, régió a legnagyobb landmasson **65 → 10**; level 6-on 63,3% → 100%, 20,0% → 0, 207 → 11. Ami MARADT: az ND-05 hibridjének másik két tagja (biome-klaszter, domborzati törés) továbbra is nyitott — és a vizuális elfogadás (2. tábla). | ND-127, ND-05 |
| 4 | **`useGpuGeometry` eleváció-eltérése** | Az ND-120 a GPU-**osztályozó** utat törölte, de a `TileClassification.compute` megmaradt, mert a `useGpuGeometry` ág használja — és **az is** az elavult, ND-52 előtti `BaseElevationF`-et hívja. Bekapcsolva a tile-ok ~22%-a kerülne más oldalra a tengerszinthez képest. Nem volt része az ND-120 döntésnek, ezért maradt nyitva. A shaderhez óvatosan kell érni (kétszer futott „Compiler timed out"-ba). | ND-120, ND-52 |
| 5 | **Hidrológia tile-középpont bázisa** | A hideg Build **legnagyobb egyedi tétele**: `terrainBasis=2172 ms` a `hydrology(...)` soron belül (ez NEM a statikus sarok-bázis, amit már gyorsítótáraztam — a névegyezés megtévesztő volt). Ugyanazt a validált lemez-cache kezelést kaphatná. | ND-64, ND-122 |
| 6 | **Folyó-blokkosság (#5 maradéka)** | Az ND-124 a **fa-alakot** javította (összefolyás 17% → 42%, max vízhozam-súly 2 → 6), a **blokkosságot** nem érinti. Határesetnek mondtad — a vizuális elfogadás után dől el, kell-e még vele foglalkozni. | ND-49, ND-124 |
| 7 | **Kör-alapú (round-major) folyó-forrás sorrend** | Opcionális optimalizáció: a 16×6-os hálózat 32 s háttérszálon (a `Build()`-et nem blokkolja). Ha a medencék ELSŐ forrásai külön körben futnak előre, a későbbi körök a már lefoglalt főágnál korán megállnak — ez a szekvenciális szemantika, tehát **bitre azonos** kimenet. Csak akkor érdemes, ha a várakozás zavaró. | ND-124 |
| 8 | **Deep-time újraépítés sebessége** | Cél <1 s, jelenleg ~2,8 s meleg (a 22,6 s-os kiindulásról). Ideiglenesen elfogadtad; a cél a backlogban marad. | backlog |
| 9 | **ND-108 — generátorverzió a mentéshez** | A mentés/betöltés kompatibilitási kapuja. Nyitott, nem blokkol semmit, de a seed-törő #4 ELŐTT érdemes rendezni — épp ilyen változásnál kell működnie. | ND-108 |
| 10 | **ND-20 / ND-21** | Az ND-01 (Unity 6 + HDRP) lezárása nyitotta őket: Burst `FloatMode.Strict` CI-kikényszerítés, illetve HDRP felhő űrből (prototípussal ellenőrizendő). | CLAUDE.md, ND-20, ND-21 |
| 11 | **ND-19 — floating origin** | M9-re halasztva (M3-at nem blokkolta, mert a `Directional Light` csak irányt igényel). A valós léptékű koordináta a közeli zoomnál fog számítani. | ND-19 |
| 12 | **ND-111 — kiadási identitás** | Alkalmazásverzió, névadás. Részben nyitott, nem blokkol. | ND-111 |
| 13 | **Talaj: maradék 7 regolit-mező** | MineralDiversity, Phosphorus/Nitrogen, Iron, Sulfur, Salinity, pHProxy. **Nem ütemezhető**, amíg nincs kőzettípus/litológia-modul ÉS perzisztált vulkáni hamu-/tefra-mező — egyik sem létezik. Itt csak azért szerepel, hogy ne felejtődjön el, miért hiányzik. | ND-117 |

---

## 2. A te feladataid (élő Unity Play kell hozzá)

| # | Feladat | Hogyan teszteld | Miért kell |
|---|---|---|---|
| 0 | **Lemez-overlay újranézése** | Play → tektonikus overlay. Most először a VALÓDI, szabálytalan lemezhatárokat látod (eddig egy másik, konvex felosztást rajzolt). Még mindig „négyszög/háromszög"? A méretek zavaróak-e? | Az ND-125 2. része (méret-eloszlás) **seed-törő** lenne — ezt csak azután érdemes eldönteni, hogy a valódi alakokat láttad. A mérés szerint a Földnél *egyenletesebb* az eloszlásunk: a legnagyobb lemezünk 8,7–12,1%, a Csendes-óceáni 20,4%. |
| 1 | **Folyó-hálózat vizuális elfogadása** | Play → zoomolj rá egy nagy kontinens folyóhálózatára. A finomított vonalak **háttérszálon** készülnek, most ~30 s-ig (96 forrás), addig a durva hálózat látszik. Fa-szerűen ágazik-e el, forrástól a torkolatig? Elég sűrű-e a bolygón? | Az ND-124 metrikái javultak (összefolyás 8 → 40, max vízhozam-súly 2 → 6, „súly ≥ 3" 0 → 13 folyó), de hogy ez a képernyőn **fának látszik-e**, azt én nem tudom megítélni. Ha a 30 s várakozás zavaró, az 1. tábla 7. sora a válasz. |
| 2 | **Lemez MÉRET-eloszlás: kell-e (B)?** | Az ND-125 2. része — a 0. sor megnézése után döntsd el. | **Seed-törő**: verzió-emelés + ND-09 ordinális kalibráció újrafuttatása, és a meglévő világok domborzata megváltozik. Ezt nem hozom meg egyedül. |
| 3 | **Új biome-térkép megítélése** | Play → nézd meg a felszínt. Öt szárazföldi biome van: sivatag (homok), sztyeppe (szalma), szavanna (olajzöld), mérsékelt erdő (zöld), esőerdő (mélyzöld). Kérdés: (a) jó-e az arányuk, (b) túl sok-e a jég, (c) jók-e a színek. | A percentilisek (20/45/75) konstrukció szerint 20/25/30/25 arányban osztják a vegetált szárazföldet — hogy ez jó-e, arra nincs numerikus kritérium, csak a szemed. A jégtúlsúly külön ok (ND-126b). |
| 4 | **Blokkosság — még zavaró-e?** | Play → közeli zoom egy folyóra. A vonal töredezett-e, vagy már elfogadható? | Határesetnek mondtad; ha most már rendben van, az 1. tábla 6. sora törölhető. |
| 5 | **Régió-lista megítélése (ND-127)** | Play → navigációs menü: válassz egy nagy kontinenst, nézd a régió-listát. Most ~10 régió van egy kontinensen a korábbi 65 helyett, mindegyik összefüggő földdarab, és a szárazföld 100%-a benne van. Kérdés: (a) jó-e ez a felbontás (a `RegionTargetLandSharePercent` egy szám, könnyen hangolható), (b) a régiónevek/alaktípusok illenek-e a területre. | A darabolás FINOMSÁGA ízlés kérdése: kevesebb, nagyobb régió áttekinthetőbb, több, kisebb részletesebb. A számokat (lefedettség, összefüggőség) én mérem, a jó felbontást te látod. |

---

## Megjegyzés a sorrendhez

Az 1. táblázat **1.** sora (lemez-alak) lezárult; ami maradt belőle
(méret-eloszlás), az a te döntésedre vár, és csak a 2. tábla 0. sorának
megnézése után van értelme.

A klíma szerkezeti fele (ND-126) kész; ami maradt, az hangolás, és ahhoz
**a szemed kell** (2. tábla 3.). A két MÉRT konstans-hiba (ND-126b: jégtúlsúly,
sarki szél) viszont a te ítéleted nélkül is javítható — csak érdemes egyszerre,
a percentilisekkel együtt, különben kétszer kalibrálunk.

Amíg a vizuális visszajelzésre várok, a **4.** (`useGpuGeometry` — helyességi
eltérés, ma is aktiválható) és az **5.** (hidrológia tile-középpont bázisa,
2172 ms) mennek önállóan.

A **4.** (`useGpuGeometry`) nem teljesítmény, hanem **helyességi** eltérés, és
a kapcsoló bekapcsolásával ma is aktiválható — ezért van ilyen előre sorolva a
mérete ellenére.
