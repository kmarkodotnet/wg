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

## Egy nyitott modellezési kérdés, kimondva

A csapadék-mező EGYETLEN `dayT` pillanatra készül, a csatolt szél viszont a
hőmodell állapotától függő, tick-szintű mennyiség. Az átálláskor el kell
dönteni, melyik időpont (vagy milyen átlag) a csapadék szél-bemenete. A
legvalószínűbb válasz az ÉVES adatúton belüli átlag (az ND-158 mintájára),
de ezt mérni kell. Ez a következő lépés, nem ezé.

## Becslés

A7 funkcionális súlyozással kb. **90%**. Erre a körre durván **1 munkaóra**
ment rá (a vártnál lényegesen kevesebb, mert nem kellett új numerika), a
maradék durván **3–6 munkaóra** — becslések, nincs valós idő-naplózás a
projektben.
