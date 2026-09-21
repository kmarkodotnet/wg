# 2026-09-21 — ND-128: a `useGpuGeometry` út törlése (a todo2 A1 tétele)

**Feladat:** a rangsor első sora — a GPU-geometria ág elavult elevációt
használ. Az ND-120 (ugyanaznap reggel) a GPU-*osztályozó* utat már törölte,
de a shadert megtartotta, mert a geometria-ág még hívta. Ez a menet zárja be
a rést.

## Amit a vizsgálat hozzátett a todo2-ben rögzített diagnózishoz

A todo2 A1 sora egy helyességi hibát írt le (a tile-ok ~22%-a a tengerszint
rossz oldalára kerülne). A kód olvasása után **négy** érv állt össze, és
együtt döntöttek:

1. **A GPU-ág mérve LASSABB**, nem gyorsabb: a mező saját doksija rögzíti a
   2026-09-02-i élő mérést (~50 s/újraépítés). Ok: a kernel tile-onként
   külön számolja mind a 4 sarkot + a középpontot, míg a CPU-út a
   sarok-cache-en keresztül a megosztott sarkakat csak egyszer. Vagyis a
   „felzárkóztatás" egy negatív előjelű gyorsításért vállalt volna állandó
   karbantartást.
2. **Nincs benne geomorphing** — bekapcsolva „pattanás" látszana.
3. **Öt másik, ma is fejlesztett utat béklyózott** `!useGpuGeometry`
   feltételként: aszinkron mesh-újraépítés, szakaszolt terep-upload, önálló
   víz-finomítás, terep-LOD-proxy, ND-75 kirajzolt-méret diagnosztika.
4. **A `RebuildAdaptiveMesh` CPU-ága ELÉRHETETLEN kód volt.** A metódus
   elején `if (!useGpuGeometry || compute == null) { CPU; return; }` állt,
   tehát a lentebbi `else` ág (klasszifikáció + sarkak + emit-ciklus) sosem
   futhatott. ~90 sor halott kód, ami ráadásul úgy festett, mintha a CPU-út
   két helyen élne.

## Amit tettem

- Törölve: `useGpuGeometry` és `tileClassificationCompute` mező,
  `_gpuGeometryGenerator`, `EmitAdaptiveTilesGpu`,
  `Assets/Scripts/Viewer/Gpu/` (generátor + `TileClassification.compute`,
  `.meta`-kkal), a jelenetbeli két sor.
- A `RebuildAdaptiveMesh` **három sorra** egyszerűsödött (cut → CPU-buffer →
  alkalmazás); a halott ág elment vele.
- Az öt `!useGpuGeometry` feltétel eltűnt.
- `docs/10-tile-pipeline.md` §12 átírva: a fejezet eddig azt állította, hogy
  a jelenetben `useGpuClassification: 1` **aktív** — ez az ND-120 óta nem
  igaz volt. Most azt írja le, mi volt, miért ment el, és mit kellene tudnia
  egy jövőbeli GPU-útnak.
- `GpuShaderElevationParityTests` **marad**, újrakeretezve: innentől minden
  jövőbeli „egyszerűsített" eleváció-közelítés (GPU, sütött textúra,
  LOD-proxy, mentett cache) belépési kapuja.

**Mérleg:** −398 sor a viewerben, −1113 sor a `Gpu/` mappában.

## Amit érdemes megjegyezni

1. **A duplikált világmodell-matek a gyökérok, nem a lemaradt port.** A
   shader újraírta a Threefry4x64-et (32 bites párokon) és a trigonometriát
   Taylor-sorral. Két független implementáció elcsúszik — itt két
   Core-újítással (ND-52, ND-90) csúszott el. Nem az a tanulság, hogy „nem
   vezették utána", hanem hogy egy ilyen párhuzamos implementáció
   karbantartási kötelezettséget teremt, amit senki nem vállalt be írásban.
2. **A halott kód eltakarta magát.** A `RebuildAdaptiveMesh` CPU-ága
   olvasásra teljesen hihetőnek tűnt; csak a korai `return` feltételének
   végiggondolása mutatta meg, hogy elérhetetlen. Ha csak a mezőt töröltem
   volna, a halott ág „élővé" vált volna — és egy régi, chunk-mentes,
   proxy-mentes úton rajzolt volna.
3. **A „kapcsolóval kikapcsolt" kockázat nem nulla kockázat.** Egy
   Inspector-kattintás választotta el a felhasználót egy másik bolygótól.

## Állapot

- Core tesztek: a teljes készlet **550 ✓** ma futott (ND-127); azóta a
  `src/` érintetlen, a Core-oldali változás egyetlen teszt-komment. A
  `GpuShaderElevationParityTests` a törlés UTÁN újra lefutott: **3/3 zöld**,
  a ~22%-os küszöb ma is teljesül.
- Offline Unity fordítási kapuk: 0 hiba mindkettőn
  (`WorldGen.Viewer.Compile`, `WorldGen.App.UnityBinding.Compile`).
- **Unity Editor-fordítás: MÉG NEM IGAZOLT.** Az Editor a törlés idején
  foglalt volt (a `Logs/PerfLog_20260921_203109.txt` szerint 20:31–21:16
  között Play-menet futott), a pipeline-parancsok 60 s-os időtúllépéssel
  tértek vissza, és a `recompile` nem tudott lefutni. A konzolban ebből két
  `Failed to handle /api/exec request: Main thread operation timed out`
  hibasor keletkezett — ezek a PIPELINE időtúllépései, nem fordítási hibák.
  **Teendő a következő szabad Editor-állapotban:** `AssetDatabase.Refresh` +
  recompile + konzol-ellenőrzés. A `Gpu/` mappa törlése asset-import, amit a
  glob-alapú offline kapu definíció szerint nem lát (ld. CLAUDE.md).
- **Élő Play-ellenőrzés nem szükséges** ehhez a tételhez: a törölt ág nem
  futott (alapból ki, a jelenetben is `0`), tehát a képen semmi nem változik.
  Ha mégis eltérést látsz, az önmagában információ.
