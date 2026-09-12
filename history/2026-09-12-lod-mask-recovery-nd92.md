# 2026-09-12 — ND-92, terepmaszk commit-hibafallback

A felhasználó explicit subagentet kért a következő tile-feladatra, miközben
a főszál a régóta ismételt M9-becslést auditálja. A választott részfeladat
az ND-89 CPU/GPU-maszk részleges feltöltési hibájának igazolása és szűk
javítása. Kód előtti döntés: ND-92; architektúra 3.24.

A javítás előtt három eltérő megszakítási pontot szimuláló teszt mind
elbukott: a CPU által már visszaállítottnak hitt, de GPU-n még rejtett
quad kimaradt a SetHidden(empty) fallbackból. Az explicit RestoreAll a
teljes eredeti indexbuffert állítja vissza, a viewer hibaága teljesen
feltölti, és csak siker után publikál diagnosztikai snapshotot. Második
hiba esetén a dinamikus réteg kikapcsolása és cache-invalidálás is lefut,
a két kivétel együtt marad. A víz kikapcsolása önmagában nem igazol
sikeres statikus maszkhelyreállítást; a Build továbbra is külön üríti az
új vízmesh diagnosztikáját. A főszál review-ja alapján ugyanaz a bizonyított
hibaosztály a vízmaszk teljes fallback-helyreállítását is indokolta: ez is
RestoreAll-t használ, a két réteg helyreállítási kísérlete egymástól független.

Ellenőrzés: új .NET 5/5, teljes LOD 295/295 Debug/Release; solution build
0 warning/0 hiba; Unity Assembly-CSharp 83 korábbi warning/0 hiba;
Editor assembly 4 korábbi warning/0 hiba. Négy új valódi viewer catch-
integrációs Editor-eset csak fordított, **nem futtatott**. A főszál friss
Core-futása 384/384 sikeres, a korábbi kilenc referenciahibát nem vittük
tovább aktuális blokkolóként. Nem volt Core/scene/relief/kamera/küszöb-
módosítás e feladatban; másik szál változásai megmaradtak. Nincs commit/push.

[Részletes bizonyítékok és tesztkapu](../docs/reviews/lod-mask-recovery-nd92-2026-09-12.md).
Ez hibabiztonsági javítás, nem a normál zoomélesség/késés megoldása.
A becslést a főszál új [M9-auditja](../docs/reviews/m9-progress-audit-2026-09-12.md)
vezeti, nem a régi 60–70% / 8–20 óra sáv. E csomag durva, nem mért
ráfordítás-egyenértéke 1–2 óra; a natív Editor-futtatás még nyitott.
