# A 2:1 kiegyensúlyozás felesleges szkennelése (ND-106, 2026-09-19)

## Honnan jött

A budget-újrahangolásnál feljegyeztem egy gyengeséget: az
`EnforceRestrictedBalance` költsége nem a tényleges munkával nő. Ez a plafont
(48 000) megkötő két ok egyike, és a plafon köti meg a legközelebbi zoom
részletességét — tehát közvetlenül a felhasználó eredeti panaszát érinti.

## Első mérésem hibás volt

Izolált balance-mérést csináltam a `BuildCut` visszaadott cutján. Az
eredmény `hozzaadott=0` volt minden budgetnél — csakhogy a `BuildCut`
**már kiegyensúlyozott** cutot ad vissza, azon természetesen nincs mit
tenni. A mérés semmit nem mondott.

Ezért számlálót tettem a dolgozó kódba (`BalanceIterations`,
`BalanceSplits`, `BalanceLeavesScanned` a `LodSelectionWork`-ön), és
onnantól látszott a szerkezet.

## A szerkezet (D=103, horizont-vágás után)

| budget | levelek | balance | körök | splitek | szkennelt levél |
|---|---|---|---|---|---|
| 20 000 | 19 998 | 31 ms | 1 | **0** | 19 998 |
| 39 807 | 39 807 | 50 ms | 1 | **0** | 39 807 |
| 48 000 | 48 000 | 62 ms | 1 | **0** | 48 000 |
| 64 000 | 63 999 | 78 ms | 1 | **0** | 63 999 |
| 96 000 | 66 900 | 332 ms | 4 | 108 | 267 258 |

Két különböző jelenség:

1. **Ahol a budget megköt** (`cut.Count == budget`): a ciklus 1 kört fut,
   **nulla felosztással**, mert a `strictBudget` őr (`cut.Count + 3 >
   maxLeafCount`) minden split előtt blokkol. De ezért **végigszkenneli a
   teljes cutot** (|cut| × 4 szomszéd): 31-78 ms tiszta veszteség.
2. **Ahol a metrika a budget ALATT telítődik** (96 000-es budget, 66 900-as
   cut): van fejtér, tehát valódi munka történik — de 108 felosztás **332
   ms**, azaz ~3 ms felosztásonként, mert minden kör újraszkennel mindent.

## A javítás: korai kilépés (az 1. jelenségre)

```csharp
if (strictBudget && cut.Count + 3 > maxLeafCount) return;
```

Ha a feltétel belépéskor igaz, akkor **örökre** igaz marad: a `SplitOnce`
egyet kivesz és négyet betesz, tehát a `cut.Count` csak NŐ. A ciklus
ugyanezt a feltételt minden felosztás előtt ellenőrzi, vagyis a
végigszkennelésnek nincs kimenete, csak költsége.

**Ez a produkciós eset**: telített vágásnál `cut.Count == budget`, és a
felhasználó 2026-09-18-i naplójában **68 vágásból 44** volt ilyen.

## Ellenőrzés

- **180 próbaeset** (6 távolság × 2 tengely × 3 dőlésszög × 5 budget), a
  javítással és nélküle: **0 megváltozott cut**, a balance összideje
  **9068 ms → 1832 ms (−80%)**.
- Budget-megkötéses esetben a balance 31-78 ms-ról **0,0-0,5 ms**-ra esett.
- Négy új teszt a `BalanceEquivalenceTests`-ben, a meglévő
  referencia-implementáció mellé: a blokkolt eset no-op és nem is szkennel
  (0 kör, 0 szkennelt levél), fejtérrel viszont változatlanul lefut és
  oszt, és a blokkolt eredmény egyezik a referenciával.
- LOD-tesztcsomag: **443/443 zöld**. Unity: 180 hiba, a 8 ismert osztályban.
- A számlálók a naplóba is kikerülnek (`balanceIterations`, `balanceSplits`).

## Amit MEGTANULTAM a plafonról — és ezért NEM emeltem

Kézenfekvő lett volna azt gondolni, hogy a balance megolcsóbbodásával a
48 000-es plafon emelhető. A mérés az ellenkezőjét mondja: a balance csak
akkor dolgozik, ha a budget a metrika telítődése FÖLÖTT van. Nagyobb
plafon tehát **nagyobb eséllyel** visz a drága tartományba, nem kisebbel.
A plafon marad, az indoklása a doksiban pontosítva.

## Nyitva maradt (a 2. jelenség)

A fixpont-ciklus minden körben a TELJES cutot újraszkenneli, holott egy
felosztás után csak az új gyerekek és a felosztott csempe szomszédai
válhatnak kiegyensúlyozatlanná. Munkalistás átírás a 267 258 szkennelt
levelet ~108 × kis konstansra vinné.

**Miért nem csináltam meg most**: a jelenlegi algoritmus körönként
gyűjti és `TileId.Value` szerint RENDEZVE hajtja végre a felosztásokat, és
a budget-őrök mid-iterációban is blokkolhatnak — tehát a kimenet
sorrend-függő. Egy munkalistás változat más sorrendben osztana, ami a
korlátok bekötésekor MÁS cutot adhat. Ez nem "óvatosság", hanem ugyanaz a
hiba, amit ma már kétszer elkövettem és méréssel kaptam el (az
érintősíkos horizont-teszt és a 100%-os fedettség-elvárás). Egy ilyen
átírás előtt előbb azt kell tisztázni, hogy a fixpont egyértelmű-e a
korlátok nélkül, és hogyan viselkedik a korlátok mellett.
