# 2026-09-22 — A4: a folyómunka és a terep szálkészlet-versengése (ND-132)

## Kiindulópont és az első javítás kudarca

A felhasználó az első javítás után ismét kb. 30 s-os deep-time újraépítést
mért. A `PerfLog_20260922_160327.txt` megerősíti: hideg Build 24 932,7 ms,
meleg Build 27 001,6 ms. A meleg klasszifikáció önmagában 20 896,3 ms.
Az új `riverSources(cacheHit=True)` és `riverRefinement` logok bizonyítják,
hogy már az első javított kód futott; nem régi assembly okozta a panaszt.

Az első változat megszakíthatóvá tette a folyó-nyomkövetést és kivette a
szinkron durva folyóhálózatot, de továbbra is közvetlenül a statikus terep
előtt indított új, korlátlan `Parallel.For` folyómunkát a közös ThreadPoolban.
A megszakítás ráadásul csak a Build késői szakaszában történt. A korábbi
„gyökérok bizonyított / javítás kész” megfogalmazás nem volt megalapozott.

## Második változat

- Megszakítás már a `Build()` belépésénél, a korai visszatérési ág előtt.
- Új folyómunka csak a szinkron Build és a panel-frissítési esemény után.
- A viewer a meglévő szekvenciális Core-nyomkövetőt külön `LongRunning`
  feladatban használja. A terep ThreadPooljába nem küld folyó-iterációkat.
- A token a szekvenciális és párhuzamos API-ból is eljut a nyomkövető és
  spillway-keresés ciklusaihoz. A számítási sorrend és képletek változatlanok.
- A forrás-cache az immutábilis csapadék-snapshothoz kötött: a korábbi kézi
  kulcs kihagyta a referencia-napot, forgási és keringési periódust.
- Letiltáskor/megszűnéskor leállítási jelzés; visszakapcsoláskor szükség
  esetén új Build. A tokenforrás felszabadítása a feladat befejezése után
  történik. Elhagyott feladat kivétele is megfigyelt marad.
- Külön PerfLog a folyómunka indításáról, megszakításáról, elkészüléséről és
  a kész nyomvonal mesh-ének felépítéséről.

A számítás idejére a folyóvonalak hiányoznak. A szekvenciális folyómunka
teljes elkészülési ideje nincs megmérve, és lehet hosszabb a régi párhuzamos
útnál. Ezt a rövidebb szinkron Build idejétől külön kell értékelni.

## Reprodukció a Unity saját Mono-futtatójában

Eszköz: `tools/diagnostics/probe-river-scheduling.ps1`. Nem indítja és nem
vezérli az Editort. Debug Core DLL, 16 logikai processzor, kanonikus seed,
96 valós medenceforrás. Az előtérmunka 393 216 cache-elt terrain-bázist,
napi hőmérsékletet és zajt értékel ki; nem tartalmaz Unity mesh-feltöltést.

| Mód | Három előtérmenet (ms) |
|---|---|
| `baseline`, folyómunka nélkül | 1555,4 / 1540,8 / 1572,4 |
| `old`, közös ThreadPool | 2136,1 / **28 345,3** / 1511,9 |
| `dedicated`, külön szálas folyómunka | 1615,1 / 1653,9 / 1666,9 |
| `dedicated`, ismétlés a mentett parancsból | 1676,5 / 1703,8 / 1617,0 |

A régi mód második menetét a 30 s-nál, KÜLÖN szálról küldött folyó-
megszakítás szabadította fel. A harmadik már visszaállt a kontroll idejére.
Az ellenőrzőösszeg minden befejezett menetben pontosan
`119857806.39517573`. Két előzetes, külön watchdog nélküli régi futás
második menetét >60 s várakozás után kézzel megszakítottuk; a
ThreadPool-időzítős `CancelAfter(30 s)` sem oldotta fel időben a várakozást.
A végleges próba ezért dedikált watchdog-szálat használ.

Futtatás a repó gyökeréből, egymás után, nem párhuzamosan:

```powershell
./tools/diagnostics/probe-river-scheduling.ps1 -Mode baseline
./tools/diagnostics/probe-river-scheduling.ps1 -Mode old
./tools/diagnostics/probe-river-scheduling.ps1 -Mode dedicated
```

## Ellenőrzés és nyitott munka

- Célzott Core-tesztek: 4/4 zöld. Előre megszakított kérés; megszakítás a
  forrás kiválasztása UTÁN, mindkét hálózatépítőn át; szekvenciális és
  párhuzamos nyomvonalak, megállási okok, összefolyások és vízhozam-súlyok
  egyezése engedélyezett, de nem megszakított tokennel.
- `dotnet build WorldGen.sln --no-restore`: 0 hiba, 0 figyelmeztetés.
- Külön viewer-forrásfordítás Unity DLL-ek ellen: 0 hiba, 122 meglévő
  nullable-annotációs figyelmeztetés. Ez nem élő Editor-fordítás.
- Unity-független viewer-LOD tesztcsomag: 490/490 zöld.
- Új teljes Unity Build-/vizuális mérés még nincs. A legutolsó élő mérés
  továbbra is 27,00 s. Az A4 nincs lezárva, az <1 s cél nem teljesült.
- Következő bizonyíték: az új logban a `[ND-132 river start]` a
  `Build() TELJES` után jelenjen meg; a második deep-time lépésnél a
  klasszifikáció ideje, a teljes Build és a későbbi river-mesh ideje külön
  értékelendő. Ezután folytatható a statikus emit és az A5 allokációprofil.

Commit és push nem készült.

## A4 lezárása (2026-09-22, utólagos állapot)

A frissebb `PerfLog_20260922_163455.txt` 12 meleg Buildje 2,251–2,635 s,
átlaga 2,479 s. A felhasználó kérésére A4 lezárva; a korábbi „új log még
nincs” megjegyzések a fenti átadási pillanatképre vonatkoznak. A <1 s küszöb
nem igazolt. Folytatás: [A5 profilozás](2026-09-22-deep-time-allocation-profile.md).
