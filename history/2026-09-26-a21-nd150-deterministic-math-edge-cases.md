# A21 / ND-150 — `DeterministicMath` Exp/Ln/Pow tartomány-élesetek

**Dátum:** 2026-09-26
**Ág:** `a19-plate-frame-noise`
**Kiindulás:** a felhasználó kérése („A21 legyen megvalósítva")
**Döntés:** `docs/04-decisions.md` → ND-150

---

## Honnan jött

Az A21 az **ND-137 (A20) éleset-tesztjéből** esett ki. Ott a javítás *helyi*
volt — `DeepTimeErosionGlaciation.MaxDecayExponent = 700` —, és a kódkomment
maga jegyezte fel, hogy ez „a `DeterministicMath` saját hiányossága". A todo2
A21 sora ezt a hiányosságot vette fel önálló tételként.

## A vizsgálat SZÉLESEBB hibát talált, mint a felvételkor gondoltuk

A todo2 három értéket idézett (`exp(-710)`, `exp(-750)`, `exp(-4e6)`). A
Python-orákulummal végigmért éleset-táblázat a túlcsordulási oldalt, a `Pow`-ot
és a **`Ln`-t is** érinti:

| bemenet | ND-150 előtt | helyes | ND-150 után |
|---|---|---|---|
| `exp(-710)` | `-1,4466e+308` | `4,476e-309` | `0,0` |
| `exp(-750)` | `-6,1457e+290` | alulcsordulás | `0,0` |
| `exp(-4e6)` | `+4,4595e+145` | alulcsordulás | `0,0` |
| `exp(710)` | **`NaN`** | túlcsordulás | `+∞` |
| `exp(711)` | `-1,5331e-308` | túlcsordulás | `+∞` |
| `exp(1e6)` | `6,9566e+271` | túlcsordulás | `+∞` |
| `exp(NaN)` | **kivétel** (`cannot convert float NaN to integer`) | `NaN` | `NaN` |
| `ln(5e-324)` | `-709,09` | `-744,44` | `-744,44` |
| `ln(1e-310)` | `-709,085` | `-713,801` | `-713,801` |
| `ln(+∞)` | `709,7827` | `+∞` | `+∞` |
| `ln(NaN)` | `710,1882` | `NaN` | `NaN` |
| `pow(10, 400)` | `-3,0943e-217` | túlcsordulás | `+∞` |
| `pow(10, -400)` | `-3,2317e+216` | alulcsordulás | `0,0` |
| `pow(+∞, 2)` | `-1,0` | `+∞` | `+∞` |

A `pow(10, 400)` **előjelváltása** a legbeszédesebb: egy pozitív hatvány
negatív eredményt adott, hibajelzés nélkül.

## Gyökérok

Az `Exp` a Taylor-sor eredményét `ScaleByPowerOfTwo` bit-manipulációval
skálázza: a `rawExponent + k` összeg **közvetlenül** az IEEE-754 exponens-mezőbe
íródik (`newExponent << 52`). Ha az összeg kicsúszik a normál double
tartományból (`[1, 2046]`), a bitek átfolynak a szomszédos mezőkbe — lefelé az
**előjelbitbe**, felfelé a NaN/végtelen mintákba. Az `Ln` párhuzamos hibája: a
`FrexpBits` normál double-t vár (subnormálisnál nincs implicit vezető 1-es bit),
és a `NaN <= 0.0` összehasonlítás **hamis**, tehát a NaN átfolyt a kapun.

## Miért komoly, ha ma egy modul sem hajt ilyen tartományba

Az **I1 nem sérül** — a szemét minden platformon ugyanaz a szemét. Az **I3/I4
igen**, és semmi nem jelzi: nincs NaN, nincs kivétel, nincs vizuális robbanás,
csak egy hibás szám a láncban. Az ND-137 helyi korlátja működött, de minden új
`Exp`-használónak meg kellett volna ismételnie — ez előbb-utóbb kimarad.

## Megoldás (a sorrend a repo munkarendje szerint: Python → C#)

1. **`ScaleByPowerOfTwoChecked`** (új): a megnövelt exponens `<= 0` → `0,0`,
   `>= 0x7FF` → `+∞`. A nyers `ScaleByPowerOfTwo` **szándékosan ellenőrzés
   nélkül marad**, hogy az „exakt bit-eltolás" jelentés egyértelmű legyen.
2. **`Exp`**: explicit `NaN → NaN`, plusz `ExpArgumentGate = 800,0` durva kapu.
   Ez **nem** a valódi határ (`709,783` / `-708,396`, azt az 1. pont kezeli
   exaktan) — azért kell, hogy a `double → long` konverzió biztonságos legyen.
3. **`Ln`**: `NaN → NaN`, `+∞ → +∞`, subnormális bemenet exakt felskálázása
   (`× 2^54`, majd `-54` a kitevőből). 54 eltolás minden subnormálisra elég
   (`2^-1074 · 2^54 = 2^-1020 > 2^-1022`), és a 2 hatványával való szorzás
   kerekítésmentes. Az `x <= 0` **továbbra is kivételt dob** — a modul meglévő
   szerződését nem változtattuk meg.
4. **`Pow`**: `y == 0 → 1,0` rövidzár. Véges `x`-re numerikusan változatlan
   (`Exp(0 · Ln(x)) = Exp(0) = 1,0`), de `x = +∞` mellett feloldja a `0 · ∞ = NaN`
   csapdát. A `x == 0` vizsgálat **szándékosan előbb** van, így a `pow(0, 0) = 0,0`
   ND-27-es konvenció bitre változatlan.

### Amit elvetettünk

**Fokozatos alulcsordulás** (subnormális eredmény a `[-745; -708,4]` sávban,
ahogy a `Math.Exp` teszi). A bit-eltolás ezt nem tudja előállítani — külön,
mantissza-eltolós utat igényelne, aminek a kerekítése platformfüggetlen
bizonyítást kérne. A `0,0`-ra vágás dokumentált konvenció; a fizikai
modellekben egy `1e-309`-es és egy `0`-s csillapító között nincs érdemi
különbség.

### Amihez nem nyúltunk

Az **ND-137 helyi `MaxDecayExponent`-je marad.** Redundáns, de eltávolítása
**bit-változás** lenne a `(-708,396; -700)` sávban: ott az `Exp` valós, apró
értéket ad, a helyi rövidzár viszont az egyensúlyi hányadot. A modellben ez a
sáv nem elérhető, de a változtatásnak nincs haszna. A kódkommentek
pontosítva — a helyi határ már nem *az egyetlen* védelem.

## Bizonyítás: NEM seed-törő

Minden megváltozott érték a korábban szemetes tartományban van.

- **3000 KAT-vektor** (`sinCos`, `pow`, `atan`, `asin`/`acos`, `atan2`, `tanh`)
  újraszámolva a javított orákulummal: **0 eltérés**. A
  `deterministic_math_vectors.json` újragenerálva — `git diff` **üres**, tehát
  bitre azonos.
- A `pow`-vektorok belső `Exp`-argumentuma `[-34,14; 33,15]`, a `tanh`-é
  `>= -40` — messze a biztonságos `[-708,396; 709,783]` sávon belül, tehát
  egyetlen meglévő vektor sem esett a javított tartományba.
- **Világ-hash** (`worldgen hash --seed A7C944210000 --plates 20 --level 6`),
  `git stash`-szel a változás előtti kód ellen mérve:

  | `--time` | előtte | utána |
  |---|---|---|
  | 0 | `2b98af9ad8792b396a0a33917c2afb8713e40fe4a6e0cde76f82ad2b6213738b` | azonos |
  | 400 | `14dc8ad2aa98f0fc37e14d418dad7164cad8ab6a0b2bedf8afbfa61c59a7ea07` | azonos |
  | 3000 | `823e8ba03dd6bd6a064cdb67f23fe4235f1c5b832d46202e87dbd100cdcfe7ab` | azonos |

  A `WorldGeneratorVersion` **marad `"5"`** — nincs verzióemelés.

## Tesztek

Új `DeterministicMathEdgeCaseTests` osztály:

| Teszt | Mit fog meg |
|---|---|
| `ExpUnderflowsToExactZeroNeverToGarbage` (9 eset) | alulcsordulási szemét |
| `ExpIsNonNegativeAndMonotonicAcrossTheUnderflowBoundary` (240 minta, `-700`→`-760`) | **a régi hiba előjelváltása** és ugrása |
| `ExpOverflowsToPositiveInfinityNeverToGarbage` (6 eset) | túlcsordulási szemét, `NaN` |
| `ExpIsNonDecreasingAcrossTheOverflowBoundary` | monotonitás a felső átmeneten |
| `ExpStaysFiniteAndPositiveInsideTheValidRange` | a védett sáv nem regresszált |
| `LnPropagatesNaN…` / `LnOfPositiveInfinity…` | a `x <= 0` kapun átfolyó minták |
| `LnHandlesSubnormalInput` (4 eset) | a `FrexpBits` hamis mantisszája |
| `LnIsMonotonicAcrossTheSubnormalBoundary` | a felskálázási ág illeszkedése |
| `LnRoundTripsThroughExpInsideTheNormalRange` | `exp(ln(x)) = x` a normál sávon |
| `PowOverflows…` / `PowKeepsTheDocumentedZeroBaseConvention` / `PowWithZeroExponent…` / `PowPropagatesNaN` | `Pow`-élesetek, a `0^0 = 0,0` konvenció |
| `EdgeCaseResultsAreRepeatable` | I2 (tisztaság) az új ágakon is |

**Eredmény Release-ben: 1675/1675 zöld** — Core 673, Viewer LOD 526,
App Foundation 452, CLI 24. Debug Core: 673/673. A Python-orákulum saját
önellenőrzése végigfut (`max relativ exp hiba` stb. változatlan).

**Unity:** `unity recompile` → `completed`, `compilationFailed: false`,
`consoleErrors: 0`. Offline fordítási kapuk: `WorldGen.Viewer.Compile` és
`WorldGen.App.UnityBinding.Compile` 0 hiba.

## Ami NEM történt meg

- **Nincs élő vizuális átvétel** — nem is kell: a világ-hash bizonyítja, hogy
  a megjelenített világ bitre változatlan.
- **A `Ln` polinom-pontosságához nem nyúltunk.** A subnormális ágon mért
  `1,01e-9` abszolút hiba **nem** az új ág hibája: a normál tartomány alsó
  sarkán (`2^-1000` körül) ugyanennyi. A forrása a `ln(m)` sor `c15`-nél
  történő csonkolása (`2·(1/3)^17/17 ≈ 9e-10`), és bent van az ND-27
  dokumentált `1e-8` abszolút célján.
- **A CI négy platform-kombinációja** ezen az ágon még nem futott le — a
  helyi mérés Windows x64.
