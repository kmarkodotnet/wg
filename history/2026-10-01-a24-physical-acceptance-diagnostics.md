# A24 — az új hőmodell fizikai átvételének előkészítése

**Utóállapot:** a felhasználó engedélyezte a futtatást és az aktuális
alkalmazást rendben lévőnek jelezte. Az alábbi függőben lévő kérés az
előkészítés történeti állapota; a tényleges mérés és javítás a
[következő naplóban](2026-10-01-a24-physical-validation.md) szerepel.

**Ág:** `a19-plate-frame-noise`. **Döntés:** ND-170.

## Kiinduló állapot

A munkafában az A7 ND-168/169 implementációja már elkészült, de a korábbi
felhasználói kérés szerint teszt, KAT és élő Unity-átvétel nélkül adták át.
Az AGENTS.md feladatváltási szabálya szerint ezt külön helyi checkpointba
mentettük: `e3d9049`. Push nincs. A commit nem jelent igazolt fizikai
helyességet. A todo2 A24 sora még a transzport elkészítése előtti állapotot
írta le; most a sor aktuális összefoglalót kapott.

## Változás

- `ThermalBaseline.AnnualTargetK`: a már kiszámított éves célmező csak
  olvasható nézete. Nincs új numerika vagy verzióemelés.
- A `thermal-climate --decompose true` a tényleges célmezőn ellenőrzi a
  `λδ − P(T₀+δ)/A` mérleg maximum abszolút maradékát (W/m²), a belső
  teljesítmény globális összegét és az átlagkorrekciót. A korrekció puszta
  nullátlaga kevés, mert a solver utólag központosítja azt.
- Minden klímariport külön közli a felszíni és levegőhő területi átlagát,
  a két félteke 60–90°-os levegőátlagát és a fagypont feletti éves
  felszíni átlagú tartós-jég arányát a teljes bolygóterülethez képest.
- A régi analitikus jégdarabszámot a CLI már történeti összehasonlításnak
  nevezi. Egy rangértéket sem nevez pontosan ugyanannyi jeget visszaadó
  küszöbnek: a szigorú összehasonlítás és a holtverseny ezt nem garantálja.
- Level 5/6 regressziós eset készült a solverhez sima sarki célmezővel és
  lokális töréssel. A 0,01 W/m² maradékkapu λ-val osztva 0,005 K alatti
  hibának felel meg; ez numerikus kapu, nem földi klímakalibráció.

A transzport forrásellenőrzése: a [climlab dokumentációja](https://climlab.readthedocs.io/en/latest/api/climlab.dynamics.MeridionalHeatDiffusion.html)
valóban 0,555 W/(m² K) alapértéket közöl, de szélességi irányú diffúzióra.
Ez önmagában nem bizonyítja a saját cubed-sphere diszkretizáció
konzisztenciáját vagy a generált bolygó földszerű klímáját.

## Következő mérési menet

Az aktuális munkamenetben külön kérdés tisztázza, hogy a korábbi kézi
ellenőrzési kérés helyett futtathatjuk-e a kapukat. Válasz még nem érkezett;
build, teszt, klímamérés és Unity-menet ebben a körben nem futott.
A `git diff --check` hibamentes; ez kizárólag diff-formázási ellenőrzés.

1. Solution build, három alap tesztprojekt, referencia KAT és az ND-168
   KAT; a generált vektorok byte-összevetése a checkpoint tartalmával.
2. Kanonikus seed `A7C944210000`, 20 lemez, level 5 és 6, 12 mintanap,
   `--decompose true --biome true`. A solver-maradék és a globális
   energiamérleg ellenőrzése előzze meg a paraméterválasztást.
3. Ugyanezen seed level 5-ös külön futása `--ice-percentile absolute`
   mellett. Ez a meglévő −15 °C-os évesátlag-proxy, nem új, igazolt
   jégtömegmérleg. A teljes kétmenetes modell fusson, ne csak a végleges
   percentilis mezőt osztályozzuk át.
4. Két további seed ugyanilyen kontrollja és a β=0,3/0,2 diagnosztika
   csak a szükséges fizikai kérdés eldöntésére. Az alapérték marad β=0,5.
5. Az eredmények után döntés az A24(c) küszöbéről, a megmaradt szezonális
   bázisproblémáról és az ND-165 analitikus útról. Numerikus változáskor
   referencia → vektor → C# és új verzió szükséges.
6. Élő Unity-kép és felhasználói átvétel. A CLI nem helyettesíti ezt.

## Becslés

A24 kb. **45%**: okfeltárás és új transzport-implementáció megvan,
a fizikai ellenőrzés, a küszöbváltás és a vizuális átvétel hiányzik.
Ez részfeladatbecslés, nem teljesprojektes audit. A mostani kiegészítés
durván **1–2 munkaóra**, a következő mérési/döntési kör **4–8 munkaóra**;
nem mért idő, és egy új fizikai modell szükségessége növelheti.
