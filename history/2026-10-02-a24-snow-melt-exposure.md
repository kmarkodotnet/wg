# A24 — évszakos olvadási kitettség (ND-172)

## Kiindulás és változás

Az előző lezárt ellenőrzési kör külön commitja `0cd51ef`.
A felhasználó a folytatást kérte. A −15 °C-os éves küszöb sikertelen
kontrollja után a szárazföldi hómegmaradás hőmérsékleti oldalát mértük.

- `TemperatureIndexSnow`: pozitív foknap és explicit hótároló;
  havazás a lépés elején, készletkorlátos olvadás, m vízegyenérték.
- `ThermalMeltExposure`: a tényleges levegő-hőmérséklet 96 tick-eleji
  mintája naponta; az olvadási integrál évesítése és a legmelegebb napi átlag.
- CLI `--melt-exposure-days 12,24,48`: ugyanazon B-menet típusmezőn és
  ugyanazon végleges szárazföldi jégmaszkon mér, így a mintavétel hatása
  nem keveredik az újrabesoroláséval.
- Python skalár KAT, 96 generált vektor és a kis éves hővilág független
  Python integrálja. Mindkét vektorkészlet CI-ben újragenerálódik és
  byte-szinten összevetődik a C# tesztadatokkal.

Forrás: [PISM felszíni modell dokumentáció](https://www.pism.io/docs/climate_forcing/surface.html).
A viszonyítás 3 mm folyékony vízegyenérték / pozitív foknap hóra.
Ez empirikus potenciál; a kernel nem tartalmaz visszafagyást vagy
csupaszjég-olvadást. A meglévő csapadékproxyhoz nem rendeltünk fizikai egységet.

## Mérés

```text
dotnet artifacts/bin/WorldGen.Cli/Release/net8.0/worldgen.dll thermal-climate --seed A7C944210000 --plates 20 --level 5 --days 12 --melt-exposure-days 12,24,48 --parallel true
```

Alap β=0,5, t=0, 365,25 napos év, 23,44° dőlés. A teljes 430 tartós
jégcellából 267 szárazföldi. Nyers helyi log: `artifacts/a24-melt-exposure-l5.txt`.

| Mintanap | PDD min / területi átlag / max (K·nap/év) | Hóolvadási potenciál min / területi átlag / max (m vízegyenérték/év) |
|---:|---:|---:|
| 12 | 2301,232 / 3353,651 / 3906,935 | 6,9037 / 10,0610 / 11,7208 |
| 24 | 2286,893 / 3313,476 / 3851,879 | 6,8607 / 9,9404 / 11,5556 |
| 48 | 2283,543 / 3305,005 / 3824,892 | 6,8506 / 9,9150 / 11,4747 |

Mindhárom sűrűségnél **0 darab olvadásmentes cella**, a legmelegebb
mintanap cellánkénti levegőátlagának tartománya **+21,82…+39,85 °C**.
A 24→48 nap változás a területi átlagban −0,256%; a 12→48 változás −1,451%.
Ez az aggregált mutató stabilitása egy seeden és egy rácsszinten,
nem cellánkénti konvergencia vagy teljes éves integrálra vonatkozó hibabecslés.
Futás 171,6 s, más tesztekkel részben párhuzamosan; nem teljesítménybenchmark.

## Következtetés és nyitott munka

Az éves átlag alapján hideg, percentilissel kijelölt szárazföldi jég
erős nyári olvadásnak van kitéve. A nagy potenciál nem tűnik el a
mintanapok sűrítésével. Az éves negatív hőátlag tehát nem elegendő
fizikai átvételi feltétel. A potenciál **nem tényleges veszteség vagy
szükséges havazás**: az időzítés, a rendelkezésre álló hó, visszafagyás és
a felszíni albedó-válasz további feltételek. A kernel önmagában nem
zárja le a tartós jég kérdését.

Következő modellmunka: a nyári sarki hőmérsékletek és a szezonális
energiamérleg felbontása, fizikai egységű hó-utánpótlás terve, majd külön
szárazföldi és tengeri jégszabály. Ezt explicit döntés/verzióváltás előzze
meg. Az aktív viewer jégmaszkja és az elfogadott kép nem változott;
új Unity-vizuális ellenőrzést ebben a körben nem végeztünk.

## Ellenőrzés

- Release solution build: 0 hiba, 0 figyelmeztetés.
- Teljes Release suite az első 10 új teszttel: **1986/1986**
  (Core 846, viewer-LOD 664, CLI 24, App 452).
- Az utólag hozzáadott Python hőintegrál-vektortesztet is tartalmazó
  célzott kör: **11/11 Debug és Release**. Összesen 1987 külön teszt
  ellenőrizve Release-ben; nem állítunk új teljes Debug-futtatást.
- Mindkét új Python vektorkészlet második generálása byte-egyező.
- KAT: +2 °C × 3 nap = 6 K·nap, 18 mm potenciális hóolvadás;
  vízmérleg, készletkorlát, paraméterhatás, párhuzamos ismétlés,
  hibás bemenet és túlcsordulás ellenőrizve.

Tartalmilag súlyozott, durva becslés: **A24 ~75%**. Ez a kör **2–4
munkaóra** nagyságrendű; a szezonális korrekció és a tényleges jégmodell
hátralévő tervezése/implementációja/validációja **12–24 munkaóra**, nagy
bizonytalansággal. Nem a teljes projekt készültségi becslése.
