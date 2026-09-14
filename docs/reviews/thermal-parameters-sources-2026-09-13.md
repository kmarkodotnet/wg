# Hőmodell paraméterforrásai — első gyűjtés (ND-100)

2026-09-13. A pillanatnyi hőmodell (ND-100) együtthatóinak jelöltjei és
forrásai. A `python-reference` szabvány szerint emlékezetből nem kerül
konstans a referenciába; ahol nincs hivatalos, mért érték, ott a szám
**modellválasztás**, amelyhez a felhasználó megerősítése kell.

Ellenőrzési oszlop:

- **letöltve:** a forrás szövegét ebben a munkamenetben olvastuk, az idézett
  szám onnan származik;
- **másodlagos, letöltve:** egy letöltött összefoglaló idézi a megnevezett
  elsődleges forrást, az elsődleges szöveg nem volt elérhető;
- **csak keresési kivonat:** a szám keresőtalálatból jön, az eredeti forrás
  nem volt elérhető (403, DNS- vagy tanúsítványhiba) — újraellenőrzendő;
- **kód:** a meglévő Core-ban már szereplő érték.

## 1. Mért vagy definiált fizikai értékek

| Paraméter | Érték | Forrás | Ellenőrzés |
|---|---|---|---|
| Stefan–Boltzmann-állandó `σ` | 5,670374419·10⁻⁸ W m⁻² K⁻⁴ | `Temperature.Sigma` | kód |
| Napállandó `F` | 1361 W m⁻² | `Temperature.DefaultFPeak` | kód |
| Tengervíz fajhője `cp0` | 3991,86795711963 J kg⁻¹ K⁻¹ | [TEOS-10 `gsw_cp0`](http://teos-10.org/pubs/gsw/html/gsw_cp0.html) | letöltve |
| Tengervíz in-situ sűrűsége | a TEOS-10 példájában 1021,84 kg m⁻³ (10 dbar, CT 28,8 °C) … 1032,00 kg m⁻³ (1000 dbar) | [TEOS-10 `gsw_rho`](https://www.teos-10.org/pubs/gsw/html/gsw_rho.html) | letöltve |
| Nyílt óceán albedója | ~0,06 | [NSIDC: Science of Sea Ice](https://nsidc.org/learn/parts-cryosphere/sea-ice/science-sea-ice); egyezik `Temperature.AlbedoOcean`-nel | letöltve |
| Csupasz tengeri jég albedója | ~0,5–0,7 | NSIDC (fent) | letöltve |
| Hóval fedett vastag tengeri jég | legfeljebb ~0,9 | NSIDC (fent) | letöltve |
| Sekély olvadási tócsák | ~0,4–0,5 | NSIDC (fent) | letöltve |
| Víz széles sávú emisszivitása (1–100 µm) | 0,96 (0–30 °C) | Sidran (1981), *Applied Optics*, a [Science of Doom összefoglalója](https://scienceofdoom.com/2010/12/27/emissivity-of-the-ocean/) szerint | másodlagos, letöltve |
| Tengerfelszín emisszivitása (8–14 µm, közel függőleges) | 0,98–0,99; 0,984 ± 0,004 | Niclòs et al. (2005), *Remote Sens. Environ.*; Konda et al. (1994), *J. Oceanography*; Smith et al. (1996), *BAMS* — ugyanazon összefoglaló szerint | másodlagos, letöltve |
| Talaj fajhője (víz = 1) | ~0,19–0,20 általában; ~0,17 fagypont közelében | Kersten: *Thermal Properties of Soils*, HRB/TRB Special Report 2, 161–162. o. ([PDF](https://onlinepubs.trb.org/Onlinepubs/sr/sr2/sr2-015.pdf)) | letöltve |
| Nedves talaj fajhője | `Sm = (100·S + w·1,0) / (100 + w)`, `w` nedvességtartalom a száraz tömeg %-ában | Kersten (fent) | letöltve |
| Talaj hődiffúzivitása (alapérték) | 5·10⁻⁷ m² s⁻¹ (~1 K·m/W hőellenállás, ~7% nedvesség) | IEC 60853-2, [cableizer dokumentáció](https://www.cableizer.com/documentation/delta_soil/) | letöltve |
| Tengeri nedvességátadási együttható, semleges 10 m | `Ce10n` ≈ 1,15·10⁻³ állandóként 5–20 m s⁻¹ között; mérve 1,08·10⁻³ (5 m s⁻¹) → 1,2·10⁻³ (18 m s⁻¹) | Fairall et al. (2003), *J. Climate* 16, 571–591, 585. o. ([PDF](https://ftp.soest.hawaii.edu/kelvin/OCN665/OCN665_full/papers/COARE_flux3.0.pdf)) | letöltve |
| Napi felső óceáni felmelegedés | „light wind and sunny conditions the sun may warm the upper few meters of the ocean by 1°–3°C” | Fairall et al. (2003), 573. o. | letöltve |
| Held–Suarez newtoni relaxáció | határréteg `k_s = 1/(4·86400) s⁻¹`, szabad troposzféra `k_a = 1/(40·86400) s⁻¹`, `σ_b = 0,7` | [MITgcm Held–Suarez példa](https://mitgcm.readthedocs.io/en/latest/examples/held_suarez_cs/held_suarez_cs.html), Held & Suarez (1994) alapján | letöltve |
| Slab-óceán mélységek | 2,4 / 6 / 12 / 24 / 50 m kísérletek; a 2,4 m-es réteg hőkapacitása ≈ a légkörével, az 50 m-es több mint 20-szorosa | Donohoe, Frierson, Battisti (2013), *Clim. Dyn.*, DOI 10.1007/s00382-013-1843-4, 2. o. | letöltve |
| Nappali határréteg-magasság | rádiószondás közel globális napi maximum 1926 m (17 LST); ERA5 nappal ~130 m-rel alulbecsül | [ACP 21, 17079 (2021)](https://acp.copernicus.org/articles/21/17079/2021/) | letöltve |
| Levegő sűrűsége tengerszinten | 1,225 kg m⁻³ (U.S. Standard Atmosphere 1976) | NASA NTRS 19770009539 keresési kivonata; a NASA modelweb és a pdas.com oldala nem adta vissza a számot | csak keresési kivonat |
| Száraz levegő `cp` | ~1004–1005 J kg⁻¹ K⁻¹ (WMO 1966 kerekítve 1005) | NIST-publikáció és AMS Glossary keresési kivonata | csak keresési kivonat |
| Szárazföldi emisszivitás | talaj 0,95, növényzet 0,98 (TSEB-modell); a legtöbb természetes felszín > 0,95 | keresési kivonat (AMS/ScienceDirect oldalak 403) | csak keresési kivonat |
| Kevert rétegmélység változékonysága | nyári féltekén < 20 m, szubpoláris télen > 500 m | de Boyer Montégut et al. (2004), *JGR* 109 | csak keresési kivonat |

Levezetett, számolt érték (nem forrásból idézett szám):

- napi csillapítási mélység `d = sqrt(2D/ω)`, `ω = 2π/86400 s`:
  `D = 5·10⁻⁷ m² s⁻¹` mellett `d ≈ 0,117 m`.

## 2. Modellválasztások

**A felhasználó 2026-09-13-án az M1–M10 javasolt kiindulási értékeit
jóváhagyta**; ezek az ND-100 első modellverziójának paraméterei. Új
modellválasztás csak külön megjelöléssel kerülhet be.

| # | Paraméter | Javasolt kiindulás | Tartomány a vizsgálathoz | Indok |
|---:|---|---|---|---|
| M1 | `Ice` albedó | 0,6 | 0,5–0,9 | NSIDC csupasz jég és hóval fedett jég közé |
| M2 | `Freshwater` albedó | 0,06 | — | Nincs külön forrás; az óceáni értékkel azonos, amíg nincs jobb |
| M3 | Óceán effektív napi hőkapacitásának mélysége | 10 m | 2,4–50 m | A napi anomáliához a felső néhány méter és a kevert réteg közötti mélység releváns (Fairall 2003; Donohoe 2013 tartománya) |
| M4 | Szárazföld effektív mélysége | **0,5 m** (módosítva a mérés után) | 0,05–0,5 m | IEC diffúzivitásból a napi csillapítási mélység ~0,12 m, de az egycellás mérés szerint ezzel a napi félamplitúdó 26–31 K (53–62 K napi tartomány), ami párolgási hűtés és mélyebb talajvezetés nélkül túl nagy. 0,5 m-rel 10–12 K félamplitúdó, 4,1–4,4 h késés a helyi déltől. A hőkapacitás a Kersten-fajhőből és a választott száraz sűrűségből (M10). A „helyes” napi amplitúdóhoz nincs még letöltött megfigyelési forrás |
| M5 | Felszínközeli levegőoszlop vastagsága `H` | 1000 m | 500–2000 m | Rádiószondás nappali határréteg-magasság nagyságrendje |
| M6 | Felszín–levegő hőcsere `ksa = ρ·cp·C·U` | `C = 1,15·10⁻³`, `U` a szélsnapshotból | `C` 1,0–1,2·10⁻³ | A COARE nedvességátadási együtthatója a hőátadásra is közelítésként; szárazföldre nincs külön forrás |
| M7 | Levegőanomália relaxációja `λa = Ca·k` | `k = 1/(4 nap)` | 1/(2 nap) … 1/(40 nap) | Held–Suarez határréteg-érték; benchmark-választás, nem mérés |
| M8 | Felszíni emisszivitás `ε` | óceán/édesvíz 0,96; szárazföld és jég 0,95 | 0,95–1,0 | Víz: Sidran (1981) széles sávú átlaga; szárazföld: csak kivonat — megerősítendő |
| M9 | Tengervíz és levegő sűrűsége | tengervíz 1025 kg m⁻³; levegő 1,225 kg m⁻³ | tengervíz 1021,84–1032,00; levegő — | Tengervíz: a TEOS-10 példatartományán belül; levegő: csak kivonat — megerősítendő |
| M10 | Száraz talaj sűrűsége | 1400 kg m⁻³ | — | Nincs letöltött forrás; a Kersten-diagramok száraz sűrűség-tengelye angolszász egységben van |

### Ideiglenes választások az implementáció közben (megerősítendő)

| # | Paraméter | Érték | Indok |
|---:|---|---|---|
| M11 | Szélcsend-alsóhatár a felszín–levegő hőcserében | `U_eff = max(U, 1 m/s)` | Fairall et al. (2003), 573. o.: a gustiness a szélcsend közeli skalárfluxus-szingularitást oldja fel; az 1 m/s érték választás |
| M12 | Édesvíz és jég hőkapacitása | édesvíz = óceáni képlet (M3), jég = szárazföldi (M4) | Nincs külön letöltött forrás |
| M13 | Radiatív simítás a bázisban és a szél hőmérsékletében | β = 0,5 | Mérés: a napi faktoros bázis sarki éjszakán ~29 K; β = 0,5-tel 80°-os szárazföld −58 … +21 °C, β = 0,3-mal −80 °C minimum (`thermal_seasonal_column_ref.py`) |

## 3. Nyitott forráskeresés

- Szárazföldi hőátadási együttható (M6).
- Levegősűrűség, száraz levegő `cp` és szárazföldi emisszivitás letöltött
  elsődleges forrásból (a jelenlegi csak kivonat).
- Száraz talajsűrűség SI-egységben (M10), a kalória→joule átváltás forrása.
- Édesvízi tavak effektív keveredési mélysége.
