# Tektonika, domborzat, tengerszint és deep-time

## 1. Lemezmagok és területek

**Státusz:** aktív.

A rendszer `plateCount` darab, a gömbön egyenletesen mintázott lemezmagot
generál. Egy felszíni pont ahhoz a lemezhez tartozik, amelynek magjával a
pontszorzata a legnagyobb, vagyis gömbi értelemben a legközelebbi.

A lemez-hozzárendelés előtt a pont koordinátája determinisztikus domain
warpot kap. Ettől a határok nem tiszta Voronoi-nagykörívek, hanem térben
koherensen hullámzó vonalak.

## 2. Kéregtípus

**Státusz:** aktív.

Minden teljes lemez egyetlen kéregtípust kap. A döntés seedelt Bernoulli-próba:

```text
P(oceanic crust) = 0.40
P(continental crust) = 0.60
```

Ez geológiai kéregtípus, nem azonos a felszín vízzel borítottságával. Egy
kontinentális kérgű, alacsony tile is kerülhet a tengerszint alá.

## 3. Alap-eleváció

**Státusz:** aktív.

Kéregbázisok:

```text
oceanicBase     = -4000 m
continentalBase =  +800 m
```

Az eleváció összeállítása:

```text
baseElevation = crustBase
              + primaryNoise * mountainMask * primaryAmplitude
              + secondaryNoise * secondaryAmplitude
```

Kontinentális amplitúdók:

```text
primaryAmplitude   = 3000 m
secondaryAmplitude =  900 m
```

Óceáni kérgen mindkettő 0,25-szörös, ezért az óceáni relief simább.

A primer zaj ridged multifractal. A `mountainMask` egy alacsony frekvenciás
fBm-ből képzett `[0,1]` regionális maszk; ettől nagy összefüggő sík és
hegyvidéki területek jönnek létre. A másodlagos zaj külön koordináta-eltolást
kap, így nem ugyanazt a mintát erősíti fel.

## 4. Lemezhatár-kiemelkedés

**Státusz:** aktív proxy.

A két legközelebbi lemezmag pontszorzatának különbsége:

```text
gap = bestDot - secondBestDot
```

Minél kisebb a `gap`, annál közelebb van a pont a lemezhatárhoz. Ha:

```text
gap >= 0.04
```

akkor nincs határkiemelkedés. Egyébként:

```text
rawUplift = 1500 m * (1 - gap/0.04)
uplift = rawUplift * mountainMask
```

Ha mindkét szomszédos lemez óceáni, a `rawUplift` további 0,15 szorzót kap.

A modell nem számol relatív lemezsebességet, ezért nem különböztet meg valódi
konvergens, divergens és transzform határt. Minden határ egy pozitív
hegységképző proxy.

## 5. Pillanatnyi eleváció

Az alapképlet:

```text
elevation = baseElevation + boundaryUplift + craterDelta
```

Ha a deep-time erózió aktív, a határkiemelkedés relaxált változata kerül bele:

```text
H_eq = 0.35 * H0
H(t) = H_eq + (H0 - H_eq) * exp(-t/50 Myr)
elevation(t) = baseElevation + H(t) + craterDelta(t)
```

Ez kizárólag a határból származó upliftet koptatja; a zajos alapdomborzatot és
a krátereket nem erodálja.

## 6. Lemezmozgás

**Státusz:** aktív a domborzatban.

Minden lemez determinisztikus Euler-pólust és sebességet kap:

```text
omega in [0.01, 0.09] rad/Myr
angle = omega * timeMyr
P(t) = RodriguesRotate(P0, axis, angle)
```

A rendszer valójában a lemezmagokat mozgatja, majd minden időpontban újra
kiértékeli a legközelebbi-mag felosztást. Nem advectál anyagrészecskéket és
nem őrzi meg explicit módon a kéreg történetét.

## 7. Lemez-életciklus

**Státusz:** Core-ban implementált és tesztelt, a fő domborzati útba nincs
bekötve.

Egy lemez sorsa seedből származik:

```text
P(split) = 0.45
P(merge) = 0.25
P(no event) = 0.30
lifespan in [80, 400] Myr
```

Split esetén a rift az élettartam 40–85%-ánál aktiválódik, majd az eseménykor
két gyermekmag keletkezik a szülő körül `±0.12 rad` eltéréssel. Merge esetén
a lemez eltűnik, és területét a megmaradt Voronoi-magok veszik át. A
leszármazási mélység alapból legfeljebb három generáció.

Mivel a Viewer domborzata jelenleg a fix gyökérmagok `MovedSeeds` eredményét
használja, ezek a split/merge események még nem alakítják a látható világot.

## 8. Tengerszint és vízmennyiség

**Státusz:** aktív.

Kezdetben a tengerszint az elevációeloszlás `targetWaterFraction`
percentilise. Alapérték:

```text
targetWaterFraction = 0.65
```

Egy tile óceáni, ha:

```text
elevation < seaLevel
```

Deep-time-ban a Viewer a kezdeti elárasztott térfogat-proxyt őrzi:

```text
V_proxy(seaLevel) = sum(max(0, seaLevel - elevation_i))
```

Az új elevációmezőhöz 60 lépéses fix bináris keresés talál olyan
tengerszintet, amely ezt a kezdeti proxyt tartja. A tile-ok eltérő valódi
területe nincs beleszámítva, tehát ez nem pontos fizikai víztérfogat.

## 9. Kontinensek

**Státusz:** aktív levezetett jellemző.

A szárazföldi tile-ok (`elevation >= seaLevel`) négy-szomszédos összefüggő
komponensei alkotnak kontinenseket. A túl kicsi komponensek egy megadott
minimum tileszám alapján kiszűrhetők.

## 10. Fő korlátok

- nincs kéregvastagság, sűrűség, izosztázia vagy köpenyáramlás;
- nincs valódi szubdukció, rifting, transform vetődés vagy anyagmegmaradás;
- nincs üledékszállítás a deep-time domborzatban;
- a lemez-életciklus nincs bekötve a felszínbe;
- a tengerszint víztérfogat-proxy, egyenlő tile-terület közelítéssel;
- a tektonikai aktivitás és belső hő nem bolygóparaméter;
- az eróziós `Math.Exp` miatt a szigorú platformközi bitazonosság ezen az
  útvonalon még dokumentált kockázat.

