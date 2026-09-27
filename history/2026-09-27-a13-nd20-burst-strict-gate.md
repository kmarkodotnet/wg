# A13 / ND-20 — Burst `FloatMode.Strict` CI-kapu

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**ND:** ND-20 (lezárva: CI-szkript)
**Érintett fájlok:** `tools/ci/check_burst_strict.py` (új),
`.github/workflows/ci.yml`, `docs/04-decisions.md`,
`docs/03-unity-hdrp-evaluation.md`, `CLAUDE.md`, `unity/README.md`, `todo2.md`

## Miért most, amikor nulla a találat

A repóban továbbra sincs egyetlen `[BurstCompile]` sem (301 átvizsgált `.cs`) —
az ND-39 „A" opciója (CPU Job System + Burst) még nem indult el. A tétel tehát
ma tárgytalan, és pontosan ezért készült el most: ha a kapu a használat **után**
kerül be, akkor a bevezető commit már zöld CI-vel ment át, és a hiányzó
attribútum-paramétert csak egy platformok közötti hash-eltérés fogja
felszínre hozni — ez a projektben a legdrágább hibakeresési fajta. A kapu
így a bevezető commit-tal **együtt** vált pirosra.

## A döntés: CI-szkript, nem Roslyn analyzer

Az ND-20 eredeti javaslata „CI-szkript vagy Roslyn analyzer" volt. A szkript
mellett döntött egy konkrét, mérhető ok: az analyzert csak a `dotnet build`
futtatja, a Unity-oldali `Assets/` fordítását pedig a CI-gépeken nincs mivel
elvégezni (nincs Unity — ugyanaz az ok, amiért a `tests/WorldGen.*.Compile`
kapuk nincsenek a `WorldGen.sln`-ben). A szöveges scanner egyetlen futással
látja a `src/`, `tests/`, `tools/` és `unity/WorldGenViewer/Assets/` alatti
**összes** `.cs`-t, függetlenül attól, melyik assembly-be fordulna.

## A kikényszerített szabály

1. Minden `[BurstCompile]` attribútum-használat explicit
   `FloatMode = FloatMode.Strict`-et kap. A `Default`, `Fast` és
   `Deterministic` mind sértés — a `Deterministic` is, mert a Burst-ben ma
   nem garantált szemantika.
2. A `FloatPrecision` nem lehet `Low` vagy `Medium`. Ezek nem az
   átrendezést, hanem a matematikai függvények approximációját engedélyezik,
   tehát ugyanabba az I1-osztályba tartoznak. A `Standard` és a `High` átmegy.
   Ez a pont túlmegy az ND-20 eredeti szövegén — ezért **le van írva** az
   ND-ben is, nem csendben került be.

## Hogy ne legyen „vakon zöld" kapu

Egy kapu, ami üres halmazon fut és mindig zöld, semmit nem bizonyít. Ezért a
szkriptnek van `--self-test` módja 28 fixture-tal (13 átengedendő, 14
elutasítandó, 1 sorszám-ellenőrzés), és a CI **mindkettőt** futtatja:

```
- name: A kapu onellenorzese (fixture-ok)
  run: python tools/ci/check_burst_strict.py --self-test
- name: A repo atvizsgalasa
  run: python tools/ci/check_burst_strict.py --verbose
```

A fixture-ok a valós írásmódokat járják végig: kvalifikált névtér
(`Unity.Burst.BurstCompile`), `Attribute` utótag, assembly-szintű cél
(`[assembly: BurstCompile]`), több soros paraméterlista, összevont
attribútum-lista (`[StructLayout(...), BurstCompile]`), valamint a
*nem*-találatok: komment-, blokk-komment-, sztring- és verbatim-sztring-beli
említés, illetve `typeof(BurstCompileAttribute)`.

## A self-test azonnal fogott egy valódi hibát

Az első futáskor az „összevont listában, strict nélkül" fixture **átment**,
holott sértés. Ok: az attribútum-kontextus felismerése visszafelé olvas a
`[`-ig, és megállt az előző attribútum záró zárójelén
(`[StructLayout(LayoutKind.Sequential), BurstCompile]`). Javítva: a
visszafelé-olvasás átlépi a kiegyensúlyozott zárójel-párt. Ez a bug pont az,
amit a kapu megírása nélkül soha nem vettünk volna észre — és amit egy üres
halmazon futó, self-test nélküli kapu csendben magával hordott volna.

## Verifikáció

```
$ python tools/ci/check_burst_strict.py --self-test
Burst-kapu self-test: OK (28 eset - 13 atengedendo, 14 elutasitando, 1 sorszam)   # exit 0

$ python tools/ci/check_burst_strict.py
ND-20 kapu: OK - 301 C# fajl atvizsgalva, egyetlen [BurstCompile] sincs
(a kapu elore keszult, ld. ND-20).                                                # exit 0
```

Végponttól végpontig (nem csak memóriában lévő fixture-ral): egy eldobható
`src/WorldGen.Core/__BurstGateProbe.cs` három job-struct-tal — csupasz
`[BurstCompile]`, `Strict` + `FloatPrecision.Low`, és egy helyes `Strict`:

```
ND-20 SERTES: 2 hibas [BurstCompile] attributum
  src/WorldGen.Core/__BurstGateProbe.cs:5: [BurstCompile] parameter nelkul - FloatMode = FloatMode.Strict kotelezo (ND-20)
  src/WorldGen.Core/__BurstGateProbe.cs:8: [BurstCompile] FloatPrecision.Low approximaciot engedelyez - csak Standard vagy High (ND-20)
                                                                                  # exit 1
```

A helyes harmadik struct nem adott találatot, a próbafájl törlése után a kapu
újra zöld, `git status` tiszta.

## Ami tudatosan kimaradt

A scanner szöveges, nem szemantikus: saját, `BurstCompile` nevű attribútum
vagy `using`-alias megtévesztheti. Ez elfogadott csere — a hamis pozitív
zajos, de biztonságos irányba téved. A `Unity.Mathematics` `float`-alapú
API-ja (`math.sin` stb.) továbbra is az ND-23 hatálya alá tartozik, nem ezé a
kapué.

## Nincs seed-törés

Nulla produkciós kód változott — a commit egy CI-kapu, egy workflow-job és
dokumentáció. `WorldGeneratorVersion.Current` érintetlen.
