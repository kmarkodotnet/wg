# Third-Party Notices — VÁZLAT

> **Állapot: vázlat, jogi átnézés előtt.** A felhasználó (kiadó) feladata a végleges
> ellenőrzés. A lenti tények a repóból és a helyi Unity PackageCache licencfájljaiból
> származnak (2026-09-13); ahol forrás kell, jelölve van. A kiadott buildbe és a Credits
> képernyőre az ellenőrzött változat kerül.

## WorldGen

Copyright © [év] [kiadó neve — ND-111, még nyitott]. Minden jog fenntartva.

## Unity

- **Unity Engine** (6000.0.77f1) — a Unity Terms of Service / a kiadó Unity-licence szerint.
  A „Made with Unity” splash: a `ProjectSettings` szerint jelenleg be van kapcsolva
  (`m_ShowUnitySplashScreen: 1`). Hogy kötelező-e, a kiadó Unity-előfizetésétől függ — **felhasználói döntés**.
- **Unity csomagok**, a helyi `LICENSE.md` fájlok szerint „Licensed under the Unity Companion
  License for Unity-dependent projects”:
  `com.unity.render-pipelines.high-definition`, `com.unity.render-pipelines.core`,
  `com.unity.shadergraph`, `com.unity.visualeffectgraph`, `com.unity.collections`,
  `com.unity.mathematics`, `com.unity.timeline`, `com.unity.ugui`.
- `com.unity.burst`: a `LICENSE.md` szerint a forráskód Unity Companion License, a többi rész
  eltérő feltétel alatt — **a teljes szöveget a csomag `LICENSE.md`-jéből kell átvenni.**
- Csak fejlesztői (a buildbe nem kerülő) csomagok: `com.unity.feature.development`,
  test-framework, code coverage, IDE-integrációk, profile-analyzer.

## Betűkészlet

- **Liberation Sans** (a TextMesh Pro alapbetűkészlete) — SIL Open Font License 1.1.
  „Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and
  Cousine. Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation.”
  A teljes licencszöveg: `unity/WorldGenViewer/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.
  **Az OFL a licencszöveg mellékelését kéri a terjesztéskor.** Ha a végleges UI más betűkészletet
  használ, ez a tétel változik.

## Algoritmusok

- **Threefry-4x64 (Random123, D. E. Shaw Research)** — a `src/WorldGen.Core/Random/Threefry4x64.cs`
  a Random123 hivatalos KAT-vektoraihoz van hitelesítve. **Tisztázandó:** a C#-kód független
  újraimplementáció-e vagy a Random123 forrásából származik. Utóbbi esetben a Random123 licencének
  (BSD-stílusú) szerzői jogi közleményét szó szerint, a hivatalos repóból kell ide átvenni.
  (A `tools/reference/kat_vectors` csak fejlesztési tesztadat, nem kerül a buildbe.)

## Nem kerülnek a buildbe

A `tools/reference` Python-referencia, a `tests/` projektek és a .NET SDK/xUnit csak a
fejlesztéshez kellenek.

## Hanganyagok, ikon, splash

Még nincsenek (WF-AUDIO-002…004, ND-111). Minden később beszerzett assetnél a forrást,
a licencet és a kötelező feltüntetést ide kell felvenni.
