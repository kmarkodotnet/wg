# A7 / ND-166 — a hőmodell hideg tartományának mért szétválasztása

**Dátum:** 2026-09-29  
**Ág:** `a19-plate-frame-noise`  
**Kiindulás:** az ND-160 után a felhasználó a `useThermalClimateBiome` kikapcsolt látványát továbbra is jobbnak ítélte; a cél a hőmodell fizikai javítása.

## Mit változtattunk

- A CLI `thermal-climate` parancsa az effektív óceáni és talajmélységet külön mérőparaméterként fogadja (`--ocean-depth`, `--land-depth`). A bázisfelbontás a leghidegebb éves cella tagjait is kiírja.
- A meglévő meridionális K-proxy `--meridional-scale` mérőparamétert kapott a Core-ban (0–1). Az **1,0 alapérték ugyanazon a műveleti úton fut**; a nem alapérték külön modellazonosítót kap, így nem találhat a régi checkpointjára vagy cache-ére. A `ThermalBaseline` és a `ThermalWind` ugyanazt a paramétert olvassa.
- Az ND-163 csapadék A/B mérőútja a jégmentes A menet helyett a **végleges B menet** éves szelét használja. A hőmező bitazonosságát futás közben ellenőrzi, és a csapadékváltozást szélállandósági sávonként is közli.

Ez **diagnosztika**: a viewer, a generátorverzió és az alapértelmezett numerikus kimenet nem változott. Az ND-166 egyelőre nem választott új fizikai modellt.

## Kanonikus mérés

`worldgen thermal-climate --seed A7C944210000 --plates 20 --level 5 --parallel true`, 12 mintanap, 6144 cella, 20 lemez, t=0. Minden sorban a bolygó-albedós ND-160 bázis és a kétmenetes éves klíma futott.

| β | Meridionális skála | Óceán / talaj (m) | Éves minimum | P5 | P50 | 7%-os jégvágópont | Pillanatnyi minimum |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0,5 | 1,0 | 10 / 0,5 | −1,7 °C | +5,4 °C | +18,1 °C | +6,07 °C | −33,1 °C |
| 0,3 | 1,0 | 10 / 0,5 | −9,9 °C | −0,1 °C | +17,7 °C | +1,18 °C | −55,7 °C |
| 0,2 | 1,0 | 10 / 0,5 | −16,4 °C | −4,5 °C | +17,4 °C | −2,46 °C | −71,7 °C |
| 0,2 | 0,75 | 10 / 0,5 | −26,4 °C | −12,4 °C | +16,6 °C | −9,89 °C | −78,8 °C |
| 0,2 | 0,0 | 10 / 0,5 | −56,3 °C | −36,2 °C | +14,3 °C | −31,55 °C | −105,3 °C |
| 0,5 | 1,0 | 10 / 0,12 | −1,7 °C | +5,4 °C | +18,0 °C | +6,06 °C | −33,1 °C |
| 0,5 | 1,0 | 2,4 / 0,5 | −1,7 °C | +5,4 °C | +18,1 °C | +6,08 °C | −33,1 °C |

A 0,12 m-es talajréteg a pillanatnyi felszíni **maximumot** +55,8 → +74,2 °C-ra növelte; az éves hidegvéget nem mozdította. A legkisebb éves hőmérséklet a helyi bázistól ≤0,1 K-re volt. β=0,2 és skála 1 mellett a leghidegebb cella bázisában a meridionális tag **+39,90 K**, a globális, területtel súlyozott átlaga **+8,00 K**.

Az A/B alapján a hidegvég fő oka a **bázis felépítése**: a β simítás és a meridionális pozitív hőmérséklet-proxy. A hőkapacitás a napi ingadozást szabályozza, ezen a világon az éves minimumot nem. A jelenlegi proxy globális hőforrás; belső hőszállításként fizikailag nem zárja az energiamérleget. A nulla skála túl hideg világot ad, tehát a proxy puszta elhagyása sem kész megoldás. Következő numerikus lépés az energiamegmaradó fluxusforma megtervezése Python-orákulummal és külön seed-verziós döntéssel.

## ND-163 csapadékszél

Az ND-160 utáni alapmodellen, a **végleges B menet** éves szelével: a szélállandóság 0,737. A szárazföldi csapadéknegyed egyezése az analitikus útéhoz képest csak hőcserével **89,3%**, hővel és széllel **66,9%**; a szél önmagában 70,4% egyezést ad. A 0,50–0,75 szélállandósági sáv 1374 szárazföldi cellájából 517 (37,6%) negyedet vált. Ezért az eltérés nem szűkíthető a majdnem teljesen kioltódó szélű néhány cellára. A fogyasztói kapu zárva maradt.

## Ellenőrzés és korlát

- `dotnet build tools/WorldGen.Cli/WorldGen.Cli.csproj -c Release --no-restore`: 0 hiba, 0 figyelmeztetés.
- Célzott Core tesztek (`ThermalCheckpointTests`, `ThermalAnnualStatisticsTests`, `ThermalFeedbackTests`): **39/39** zöld, köztük a Python checkpoint-vektorral egyező alapértelmezett modellazonosító és a nem alapértelmezett skála elutasítása idegen checkpointnál.
- Teljes Core Release regresszió: **826/826** zöld.
- A `tools/reference/thermal_field_ref.py` ugyanazt a diagnosztikai skálát kapta a bázis és a termikus szél ágán, 1,0 alapértéken a régi műveleti út megőrzésével. **Utólagos kiegészítés (ND-167):** a Python nem volt a `PATH`-on, de a gépen elérhető a `C:\OctoPrint\WPy64-31050\python-3.10.5.amd64\python.exe`; a `verify_kat.py` 9/9 ellenőrzése lefutott. A nem alapértelmezett meridionális skálához továbbra sincs külön hőmező-KAT-vektor.
- A level-5 számok **egy seed** diagnózisai. Nem bizonyítják más seedek/LOD-k fizikai hitelességét, és nem helyettesítik az élő Unity-képet. A Windows Computer Use kapcsolata ebben a munkamenetben `native pipe` hibával nem volt elérhető; új Play-képet nem készítettünk. A felhasználó szöveges vizuális ítéletét rögzítettük.

## Haladásbecslés

Az A7 megvalósított adatútja továbbra is durván **97%**; a szélcsere és a fizikai alap hitelesítése nyitott, így a teljes hőmodell vizuális elfogadása nem következik a százalékból. Erre a diagnosztikai körre durván **2–3 munkaóra** ráfordítást, az energiamegmaradó hőszállítás tervezésére, referencia-implementációjára és validálására **további 15–30 munkaórát** becsülünk. Ezek tervezési becslések, nincs időnapló.
