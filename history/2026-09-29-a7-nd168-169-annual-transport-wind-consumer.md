# A7 — éves energiamegmaradó hőmérleg és a végleges szél fogyasztói útja

**Dátum:** 2026-09-29. **Döntések:** ND-167–169. **Állapot:** az A7
implementációja lezárva; a felhasználó az ellenőrzéseket ezután végzi.
Commit és push nem készült.

## Mi változott

Az ND-166 diagnózisa szerint a régi `40 K · z⁴` hőmodell-tag a level-5
bázist területileg átlagolva +8 K-nel emelte. Belső meridionális transzport
nem adhat globálisan pozitív energiaforrást. Az ND-167 páronként konzervatív
élfluxusára épülő `MeridionalEnergyBalance` a 12 ablakos éves radiatív
célmezőből meridionális vezetőképességet számol, majd a `λ A δ = P(T₀+δ)`
egyenletet 256 rögzített PCG-lépéssel oldja. `D = 0,555 W/(m² K)`, `λ =
2,09 W/(m² K)`. A kapott korrekció területi átlaga nulla. A
`ThermalBaseline` órás mezője és a `ThermalWind` statikus gradiensrésze
ugyanazt a mezőt olvassa. A `Temperature` régi analitikus útja nem változott.

A Python-orákulum új `meridional_energy_balance_ref.py` modult, kézzel
számolható kétcellás KAT-ot és level-1 geometriavektort kapott. A
`thermal_field_ref.py` az új modellt használja; a checkpoint, visszacsatolt
szél és kétmenetes éves éghajlat verziózott vektorai újragenerálódtak. Az
éves referencia saját mezőépítését az új szélkonstruktorhoz igazítottam.

Az ND-163 mérőút már számolt éves szélvektort. Az ND-169 beköti ezt a
végleges B menetbe a viewer számára: az éves statisztika széladatai a
`WGTC0002` lemez-cache-be kerülnek, és az aktív hőmodell-biome csapadék-
számítása ugyanazon B menet felszíni hőmérsékletét és szelét kapja. A
kapcsoló kikapcsolt állapota az analitikus adatutat használja. A cache új
előtagja `climate_k2`, ezért a régi tartalom nem olvasható be. A hőmodell
verziója 4 → 5, a generátoré 6 → 7; a régi checkpoint verzióeltérésnél
elutasított. Széladat nélküli éves statisztikát a cache explicit hibával
utasít el; nulla helyőrző szélmezőt nem tárol.
Az élő viewer revíziós bemenetazonosítója is keveri a modell- és
generátorverziót, így script-frissítés után nem tartja életben a korábbi
algoritmus kész éghajlatát azonos fizikai bemenetek mellett.

A viewer csapadékának paraméteres elevációs útja továbbra is a
`SeaLevelCalibration` t=0 mezőjét használja. Ez a korábbi ND-157-ben
azonosított deep-time korlát; az ND-169 ebben a körben a hő és a szél
forrását egységesíti. A deep-time csapadék topográfiai átállítása külön
döntést és mérést igényel.

## Új kód és ellenőrzési célok

- `MeridionalEnergyBalanceTests`: Python-geometria és -korrekció, nullátlag,
  állandó mező, pozitív és negatív korrekció, bitazonos párhuzamos/ismételt
  megoldás, mérlegegyenlet-maradék, paraméterhatás és hibás bemenetek.
- `ThermalCheckpointTests`: a transzport-paraméter hatása az új egyenletben.
- `ThermalWindVectorTests`: a csapadék paraméteres wrapperje az explicit
  elevációs és hő/szél bemenettel azonos eredményt adjon.
- `ThermalClimateDiskCacheTests`: a B menet szélkomponensei írás/olvasás
  után egyezzenek; hiányzó szél ne váljon helyőrző nullává.

Ezeket a teszteket **nem futtattam**. A vektorgenerálás adat-előállítás volt;
sem a C# tesztek, sem a `verify_kat.py`, sem a Unity Editor futtatása nem
történt meg ebben a körben, a felhasználó kérése szerint. A Python-orákulum
level-6 pillanatnyi referenciafutása a generáláskor `[-40,2; +22,3] °C`
felszíni tartományt írt ki; ez **nem** a level-5/6 kétmenetes éghajlat vagy
az élő viewer mérési eredménye.

## Felhasználói átvételi menet

1. A gyökérben `dotnet build WorldGen.sln`, majd a három tesztprojekt:
   `tests/WorldGen.Core.Tests`, `tests/WorldGen.Viewer.LodChunking.Tests`,
   `tools/WorldGen.Cli.Tests`. Futtasd külön a referencia `verify_kat.py`
   ellenőrzését, az ND-168 `meridional_energy_balance_ref.py --verify-kat`
   KAT-ot és a verziózott vektorok újragenerálás utáni byte-összevetését.
2. Unity Play-ben várd meg a hideg éghajlat-számítást és a második Buildet.
   Figyeld, hogy van-e fordítási/Console hiba, a végleges jég és biome
   megjelenik-e, a cache íródik-e. Új Play-ben a `climate_k2` találatnak
   ugyanazt a kimenetet kell adnia.
3. Ugyanabból a kameranézetből nézd meg a `useThermalClimateBiome` be és ki
   állapotát overlay nélkül. A korábbi visszajelzés szerint a régi modellen
   **kikapcsolva** festett jobban; az új modellről még nincs ítélet. A jég
   kiterjedése, a tundra/erdő/sivatag átmenete, a tengeri jég, a csapadék
   és a felhő vizuális kapcsolata számít. A háttérszál idejét és a cache-
   találat költségét az új verzión újra kell mérni.

**Átvételi határ:** ha a teszt vagy a kép hibát mutat, az A7 javítása nyitott.
Ha az élő kimenet továbbra is kevésbé valószerű a kikapcsolt változatnál,
az A24 fizikai kalibrációja nyitott; nem állítok vizuális készséget a
felhasználó ítélete előtt. A7 implementáció ~100%, az ellenőrzött átvétel
~90%; az ellenőrzés és esetleges javítás **durva becsléssel 1–4 munkaóra**,
amely a látott hibától függően jelentősen változhat.
