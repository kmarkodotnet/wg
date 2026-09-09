# 2026-09-05 — autonóm backlog-menet (branch: core-deferred-features)

Felhasználói kérés: "kezdd el megcsinálni az összes visszamaradó feladat közül
azokat, amiket programozással meg tudsz oldani … mire visszaérek, minden feladat
legyen kész. Tőlem ne is kérdezz." Előbb egy verzió-checkpoint, majd autonóm
végrehajtás. A Unity-oldali vizuális ellenőrzés a felhasználóé (offline nem
fordítható/nézhető); a Core-munkák teszttel bizonyítva.

## Verzió-checkpoint
- `VERSION` = 0.11.0-dev, git tag `v0.11.0-dev` (commit `f47904c`).

## Elvégezve

### A 3 felhasználó-jelentette viewer-hiba (kódban javítva, vizuális ellenőrzés hátra)
- **#3 Vertex-szín árnyalás-regresszió** (`1efd5fa`) — a folytonos felszín unlit
  shadere miatt nem volt spekuláris/valós fény. A `VertexColorUnlit` shader
  mostantól VALÓS IDEJŰ Lambert + Blinn-Phong (explicit Nap-uniform a C#-ból,
  `LateUpdate`); a besütött fix-irányú Lambert (AddQuad) megszűnt.
- **#1 Folyó-blokkosodás** (`da5a67e`) — `thinRivers`: referencia-tile él-maszk
  (mely szomszéd folyó/óceán) → al-tile centerline; a folyó vékony vonal, nem
  nagy kék téglalap. A tavak területként maradnak (helyes).
- **#2 Északi sarki jég-gödör** (`905b07c`) — a SeaIce a mély fenéken, felszín
  nélkül renderelődött → gödör. Most minden óceáni tile (víz + jég) kap
  tengerszintű opak felszínt (jég = fehér), elrejtve a mély fenéket.

### Core-feladatok (teszttel bizonyítva)
- **M11 kráter-konzisztencia** (`009af39`) — `ApplyToField(field, craters)`
  overload; a viewer arra állítva (nincs duplikált sim-matek); 2 új teszt.
- **M8 morfológiai típusfelismerés** (`a4c0915`) — `ClassifyLandform`
  (sziget/hegyvidék/fennsík/medence/síkság/alföld); 8 unit-teszt.
- **M5 nedvesség-transzport + csapadék** (`3f45e9e`, `a6792d2`) — reference-first:
  `moisture_transport_ref.py` (+140 vektor) → `MoisturePrecipitation` C#-port
  (1e-6-on belül 24 iteráció után is) → 3 teszt → viewer **csapadék-overlay**.
  Ez pótolja az M5 hiányzó advekcióját (eddig a szárazföldi csapadék ~0 volt).

### Egyéb
- **Húzható idő-csúszka + képernyős OnGUI-panel** (korábbi commitok ebben a
  sessionben): a `deepTimeMyr` végre scrubbolható (Inspector `[Range]` proxy +
  Game view-beli IMGUI csúszka), ÉS a világ-config változása most tényleg
  teljes `Build()`-et vált ki (fékezve) — eddig csak a dinamikus LOD épült újra.
- Backlog-higiénia: a session összes kész tétele jelölve (`docs/backlog.md`).

## Állapot
Teljes Core-suite: **303/303 zöld** (a baseline 290 + 13 új: 2 kráter, 8 landform,
3 moisture). Nulla regresszió. A viewer-változások Unity-oldaliak → élő
ellenőrzés a felhasználóé.

## Ami PROGRAMOZÁSSAL még hátravan (vizuális kalibrálás/ellenőrzés vagy nagyobb önálló munka)
- A klíma/csapadék MVP-konstansok vizuális kalibrálása (felhasználó).
- ND-46 screen-space LOD-metrika (offline harness-szel tesztelhető, de nagy).
- Kontinens/régió kamera-átmenetek; M12 checkpoint/.worldpkg + CLI.
- Landform + Habitability/Coastal bekötése a régiónevekbe / panel-UI-ba.
