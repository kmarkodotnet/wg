# Horizont-vágás a vetített terep-úton (2026-09-19)

## Kiindulás

A budget-emelés után két tétel maradt nyitva, ebből a második: *"a 25
látogatás/levél arány — ez a valódi strukturális tartalék"*. Ez ND-döntés
nélkül vihető, ha a látható eredmény nem romlik.

## Mérés — hol van a munka

A `maxLevel` léptetésével a munka szintekhez rendelhető. **`maxLevel=8`-nál,
amikor a base fölött SEMMI finomítás nem történik és nulla levél születik:**

| | kiértékelés a base-ig | a teljes finomítás hozzáadása |
|---|---|---|
| D=120 | 204 976 | +8 000 |
| D=103 | 180 564 | +10 520 |

**A munka 96%-a a statikus alapszinten vagy alatta zajlik, és egyetlen
levelet sem termel.**

Szintenkénti bontásban (D=103, magasság 3, a látható sapka a gömb ~1,4%-a):

| szint | látogatott | látókúpon kívül | kupacra |
|---|---|---|---|
| 6 | 10 920 | 331 | 10 589 |
| 7 | 42 356 | 678 | 41 678 |
| **8** | **123 152** | **2 306** | **120 846** |

Horizont-vágás: **0**. Súroló-vágás: **0**. Minden szinten.

## A gyökérok

```csharp
if (work?.View != null && terrainProxy != null)
{
    inView = work.EvaluateTerrain(terrainProxy, node, out error);
    return;                      // <- horizonCulled/grazeStop VÉGIG false
}
```

A korai `return` miatt a lentebb MÁR MEGLÉVŐ, kiterjedés-tudatos
horizont-teszt ezen az ágon soha nem futott le. És épp ez a produkciós ág:
a PerfLog minden sora `terrainProxy=True projectedTerrain=True`. Vagyis a
bolygó túlsó felének csempéit is végigjártuk, kupacra tettük, kivettük, és
csak a küszöbnél dobtuk el. 123 152 level-8 látogatás ~5 500 látható tile-ra.

## Megdőlt első próbálkozás

Először a lentebbi ág érintősík-tesztjét (`C·X + |C|·r < R²`) vettem át.
**Megbukott:** 54 próbanézetből 6-ban megváltoztatta a cutot, kettőben
katasztrofálisan (5 636 levél → 0). Az érintősík-teszt CSAK a gömb
FELSZÍNÉN lévő pontra helyes; egy emelt pont (hegycsúcs) átkukucskál a
horizonton, és a teszt a limbus menti, **valóban látható** csempéket vágta ki.

Ezt nem levezetéssel találtam meg, hanem mert a cut-hash összehasonlítást
lefuttattam, mielőtt bármit állítottam volna.

## A helyes feltétel

Szögekben: egy `rP` sugarú pont akkor van az `R` sugarú takaró mögött `d`
távolságból, ha a kamerától mért szöge nagyobb, mint
`acos(R/d) + acos(R/rP)`. Koszinuszban kifejtve csak szorzás és
`Math.Sqrt` kell — transzcendens függvény nem, ami azért fontos, mert a
CLAUDE.md szerint az nem bitpontos, a cut determinizmusa pedig dokumentált
garancia.

Konzervatív burok: a takaró a proxy **legkisebb** sugara (a legkisebb takaró
rejti a legkevesebbet), a csomópont a **legnagyobb** sugarú burkon ül, és a
csempe oldalsó kiterjedését is levonjuk a szögből.

Hatókör: `node.Level < staticBaseLevel`. A megtakarítás úgyis a leszállásból
jön (a kivágott csomópont gyerekei létre sem jönnek), a base-szintű
csomópontok pedig változatlanul a régi úton értékelődnek ki. Mérve
ugyanannyit hoz, mint a `<=` (75,5% vs 75,6%), kisebb hatósugárral.

## A vágás NEM bitre azonos — és ezt meg is mértem

54 próbanézet (távolság × tengely × dőlésszög):
- **48 bitre azonos**,
- 6 eltér — ezek telített budgetnél tolják el a best-first határát, mert a
  frontierről eltűnnek a láthatatlan csomópontok.

Ezért nem halmaz-azonosságot védek, hanem azt, ami számít. **54 nézet ×
21×11 képernyő-minta, a gömbre visszametszve:**

| | érték |
|---|---|
| finomított látható minta, vágás ELŐTT | 6621 / 9561 |
| finomított látható minta, vágás UTÁN | **6621 / 9561** |
| nézet, ahol romlott | **0** |

Nézetenként is pontosan ugyanannyi. A megtakarítás tehát tisztán a
láthatatlan munkából jön.

## Eredmény

| | előtte | utána |
|---|---|---|
| metrika-kiértékelés (D=103/110/120) | 180 564 / 199 040 / 204 976 | **26 944 / 27 288 / 28 269** |
| 54 nézet összesen | 12 524 060 | 3 065 600 (**−75,5%**) |
| a `HorizonCullTests` futásideje | 7 s | 1 s |

Ez közvetlenül a hideg metrika-cache költségét csökkenti, ami a mért
selection-idő 48%-a volt.

## Tesztek: `HorizonCullTests` (12 eset)

Szándékos felosztás:
- **invariánsok** (a vágás ELŐTT és UTÁN is zöldek):
  `VisibleCoverageIsNoWorseThanBeforeCulling` (5 nézet, a várt értékek a
  vágás előtti kódból MÉRTEK), `NadirStaysRefined`, `CullingIsDeterministic`;
- **regressziós őr** (a vágás előtti kódon BUKIK):
  `CullingRemovesMostOfTheDescentWork` — ha a kiértékelésszám visszakúszik,
  a horizont-szűrés megint kiesett a produkciós ágból.

Ezt a felosztást ellenőriztem: a régi kódon 3 bukás / 9 siker, az újon
12 siker.

Első nekifutásra a fedettség-tesztet 100%-os elvárással írtam meg —
**a régi kóddal is megbukott** (20 000 levél nem is tudja az egész képernyőt
8 px-re finomítani, a periféria alapszinten marad). Az abszolút elvárást
ezért a mért alapértékekre cseréltem.

## Egy meglévő teszt elvárása módosult

`TerrainDecisionDiagnosticsTests.OutsideViewRecordsCulledAncestorWithNoInventedThreshold`
→ `CulledAncestorIsRecordedWithNoInventedThreshold`. A vizsgált csempe a
+Y lap közepe, a kamera (120,0,0)-ban: az ősök `C·P` értéke 37..2298 a
10000-es horizont-küszöbhöz képest, tehát nagyságrenddel a bolygó túloldalán
vannak. A trace eddig `outside-view`-t írt, mert horizont-vizsgálat nem
futott; most `horizon`-t ír. A teszt LÉNYEGE (a kivágott ős kap bejegyzést,
és nem találunk ki hozzá küszöböt) változatlan — a diagnosztika pontosabb lett.

## Ellenőrzés

- LOD-tesztcsomag: **434/434 zöld**, 16 s.
- Unity `Assembly-CSharp`: 180 hiba, mind a 8 ismert, elfogadott osztályban;
  **nulla** a módosított fájlokban.

## Nyitva maradt

A hideg metrika-cache (`SameProjection` bitpontos egyezést vár, így a kamera
bármely mozdulata ürít). Tűrésalapú újrahasználat a vágást a kamera ÚTJÁTÓL
tenné függővé, ami sérti a `SelectCutPrioritized` dokumentált garanciáját —
ND-döntést igényel, nem csendes változtatást.
