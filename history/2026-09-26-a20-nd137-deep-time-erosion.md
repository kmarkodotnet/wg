# 2026-09-26 — A20 / ND-137: valódi deep-time erózió

**Kérés:** „todo2.md-ből implementáld a Deep-time erózió: ma csak
uplift-relaxáció, nem erózió (felhasználói észrevétel, 2026-09-22) — ELLENŐRIZVE."

## A hiba

A `DeepTimeErosionGlaciation` neve többet ígért, mint amit tett. A teljes
„erózió" a **lemezhatár-uplift bónusz** exponenciális relaxációja volt
(τ = 50 Myr, 35%-os maradvány). Az alap-eleváció — az elsődleges ridged zaj és
a másodlagos regionális hullámzás — **időben teljesen változatlan** maradt.
A deep-time csúszka tehát lelapította a hegycsúcsokat, és a domborzattal ezen
kívül semmit nem csinált.

## A megoldás — ND-137 (B) opció

A két relief-zajtag amplitúdója **külön időállandóval** csillapodik:

```
h(t) = base_c + T_p(t) * A_p + T_s(t) * A_s
T_i(t) = m_i + (term_i - m_i) * D_i(t)
D_i(t) = eq_i + (1 - eq_i) * exp(-t_eff / tau_i)
t_eff  = (W(|lat|) + 3 * f_ice(|lat|)) * t
```

| Új / módosult | Hol |
|---|---|
| `DeepTimeErosionGlaciation` — teljes eróziós réteg | `AbsLatitudeRad`, `ZonalWaterFactor`, `GlaciatedFraction`, `ErosionEfficiency`, `EffectiveErosionTimeMyr`, `ReliefDecay(Factors)`, `ChainReliefDecay` |
| ugyanott: `DeterministicMath`-váltás | `Exp`/`Sin`/`Cos`/`Asin` a nyers `Math.*` helyett |
| `CrustElevation.ErodedReliefTerm` + a két gömbfelszíni átlag | `PrimaryReliefSphericalMean`, `SecondaryReliefSphericalMean` |
| `CrustElevation.BaseElevationFromNoiseBasis` / `…FromPlateFrameBases` | két csillapító tényező, 1,0-nál egzakt rövidzár |
| `PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime` / `ElevationWithBoundaryFromWarpedAtTime` | `erosionTimeMyr` paraméter |
| `TerrainPointBasis.EvaluateAtTime` | `erosionTimeMyr` paraméter |
| `SeaLevelCalibration.ComputeElevationFieldAtTime` | `erosionTimeMyr` paraméter |
| `RiverPathTracing` (teljes lánc) | `erosionTimeMyr` átvezetve |
| `WorldGeneratorVersion` | 3 → 4 (ND-108) |
| viewer | az eróziós idő már a mezőszámítás ELŐTT beáll; a folyó is megkapja |

### Négy dolog, ami másképp jött ki, mint a döntés tervezete jósolta

1. **A hidrológiai mező nem köthető rá — és ez architekturális, nem lustaság.**
   Az ND-137 (B) eredeti szövege a meglévő vízhozam-súly-mezőt (ND-124) szánta
   eróziós hatékonyságnak. Ez nem megy: az eleváció **pontonkénti tiszta
   függvény** (minden mesh-sarok, minden folyó-nyomvonalpont külön kérdezi),
   a vízhozam viszont globális, rács-alapú mennyiség, ráadásul a pipeline-ban
   a tengerszint **után** számolódik, a tengerszint pedig az elevációból —
   körkörös lenne. Helyette pontonként kiértékelhető, bit-egzakt **zonális**
   csapadék-proxy (három-cellás: ITCZ `cos^8`, szubtrópusi sivatagöv,
   viharpálya-Gauss) + a jeges időhányad zárt alakja. Ugyanaz a modellezési
   szint, mint a modul már meglévő `IceLineAbsLatitude` profilja.

2. **Nulla felé relaxálni elárasztotta volna a világot.** A tervezet
   hallgatólagosan feltételezte, hogy a relief-tagok nulla-átlagúak. Nem azok:
   mérve `primary*mask` ≈ 0,165 és `secondary` ≈ 0,42 (hat seeden). Nullához
   relaxálva a kontinensek átlagosan **~490 m-t süllyedtek volna** — fiktív
   tömegveszteség (nincs izosztázia-modell), amit az ND-38 térfogat-megmaradó
   tengerszint a világ elárasztásaként fordított volna le. Ezért a csillapítás
   a tag **saját gömbfelszíni átlaga** felé tart: implicit izosztatikus
   kompenzáció. Mérve 3000 Myr-nél a globális eltolódás −13,8 m.

3. **A maszkkal MÁR ÖSSZESZOROZVA kell relaxálni.** Az első változat a nyers
   `primary` zajértéket csillapította, és a `mask` szorzót érintetlenül hagyta.
   Eredmény: a hegységöv a végtelenségig őrzött egy ~1300 m-es, mask-alakú
   reliefet, a szórás csak 8,4%-ot csökkent. A `primary * mask` szorzatra
   alkalmazva 16,5% lett — és telítésben valódi peneplán.

4. **Kiesett egy latens `DeterministicMath` hiba.** Az éleset-teszt megfogta,
   hogy az `Exp` kb. −710 alatt nem 0-hoz tart, hanem szemetet ad
   (`exp(-710)` = −1,45e+308) — a `ScaleByPowerOfTwo` nem kezeli az
   exponens-alulcsordulást. Itt lokális kitevő-korlát került be; a
   `DeterministicMath` javítása **todo2 A21** lett (minden `Exp`-használót
   érint, külön mérlegelendő).

## A „lerakódás" kérdése — mi van benne és mi nincs

**Van:** ahol a relief-tag az átlaga **alatt** van (medence, völgytalp,
intramontán árok), ott a csillapítás **felemeli** a felszínt. 500 Myr-nél
161 minta emelkedett (max +321 m), 127 süllyedt (max −874 m). Ez a
relief-mezőn belüli tömeg-átrendezés, nem egyirányú lehúzás.

**Nincs:** hálózat menti hordalékszállítás, delta-építés, medencék közti
tömegátvitel, parti abrázió. Ezek globális, rács-alapú számítást kívánnak —
ez az ND-137 (C) opciója, külön döntéssel.

## Mennyire érezhető (a todo2 A20 tényleges elvárása)

Kontinentális tile-ok átlagos elmozdulása az erózió nélküli állapothoz képest
(lemez-idő fixen 0, hogy tisztán az erózió látszódjon):

| t (Myr) | 100 | 250 | 500 | 1000 | 2000 |
|---|---|---|---|---|---|
| kontinentális átlag | 117 m | 172 m | 200 m | 215 m | 222 m |
| max | 676 m | 888 m | 926 m | 938 m | 949 m |
| óceáni átlag | 32 m | 45 m | 52 m | 55 m | 57 m |

## Bizonyíték

**Python orákulum** (`tools/reference/erosion_glaciation_deep_time_ref.py`,
a CLAUDE.md „referencia előbb" rendje szerint):

```
1a. t=0 visszamenoleges kompatibilitas   288 minta, max elteres 4.55e-13 m
1c. uplift-relaxacio lancolasa           max elteres 1.14e-13 m
3a. zonalis profil harom-cellas alakja   0.152 .. 1.150
3b. ISMERT-VALASZ: a jeges idohanyad zart alakja vs. numerikus
    idointegral 40 perioduson            max elteres 1.66e-05 (a mintavetelezes
                                         felbontasan, nem a zart alake)
3c. relief-csillapitas timestep-invariancia  max elteres 1.50e-14
3d. lerakodas: 161 fel / 127 le, szoras 449 -> 375 m (-16.5%)
3e. atlagtartas: -13.8 m eltolodas 3000 Myr-nel
3f. lathatosag + differencialt kopas (nedves 0.579 < sivatag 0.726, polaris 0.355)
400 relief-erozios + 120 szetvalasztott-ido tesztvektor generalva
```

**C# tesztek** — `tests/WorldGen.Core.Tests/Tectonics/DeepTimeReliefErosionTests.cs`
(27 teszt): KAT a két új vektorhalmazra **1e-12 tűréssel** (mindkét oldal
ugyanazt a `DeterministicMath`-ot implementálja), a modell-konstansok egyezése,
`t = 0` bit-regresszió, tisztaság, párhuzamos = szekvenciális,
timestep-invariancia, zonális alak, a jeges időhányad numerikus ellenőrzése,
differenciált kopás, kétirányú mozgás, láthatóság + monotonitás, élesetek,
simulás + átlagtartás.

```
dotnet test WorldGen.sln
  WorldGen.Core.Tests            624 zöld
  WorldGen.Viewer.LodChunking    526 zöld
  WorldGen.App.Foundation        452 zöld
  WorldGen.Cli                    24 zöld
  --------------------------------------
  összesen                      1626 zöld

dotnet build tests/WorldGen.Viewer.Compile           0 error
dotnet build tests/WorldGen.App.UnityBinding.Compile 0 error
```

A verzióemelés miatt a `thermal_checkpoint_ref.py` generátorverzióját is
emelni kellett (3 → 4), és a vektorait újragenerálni — a checkpoint-identitás
hasheli a generátorverziót. Ez ugyanaz a csapda, ami az ND-136-nál is előjött.

## Menet közben javított, pre-existing hiba: a kopás szétesett a fogyasztók között

Kiderült, hogy az uplift-relaxációt **csak a viewer** adta hozzá
(`ApplyDeepTimeErosionToField`, külön mezőpassz). A Core
`ElevationWithBoundaryFromWarpedAtTime`-ot hívó többi fogyasztó — a
`SeaLevelCalibration` mezője, a `RiverPathTracing` és a `worldgen hash` CLI —
kihagyta. Következmény `t > 0`-nál: a folyó relaxáció nélküli hegyeken keresett
lejtőt, a determinizmus-eszköz pedig egy olyan világot hashelt, amit a viewer
nem is jelenít meg.

A kopás ezért **teljes egészében a Core-ba került** (relief-csillapítás +
uplift-relaxáció az `ElevationWithBoundaryFromWarpedAtTime`-ban), a viewer külön
passza törölve (50 sor), a CLI átadja a `--time`-ot eróziós időként is. Ráadásul
a három elevációs út (Core mező, cache-elt tile-közép, pontszerű sarok)
mostantól ugyanabban a műveleti sorrendben dolgozik — korábban a tile-közép
útján a kráter közbeékelődött, és az utolsó biteken eltért.

## Világ-hash: a `t = 0` bit-azonosság a TELJES világ szintjén

`git stash`-szel, a változás előtti kód ellen mérve
(`worldgen hash --seed A7C944210000 --plates 20 --level 6`):

| `--time` | előtte | utána |
|---|---|---|
| 0 | `2b98af9a…6213738b` | `2b98af9a…6213738b` — **bitre azonos** |
| 400 | `6c7a8d2f…c4003956` | `590ec46e…957e441f` — változik, ahogy kell |

## Unity

`unity recompile` → `completed`, `console_status`: `compilationFailed: false`,
`consoleErrors: 0` (133 warning, mind pre-existing `CS8632` nullable-annotáció).

## Ami MÉG NEM történt meg

**Élő Unity vizuális megerősítés.** A konstansok (`PrimaryReliefTauMyr = 250`,
`eq = 0,30/0,60`, `GlacialErosivity = 3`) illusztratívak — a számok azt
mutatják, hogy a hatás mérhetően jelen van, de hogy *jól néz-e ki* a
csúszkát húzva, az felhasználói ítélet. Ez a todo2 **B12** sorába tartozik
(tavak, jég, erózió + dinamikus tengerszint élőben).

---

# 2. kör — „fejezd be ND-137-t"

**Kérés:** „fejezd be ND-137-t" (a nyitott tételek: vízgyűjtő-súlyozott
bevágódás, hordalékszállítás, parti abrázió, lemez-klímatörténet).

## Mi készült el

### 1. Folyóvízi bevágódás (dissection) — a hiányzó FÉL

Az 1. kör modellje tisztán **simít** (lejtő-diffúzió: csúcs le, medence fel).
Ez az erózió fele. A másik fele a **bevágódás**: a völgy mélyebbre vágódik, a
gerinc marad, tehát a relief **NŐ**. Enélkül a csúszka soha nem tud mást, mint
lapítani — 500 Myr csak fakóbb változata 0 Myr-nak, holott a valódi ciklusban a
fiatal orogén **először felszabdalódik**.

```
D_primary(t) = D_p(t) * A_f(t)
A_f(t) = 1 + 0.6 * fluvialFraction(|lat|) * (1 - exp(-W(|lat|)*t / 40 Myr))
fluvialFraction = W / (W + 3*f_ice)     -- SZÁRMAZTATOTT, nincs saját konstansa
```

Mért görbe (egyenlítő): 1,0000 (t=0) → **1,2441 (t≈40 Myr, csúcs)** → 0,9252
(200) → 0,5923 (500) → 0,4800 (3000). **Relief-emelkedés, majd -hanyatlás.**

A jég elnyomja (gleccser planál, nem szabdal): folyóvízi hányad egyenlítő
1,0000 → 45° 0,2611 → 80° 0,0549.

### 2. Parti abrázió

A tengerszint körüli Gauss-sávban a felszín a **statikus (t=0) tengerszint**
felé planálódik: szirt vissza, self fel. 400 Myr-nél, sea = −150 m: +300 m-en
−77,45 m, −300 m-en +77,45 m, ±4000 m-en 0,00 m. Jégtakaró alatt kikapcsol
(`1 − f_ice`). **Soha nem lő túl** a tengerszinten — külön teszt, mert egy
villogó partvonal lenne a legszembetűnőbb hibaosztály.

## A kulcs-belátás: miért NEM kell a vízgyűjtő-mező

Az ND-137 (C) opciója a rács-alapú `FlowAccumulation`-t akarta behúzni a
pontonkénti elevációfüggvénybe. Ez körkörös és architekturálisan rossz. **De egy
procedurális világban a lefolyás-hálózatot maga a zaj határozza meg**: a víz a
topográfiai mélyedésekbe fut, azok pedig pontosan a ridged multifractal alacsony
értékű helyei. A `primary` zajérték tehát **nem proxyja a vízgyűjtőnek, hanem az
oka** — közvetlenül használható, nem közelítés. Ezért teljesíti a 2. kör a (C)
célját anélkül, hogy a globális mezőt be kellene húzni.

## Mérhető változás az 1. körhöz képest

| | 1. kör | 2. kör |
|---|---|---|
| kontinentális átlagos elmozdulás 2 Gyr-nél | 222 m | 195 m |
| relief szórásának csökkenése 500 Myr-nél | −16,5% | −13,0% |
| `D_primary` maximuma | 1,0 (`t = 0`) | **1,2441** (`t ≈ 40 Myr`) |
| globális átlag eltolódása 3 Gyr-nél | −13,8 m | −9,8 m |

A telítési „mennyire más" metrika **szándékosan** kisebb: a bevágódás megőrzi a
relief egy részét (egyensúlyi `D_primary` 0,30 helyett 0,48). Közben a domborzat
**változatosabb** — a köztes időkben felszabdalt felföldek jelennek meg.

## Két éleset, amit a tesztek fogtak meg

1. **Negatív idő → ±végtelen.** Az 1. körben a negatív erózió-idő matematikailag
   felerősített; a 2. körben a két felerősítő tényező szorzata `t = -1e9`-nél
   ±végtelenbe csordult, ami csendben megmérgezte volna az egész elevációmezőt.
   Mostantól minden tag „nulla előtt nincs erózió"-ként értelmezi a negatív időt.
2. **A `progress` kitevő is alulcsordul.** A parti abráziónál a `1 - exp(-t/tau)`
   tagot is korlátozni kellett — ugyanaz a `DeterministicMath.Exp` hiba (todo2
   A21), csak másik helyen. Ez megerősíti, hogy a lokális védekezés
   modulonként megismétlendő, tehát a korlátnak a `DeterministicMath`-ba kell
   kerülnie.

## Ami NYITVA MARAD — és miért döntés-köteles

- **Hálózat menti hordalékszállítás, delta-építés, medencék közti tömegátvitel.**
  A modell a relief-mezőn **belül** rendez át tömeget, de nem mozgat anyagot
  **lefelé a folyóhálózaton**. Ehhez a hálózat mentén akkumuláló számítás kell
  (az eredeti (C) opció), ami (1) `O(N × tile)` minden időlekérdezésnél a mai
  ~2,5 s-os Build tetejére, és (2) **megtöri a láncolhatóságot**: a csúszkával
  0 → 100 → 200 Myr más világot adna, mint a közvetlen 200 Myr. Ez az ND-04
  **értelmezését módosítja**, tehát felhasználói döntés, nem elvégezhető munka.
- **A lemez vándorlási klímatörténete.** A `W` (cos^8 + Gauss) nem integrálható
  analitikusan a pálya mentén, tehát fix-`N` kvadratúra kellene — ugyanaz az
  ND-04 kérdés.
- **`GlaciationPeriodMyr` / `AmplitudeK` megerősítése** (B14): a 2. kör óta
  **három** tag épül rájuk (jégvonal, glaciális erózió, a bevágódás
  jég-elnyomása).
- **Technikai adósság:** az `ElevationWithBoundaryFromWarpedAtTime` 16
  paraméteres. A `(plateTime, erosionTime, staticSeaLevel)` hármas egy
  `readonly struct` kontextusba fogandó — seed-semleges, felvéve todo2 A22.

## Bizonyíték

```
Python orakulum (uj szakaszok):
4a. bevagodas: relief-emelkedes majd -hanyatlas, csucs 1.2441 a t=40 Myr korul
4b. a jeg elnyomja: folyovizi hanyad 1.0000 (0 fok) -> 0.0549 (80 fok)
4c. TIMESTEP-INVARIANCIA a bevagodasra: max lancolasi elteres 8.88e-16
4d. parti abrazio: +300 m -> -77.45 m, -300 m -> +77.45 m, savon kivul 0.00 m,
    tullovés nelkul (121 minta -1500..+1500 m, 5 idopontban)
4e. a ket tag egyutt: t=300 Myr, atlagos |elteres| 70.7 m, max 457.2 m;
    t=0-nal BITRE azonos
3c. a teljes D_primary (2 tenyezo szorzata) lancolasa: max elteres 1.53e-14
300 bevagodasi + 300 parti abrazios uj tesztvektor

C# tesztek: tests/.../DeepTimeFluvialCoastalTests.cs (17 uj teszt)
  KAT a ket uj vektorhalmazra 1e-12 turessel, relief-emelkedes-majd-hanyatlas,
  jeg-elnyomas, timestep-invariancia (kulon a bevagodasra ES a szorzatra),
  tisztasag, elesetek, a sav ket iranyu planalasa, "soha nem lo tul",
  jegtakaro alatt kikapcsol, parhuzamos = szekvencialis, t=0 bit-regresszio,
  a mezore gyakorolt tenyleges hatas.

dotnet test WorldGen.sln
  WorldGen.Core.Tests            641 zold   (624 -> 641)
  WorldGen.Viewer.LodChunking    526 zold
  WorldGen.App.Foundation        452 zold
  WorldGen.Cli                    24 zold
  --------------------------------------
  osszesen                      1643 zold

dotnet build tests/WorldGen.Viewer.Compile           0 error
dotnet build tests/WorldGen.App.UnityBinding.Compile 0 error
```

**Világ-hash — a `t = 0` bit-azonosság MINDKÉT kör után:**

| `--time` | változás előtt | 1. kör után | 2. kör után |
|---|---|---|---|
| 0 | `2b98af9a…6213738b` | `2b98af9a…6213738b` | `2b98af9a…6213738b` |
| 400 | `6c7a8d2f…c4003956` | `590ec46e…957e441f` | `14dc8ad2…59a7ea07` |

`WorldGeneratorVersion` 4 → 5; `thermal_checkpoint_ref.py` generátorverzió 4 → 5.

## Ami MÉG NEM történt meg (változatlanul)

**Élő Unity vizuális megerősítés.** Az öt új konstans
(`FluvialDissectionGain/TauMyr`, `CoastalBandMeters/PlaningFraction/AbrasionTauMyr`)
illusztratív. A számok mutatják, hogy a hatás jelen van és a görbe alakja
helyes, de hogy *jól néz-e ki* a csúszkát húzva — és különösen, hogy a
`t ≈ 40 Myr` körüli relief-csúcs nem túl erős-e — az felhasználói ítélet
(todo2 **B12**).
