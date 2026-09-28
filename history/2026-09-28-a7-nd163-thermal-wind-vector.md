# A7 7. fázis — a csapadék szél-tagja: a vektor már megvolt, csak eldobtuk

**Dátum:** 2026-09-28. **Ág:** `a19-plate-frame-noise`. **ND:** ND-163.
**Állapot:** a Core adatút KÉSZ; a fogyasztói kapu SZÁNDÉKOSAN zárva.

## A feladat

Az A7 7. fázisából ez maradt: a `MoisturePrecipitation` még mindig a régi,
analitikus `WindPrecipitation.WindVector`-ból veszi a szelet, tehát a
csapadék-mező nem látja az ND-142 csatolt szelét (a levegőanomália
visszahatását).

Előzetesen azt vártam, hogy ez új numerikus lépés lesz: a cellaközépponti
szélvektort a termikus ÉL-sebességekből kellene visszaállítani (legkisebb
négyzetes illesztés vagy hasonló), ami Python-orákulumot és új ND-t igényel.

## Amit a kód olvasása megmutatott

**Nincs itt új numerika.** A csatolt szél belső `Wind(...)` függvénye
(`ThermalWind.Feedback.cs`) MÁR kiszámolja a cellaközépponti teljes 3D
szélvektort (`wx, wy, wz`) és a kelet/észak komponenseket — a `SampleCoupled`
viszont a celláknál eldobja őket:

```csharp
Wind(..., out cellSpeed[c], out _, out _, out _);
```

és csak a NAGYSÁGOT tartja meg. A hiányzó mennyiség tehát már ott volt a
memóriában; csak kivezetni kellett.

Ezért ez a lépés **nem igényelt új Python-orákulumot**: a számolás bitre
ugyanaz, amit az ND-142 vektorai már lefednek.

## Megvalósítás (Core)

1. `ThermalWind.SampleCoupled` új túlírása, ami a cellaközépponti
   szélvektort is kitölti. A régi alak változatlanul delegál.
2. `SurfaceWindSample` értéktípus (érintősíkbeli 3D irány + sebesség).
   A sebesség KÜLÖN mező, nem az (X,Y,Z) hosszából számolódik: a hőmodell a
   sebességet a helyi kelet/észak komponensekből képzi, és ezt a két utat
   nem mostuk össze egy gyökvonással.
3. `MoisturePrecipitation.ComputeFromFields` — a hőmérséklet MELLETT a szél
   is kívülről jöhet. Mindkettő `null` esetén a régi út fut, bitre azonosan;
   hiányzó tile explicit hiba, nem csendes visszaesés.

## Ellenőrzések

- **Bitazonosság bizonyítva:** a vektoros túlírás után az él-sebességek és a
  cella-sebességek `Assert.Equal`-lal (tehát BITRE) azonosak a régi úttal.
- A vektor az érintősíkban fekszik (sugárirányú komponens < 1e-9) és a hossza
  pontosan a sebesség; a teljes sebesség az ND-142 szerint ≤ 40 m/s.
- Tisztaság: ismételt hívás bitre azonos, a bemenetet nem módosítja.
- Élesetek: a három vektortömböt együtt kell megadni, méret-eltérés hiba, a
  bemenet nem lehet egyben kimenet.
- A nedvesség-transzport: `null` mezőkkel bitre a régi eredmény; megadott
  széllel más csapadék (tehát tényleg olvassa); hiányzó tile kivétel;
  hőmérséklet és szél együtt is hat.
- Core **812/812** (803 + 9 új), LodChunking 662/662, solution 0 hiba /
  0 warning, ND-20 kapu OK, Unity 0 hiba.
- Nem seed-törő: csak új API-k; `WorldGeneratorVersion` marad `"5"`,
  hőmodell-verzió 3.

## Amiért a fogyasztói kapu ZÁRVA marad

A viewer nem áll át. Ugyanaz az indok, mint az ND-162 3. pontjában: a
csapadék-mező hat a FELHŐRE, a folyó-forrásokra ÉS a biome csapadék-
tengelyére is — három látható következmény egyszerre. A jég átállásának
vizuális átvétele még nem történt meg; két változást egy ítéletbe keverni
pontosan az a hiba, ami az ND-142 kalibrációs köréhez vezetett.

## A mintavétel kérdése — megmérve

A csapadék-mező egyetlen `dayT` pillanatra készül, a csatolt szél viszont
tick-szintű. Választott bemenet: az **éves adatúton belüli átlag** — a
mintanapok minden tickjén vett cellaközépponti vektor és sebesség átlaga.
Ez INGYEN jön: a `Step` amúgy is kiszámolja a szelet, csak eddig eldobtuk
(`CaptureCellWind`). A hőmérséklet-kimenetek bitazonossága tesztben
bizonyítva a szél bekapcsolása mellett is.

**Szél-állandóság (|átlagvektor| / átlagsebesség): 0,746** — tehát
átlagosan ~25% kioltás, mert a szélirány az év során forog. Valódi fizikai
tartalom (monszun-jelleg), ezért a vektorátlagot és a sebességátlagot
külön tartjuk: az irány a nedvesség kifolyásához, a sebesség a
párolgáshoz kell.

## A fogyasztói A/B — ez dönti el, miért marad zárva a kapu

`worldgen thermal-climate --precip true`, level 5, 2151 szárazföldi tile.
A mérőszám a NEGYED-besorolás változása, nem a nyers különbség: az ND-126
szerint a biome-ot a csapadék percentilisei döntik el, tehát egy egyenletes
skálázódás semmit nem változtatna a képen.

| változat | átlagos \|eltérés\| | negyed-besorolás egyezés |
|---|---:|---:|
| (1) csak a HŐMÉRSÉKLET a hőmodellből | 6,8% | **86,2%** |
| (2) + a SZÉL is | 42,7% | **67,7%** |
| (2) a (1)-hez képest (a szél önmagában) | 41,0% | 70,5% |

**A két átállás nem egyenrangú.** A hőmérséklet-csere mérsékelt; a
szél-csere a szárazföld csaknem harmadát átsorolja és átlagosan 41%-kal más
csapadékot ad — átrajzolná a biome-térképet, a felhőket és a
folyó-forrásokat is.

**Figyelmeztető szám:** a maximális eltérés **38,94**, miközben az átlag
1,06 — egyes tile-okon 37-szeres. Ott várható, ahol az éves átlagos szél
majdnem kioltódik: a nettó szállítás eltűnik, és a nedvesség helyben
halmozódik. Modellezési kérdés, nem implementációs hiba — de az átállás
előtt tisztázni kell.

**Következtetés:** a három fogyasztó átállítása HÁROM KÜLÖN ÍTÉLET — a jég
(kész és aktív), a biome/párolgás hőmérséklete (mérsékelt változás), és a
csapadék szele (nagy változás + nyitott modellkérdés).

## Az A7 ÁLLAPOTA ezzel

Az A7 **implementációs és mérési része lezárult.** Ami hátravan, az
kizárólag FELHASZNÁLÓI ÍTÉLET, nem kód:

1. A jég átállásának vizuális átvétele (ND-162 — kész, aktív, élőben
   igazolt).
2. A biome/párolgás hőmérséklet-cseréjének elfogadása (mérve: mérsékelt,
   86,2% negyed-egyezés).
3. A csapadék szél-cseréjének elfogadása (mérve: nagy, 67,7%), és a
   kioltódó éves szél modellkérdése.
4. ND-160 (bázis-albedó) — seed-törő kalibrációs döntés.

A Core adatút, a lemez-cache, a háttérszál, a mérőeszközök és a
fogyasztói felületek mindhárom átálláshoz készen állnak.

## Becslés

A7 funkcionális súlyozással kb. **92%** — a maradék 8% a három vizuális
átvétel és az ND-160 döntés, ami nem kód. Erre a körre durván **2 munkaóra**
ment rá; a maradék kód-jellegű munka (a három kapcsoló átfordítása és a
viewer-oldali bekötés) durván **2–4 munkaóra**, az ítéletek után —
becslések, nincs valós idő-naplózás a projektben.
