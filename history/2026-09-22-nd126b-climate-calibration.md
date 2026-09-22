# ND-126b — klíma-konstansok kalibrálása

## Cél

A kanonikus világ túl nagy hideg szárazföld-arányának és korlátlan sarki
termikus szelének javítása a csapadék-percentilisek módosítása nélkül.

## Döntés és implementáció

- A hőmérséklet minden autoritatív és diagnosztikai útján
  `T_transport = 40 K * sin⁴(szélesség)` additív meridionális
  hőszállítás-proxy került be.
- A kétdimenziós termikus szélkomponens iránytartó
  `30 m/s * tanh(|v| / 30 m/s)` korlátot kapott a Coriolis-forgatás előtt.
- A `ThermalModelParameters.ModelVersion` 1-ről 2-re nőtt.
- A csapadék 20/45/75 percentilis-küszöbei változatlanok maradtak.

## Mérés

Kanonikus világ: seed `0xA7C944210000`, 20 lemez, level 6, 65% víz,
napéjegyenlőség.

| Metrika | Előtte | Utána |
|---|---:|---:|
| Hideg szárazföld (`IceSheet + Tundra`) | 40,71% | 16,91% |
| 70–90° szárazföldi átlag | −62,21 °C | −26,31 °C |
| Egyenlítői szárazföldi átlag | 27,58 °C | 27,59 °C |
| 70–90° átlagos szél | 120–126 m/s | 34,66 m/s |
| Teljes szélmaximum | 120 m/s felett | 36,79 m/s |

## Referencia és ellenőrzés

- A Python-orákulumok frissültek: `temperature_ref.py`,
  `wind_precipitation_ref.py`, `thermal_field_ref.py`.
- Kilenc érintett vektorfájl újragenerálva és byte-szinten azonosan átmásolva
  a C# tesztadatok közé.
- A Threefry KAT 9/9 egyezik; az összesített random `testvectors.json`
  változatlan és byte-azonos.
- Új kalibrációs regressziós tesztek védik a hideg szárazföld arányát, a
  szélességi hőmérséklet-gradienst, a szélmaximumot és a korlát alakját.
- `dotnet build WorldGen.sln --no-restore`: 0 hiba, 0 figyelmeztetés.
- Core: 563/563; viewer-LOD: 490/490; CLI: 8/8; app Foundation: 438/438.
- Az aktív Unity 6000.0.77f1 Editor ehhez a projekthez tartozó naplója három
  friss fordításnál `LogAssemblyErrors (0ms)` eredményt mutatott.

## Kompatibilitás

Ez numerikus, seed-kimenetet módosító változás. A diagnosztikai hőmodell saját
verziója emelkedett. A teljes mentés generátorverziója továbbra is az ND-108/A6
nyitott feladata; a jelenlegi `.worldpkg` klímaállapotot nem tárol.
