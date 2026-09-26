# 2026-09-23 — A8/3 korlátos párhuzamos Core-út

A felhasználó az A8/2 után a következő feladat folytatását kérte. Az A8/2
kapuját a scene-pontos párhuzamos Core-mérés újranyitotta; a kör szerinti
forrássorrend elvetése változatlan. A pontos konfiguráció, mérési táblázat,
lenyomatok és újrafuttatás az [A8/3 eredményben](../docs/plans/a8/03-core-megvalositas-es-differencialis-tesztek-eredmény.md)
szerepel; döntés: [ND-146](../docs/04-decisions.md).

A Core meglévő spekulatív, kanonikus commitot használó párhuzamos API-ja
opcionális `maxDegreeOfParallelism` paramétert kapott. Az alapértelmezett
`-1` megtartja a korábbi működést; 4 workerrel t=0 70,487 s, ismétlésben
67,347 s (222,203 s processz-CPU-idő), t=22 Myr 47,434 s. A teljes
96 forrásos bitpontos SHA-256 minden menetben egyezik az A8/1
szekvenciális lenyomatával. A korlátlan t=0 menet 26,378 s, de a 16 logikai
szál ND-132 szerinti viewer-versengési kockázata miatt nem viewer-jelölt.
Két workerrel t=0 129,422 s; ez kevés nyereség.

Nyers lenyomatok: [korlátlan t=0](a8-2026-09-23/t0-parallel-unbounded-fingerprint.txt),
[2-worker t=0](a8-2026-09-23/t0-parallel2-fingerprint.txt),
[4-worker t=0](a8-2026-09-23/t0-parallel4-fingerprint.txt),
[4-worker ismétlés](a8-2026-09-23/t0-parallel4-repeat-fingerprint.txt),
[4-worker t=22](a8-2026-09-23/t22-parallel4-fingerprint.txt).

Célzott Core-tesztek 13/13; teljes Release Core 600/600; solution build
0 hiba és 0 figyelmeztetés. Új numerikus kimenet és generátorverzió nincs.
Viewer-hívás nem változott; a következő A8/4 feladata a 4-worker bekötés,
a meglévő ND-145 előnézet megtartása és az élő Unity-teljesítmény/Play
ellenőrzés. A legújabb elérhető PerfLog továbbra is a javítás előtti
2026-09-23 11:38-as fájl, így a folyó-előnézet láthatósága nyitott.

Branch `main`, kiinduló HEAD `e3713bf`, előzetesen is módosított munkafa.
Commit és push nem készült.
