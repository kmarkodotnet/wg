# A24 — numerikus átvétel és az abszolút jégküszöb kontrollja

**Döntés:** ND-171. **Kiindulás:** `ca47f56` diagnosztikai checkpoint.
Az előző lépést az AGENTS.md szerinti külön helyi commit zárta; push nincs.

## Felhasználói visszajelzés

A futó alkalmazás jég/biome A/B-jére és Console-ellenőrzésére adott
visszajelzés: **„minden rendben, mehgetsz a következő lépésre”**.
Ez az ND-168/169 utáni működés általános vizuális elfogadása és a
parancssori ellenőrzések engedélyezése. Képernyőkép, részletes peremteszt
vagy új performance-mérés ebből nem következik.

## Valódi átadási hiba és javítása

A Release solution build 0 hibával és 0 figyelmeztetéssel fordult.
Az első teljes tesztkörben **9 Core-teszt bukott**: az ND-168/169 Python
generátorai és `tools/reference` vektorai már az új modellhez tartoztak,
de a `tests/WorldGen.Core.Tests/testdata` négy hővektor-másolata még régi
volt. Az újragenerált adatokat másolással szinkronizáltuk, kézi
vektorszerkesztés nélkül. A szimuláció kódja és verziója nem változott.

A meridionális kétcellás KAT és geometriavektor újragenerálása mostantól
külön CI-lépés; a verziózott kimenet eltérése hibát okoz. A meglévő
platformmátrix `fail-fast: false` beállítása megmaradt.

**Automata ellenőrzés:** Debug és Release solution build 0 hiba / 0
figyelmeztetés. Mindkét konfigurációban **1976/1976** teszt zöld:
836 Core, 664 viewer-LOD, 24 CLI és 452 app-foundation. Release-ben a
három változatlan assembly az első solution-futásban ment át, a Core a
vektorjavítás utáni ismétlésben; Debugban a teljes solution futott újra.
A KAT 9/9, a kétcellás mérleg `[-25; +25] K`; a random vektorok
újragenerálása byte-egyező. Ezek helyi Windows-futások, nem távoli CI.

Az éves, checkpoint-, visszacsatolt szél- és teljes level-6 hőmezővektor
újragenerálása is **byte-ra egyezik** a szinkronizált C# tesztadatokkal;
a `tools/reference` korábban generált fájljai változatlanok. A meridionális
geometriavektor újragenerálása szintén változatlan. A teljes hőmező
1060 tickes kanonikus futását egy szerver-újraindulás megszakította;
újraindítottuk, és a második futás 0 kilépési kóddal, sikeres
byte-összevetéssel zárult. A korábbi C# teszteket nem kellett ismételni,
mert a végleges referenciafájl pontosan ugyanaz maradt.

## Fizikai mérés

Mindegyik futás: 20 lemez, t=0, 12 mintanap, `--parallel true`, az új
generátor 7 / hőmodell 5. `thermal-climate --decompose true`.

| Seed vége / szint | β / jégszabály | Éves Ts minimum | Globális Ta területi átlag | Tartós jég | Max mérlegmaradék W/m² |
|---|---|---:|---:|---:|---:|
| `A7C944210000` / L5 | 0,5 / q=0,07 | −11,7 °C | 9,049 °C | 430 | 1,418e-9 |
| `A7C944210000` / L6 | 0,5 / q=0,07 | −11,9 °C | 9,019 °C | 1720 | 0,0038911 |
| `A7C944210001` / L5 | 0,5 / q=0,07 | −8,2 °C | 13,111 °C | 430 | 1,794e-9 |
| `A7C944210002` / L5 | 0,5 / q=0,07 | −12,4 °C | 8,921 °C | 430 | 1,200e-9 |
| `A7C944210000` / L5 | 0,5 / −15 °C | −11,8 °C | 9,048 °C | **0** | 1,418e-9 |
| `A7C944210000` / L5 | 0,3 / −15 °C | −15,4 °C | 7,825 °C | 5 | 1,463e-9 |
| `A7C944210000` / L5 | 0,2 / −15 °C | −18,3 °C | 6,931 °C | 140 | 1,443e-9 |

Mindhárom alapbeállítású seeden **0%** a fagypont feletti éves átlagú
tartós jég bolygóterülete. A korábbi pozitív globális hőforrás megszűnt:
a belső teljesítmény területre osztott globális összege minden futásban
`4e-14 W/m²` alatt maradt. Az L6 maradéka nem gépi nulla, de a kitűzött
`0,01 W/m²` kapun belüli. A 256 lépés magasabb szintekre ebből nem igazolt.

Kanonikus seed sarki (60–90°) Ta átlaga L5 észak/dél **−6,027 / −6,014 °C**,
L6 **−6,068 / −6,032 °C**. Az L5→L6 globális Ta eltérés 0,030 K.
Ez két rácson mért stabilitás, nem teljes konvergenciarend-vizsgálat.

β=0,5 mellett a pillanatnyi Ts L5 **[−40,2; 41,6] °C**, L6
**[−40,9; 41,9] °C**. Az abszolút jégkontrollban β=0,3-nál
**[−61,2; 57,6] °C**, β=0,2-nél **[−76,1; 66,7] °C**.
A kisebb β egyszerre változtatja a napi szélsőségeket és az éves átlagot;
pusztán a jégdarabszám miatt nem választunk új alapértéket.

A 9 °C-os átlag seedfüggő klímaciklussal (−2,05 K) készül; a második
seeden a ciklus +2,07 K. A medián nem globális területi átlag, és a világ
nem a Föld topográfiája. Egy fix 15 °C-os célra hangolás nem indokolt.

## Következtetés és folytatás

Az ND-168 diszkrét energiamérlege numerikusan átvehető a mért L5/L6-on.
A hideg tartomány visszatért, és az új hőút általános képi ellenőrzését a
felhasználó elfogadta. **A24(c) továbbra is nyitott:** a −15 °C-os régi
küszöb visszaállítása kiürítené a tartós jeget. Nem vezettünk be új,
pusztán a kívánt darabszámhoz illesztett abszolút küszöböt.

Az ND-171 külön kezeli a szárazföldi hó/jég éves mérlegét és a tengeri
fagyást. A [NSIDC](https://nsidc.org/learn/parts-cryosphere/glaciers/science-glaciers)
szerint a jég fennmaradása a felhalmozódás és veszteség mérlegétől függ;
ennek fizikai megvalósításához a mostani MVP-csapadékskála egységeit és az
évszakos hőmintavételt is rendezni kell. A percentilis átmeneti korlátja
(minden világon rögzített jégarány) nem szűnt meg.

## Reprodukálás

```powershell
dotnet build WorldGen.sln --no-restore -c Release
dotnet test WorldGen.sln --no-build -c Release
dotnet build WorldGen.sln --no-restore -c Debug
dotnet test WorldGen.sln --no-build -c Debug
dotnet artifacts/bin/WorldGen.Cli/Release/net8.0/worldgen.dll thermal-climate --seed A7C944210000 --plates 20 --level 5 --parallel true --decompose true --biome true
```

Változatok: `--level 6`; a fenti két további seed; külön
`--ice-percentile absolute`; majd `--beta 0.3,0.2 --ice-percentile absolute`.
A logok helyben az `artifacts/a24-*.txt` fájlokban vannak. A mérések
részben párhuzamosan futottak, ezért a 16–244 s futásidők **nem kontrollált
sebesség-összehasonlítások** és nem Unity-teljesítményadatok.

## Haladás

M5/A24 részfeladat: **kb. 70%**, tartalmi becslés. Az albedó, az
okfeltárás, a konzervatív transzport és az első numerikus/vizuális átvétel
megvan; a szezonális fizika és a percentilis jég kiváltása maradt nyitott.
A mostani ellenőrzés/javítás durván **2–4 munkaóra** ráfordítás;
a következő hó/jégmodell tervezése és mérési előkészítése **4–8 munkaóra**.
Nem időnapló, és nem a teljes fizikai jégmodell megvalósítási ígérete.
