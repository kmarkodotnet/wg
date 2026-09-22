# A6 — ND-108 generátorverzió és mentési kompatibilitás

Kiindulás: `main`, `4e4ab67` (`deeptime fix`), tiszta munkafa. Az A5
változásai már ebben a commitban voltak; a jelen munka nem módosítja őket.
Felhasználói kérés: a `todo2.md` A6 megvalósítása.

## Probléma és döntés

A Core-nak nem volt mentéshez használható generátorazonosítója. A CLI
`.worldpkg` v2 csak a fájlformátumot vizsgálta; az app `AppBootstrap`
Inspector-mezőből, alapból `unversioned-dev` helyőrzővel építette a
kompatibilitási szabályt. A `SaveRepository.List` besorolta a mentést,
de a közvetlen `Load` nem ellenőrizte a kompatibilitást.

Az ND-108 A opciója az implementáció előtt rögzítve: egy közös,
monoton emelendő, szöveges egész azonosító a Core-ban. Első értéke `"1"`;
ez a 2026-09-22-i numerikus modell alapvonala, nem régi mentések utólagos
kompatibilissé nyilvánítása. Az alkalmazás kiadási verziója és a fájl
formátuma külön marad. Seed-/világadat-törő változáskor a
`WorldGeneratorVersion.Current` és az érintett ND frissítendő; bitazonos
optimalizáláskor nem. Ezt a `CLAUDE.md` és `VERSION` is rögzíti.

## Megvalósítás

- `WorldGen.Core/Persistence/WorldGeneratorVersion.cs`: kizárólag konstans;
  nincs új numerikus algoritmus, random-bemenet vagy modellváltozás.
- CLI `.worldpkg` **v3**: `WorldGeneratorVersion` mező, a `Create` adja
  az aktuális értéket. Hiányzó formátum/generátor olvasáskor nem kaphat
  aktuális alapértéket. `Load`, `Save`, `VerifyByRecomputation` is ellenőriz,
  még írás/számítás előtt. A külön formátumemelés a régi olvasót is
  kizárja, amely különben figyelmen kívül hagyná az új generátormezőt.
  V1: továbbra is ND-90-hiba; v2: külön ND-108-hiba; nincs automatikus migráció.
- `CoreSavePolicy`: kis, Unity API nélküli adapter a UnityBindingben.
  `CreateHeader` a Core-verzióval bélyegzett új fejlécet ad; a többi adat
  kitöltése a későbbi session-host feladata. `Evaluate` a meglévő
  Foundation-kompatibilitást a Core-konstanssal hívja. Az `AppBootstrap`
  a valódi adaptert használja, a szerkeszthető helyőrző megszűnt.
- A Foundation **nem** kapott Core-referenciát. A UnityBinding asmdef és
  offline kapu igen; az adapter a Foundation-tesztprojektbe linkelve,
  külön tesztoldali Core-referenciával ellenőrizhető Unity nélkül is.
- `SaveRepository.Load`: a CRC-ellenőrzött fejléc után, a szekciótábla
  és payload előtt újra ellenőriz. Eltérés → `SaveIncompatibleException`,
  benne a besorolás és meglévő lokalizációs kulcs. Nincs korábbi listázásra
  támaszkodás vagy második fájlmegnyitás. Nem kereshető streammel is működik.
  `LoadHeader` külön, fejléc-only API a konfiguráció újrafelhasználásához.
- A `.wgsave` formátum maradt 1: már volt generátormezője. A codec
  hiányzó azonosítót üresen hagy; a jelenlegi policy ilyen mentést
  `ConfigurationOnly`-nak sorol. A nyers konténercodec továbbra is alkalmas
  idegen verziójú fájlok eszközszintű olvasására; állapotbetöltésre a
  repository ellenőrzött útja használandó.

## Bizonyíték

- Debug és Release solution build: **0 hiba, 0 figyelmeztetés**.
- App Foundation: **451/451**, CLI: **23/23**, mindkettő Debug és Release.
  Ebből 13 új app- és 15 új CLI-eset. Aktuális round-trip, korábbi/jövőbeli,
  üres/hiányzó/helyőrző verzió, formátum elsőbbsége, app-verzió függetlensége,
  közvetlen API-megkerülés, mentés előtti védelem, megváltozott fájl,
  payload előtti elutasítás, CRC-kapu és nem kereshető stream ellenőrizve.
- Viewer-LOD Debug: **490/490**.
- Teljes Core Debug-regresszió: **567/567**, 6 perc 32 s. A négy
  tesztassembly együtt **1531/1531**, nincs kihagyott vagy hibás teszt.
- App UnityBinding offline fordítás: **0 hiba, 0 figyelmeztetés**.
- Élő Unity `recompile_status=completed`; Console ground truth **0 hiba**,
  129 figyelmeztetés, ezért nem állítunk warningmentes Unity-fordítást.
  A két új forráshoz a Unity létrehozta a `.meta` fájlokat.
- Élő Editorban az adapter próbája: generátor `"1"`, aktuális fejléc
  `CanLoadState=true`; hiányzó verzió `false`, `ConfigurationOnly`.
  Nem indítottunk saját Play-menetet; az észlelt futó Play-t meghagytuk.
  Scene-t nem mentettünk, Player-build vagy hálózati jogosultságkérés nem volt.
- Python: **9/9 KAT**; 512 vektor újragenerálva, a verziózott fájllal
  azonos SHA-256:
  `2CB4EF00BB52934DFE682AA0D810A4850744567EFC496839B1BB956F93C74990`.
  A `python` PATH-név nem volt elérhető; a telepített `py -3` launcherrel
  futott az ellenőrzés. Referencia-/vektorfájl nem változott.
- Kész Release CLI külön fájlos próbája: `artifacts/a6-current.worldpkg`,
  seed `A7C944210000`, 20 lemez, level 1, 12,5 Myr; mentés és verify 0 kód.
  Hash: `18f7886ebb847722e9c5e1972385a502f95bad891b5a6123ab2b82403dd19e70`.
  `artifacts/a6-legacy-v2.worldpkg`: verify 1 kód, explicit ND-108-hiba.
  Mindkét diagnosztikai fájl a nem verziózott `artifacts/` alatt marad.
- Tesztjelentések: `artifacts/test-results/a6/*.trx` (helyi fájlok).

## Hatókör és következmény

A6 feloldja a generátorazonosító hiányából adódó akadályt B2, C3/D4 és C5
előtt, de nem dönt a lemezmodell módosításáról és nem implementál teljes
Save/Load UI-t vagy szimulációs állapotszerializálást. Az utóbbi feladatok
nyitva maradnak, a roadmap és a release-QA előfeltétele ezt egyértelműsíti.
A régi `.worldpkg` v2 fájlok ettől kezdve explicit elutasítottak; azokat
nem írtuk át. Az app fejlécének seed/paraméter adatai külön kiolvashatók,
de az új világ ezekből nem a régi állapot folytatása.

Ehhez a verziókapuhoz nincs szükség felhasználói Play-próbára. A későbbi
menü-/session-bekötés saját élő mentés/betöltés próbát igényel majd.
Commit/push nem készült.

**A6 / M12 generátorverzió-részfeladat: 100%, lezárva.** Nem teljes M12
vagy teljes app-mentés készültségi százalék. Durva ráfordítás **0,5–1
munkaóra**, A6-ban hátralévő **0 óra**; nem mért munkaidőnapló.
