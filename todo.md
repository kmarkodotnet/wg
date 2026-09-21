# TODO — rövidített backlog

A `docs/backlog.md` teljes, történeti; ez a kivonat csak azt mutatja, **mi van
hátra**, két részre osztva: amit én el tudok végezni önállóan, és amihez te
kellesz (élő Unity Play-menet, vizuális elfogadás).

Állapot: **2026-09-21** (az ND-124 lezárása után).

> A 2026-09-20-i lista mind a 10 saját tétele lezárult; az élő visszamérések is
> megtörténtek (`terrainBasis` 7258 → 332 ms, `lakes` 180–190 → 6,2–9,5 ms,
> `precipitation` 322–410 → 0,0 ms, teljes hideg Build 12 731 → 5271 ms).
> A lezárt tételek innen kikerültek — a `history/` naplókban maradnak meg.

---

## 1. Az én feladataim (önállóan elvégezhetők)

| # | Feladat | Leírás / mi hiányzik | Hivatkozás |
|---|---|---|---|
| 1 | **#4 — lemez-szabálytalanság** | „A lemezek rendkívül szabályosak, szinte mindegyik egy négyszög vagy háromszög, némelyik pedig brutál nagy." Ez **seed-törő** változás: verzió-emelés ÉS az ND-09 ordinális kalibráció újrafuttatása kell hozzá. A munkarend szerint előbb a terv megy a doksiba (opciók, hatás a meglévő seedekre), csak utána a kód. **Ez a következő.** | 2. tábla 4. sor; ND-09 |
| 2 | **Klíma-konstansok kalibrálása** | A legnagyobb tartalmi tétel, három konkrét panasszal: (a) „a ráktérítő és baktérítő közt minden sivatag, fölötte-alatta egy darabig zöld, efölött jég — nagyon nem életszerű"; (b) a csapadék-overlay eloszlása; (c) „a felhők se az igaziak". A zóna-eloszlást **offline meg tudom mérni** (szélességi sávonkénti biome-hisztogram), de az irányt neked kell megadnod. Ugyanez a talaj MVP konstansaira. | ND-41, ND-117 |
| 3 | **Régiók földrajzi összetartozása** | A navigációs menü régiói nem „tartoznak össze", mert a `FindWatershedRegions` a **torkolat óceán-tile-ja** szerint kulcsol, nem földrajzi közelség szerint. Az ND-124 mérése számszerűsítette: level 6-on **2467** medence 8602 szárazföldi tile-ra, a legnagyobb **108** tile-os. Vagy más szegmentálás kell, vagy a medencék összevonása. | 2. tábla 3. sor; ND-124 |
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
| 1 | **Folyó-hálózat vizuális elfogadása** | Play → zoomolj rá egy nagy kontinens folyóhálózatára. A finomított vonalak **háttérszálon** készülnek, most ~30 s-ig (96 forrás), addig a durva hálózat látszik. Fa-szerűen ágazik-e el, forrástól a torkolatig? Elég sűrű-e a bolygón? | Az ND-124 metrikái javultak (összefolyás 8 → 40, max vízhozam-súly 2 → 6, „súly ≥ 3" 0 → 13 folyó), de hogy ez a képernyőn **fának látszik-e**, azt én nem tudom megítélni. Ha a 30 s várakozás zavaró, az 1. tábla 7. sora a válasz. |
| 2 | **#4 tervének jóváhagyása** | Nincs mit tesztelni — a tervet olvasod el, mielőtt kódot írok. | Seed-törő változás: a meglévő világok domborzata **megváltozik**. Ezt nem hozom meg egyedül. |
| 3 | **Klíma-kalibráció iránya** | Play → szél- és csapadék-overlay. Mondd meg, **melyik irányba**: szélesebb vagy keskenyebb trópusi esőöv, erősebb vagy gyengébb orografikus hatás, hol kezdődjön a jég. | A konstansoknak nincs numerikus kritériuma (ND-41) — csak vizuális. Enélkül találgatnék. |
| 4 | **Blokkosság — még zavaró-e?** | Play → közeli zoom egy folyóra. A vonal töredezett-e, vagy már elfogadható? | Határesetnek mondtad; ha most már rendben van, az 1. tábla 6. sora törölhető. |

---

## Megjegyzés a sorrendhez

Az 1. táblázatból a **2.** (klíma-kalibráció) a legnagyobb tartalmi nyereség —
ez látszik a legjobban a képen, és a jelenlegi sivatag-/jég-övek szerinted
„nagyon nem életszerűek". De **irányt kell adnod hozzá** (2. tábla 3.), ezért
addig az **1.** (#4, lemez-szabálytalanság) megy, amihez viszont előbb terv
kell — az meg a te jóváhagyásodra vár (2. tábla 2.).

A **4.** (`useGpuGeometry`) nem teljesítmény, hanem **helyességi** eltérés, és
a kapcsoló bekapcsolásával ma is aktiválható — ezért van ilyen előre sorolva a
mérete ellenére.
