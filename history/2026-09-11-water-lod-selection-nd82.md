# ND-82 — A vízfelszín saját LOD-jának első kapuja

Felhasználói döntés: az első zoomok beragadása maradjon backlog, következzen
másik tervezett tile-feladat. A backlogban explicit halasztottuk ezt és a
hozzá tartozó küszöb-/mozgókamerás selection munkát. Következő önálló
feladatként a vízfelszín saját finomításának első, külön ellenőrizhető
kiválasztási/geometriai kapuját valósítottuk meg; ND-82 implementáció előtt.

`WaterLodSource`: immutábilis, sűrű base-vízmaszk, a renderer tengerszintjén
állandó sugarú proxy; saját kvadfa-cut és költségkeret, terepmodell-hívás
nélkül. `WaterLodSelection`: csak olvasható, rendezett levél-/gyökérsnapshot,
teljes fedés, halasztott osztások és külön közösél-resolver. A kamera a
meglévő immutábilis view-ból olvasható, nem kell eltérő pozíciót átadni.

**A runtime még nem használja.** A vízszín forrása, statikus vízmaszk,
terrain-vezérelt régi víz kizárása és az atomikus vízmesh-csere a következő
kapu. Így most nincs új számítási teher vagy vizuális változás. A 12/10 px
terepcél, a scene, a Core, a Python-orákulum és a tesztvektorok változatlanok.
Branch `codex-handoff`, HEAD `195875e`; meglévő dirty módosítások megőrizve,
nincs commit/push. Az új Unity meta GUID egyedi az Assets fájljai között.

Ellenőrzés:

- 24 új víz-LOD teszteset: üres/vegyes vízmaszk, teljes fedés, 1–4000-es
  keretek, konvergencia, visszazoom, paraméterhatás, snapshot-védelem,
  megszakítás, párhuzamos/szekvenciális egyezés, határillesztés, külön lapok
  közös pontjai, teljes L8-as szintetikus forrás három kameratávolságnál.
- `dotnet build WorldGen.sln --no-restore -p:_EnableDefaultWindowsPlatform=false`:
  **0 hiba, 0 figyelmeztetés**.
- Teljes Debug regresszió: **617/617** = Core 381 + LOD 229 + CLI 7.
- Teljes LOD Release: **229/229**. Az utolsó, szigorított lapközi ellenőrzés
  után a 24 célzott teszt Debug és Release módban külön újrafuttatva, sikeres.
- Unity-forrásfordítás a meglévő helyi validation targettel: **0 hiba,
  83 meglévő figyelmeztetés**. A target felveszi az új fájlt is; a Unity
  assembly a `noEngineReferences` LOD assembly marad.
- `git diff --check`: rendben.
- Python KAT/vektor-regenerálás nem futott: nincs telepített Python;
  szimulációs kód nem változott.

[Jelentés és a következő bekötési kapu](../docs/reviews/water-lod-selection-nd82-2026-09-11.md).
Ezen a ponton nincs értelme új Play-próbát kérni, még nem változott a kép.
Nem állítunk teljes vízfinomítást, vizuális elfogadást vagy gyorsulást.

M9 tartalmi becslés továbbra is **60–70%**. Durva, nem mért ráfordítás-
egyenérték: **2–4 óra** erre a kapura, víz renderbekötése/validáció **4–8 óra**;
teljes fennmaradó zoom-/megjelenítési munka **8–20 óra**.
