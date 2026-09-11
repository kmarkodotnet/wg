# Zoomos felszínrészletesség vizsgálata — 2026-09-11

A felhasználó külön diagnosztikai dokumentumot kért a zoom közben késve és
egyenetlenül finomodó felszínről, a javítások várható vizuális és
teljesítményhatásával. Implementációt ebben a körben nem kért.

Elkészült a [részletes diagnózis](../docs/reviews/lod-zoom-diagnosis-2026-09-11.md),
egy önálló, a tényleges kvadfa- és chunk-forrásokat linkelő .NET mérőpróbával.
Branch: `codex-handoff`, HEAD `0d993f4`; a meglévő dirty munkapéldányt
vizsgáltuk és megőriztük. Viewer/Core/scene/shader működésváltoztatás,
commit és push nem történt.

Fő megállapítások:

- Az aktív GPU-bázisbesorolásból hiányzik a CPU-modell másodlagos zajrétege;
  ez az óceániős-szűrőn keresztül szárazföld finomítását is kizárhatja.
- A durva alapmesh végig megmarad, és a finom felszínt geometriailag
  eltakarhatja. A scene Core-mezőjével konkrét ellenpélda és mintavételi
  számszerűsítés készült; ez nem élő pixelmérés.
- Az abszolút 1 egységes mozgáskapu relatíve nagy közeli zoomváltozásokat
  hagyhat frissítés nélkül; a megállás önmagában nem garantál új cutot.
- A prioritási sor budget miatti eldobása L8-visszaeséseket okoz. A tényleges
  kódon reprodukáltuk kis kerettel és 2160 px magas nézettel is.
- A scene chunk-szintje 6, effektíven 8; a chunk-diff csak a tile-halmazt
  vizsgálja, a kamerafüggő geomorph-változást nem. A worker a változatlan
  chunkok terepadatait is előállítja, majd eldobja.
- A LOD a 100-as referencia-gömböt méri, nem a displacementes felszínt.
  A mai naplóban 37–45 ms-os főszálas feltöltések és nagy cut/render
  levélszámkülönbségek szerepelnek.

Ellenőrzés: meglévő chunk xUnit tesztek 14/14 sikeresek; a diagnosztikai
projekt Release-fordítása/futása sikeres, warning/error nélkül. A tesztprojekt
nem linkeli az AdaptiveQuadTree-t; a mérőpróba igen. Új élő Unity-képi
ellenőrzés, shaderfuttatás, teljes regresszió vagy javítás utáni FPS-mérés
nem történt. A dokumentum minden mérést az igazolható hatóköréhez köt.

A javaslat még nem új ND-döntés: először célzott élő elkülönítés, majd
hiteles besorolás/kamera-frissítés, durva–finom felületcsere/morph,
fedést őrző budget és mért idejű patch-feldolgozás következhet.
Az M9 becsült készültsége 50–60%, javítás nem történt. Durva, nem naplózott
ráfordításbecslés: diagnózis 2–4, javítás és validáció további 24–48 munkaóra;
új mikroterep vagy részlettextúra külön hatókör.
