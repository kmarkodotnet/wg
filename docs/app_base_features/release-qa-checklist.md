# Release-QA ellenőrzőlista (WF-QA-001…003)

Kézi ellenőrzés, a felhasználó végzi. Minden kiadásjelöltnél **tiszta Windows-környezetben**
(új felhasználói fiók vagy VM, korábbi `LocalLow\<Company>\<Product>` mappa nélkül), a
portable ZIP-ből ÉS a telepítőből is. Ahol valami nem stimmel, a `Logs` mappa legfrissebb
`worldgen-*.log` fájlját és az esetleges `error-*.txt`-t mellékeld.

Előfeltétel: a menü-, settings- és mentésnézetek (ND-110), valamint a teljes Core/session-mentéskötés elkészültek.
Az ND-108 generátorverzió-kapu 2026-09-22-én elkészült; önmagában nem állapotszerializáló vagy Save/Load UI.
Amíg nincsenek kész, csak az A, B és F szakasz futtatható.

## A. Csomag és telepítés

- [ ] A ZIP kicsomagolva, a `WorldGen\WorldGen.exe` elindul, telepítés nélkül.
- [ ] A ZIP nem tartalmaz `*_DoNotShip` / `*_DontShip` mappát; a `.sha256` egyezik.
- [ ] A telepítő: választható könyvtár, Start menü elem, opcionális asztali ikon, indítás a végén.
- [ ] A Programok listájában egy „WorldGen” bejegyzés van, helyes verzióval és kiadóval.
- [ ] Az újabb verzió telepítése a meglévőre frissít (nem második bejegyzés), a mentések megmaradnak.
- [ ] Uninstall: a kérdésre „Nem” → a `LocalLow\…` mappa megmarad; újratelepítés után a mentés betölthető.
- [ ] Uninstall „Igen” válasszal → a felhasználói adatok törlődnek, a telepítési könyvtár is.

## B. Első indítás és környezet

- [ ] Az első indításkor létrejön: `Saves`, `Settings`, `Screenshots`, `Logs`, `Cache`, `Temp`.
- [ ] A napló első sorai: termék, verzió, build, csatorna, OS, CPU, RAM, GPU, VRAM, kijelző.
- [ ] Welcome képernyő az első indításkor; a „Don't show again” után nem jelenik meg újra.
- [ ] A főmenüben látszik a verzió (`v0.1.0-alpha…`), és egyezik a naplóval.

## C. Teljes folyamat (WF-QA-001)

Install → Launch → Create world → Play → Save → Exit → Restart → Load.

- [ ] New World: seed másolható; ugyanaz a seed + paraméterek azonos világot ad (összevetés képpel és a panel értékeivel).
- [ ] Hibás paraméterrel a Start nem indítható.
- [ ] Loading képernyő: halad, nem fagy le, a szövegek a valós szakaszokat követik.
- [ ] Save: toast; a mentés megjelenik a Load listában (név, kor, létrehozva, utoljára, seed).
- [ ] Kilépés után újraindítva a Continue aktív, és ugyanazt a világot tölti be.
- [ ] Mentés közbeni kényszerített bezárás (Task Manager) után az előző mentés továbbra is betölthető.

## D. Session-életciklus (WF-QA-002)

Main Menu → World A → Main Menu → World B → Main Menu → World C.

- [ ] Nincs duplikált UI, hang, kamera vagy napló-feliratkozás (a napló `State` sorai rendben követik egymást).
- [ ] A memóriahasználat (Task Manager / Profiler) a harmadik világ után sem nő tartósan.
- [ ] Az ESC mindenhol konzisztens: játékban pause, pause-ban resume, almenüben vissza, dialógusnál cancel.
- [ ] A nem mentett haladásra figyelmeztet: Return to Main Menu, Load, Quit, ablak bezárása.

## E. Deep Time regresszió (WF-QA-003)

Seed = X (rögzítsd): 0 → 1 Ma → 100 Ma → 1 Ga.

- [ ] Minden időpontban képernyőkép és a világ-panel értékei feljegyezve.
- [ ] Újraindítás után ugyanazzal a seeddel és állapottal ugyanezek az értékek és képek jönnek.
- [ ] (Fejlesztői) a `WorldStateHash` egyezik a két futás között.

## F. Beállítások és rendszer

- [ ] Fullscreen / Borderless / Windowed váltás; a felbontásváltás 15 s alatt megerősítés nélkül visszaáll.
- [ ] VSync, FPS-limit, quality-szint hat (FPS-számláló: F3).
- [ ] Fókuszvesztéskor a háttér-FPS-limit érvényesül; beállítástól függően a szimuláció megáll.
- [ ] Hangerők (Master/Music/Ambient/Effects/UI) és a némítás hatnak; újraindítás után megmaradnak.
- [ ] Sérült `settings.json` (kézzel elrontva) → az alkalmazás elindul alapértékekkel, a sérült fájl `settings.corrupt-*.json` néven megmarad.
- [ ] F12 képernyőkép és Shift+F12 UI nélküli kép a `Screenshots` mappában, toasttal.
- [ ] Nem várt kivételnél az alkalmazás tovább fut, egyszer értesít, és a napló nem telik meg ismétlődő soraival.
