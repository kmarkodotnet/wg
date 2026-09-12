# readmefirst.md — mit olvass fel egy ÚJ session elején

Ez a lista mondja meg, milyen dokumentumokat kell felolvastatni egy új
munkamenet kezdetén, hogy azonnal produktívan tudjunk együtt dolgozni. A
sorrend prioritás szerinti: a **Kötelező** blokk nélkül ne kezdj érdemi munkát.

---

## 1. Kötelező (mindig, minden session elején)

Claude-visszavételkor először az [aktuális Codex → Claude átadást](kt_2_co2cl.md)
olvasd el, az ott megadott kód-/teszt-/logellenőrzési sorrenddel együtt.
Az [első Claude → Codex átadás](kt_1_cl2co.md) történeti pillanatkép,
nem aktuális branch-/teljesítményállapot. Az `AGENTS.md` is projektutasítás.

| Fájl | Cél / mit ad |
|---|---|
| **`CLAUDE.md`** | A projekt működési szabályzata: a négy invariáns (I1-I4 determinizmus, tiszta random, pixel/panel a modellből), a lebegőpontos determinizmus-szabályok, a `src/` motorfüggetlensége (netstandard2.1 + C# 9), a munkamódszer (kód előtt referencia/dokumentáció), a tesztelési elvárások, a nyelv (kód angol, beszéd/komment magyar) és az aktuális **Állapot**. **Ez felülír minden alapértelmezett viselkedést.** |
| **`docs/05-milestones.md`** | Az M0-M13 mérföldkövek státusztáblája: mi kész (numerikusan/vizuálisan), mi halasztva. Innen tudod, hol tartunk. |
| **`docs/backlog.md`** | A MÉG HÁTRALÉVŐ feladatok mérföldkövenként, prioritással/komplexitással + hogy kell-e hozzá felhasználói vizuális ellenőrzés. A „mit csináljunk most" forrása. |
| **`history/` legutóbbi 1-2 fájlja** | Session-napló (a legfrissebb dátumú `history/YYYY-MM-DD-*.md`). Mit csináltunk a múltkor, mi a nyitott szál. Munka után ide is írunk. A `history/README.md` a napló használatát írja le. |

---

## 2. Erősen ajánlott (a döntések és a felépítés megértéséhez)

| Fájl | Cél / mit ad |
|---|---|
| **`docs/04-decisions.md`** | Az ND-számozott architekturális döntések (nyitott ÉS lezárt): pl. ND-01 stack (Unity 6 + HDRP), ND-23 transzcendens-determinizmus, ND-36/37/38 domborzat/tengerszint, ND-40/46/47 LOD, ND-41-45 klíma/hidrológia/tektonika. Mielőtt bármilyen seed-törő vagy vitás dolgot módosítanál, ezt nézd meg. |
| **`docs/01-architecture.md`** | A rendszer felépítése: a három nézetszint (bolygó/kontinens/régió), a modultérkép, az adatfolyam, és a panel-mezők visszakövethető számítási lánca (I4). |

---

## 3. Referencia (nem kell végigolvasni, de tudni kell róla)

| Fájl | Cél / mit ad |
|---|---|
| **`docs/00-spec-v1.0.md`** | A teljes v1.0 specifikáció (nagy). Nem session-eleji olvasmány, hanem amikor egy konkrét modul részleteire (§-szám) van szükség. |
| **`docs/02-fidelity-strategy.md`** | Hűség-stratégia: mennyire fizikai vs. hihető, hol engedünk közelítést. |
| **`docs/03-unity-hdrp-evaluation.md`** | Az ND-01-hez tartozó Unity/HDRP-kiértékelés háttere. |
| **`unity/WorldGenViewer/Assets/Scripts/Viewer/README.md`** | A Unity-viewer (`PlanetGridMesh` stb.) oldali jegyzetek. |
| **`KICKOFF.md`** | Az eredeti indító-brief (történeti kontextus). |
| **`VERSION`** | Aktuális checkpoint-verzió (jelenleg `0.11.0-dev`). Git-tag is van hozzá (`git tag`). |

---

## 4. Ami MAGÁTÓL betöltődik (nem kell külön felolvastatni)

- **Auto-memória**: `C:\Users\Krisz\.claude\projects\F--Claude-wg\memory\MEMORY.md` és a hozzá tartozó memória-fájlok minden session elején kontextusba kerülnek (felhasználói preferenciák, visszajelzések, projekt-tények). Nem kell külön megnyitni.

---

## Gyors indító-mondat egy új sessionhöz

> „Olvasd fel a `readmefirst.md` szerinti kötelező doksikat (`CLAUDE.md`,
> `docs/05-milestones.md`, `docs/backlog.md`, a legfrissebb `history/`-fájlt),
> foglald össze hol tartunk, és javasolj következő lépést a backlogból."

---

## Munkamódszer-emlékeztető (a CLAUDE.md-ből, kiemelve)

- **Kód előtt referencia/dokumentáció.** Új numerikus algoritmusnál: Python
  orákulum (`tools/reference/`) → verifikálás → tesztvektorok → C# → C# a
  vektorokhoz mérve.
- **A Python referencia az igazság**, ha a C# eltér.
- **Nyitott döntést dokumentálj** (`docs/04-decisions.md`, új ND-szám), ne oldd
  meg csendben, főleg ha seed-törő lenne.
- **Determinizmus szent** (I1): ugyanaz a seed + verzió → bitre ugyanaz a világ.
- A **Unity-viewer változásokat** offline nem lehet vizuálisan ellenőrizni — azt
  a felhasználó nézi meg élőben (screenshot/visszajelzés alapján finomítunk).
