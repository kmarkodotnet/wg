# Viewer scripts

Ide kerül a Unity-specifikus megjelenítő kód (kamera, HDRP-sky vezérlés,
panel-binding, input) — minden, ami `UnityEngine`-re hivatkozik.

**Nem ide való:** szimulációs/adatmodell logika. Az a `src/WorldGen.Core`-ban
él, ami a `com.worldgen.core` package néven van hivatkozva (ld.
`Packages/manifest.json`) — motorfüggetlen, nulla `UnityEngine`-referencia
(ND-22, kikényszerítve az asmdef `noEngineReferences: true` flagjével).
