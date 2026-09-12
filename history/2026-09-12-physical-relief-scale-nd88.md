# Fizikai függőleges relief-skála — ND-88 (2026-09-12)

## Jelenség

Az ND-84 precíz km-lépték mellett egy tektonikus ütközési perem nagyjából
200 km magasnak látszott a tengerszinthez képest. A tengerszint helyesnek
tűnt; a felhasználói elvárás szerint a közvetlen peremugrás legfeljebb kb.
1 km lehet.

## Diagnózis

- A scene `radius=100`, `elevationScale=0.001` és
  `terrainReliefExaggeration=1.5` értéket használt.
- A Core kanonikus fizikai sugara 7 420 000 m, ezért az 1:1 skála
  `100 / 7 420 000 = 0.000013477088948787...` Unity-egység/méter.
- A régi scene függőleges túlrajzolása így
  `0.001 / (100 / 7 420 000) * 1.5 = 111.3×` volt. Egy kb. 1,8 km-es
  modellbeli relief ezért kb. 200 km-esnek látszhatott a vízszintes skálán.
- A Core uplift felső értéke 1500 m. A deep-time erózió csak ezt az uplift-
  komponenst relaxálja; a kéreg alap-elevációját nem.
- Külön modellhiány, hogy az óceáni és kontinentális lemezbázis -4000 m és
  +800 m között a legközelebbi lemez ID-jével diszkréten válthat. Ezt nem
  helyes rendereroldali clamp-pal elfedni.

## Változás

A `PlanetGridMesh` új, alapból bekapcsolt `usePhysicalReliefScale` módot kapott.
Induláskor és `OnValidate` alatt automatikusan beállítja az
`elevationScale = radius / PlanetConstants.RadiusMeters` értéket, a külön
relief-túlrajzolást pedig 1-re. A scene is explicit ezt az állapotot tárolja.
A régi művészi beállítások a kapcsoló kikapcsolásával elérhetők maradnak.
A renderdiagnosztika naplózza a mód állapotát.

## Kapcsolódó Core-javítás és nyitott ellenőrzés

Élő Unityban ugyanarra a lemezperemre vissza kell menni. Az ND-88-nak a
kb. 200 km-es látszólagos falat a tényleges modellmagasság nagyságrendjére
kell csökkentenie. A diagnózisban rögzített külön Core-hiányt az ND-90 közben
megoldotta: folytonos vegyes kéregátmenet, 1000 m-es uplift-plafon és
`.worldpkg` v2 került be. Az élő ellenőrzésnek ezért már az ND-88 és ND-90
együttes eredményét kell vizsgálnia.
