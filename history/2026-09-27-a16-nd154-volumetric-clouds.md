# A16 — M13 térfogati felhő + AO (ND-154, ND-155, ND-156)

**Dátum:** 2026-09-27
**Ág:** `a19-plate-frame-noise`
**Tétel:** todo2 A16 — „M13 — volumetrikus felhő + AO, színkalibráció"

## Mi volt a kiindulás

Az A14 (ND-21) a HDRP volumetrikus felhőjét **elutasította** négy mért
blokkolóval, és ezzel rögzítette, hogy a Planet nézet felhői saját úton
maradnak. Ami addig létezett: az M6-os felhő-MVP, egyetlen **lapos quad-héj**,
a csapadék a vertex-alfában — a limbnél nincs vastagsága, nem árnyékol, és
egyetlen Lambert-tag világítja meg.

## Mi készült el

**Térfogati felhő (ND-154)** — saját, gömbi raymarch. A lefedettség, a felhő
alja és a vastagsága a MÁR KISZÁMÍTOTT csapadék- és eleváció-mezőből jön
(nulla új kiértékelés), egyetlen négycsatornás RGBA32 kocka-atlaszban
(396×66 = 104 KB), a hő-overlay (ND-104) már tesztelt atlasz-geometriájával.
A felhőalap a Lawrence-féle LCL-ből SZÁRMAZIK és a talajjal együtt emelkedik,
ezért a takaró ráborul a hegyláncra. A shaderben csak a függőleges profil
ALAKJA és a lefedettség-mező felbontása alatti részlet van — a számok
uniformként a Core-ból jönnek.

**AO (ND-155)** — és itt a mérés fordította meg a tervet. Az új
`SurfaceSkyOpenness` (zárt alakú, tisztán racionális `cos²α` sky view factor)
a valódi világon mérve level 6-on 0,999999, level 10-en 0,999992 szárazföldi
átlagot ad: **makro-léptéken okkludáló domborzat nem létezik**, mert a modell
relief-létrája ~1564 km-nél véget ér. Az AO ezért oda került, ahol meredek
relief ténylegesen van: az ND-151 per-pixel mikro-részletébe. A planetáris
léptékű valódi okkluder a FELHŐ — a terep-shader ugyanabból az atlaszból
árnyékol.

**Színkalibráció (ND-156)** — a felhő saját kalibrációja elkészült (fizikai
kioltás, HG-aszimmetria, többszörös szórás, borult diffúz padló); a
terep-paletta újrafokozása tudatosan a B17 (felhasználói vizuális átvétel)
utánra halasztva, dokumentált eljárással. Ok: a jelenlegi paletta élőben,
szemre hangolt lineáris érték, és a felhő megváltoztatja a referenciát.

## Hat hiba, amit a munka közben a MÉRÉS talált

1. **A pass nem rajzolódott.** `Cull Front` + `ZTest Always` → a megmaradó
   hátsó héjlapokat a bolygó mélysége eldobta. A beépített diagnosztika
   1-es módja (tömör kitöltés) üres képet adott → `Cull Off` + `ZTest LEqual`
   + geometriai lapválasztás.
2. **A menet átlépett a felhő fölött.** A héj 24 km, a dekk 500–2000 m; 12
   lépés nem mintázta meg → a menetet a helyi aljra/tetőre szorítjuk.
3. **Szögletes felhőárnyék-foltok.** A level 5-ös forrásmező nearest átvétele
   → bilineáris felskálázás (`UpsampleTable`), laphatárokkal együtt.
4. **Egyenletes szürke fátyol.** A multiplikatív részlet fizikailag rossz →
   sub-grid küszöbözés, zárt alakban levezetett, cella-átlag-tartó küszöbbel.
5. **Sötét szürke felhő.** Egyszeres szórással az optikailag vastag felhő
   definíció szerint fekete → oktávos többszörös-szórás közelítés.
6. **Az éjszakai oldal felhői is világítottak.** A Nap irányú optikai mélység
   nem tudja, hogy a Nap a horizont alatt van-e → nappali tényező, ami a
   szórt (égbolt-) tagot is kapuzza, mert az is szórt napfény. Az éjszakai
   felhő így sziluettként takar.

Plusz egy robusztussági hiba: Play közbeni újrafordítás után a réteg némán
eltűnt (a domain-reload a nem szerializált mezőket a típus alapértékére
állítja, tehát a `-1`-es őrszem elveszett).

## Mérés (élő Play, 1600×900, befagyasztott Nap)

| Mit | Felhő KI | Felhő BE (review előtt) | Felhő BE (végleges) |
|---|---|---|---|
| Átlagos luminancia a korongon | 75,55 | 80,93 | 96,89 |
| Fényes (L > 140) pixelek | 14,01% | 16,22% | 25,13% |
| Eltérő pixelek a teljes képen | — | 10,57% | — |
| GPU-idő (16 minta átlaga) | 2,46 ms | 2,67 ms | — |

A harmadik oszlop a code review utáni állapot. A naiv zaj-leképezés a
modellezett felhő kb. négyötödét elnyomta — a korábbi, „szebb" kép nem jobb
volt, hanem hiányos.

Az égbolt-nyitottság mérése négy szinten (6/8/9/10) a `SurfaceSkyOpennessTests`
csapdazsinór-tesztjében rögzítve.

## Code review

Egy `/code-review high` kör 11 megállapítást hozott, mind megalapozott volt.
Kilenc javítva (keret-eltérés a felhőárnyékban; a nyitottság-csatorna
számolva de nem olvasva ÉS sodródásonként újraszámolva; a sub-grid
mean-preservation egyenletes zajt feltételez, a shader viszont normálisat
adott; a menet ablaka egyetlen minta a húr közepén; a héj-anyag elvesztése
domain-reload után; mesh-szivárgás; hiányzó életciklus-takarítás; uniform-
kiadás rossz frissítési fázisban; holt státusz-property; félrevezető doksi a
transzcendensekről). Kettő dokumentált korlát maradt: a mélységtextúra
hiánya és az árnyék/felhő felbontás-eltérése. Részletek: ND-154 táblázat.

## Ellenőrzés

- `dotnet test WorldGen.sln` → **1901/1901 zöld** (Core 769, Viewer 656,
  App 452, CLI 24); +68 új teszt.
- `dotnet build tests/WorldGen.Viewer.Compile` és
  `tests/WorldGen.App.UnityBinding.Compile` → 0 hiba.
- `python tools/ci/check_burst_strict.py` → OK (314 fájl).
- **Nem seed-törő:** `worldgen hash --seed A7C944210000 --plates 20 --level 6`
  mindhárom időpontban bitre a dokumentált érték; `WorldGeneratorVersion`
  marad `"5"`.
- Élő Unity: `compilationFailed: false`, 0 konzol-hiba, mindhárom shader
  `msgCount = 0`.
- Képek: `artifacts/a16/` (A/B be-ki, kontinens-nézet, a fejlesztés közbeni
  állapotok).

## Felhasználói visszajelzés utáni második kör

*„a felhő átlátszósága nem elég magas, totálisan takarja minden felhő a
területet, emellett pontosan talajszintre van rajzolva, ... a hegységek csúcsa
környékére kellene rajzolni."*

A második kifogásból egy addig észre nem vett, komoly hiba derült ki: a
jelenetben a domborzat **111-szeres** függőleges nagyítással van rajzolva
(`terrainReliefExaggeration = 111`), a felhő magasságát viszont nem szoroztam
vele — a dekk így a rajzolt hegyek 1/111-ed magasságában, gyakorlatilag a
felszínen ült. Ezen felül a felhőalap terepkövető volt (`talaj + LCL`), pedig
egy középszintű, csapadék-hajtotta dekk vízszintes lap; az LCL a KÖD és a
gomolyfelhő alapja, nem ezé. Az átlátszatlanságnak pedig az volt az oka, hogy
minden nemnulla lefedettségnél legalább 500 m vastag (τ = 7) réteg keletkezett,
tehát vékony felhő egyszerűen nem létezett a modellben.

Javítások: a felhő ugyanazt a függőleges nyújtást kapja, mint a terep;
`MidLevelBaseMeters = 2500` vízszintes lap (a WMO középszintű osztályának alsó
pereme, ami a mért domborzat — max 2308 m, p99 1469 m — fölé kerül);
`MinThicknessMeters = 80` (τ = 1,1 áttetsző fátyol), innen nő a vastagság a
lefedettséggel; az „opacitás" csúszka az OPTIKAI MÉLYSÉGET skálázza (0,45
alapérték, 1,0 = fizikai); perem-lágyság 0,25 → 0,35. Három élő csúszka a
további hangoláshoz. Részletek: ND-154.

## Harmadik kör (2026-09-28): kipúpo sodás, magasság-ugrálás, mozgás

Három további kifogás, mind valódi hiba; a negyedik nem a felhőrétegben volt.

1. **Kipúpo sodás.** A felhő minden függőleges méretét a terep 111-szeres
   nagyításával rajzoltam; egy 9,5 km-es zivatarfelhő így 1054 km-es torony
   lett. Most két külön függőleges skála van: az ALAP a terepét követi
   (különben a hegyek átdöfik), a VASTAGSÁG saját, kisebb szorzót kap.
   A héj külső sugara 135,9 → 121,2 egység.
2. **Magasság-ugrálás kameramozgatásra.** A lépésszám nézetfüggő volt
   (12–48); mivel a menet az ABLAKOT osztja N részre, ez eltolta a
   mintavételi magasságokat. Most fix 32 lépés, és teszt őrzi.
3. **Álló felhők.** A modell beépített sodródása additív eltolás +
   újranormálás, ami π/2-nél TELÍTŐDIK — `t ≳ 100` fölött a zajmező
   elfajul, tehát hosszú távú óraként használhatatlan. Gömbön a helyes
   advekció a FORGATÁS: az új `CloudVolume.Advect` izometria, tetszőleges
   szögre. MÉRVE: egy napforduló alatt a korong változó hányada
   **1,91% → 25,89%**.
4. **Deep time — NEM a felhőréteg hibája.** A `MoisturePrecipitation.Compute`
   a deep time értékét meg sem kapja (a cache-komment expliciten rögzíti):
   a csapadék-mező deep-time-invariáns, és a felhő ezt hűen tükrözi.
   Új tétel: todo2 B19, saját ND-t igényel (a klíma-mező numerikáját
   változtatná, és a hidrológiát is érinti).

## Negyedik kör (2026-09-28): észrevehető mozgás + deep-time bemenet

„A felhők érdemben továbbra se mozognak, deep time pedig egyenesen fixek."
Mindkettő jogos; a mozgás-mérésem félrevezető volt, mert egy TELJES nap
ugrásával mértem, a nézőben viszont egy nap 100 másodperc.

1. **Az advekció a shaderbe került.** Addig az atlasz 1,5 másodperces
   újraépítése hajtotta: 1,35 pixeles ugrások, 0,9 px/s. Most a shader
   forgatja az atlasz mintavételi irányát — folytonos és nulla CPU-költségű;
   az atlasz csak világ-változáskor épül újra. Az arnyek ugyanazt a
   forgatást kapja.
2. **Prezentációs alapsebesség** (1,5 rad/nap) a fizikai 0,3 helyett —
   ugyanolyan dokumentált torzítás, mint a domborzat 111-szeres nagyítása.
   MÉRVE azonos Nap-álláson, 8 másodpercnyi advekció-különbséggel: a korong
   **77,6%-a** változik.
3. **Deep time = valódi bemenet (ND-157).** Új, additív
   `MoisturePrecipitation.ComputeFromElevationField` overload; a felhő-atlasz
   a VIEWER deep-time eleváció-mezőjéből építi a saját csapadék-mezőjét. A
   megosztott mező érintetlen. MÉRVE: lefedettség-átlag 0,081 (t=0) → 0,088
   (600 Myr), és a mintázat láthatóan más.

Melléklelet (NEM a felhőréteg): deep-time újraépítés után a kép elmosódik és a
csillagok csíkká nyúlnak; a felhő kikapcsolásával is megmarad, tehát nem ez
okozza — valószínű ok a HDRP temporális akkumulációja.

## Ötödik kör (2026-09-28): a helyben maradó apró foltok

„A tenger fölött mintha foltokban fix maradna a felhőzet." Valódi hiba: az
advekció shaderbe költöztetésekor csak az ATLASZ mintavételi irányát
forgattam el, a cellán beluli részlet-zajét nem — a felhőfoltok apró
mintázata a bolygóhoz volt szögezve, miközben a lefedettség elcsúszott
fölötte. A tengeren a legfeltűnőbb, mert ott a lefedettség sima, tehát a
rögzített zaj uralja a képet.

MÉRVE (azonos Nap-állás, 2 másodpercnyi advekció, tenger fölötti folt,
a legjobban illeszkedő eltolást keresve): előtte a legjobb illeszkedés
**(0,0) px** — a mintázat bizonyíthatóan nem transzlálódott; utána
**(+8,+8) px**, 68,7%-os hibacsökkenéssel.

## Ami nyitva maradt

- **B17** — a vizuális átvétel második köre. A limbnél a mély konvektív cellák a 111-szeres nyújtás miatt kiugró dudorokat adnak (9,5 km × 111 = 14 egység); ez a nyújtás következménye, nem hiba, de eldöntendő, hogy így maradjon-e.
- **Mélységtextúra** — űrből nézve egy a dekkbe emelkedő hegy nem takarja el a
  mögötte lévő felhőt (HDRP custom pass kellene hozzá).
- **A terep-paletta újrafokozása** (ND-156, todo2 B18), a B17 után.
