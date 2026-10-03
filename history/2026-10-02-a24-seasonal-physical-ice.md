# A24 — szezonális energiamérleg és fizikai jégbesorolás

Dátum: 2026-10-02. Ág: `a19-plate-frame-noise`.
Döntések: ND-165, ND-174, ND-175. A felhasználó a teljes A24 végigvitelét kérte.

## Megvalósítás

- A teljes szezonális bázis hőtárolása bekerült az energiamérlegbe:
  `C dT/dt = S(1−a) − 210 − 2,09 T + P(T)/A + 2,09 ΔT`.
  A hőmérséklet itt °C, a többi tag W/m²; `C` J/(m²·K).
  Az élfluxus konzervatív, az év periodikus, 48 fázisos visszalépő Euler.
  Fourier + diagonálisan előkondicionált COCG; degenerált komplex szorzatnál
  ellenőrzött Jacobi-tartalékút. A rövid évű L1 teszt valóban elkapta ezt
  a degenerációt; a javítás után is fizikai reziduumkapu védi a kimenetet.
- A szél a megoldott szezonális mező gradienséből származik. A napi
  levegőanomália visszacsatolása és a napi felszíni fluxus megmaradt.
  Az éves statikus transzportkorrekció nem adódik újra a szezonális bázishoz.
- A szárazföldi tartós jég feltétele: éves havazás > potenciális hóolvadás.
  A forrás az éves hőmérsékletből és szélből számolt nedvességtranszport;
  külön, forrással dokumentált 0,97 m vízegyenérték / 365,25 nap kalibrációval.
  Más hosszúságú évnél az utánpótlás időarányos. A dimenziótlan csapadékmező
  nem kapott utólag hamis fizikai mértékegységet.
- A vízi tartós jég konzervatív feltétele: a szezonális maximum is fagypont
  alatti (óceán 271,35 K, édesvíz 273,15 K). A szezonális fagyás külön osztály.
  Nincs előírt jégarány. A két rögzített albedómenet megmaradt.
- A viewer és a `WGTC0003 / climate_k3` cache külön kezeli a jég fennmaradási
  jelét. Ez **nem hőmérséklet**: a talaj, a párolgás és a biome hőtengelye
  valódi K értéket kap. A fizikai jégre nincs dekoratív határzaj; a biome
  hideg fallbackje nem hoz létre a mérlegből hiányzó jeget.
- A deep-time globális eltolás a Core energiamérlegében szerepel, a viewer
  nem adja hozzá másodszor.
- ND-165: az analitikus előnézet két radiatív képlete is bolygó-albedóra váltott.
  A Python hőmérséklet-, szél-, nedvesség-, tó/jég- és regolitvektorok újragenerálva.
- Generátor: **7 → 8**, hőmodell: **5 → 6**. A korábbi világ/checkpoint/cache
  nem tekinthető kompatibilisnek. A korábbi numerikus orákulumok explicit
  `Legacy` paraméterezéssel megmaradtak; az aktív út külön Python-orákulumot kapott.

## Mérési bizonyíték

Seed `0xA7C944210000`, 20 lemez, 365,25 napos év, 12 éves mintanap:

| rács | éves Ta területi átlag | éves Ta tartomány | tartós jég | szezonális hó/fagyás | max. diszkrét szezonális reziduum |
|---|---:|---:|---:|---:|---:|
| L5, 6144 cella | 11,702 °C | −21,2…28,7 °C | 0 | 1941 | 1,133·10⁻¹⁰ W/m² |
| L6, 24576 cella | 11,668 °C | −21,5…28,8 °C | 0 | 8204 | 5,042·10⁻¹⁰ W/m² |

A két felbontás globális átlagának eltérése 0,034 K. L6 északi/déli sarki
sávátlag: −13,618 / −13,592 °C. A legnagyobb mintázott pillanatnyi levegőérték
41,5 °C; a minimum −61,3 °C. A jégmentes eredményt nem korrigáltuk vissza
percentilissel. A magas, csapadékos szintetikus világban a teljes kétmenetes
Core-lánc tartós jeget ad; a hideg, száraz cella jégmentes marad.

L3, másik seed (`0xB91C55210000`): globális Ta 10,581 °C, 149 szezonális cella.
L3, 48 → 96 fázis: globális Ta 11,787 → 11,785 °C, 106 szezonális cella mindkettőben.
A 24/48/96 fázisú térbeli tesztben a finomítás csökkenti a szezonális görbék
eltérését. A deep-time klímaforcing külön mérése rögzített domborzattal:
75 Myr-nél 15,340 °C, 112,5 Myr-nél 8,328 °C. Ez nem a teljes deep-time
terepfejlődés mérése.

Az L5/L6 CLI teljes idő 23,0 / 111,0 s; párhuzamos ellenőrzések mellett,
tehát ezek **nem izolált teljesítménybenchmarkok**, és nem új <1 s-os Build-ígéretek.

## Ellenőrzések

- Release solution: **1995/1995** sikeres (855 Core, 664 viewer-LOD, 24 CLI,
  452 alkalmazás-alap).
- Debug solution: **1995/1995** sikeres; Core futás 8,30 perc.
- Az utolsó túlcsordulás-védelem és biome-fallback célzott ellenőrzése: 5/5 teszt mindkét konfigurációban.
- Az utólag hozzáadott, éles jéghatárú L6-es energiamérlegteszt külön is sikeres mindkét módban (17 s Release, 67 s Debug). A végső tesztkészlet így 1996 teszt; mindegyik ellenőrizve.
- Az éves orákulum verziómetaadata is újragenerálva; az éves/olvadási 16 teszt célzott ismétlése mindkét módban sikeres.
- Threefry KAT: **9/9**. A 512 randomvektor újragenerálva, byte-egyező.
- 16 referenciafájl byte-összevetése sikeres, beleértve a négy új orákulumot,
  az analitikus fogyasztókat és a checkpointot. A CI az új generátorokat futtatja
  és byte-szinten összeveti a verziózott tesztadatokkal; `fail-fast: false` maradt.
- Analitikus egy-/kétcellás KAT, teljes periodikus energiamérleg, fázisfinomítás,
  Python–C# aktív bázis/szél/napi állapot/éves átlag, párhuzamos bitazonosság,
  fizikai csapadékkalibráció, száraz/hideg/meleg/fagyási határok és cache-roundtrip.

## Tényleges Unity Editor ellenőrzés

Unity **6000.0.77f1**, külön projektmásolat, ugyanaz a `PlanetView.unity`, L5.
A nyitott felhasználói Editor jelenetállapotát nem módosítottuk.

- A Core, viewer és Editor-kód lefordult; fordítási hiba és futási exception nincs.
  A meglévő nullable-context figyelmeztetések nem tűntek el.
- Play, hideg cache: fizikai klíma aktív, 103,6 s háttérszámítás után.
- Új Play, meleg cache: `FromCache=True`, 5767 ms (a lenyomatot továbbra is kiszámolja).
- A tényleges alkalmazott eredmény: 6144 jégmérleg-, levegő-, teljes felszíni és
  széladat; 2151 szárazföldi felszíni hőmérséklet; küszöb 0, `UsesPhysicalIce=True`.
- A HDRP `StandardRequest` API-jával készült 1280×720-as képet megnyitottuk:
  a bolygó, felszín és felhőréteg renderel, hibás rózsaszín material nem látható.
  Ez műszaki vizuális ellenőrzés; nem új felhasználói esztétikai jóváhagyás.

Helyi bizonyíték: `artifacts/a24-physical-final-l5.txt`, `a24-physical-final-l6.txt`,
`a24-full-release-final.txt`, `a24-full-debug-final.txt`,
`a24-unity-batch.log`, `a24-unity-render.log`, `a24-unity-batch-result.txt`,
`a24-unity-batch.png`. Az ideiglenes Editor-híd nem része a termékkódnak.

## Modellhatár

Ez földszerű, csökkentett energiamérleg-modell, nem teljes légkör-/gleccserszimuláció.
A csapadék éven belüli eloszlása egyenletes; a hófeltétel felhalmozódási potenciál,
nem jégvastagság vagy gleccseráramlás. A vízi feltétel elégséges és konzervatív:
nem számol olyan többéves jéggel, amely rövid meleg időszakot a látenshő-készlete
miatt túlél. A két albedómenet nem állít fixponti konvergenciát. Az analitikus
előnézet továbbra sem az aktív szezonális fizika helyettesítője.

Ezek a kimondott modellhatárok nem rejtett percentilis-pótlások. Az A24 kért
szezonális hőtárolás / utánpótlás / külön tengeri feltétel / fogyasztói adatút
munkája elkészült. **A24: 100%**, tartalmilag súlyozott becslés, a fent rögzített modellhatárok között.
Durva, tartalmilag súlyozott ráfordításbecslés erre a körre: 12–20 emberóra;
nem mért munkaidő. A24 hátralévő implementáció: **0 óra**. A külön performance- és látvány-backlog nem része ennek.
