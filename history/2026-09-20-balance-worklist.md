# 2026-09-20 — A balance fixpont-ciklus munkalistás átírása (ND-121)

## Az előfeltétel, amit magamnak írtam elő

A todo.md 3. sora kikötötte: „előbb tisztázni, hogy a fixpont egyértelmű-e a
korlátok nélkül — a jelenlegi kimenet sorrend-függő". Ez jogos óvatosság volt:
egy munkalistás változat MÁS sorrendben találja meg a felosztásokat, tehát ha a
fixpont sorrend-függő lenne, az átírás csendben más vágást adna.

**Az eredmény: korlátok nélkül a fixpont EGYÉRTELMŰ.** Öt gyökeresen eltérő
végrehajtási sorrend — köztük egy teljesen aszinkron, egyesével véletlenül
választó kereső — azonos vágást **és azonos lépésszámot** ad, és megegyezik a
termelési, körökben dolgozó implementációval.

Az ok szerkezeti, külön tesztben rögzítve: a felosztás **monoton**. Egy tile
felosztása soha nem szüntet meg másik, fennálló szintkülönbség-sértést, mert a
fedő ős csak FINOMABB lehet, a különbség tehát csak csökken. A „mit kell
felosztani" halmaz lezárása így sorrendtől független legkisebb fixpont.

**A sorrend-függőség forrása nem a fixpont, hanem a korlát.** Szorító budget
mellett a ciklus félbeszakad, és onnantól számít, melyik felosztások fértek be
— ezt külön teszt rögzíti. Az átírás ezért nem hivatkozhatott pusztán az
egyértelműségre: a kör-szemantikát is meg kellett őriznie.

## Az átírás

Az első kör változatlanul végigjárja a teljes vágást. A többi kör csak a
*frontier*-t: az előző körben keletkezett gyerekeket **és a sértést kiváltó
leveleket**.

**A második fél nélkülözhetetlen — és az első változatomból kimaradt.** A
sértés MINDIG a finom oldalról látszik (`leaf` a finom, `covering` a durva
szomszéd-fedő ős). Egy felosztás viszont csak EGY szintet javít: egy level 9-es
levél és egy level 3-as ős között a felosztás után is 5 a különbség. A durva
oldalról ez nem vehető észre, mert az új gyerek szomszédjának nincs fedő őse a
vágásban (a szomszéd-terület finomabb), tehát a keresés `false`-t ad.
**Hat egyenértékűségi teszt bukott el rá**, mielőtt kijavítottam — pontosan
azért, mert a meglévő tesztek differenciálisan mérnek a régi implementációhoz.

## Igazolás

- **600 véletlen konfiguráción** (40 seed × 3 mélység × 5 budget, a szorító
  eseteket is beleértve) a kimenet **minden esetben azonos** a régi, teljes
  szkennelésű referenciával — 3,5× kevesebb bejárt levél mellett.
- **Nagy kaszkádon**: 4032 → 14 070 levél, **11 kör mindkettőnél** (a
  kör-szemantika tehát tényleg megmaradt), bejárt levelek 129 381 → 25 683
  (5,0×), idő 53,0 ms → 12,4 ms (4,3×).
- LodChunking 455 teszt zöld (447 → +8), offline viewer-kapu 0 hiba.

**A költség-modell:** `körök × O(|cut|)` helyett `1 × O(|cut|) + O(felosztások)`.

## A plafon — amit NEM döntöttem el

A `MaximumRenderBudget` doksija a 48 000-et két okkal indokolta, és a
másodikat kifejezetten feltételhez kötötte: „Amíg a ciklus teljes
újraszkennelés helyett nem munkalistával dolgozik, a 48 000 marad." A feltétel
most teljesült.

**De az 1. ok (költség-paritás) változatlanul érvényes**, és az emelés
UX-kompromisszum, nem technikai kérdés — ezért ND-121-ben vár döntésre.

Amit közben találtam, és a #1 panaszhoz közvetlenül tartozik: **a plafon ma
köt.** A `RenderBudgetForViewport` 1920×1080-on 8 px-es céllal **97 200**
levelet kérne, és 48 000-re vágja — **2,0×**; 1440p-n 3,6×; 4K-n 8,1×.

**Amit NEM tudtam megmérni, és ezt jeleztem is:** a doksi konkrét esetét
(96 000-es budget → 4 kör / 108 felosztás / 320 ms) offline nem lehet
reprodukálni, mert a Unity-oldali terep-proxit és felszíni metrikát igényli;
az én offline nézetemben a vágás pont a budgeten telítődik, így az ND-116
korai kilépés lép életbe és a balance 0 kört fut. Az új modellből **becsült**
80–110 ms — ez becslés, a PerfLog tudja megerősíteni.

## Tanulság

**Az „előbb tisztázd az előfeltételt" szabály megtérült — de nem úgy, ahogy
vártam.** Az előfeltétel-vizsgálat zöld lett (a fixpont egyértelmű), és ha
megállok ott, azt hittem volna, hogy a sorrend szabadon változtatható. A
`WithATightBudgetTheOutcomeDoesDependOnOrder` teszt viszont megmutatta, hogy
az egyértelműség CSAK korlátok nélkül áll — és épp ez kényszerített rá, hogy
a kör-szemantikát megőrizzem, ahelyett hogy egy „igazi" munkalistát írnék.
Az óvatosság nem a kérdésre adott válaszból jött, hanem abból, hogy a kérdést
két külön esetre bontottam.

**A második tanulság az, hogy a differenciális teszt fogta meg a hibámat, nem
a gondolkodás.** A frontier hiányzó felét logikai érveléssel „bizonyítottam"
magamnak, és tévedtem. A régi implementációt orákulumként megtartó, sok
konfiguráción futó összehasonlítás azonnal kibukott.
