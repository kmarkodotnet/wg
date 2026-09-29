# A7 / ND-167 — konzervatív hőfluxus-kernel

**Dátum:** 2026-09-29. **Állapot:** numerikus részdöntés és Core-kernel kész;
a fizikai paraméterezés és a hősolver-bekötés nyitott.

Az ND-166-ban mért `40 K · z⁴` bázisproxy globálisan +8 K-t ad; belső
hőszállításként nem teljesíti az energiamérleget. Az új kernel az `i < j`
kanonikus élhez egyetlen `Q = G · (T_j − T_i)` W teljesítményt számol, ezt
`+Q`/`−Q` előjellel írja a végpontokhoz. A bemeneti `G` W/K; nincs rejtett
klímakalibráció vagy default érték. Ez a diszkrét alap a diffúziós
energiaegyensúly-modellek belső szállításával egyezik abban, hogy globális
hőforrást nem hoz létre:
[Lohmann és mtsai, Earth System Dynamics 2020](https://esd.copernicus.org/articles/11/1195/2020/).

Az orákulum a `tools/reference/conservative_heat_transport_ref.py`; két
kézzel számolt KAT-et tartalmaz: a kétcellás eset `[30, −30]` W, a
négyszomszédos háló `[55, −40, −105, 90]` W, továbbá konstans
hőmérsékletnél nulla fluxust. A generált JSON a Core-teszt `testdata`
könyvtárában van. A C# kernel véges, nemnegatív vezetőképességet és véges
hőmérsékletet vár, az éllistát bemenetkor lemásolja, szekvenciálisan járja
be, és újrafelhasználható kimeneti tömbbe ír.

**Ellenőrzés:** a célzott 3/3 Core-teszt zöld. Level-2 teljes cubed-sphere
rácson, lapéleken átmenő gradienssel a teljesítményösszeg az összes cellán
numerikus hibán belül nulla; konstans mezőn minden cella teljesítménye nulla.
`dotnet build WorldGen.sln -c Release --no-restore`: 0 hiba, 0 figyelmeztetés.
`dotnet test WorldGen.sln -c Release --no-restore --no-build`: Core **829/829**,
Viewer-LOD **662/662**, app **452/452**, CLI **24/24**. A Random123
`verify_kat.py` **9/9**; az új ND-167 vektorok a Python-orákulumból
újragenerálva bájtszinten egyeznek. A gyökér `testvectors.json` a
`gen_vectors.py` újrafuttatása után változatlan (`git diff --exit-code`).

**Határ:** a vezetőképesség geometriai és fizikai értéke, meridionális
irányfüggés, időintegrálás, hőkapacitás és felszín/levegő elosztás még
ND-168. A kernel nincs a `SurfaceTemperatureField`-hez kötve, így nem
változtat seedkimenetet, checkpointot, biome-ot vagy Unity-látványt.

**Becslés:** az A7 megvalósított adatútja továbbra is durván **97%**;
az ND-167 numerikus részfeladat kész. Erre a körre durván **1–2 munkaóra**,
az ND-168 fizikai paraméterezésére, integrálására, A/B-mérésére és élő
Unity-átvételére további **12–25 munkaóra** tervezési becslés. Nincs időnapló.
