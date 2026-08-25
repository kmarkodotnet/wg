# Fidelity-stratégia — addendum az architektúra-tervhez

**Kérdés:** hogyan lehet a 80–85%-os vizuális közelségen lényegesen javítani, és következhet-e a seedből AI-szerű kirajzolás?

**Rövid válasz:** igen, következhet — de a fidelity-hiány nagyobbik fele nem AI-kérdés, hanem klasszikus renderelési és eróziós kérdés. Ha az AI-t a klasszikus rész helyett használjuk, rosszabb eredményt kapunk, mint ha mellette.

---

## 1. Diagnózis — hol van valójában a hiány

Mielőtt bármit eldöntünk, érdemes szétszedni, miből áll a különbség a koncepciórajz és egy naiv procedurális render között.

| Hiányzó elem | Hozzájárulás a „szépséghez" | Megoldható klasszikusan? | Költség |
|---|---|---|---|
| **Eróziós mikrodetál** (vízmosások, gerincek, hordalékkúpok) | **~30%** | Igen, teljesen | Közepes |
| **Volumetrikus felhők** valódi szórással | ~15% | Igen | Magas |
| **Anyagváltozatosság** (kőzettípus, üledék, foltosság) | ~12% | Igen | Alacsony |
| **Kompozíció és fényállás** (kamera, napszög, mélységi rétegzés) | **~12%** | Igen, triviálisan | **Nagyon alacsony** |
| **Part- és sekélyvíz-átmenetek** (hab, zátony, türkiz gradiens) | ~10% | Igen | Alacsony |
| **Színkezelés** (tonemapping, lokális kontraszt, légköri perspektíva) | ~8% | Igen | Alacsony |
| **Növényzet-klaszterezettség** (nem egyenletes zöld folt) | ~7% | Igen | Közepes |
| **Festői mikrotextúra**, ami semmiből nem következik | ~6% | **Nem** | — |

Ez a táblázat a legfontosabb megállapítás az egész addendumban: **a hiány ~94%-a klasszikus eszközökkel pótolható**, és a nagy tételek (erózió, kompozíció) nem is drágák. Az AI-render a maradék ~6%-ra, plusz a többi tétel *gyorsítására* jó — nem a helyettesítésükre.

### 1.1 Miért néz ki jól egy AI-kép

Érdemes megérteni a mechanizmust. A diffúziós modell azért ad hihető terepet, mert a tanítóadatban minden felszínnek **konzisztens vízrajzi mintázata** van — minden lejtőn van vízmosás, minden völgy összefut, minden hegylábon van törmelékkúp. Az AI ezt statisztikailag reprodukálja.

De ugyanezt egy hidraulikus eróziós pass **oksági alapon** állítja elő, nem statisztikailag — és ráadásul konzisztens lesz a folyóhálózattal, a talajmezővel és a klímával, amit már úgyis számolunk. Ez szigorúan jobb, mint az AI-utánzat.

> **Ezért javaslom: az erózió az első és legfontosabb fidelity-beruházás, nem az AI.**

---

## 2. Track A — klasszikus fidelity, ROI szerint rendezve

### A1. Nagy-LOD hidraulikus erózió *(legmagasabb ROI)*

A szimuláció bázis-LOD-ja level 6 (~205 km tile). A renderhez level 11–13 kell (~6 km – 800 m). A köztes részletet **nem noise-szal** kell kitölteni, hanem eróziós pass-szel:

```
1. Szimulált elevation (level 6)  →  upsample level 12-re
2. + hierarchikus noise (§52), determinisztikus, LOD-invariáns
3. → hidraulikus eróziós pass N iteráció, a klímamodell csapadékmezőjével hajtva
4. → kimenet: vízmosás-hálózat, hordalék-lerakódás, lejtőprofil
```

**Miért determinisztikus:** az erózió bemenete (upsampelt elevation + seedelt noise + csapadékmező) determinisztikus, az iteráció fix számú, a tile-ok határán átfedéssel dolgozunk. Nincs sorrendfüggés.

**Miért ez a legjobb befektetés:** egyszerre javítja a képet, a folyóhálózatot, a talajmezőt és a lejtő-alapú biome-osztályozást. Négy dolgot fizet ki egy áron.

**Költség:** GPU compute shader, tile-onként ~200 iteráció, cache-elve a checkpointokhoz kötve.

### A2. Kompozíció és fényállás *(legolcsóbb ROI)*

A referenciaképek nem véletlenül néznek ki filmszerűen:

| Elem | A képeken | Alapértelmezett naiv render |
|---|---|---|
| Napszög | Alacsony, oldalról — hosszú árnyékok, domborzat kiemelve | Zenit — lapos, kontraszttalan |
| Kamera | Ferde, horizont a felső harmadban | Merőleges felülnézet |
| Mélységi rétegzés | Közeli éles, távoli kékes köd | Egyenletes |
| Látómező | Szűk (~35°), teleobjektív-hatás | Széles, torzító |

Ez **shader-módosítás nélkül, pusztán kamera- és napparaméterezéssel** elérhető. A viewer alapértelmezett nézete ne zenitállású merőleges legyen. Egy „cinematic" kameramód, ami automatikusan jó napszöget választ, néhány napnyi munka és aránytalanul sokat ad.

### A3. Anyagréteg a biome fölött

A biome önmagában lapos színt ad. Fölé kell egy anyagréteg:

```
kőzettípus     ← kéregtípus + kor + vulkáni eredet  (már megvan a modellben!)
üledékvastagság ← eróziós lerakódás                  (az A1 adja)
felszíni nedvesség ← talajnedvesség                  (már megvan)
szemcseméret   ← lejtő + eróziós energia
```

Mind a négy bemenet **már létezik a szimulációban**, csak nem használjuk megjelenítésre. Ez majdnem ingyen fidelity: a foltosság, ami az AI-képeken olyan jól néz ki, ebből jön.

### A4. Sekélyvíz és partvonal

A képek türkiz sekélyvize és a partmenti átmenetek nagy vizuális súlyúak:

```
mélységfüggő abszorpció (Beer–Lambert, hullámhosszonként)
+ aljzat-albedo áttűnése sekélyben
+ hab a partvonalon (lejtő + hullámexpozíció)
+ zátony-sáv, ahol meleg + sekély + tiszta víz
```

Olcsó shader, nagy hatás.

### A5. Volumetrikus felhők

Ez a legdrágább klasszikus tétel. A lapos felhő-sphere és a valódi volumetrikus réteg között nagy a különbség, de a raymarch költséges. **Javaslat: a Planet nézeten volumetrikus, a Continent/Region nézeten síkréteg + cloud shadow.** A különbség ott kevésbé látszik.

---

## 3. Track B — neurális render, seedből

Most a tényleges kérdésre: **igen, a seedből következhet AI-kirajzolás.** Három lényegesen különböző architektúra jön szóba, és a köztük lévő különbség nagyobb, mint amilyennek látszik.

### B1. Élő diffúziós render *(nem javaslom)*

```
minden frame → diffúziós modell → kép
```

| Probléma | Súlyosság |
|---|---|
| Sebesség: 1–5 s/frame a 16 ms helyett | **Kizáró** |
| Időbeli koherencia: scrubbnál villódzik | **Kizáró** |
| Nézetkoherencia: kamera mozgatásakor „úszik" a terep | **Kizáró** |
| Hallucináció: kitalál domborzatot, ami nincs a modellben | Sérti a 0. invariánst |

Ez az út zsákutca egy interaktív viewerben. Kihagyandó.

### B2. Bakelt neurális textúra-szintézis *(ez a járható út)*

A kulcsötlet: **a neurális háló nem renderel, hanem textúrát süt** — egyszer, generáláskor, tile-onként, és az eredmény bekerül a world package-be.

```
GenerateWorld(seed)
  │
  ├─ szimuláció lefut                       → elevation, biome, klíma…
  ├─ A1 eróziós pass                        → mikrodetál
  ├─ neurális szintézis tile-onként:
  │     bemenet: [elevation, normal, biome, nedvesség, lejtő]  ← kontrollcsatornák
  │     modell:  ControlNet-szerű, terep-albedo-ra tanítva
  │     denoise strength: 0.25–0.35         ← alacsony! csak textúrát ad, geometriát nem
  │     latens seed: Sample(worldSeed, "NEURAL_TEX", tileId)
  │     kimenet: albedo + roughness + detail-normal textúra
  └─ textúrák → .worldpkg/layers/neural/    → tartalom-hash a metadata-ban
```

Ezután a **valós idejű render teljesen klasszikus** — csak jobb textúrákkal dolgozik. Nincs sebességprobléma, nincs villódzás, nincs nézetkoherencia-gond.

**A determinizmus itt hogyan oldódik meg:** nem úgy, hogy a hálót bitpontosan reprodukálhatóvá tesszük (az nehéz, lásd 3.1), hanem úgy, hogy **egyszer lefut és az eredmény tárolódik**. A reprodukálhatóság garanciája a tartalom-hash, nem az újrafuttatás. Ez pontosan ugyanaz a logika, mint a checkpointoknál.

**Az alacsony denoise strength kritikus.** 0.25–0.35 környékén a modell textúrát és anyagérzetet ad, de nem rajzol át hegyet oda, ahol nincs. Efölött elkezd hallucinálni, és sérül a 0. invariáns. Ez konfigurációs korlát, amit tesztelni kell: renderelt kép → visszamért elevation-korreláció a forrásmodellel.

### B3. Neurális szuperfelbontás *(kiegészítés B2-höz)*

Szűkebb, biztonságosabb változat: a háló nem albedót szintetizál, csak **felskáláz** egy klasszikusan renderelt, alacsonyabb felbontású képet, élességet és mikrodetált adva.

| | B2 (textúra-szintézis) | B3 (szuperfelbontás) |
|---|---|---|
| Hallucináció-kockázat | Közepes | Alacsony |
| Fidelity-nyereség | Nagy | Közepes |
| Mikor fut | Generáláskor, bakelve | Futásidőben is mehet (ESRGAN-osztály, ~5 ms) |
| Időbeli koherencia | Nem gond (bakelt) | Gond lehet, temporal-stabil modell kell |

B3 futásidőben is használható, mert a modern szuperfelbontó hálók gyorsak. De önmagában kevesebbet ad.

---

## 3.1 A neurális determinizmus valódi problémája

Ez az a rész, amit előre tisztázni kell, mert később fájdalmas lenne.

**A GPU-n futó neurális háló alapesetben nem bitpontosan reprodukálható:**

| Forrás | Miért nem determinisztikus |
|---|---|
| cuDNN algoritmusválasztás | Heurisztikus, GPU-modellenként más konvolúciós kernelt választ |
| Atomikus redukciók | Lebegőpontos összeadás nem asszociatív, a szálsorrend számít |
| TF32 / fp16 | Csökkentett mantissza, hardverfüggő kerekítés |
| Nemdeterminisztikus kernelek | `scatter_add`, egyes upsampling implementációk |
| Driver- és könyvtárverzió | Frissítés után más eredmény |

Tehát **ugyanaz a seed ugyanazon a modellen, de más GPU-n, más képet ad.** Ez első ránézésre megöli a §2.1 invariánst.

**A feloldás három szinten:**

| Szint | Módszer | Garancia | Költség |
|---|---|---|---|
| **1. Bake + hash** *(javaslom)* | A textúra egyszer készül, bekerül a .worldpkg-be, tartalom-hash a metadata-ban | **Teljes** — a bájtok azonosak, mert ugyanazok a bájtok | Package-méret nő |
| 2. Determinisztikus inferencia | fp32, `torch.use_deterministic_algorithms(True)`, rögzített cuDNN algo, pinned modellhash | Erős, de csak azonos architektúrájú GPU-n | 2–3× lassabb inferencia |
| 3. CPU inferencia | fp32, egyszálú vagy determinisztikus redukció | Teljes, platformfüggetlen | 20–50× lassabb |

Az 1. szint azért elegáns, mert **elkerüli a problémát ahelyett, hogy megoldaná**. A world package már úgyis tárol checkpointokat — a neurális textúrák ugyanolyan sütött melléktermékek. Aki megkapja a `.worldpkg`-et, pontosan ugyanazt látja.

**Következmény a verziózásra:** a `metadata.json` bővül:

```
NeuralModelId        modell azonosító
NeuralModelHash      súlyok SHA-256
NeuralSchemaVersion  kontrollcsatorna-séma
NeuralTextureHash    a sütött textúrák együttes hash-e
```

Ha valaki más modellel süti újra, az **breaking change**, ugyanúgy, mint a PRNG-verzióváltás. A `worldgen verify` ezt jelezze.

---

## 3.2 Nézetek közötti konzisztencia — a nem nyilvánvaló csapda

A bolygónézet, kontinensnézet és régiónézet **ugyanazt a terepet mutatja három léptékben**. Ha a neurális szintézis léptékenként külön fut, a három nézet nem fog egyezni: a kontinensnézeten lesz egy hegygerinc, ami a régiónézeten máshol van.

**Megoldás:** a neurális szintézis **csak egy LOD-szinten fut** (javaslom: level 12), és a durvább nézetek ebből aggregálódnak lefelé, nem külön szintetizálódnak. Ez ugyanaz az elv, mint az ND-02 LOD-invariancia-döntésnél — konzisztensen alkalmazva.

Ennek ára: a level 12 teljes bolygóra 25 M tile lenne. Ezért:

> **A neurális szintézis nem futhat a teljes bolygóra előre.** Igény szerint, régiónként kell futnia (amikor a felhasználó odazoomol), és cache-elődnie a package-be. Ez „progresszív sütés": a világ első bejárásakor lassabb, utána azonnali.

---

## 4. Javasolt hibrid architektúra

```
┌────────────────────────────────────────────────────────────┐
│ SZIMULÁCIÓ (level 6)         determinisztikus, gyors       │
│   elevation, plate, klíma, hidrológia, jég, talaj          │
└──────────────────────────┬─────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────┐
│ A1: HIDRAULIKUS ERÓZIÓ (level 6→12)   GPU, determinisztikus│
│   vízmosások, hordalék, lejtőprofil, mikro-vízrajz         │
│   ← a fidelity ~30%-a, oksági alapon                       │
└──────────────────────────┬─────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────┐
│ A3: ANYAGRÉTEG            meglévő mezőkből, ingyen         │
│   kőzettípus, üledék, nedvesség, szemcseméret              │
└──────────────────────────┬─────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────┐
│ B2: NEURÁLIS TEXTÚRA-SÜTÉS   opcionális, progresszív       │
│   ControlNet, denoise 0.25–0.35, seedelt latens            │
│   → albedo, roughness, detail-normal → .worldpkg           │
│   ← a maradék ~6% + a többi tétel gyorsítása               │
└──────────────────────────┬─────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────┐
│ KLASSZIKUS REALTIME RENDER    60 fps, teljesen determin.   │
│   A2 kompozíció, A4 víz, A5 felhők, PBR, atmoszféra        │
└────────────────────────────────────────────────────────────┘
```

**A lényeg:** a neurális réteg **kivehető**. Ha kikapcsoljuk, a világ ugyanaz marad, csak a felszín textúrája lesz proceduális. Semmilyen adat, metrika, esemény vagy geometria nem függ tőle. Ez tartja tisztán az architektúrát — a neurális rész kozmetikai réteg, nem a modell része.

---

## 5. Várható fidelity, lépcsőnként

| Fázis | Mit ad hozzá | Becsült közelség | Munkaigény |
|---|---|---|---|
| Alap (v0.2 M6) | Biome-színek, alap shading | 80–85% | — |
| + A2 kompozíció | Kamera, napszög, légköri perspektíva | 86–88% | **napok** |
| + A4 víz, A3 anyag | Part, sekélyvíz, foltosság | 89–91% | 1–2 hét |
| + A1 erózió | Mikro-vízrajz, gerincek, hordalék | **93–95%** | 3–4 hét |
| + A5 volumetrikus felhő | Valódi felhőszórás | 95–96% | 3–4 hét |
| + B2 neurális textúra | Anyagérzet, festői mikrodetál | **97–98%** | 4–6 hét + modellmunka |

A táblázat számai becslések, nem mérések — de az arányok robusztusak. **Az A1+A2 együtt többet ad, mint a teljes neurális réteg, és feleannyiba kerül.**

---

## 6. Nyitott döntések

| ID | Kérdés | Opciók | Javaslatom | Mikor |
|---|---|---|---|---|
| **ND-13** | Neurális réteg egyáltalán? | Nincs / B2 bakelt / B2+B3 | **B2, de csak M13 után** — előbb A1–A5, mert olcsóbb és többet ad | M13 |
| **ND-14** | Neurális determinizmus-stratégia | Bake+hash / determinisztikus inferencia / CPU | **Bake+hash** — elkerüli a problémát, nem megoldja | ND-13-mal együtt |
| **ND-15** | Modell forrása | Kész alapmodell + ControlNet / saját finomhangolás terepadaton | Kész modell először; finomhangolás csak ha a stílus nem stabil | ND-13 után |
| **ND-16** | Neurális sütés hatóköre | Teljes bolygó előre / progresszív, zoomra | **Progresszív** — a teljes level 12 nem fér el | ND-13-mal |
| **ND-17** | Denoise strength felső korlát | 0.25 / 0.35 / szabad | 0.35 kemény plafon, automatikus elevation-korrelációs teszttel | ND-13 után |
| **ND-18** | Erózió LOD-célszint | 10 / 12 / 14 | 12; a 14 memóriaigénye nem indokolt | M7 |

---

## 7. Amit ebből most érdemes eldönteni

Semmit — kivéve egyet: **az A1 eróziós pass kerüljön be a tervbe M7-be**, mert ez a legnagyobb egyedi fidelity-tétel, és mellékesen javítja a folyóhálózatot és a talajmezőt is. Ez nem opcionális szépítés, hanem a modell minőségét javítja.

A neurális réteg döntése nyugodtan várhat M13-ig. Addigra lesz valós összehasonlítási alap: látni fogod, mennyi hiányzik, és megéri-e érte a modellfüggőséget és a package-méretet felvállalni. Az architektúra úgy van megtervezve, hogy akkor is beilleszthető legyen, ha most nem döntünk róla.
