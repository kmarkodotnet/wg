# Kiadási folyamat (Windows x64)

Tervdokumentum: `docs/09-app-shell-architecture.md`; döntések: ND-111 (identitás, verzió),
ND-112 (csomagolás). QA: `docs/app_base_features/release-qa-checklist.md`.

## 0. Egyszeri előfeltételek

- Unity 6000.0.77f1 (a `ProjectSettings/ProjectVersion.txt` szerint).
- Inno Setup 6.3 vagy újabb, csak a telepítőhöz (`ISCC.exe`).
- **Végleges termék- és cégnév** a `release-identity.json`-ban, az első nyilvános
  kiadás ELŐTT. Ezekből képződik a felhasználói adatok helye:
  `%USERPROFILE%\AppData\LocalLow\<companyName>\<productName>`.
  Később átnevezve a mentések és beállítások migráció nélkül „eltűnnek”.

## 1. Verzió

`tools/release/release-identity.json` → `version` (SemVer, pl. `0.1.0-alpha`).
A repo-gyökér `VERSION` fájlja fejlesztési checkpoint, nem kiadási verzió.

## 2. Unity build

Editorból: **WorldGen → Build → Windows x64 Release**.

Batch módban:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.0.77f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath unity/WorldGenViewer `
  -executeMethod WorldGen.App.EditorTools.WorldGenBuild.BuildWindowsRelease `
  -buildNumber 1 -logFile artifacts/release/unity-build.log
```

Kimenet: `artifacts/builds/<exe>-<verzió>-win64/`. Development Build: kikapcsolva.
A build idejére a PlayerSettings termék/cég/verzió értékei az identitásfájlból jönnek,
utána visszaállnak; a `StreamingAssets/build-info.json` csak a buildben marad meg.
Ha az `EditorBuildSettings` scene-listája üres, a `fallbackScenes` kerül a buildbe.

## 3. Portable ZIP

```powershell
powershell -ExecutionPolicy Bypass -File tools/release/package-portable.ps1 `
  -BuildDirectory artifacts/builds/WorldGen-0.1.0-alpha-win64
```

Kimenet: `artifacts/release/WorldGen-0.1.0-alpha-win64.zip` és `.sha256`.
A Unity `*_DoNotShip` / `*_DontShip` mappái kimaradnak.

## 4. Telepítő

```powershell
powershell -ExecutionPolicy Bypass -File tools/release/build-installer.ps1 -BuildNumber 1
```

Kimenet: `artifacts/release/WorldGen-0.1.0-alpha-win64-setup.exe`.
Az `AppId` a `WorldGen.iss`-ben rögzített, soha nem módosítható.
Az uninstall rákérdez a felhasználói adatok törlésére, alapértelmezett válasz: Nem.

## 5. QA

`docs/app_base_features/release-qa-checklist.md` — tiszta gépen, a ZIP-ből és a telepítőből is.

## Offline fordítási ellenőrzés Unity nélkül

```powershell
dotnet test tests/WorldGen.App.Foundation.Tests            # Foundation, CI-ben is fut
dotnet build tests/WorldGen.App.UnityBinding.Compile       # Unity-kötés + build-script, helyi Unity DLL-ekkel
```

Az utóbbi nincs a `WorldGen.sln`-ben, mert a CI-gépeken nincs Unity. Nem helyettesíti a
Unity saját fordítását (asmdef-szabályok, define-ok), csak korán megfogja az API- és
C#-hibákat.
