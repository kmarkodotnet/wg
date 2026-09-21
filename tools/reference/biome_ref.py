"""
Biome/jeg-osztalyozas referencia-implementacioja M5-hoz (docs/05-
milestones.md §5.2) - ND-10 (Biome-kuszobok) javasolt alapertelmezese:
fix Fold-szeru kuszobok, nem bolygoparameter-skalazott (v1.0-ban).

ND-126 (2026-09-21): KETDIMENZIOS (Whittaker-jellegu) osztalyozas -
HOMERSEKLET x CSAPADEK. Korabban CSAK homersekletet kapott, es a sajat
doksija ki is mondta, hogy "a csapadek/nedvesseg (§31) halasztva van,
ezert NEM kulonboztetunk meg pl. sivatagot/esoerdot". A csapadek-mezo
azota elkeszult, de ide nem kerult be - emiatt negy, tisztan homersekleti
osztaly volt, a homerseklet pedig lenyegeben a szelesseg sima fuggvenye,
tehat az eredmeny TOKELETES SZELESSEGI SAVOK lettek.

A CSAPADEK-KUSZOBOK PERCENTILISEK, nem abszolut ertekek: a csapadek
egysege a modellben nem mm/ev, hanem onkenyes nedvesseg-egyseg, ami fugg a
transzport-parameterektol es a bolygoparameterektol. Abszolut kuszob
vilagfuggo lenne.

Nincs transzcendens fuggveny itt - csak kuszob-osszehasonlitas a mar
kiszamolt homersekleten es csapadekon, tehat BITPONTOS (nem ad
ND-kockazatot).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""

# Kuszobok Kelvinben (dokumentalt, fix ertekek - ND-10 "fix Fold-kuszobok")
OCEAN_FREEZING_K = 271.15  # ~ -2C, sos viz fagyaspontja
ICE_SHEET_THRESHOLD_K = 263.15  # -10C
TUNDRA_THRESHOLD_K = 278.15  # 5C
TEMPERATE_THRESHOLD_K = 293.15  # 20C

# Csapadek-percentilisek a SZARAZFOLDI eloszlason (ND-126, MVP-ertekek)
ARID_PERCENTILE = 0.20
SEMI_ARID_PERCENTILE = 0.45
MOIST_PERCENTILE = 0.75

BIOME_OCEAN = "Ocean"
BIOME_SEA_ICE = "SeaIce"
BIOME_ICE_SHEET = "IceSheet"
BIOME_TUNDRA = "Tundra"
BIOME_DESERT = "Desert"
BIOME_GRASSLAND = "Grassland"
BIOME_TEMPERATE_FOREST = "TemperateForest"
BIOME_SAVANNA = "Savanna"
BIOME_RAINFOREST = "Rainforest"


def _percentile(sorted_values, q):
    """Ugyanaz az index-keplet, mint a folyo-forras kivalasztasnal - egyfele
    percentilis-konvencio van a projektben."""
    idx = int(q * len(sorted_values))
    if idx < 0:
        idx = 0
    if idx > len(sorted_values) - 1:
        idx = len(sorted_values) - 1
    return sorted_values[idx]


def compute_thresholds(land_precipitation):
    """A harom csapadek-vagopont a SZARAZFOLDI ertekekbol.

    Ures bemenetnel mindharom 0 - dokumentalt konvencio: nincs eloszlas,
    amihez viszonyitsunk, tehat minden pozitiv csapadek a legnedvesebb
    osztalyba esik."""
    values = sorted(land_precipitation)
    if not values:
        return (0.0, 0.0, 0.0)
    return (
        _percentile(values, ARID_PERCENTILE),
        _percentile(values, SEMI_ARID_PERCENTILE),
        _percentile(values, MOIST_PERCENTILE),
    )


def compute_thresholds_for_vegetated_land(land_samples):
    """A vagopontok a VEGETALT szarazfold eloszlasabol - azokbol a tile-okbol,
    amelyeket a csapadek-tengely egyaltalan osztalyoz (TUNDRA_THRESHOLD_K
    folott).

    MIERT NEM A TELJES SZARAZFOLDBOL: a hideg tile-okat a homerseklet donti el
    (jegtakaro/tundra), a csapadekuk viszont szisztematikusan 0 koruli - ha
    benne vannak az eloszlasban, lehuzzak a percentiliseket, es a meleg sav
    MINDEN tile-ja "nedvesnek" latszik. Merve (seed 0xA7C944210000, level 6):
    a teljes szarazfoldbol az arid vagopont pontosan 0,000 lett, es a
    szarazfold 24,4%-a lett esoerdo (a Foldon ~7%).

    land_samples: (temperature_k, precipitation) parok iterable-je.
    """
    vegetated = [p for (t, p) in land_samples if t >= TUNDRA_THRESHOLD_K]
    return compute_thresholds(vegetated)


def classify_biome(temperature_k, is_oceanic, precipitation, thresholds):
    arid, semi_arid, moist = thresholds

    if is_oceanic:
        return BIOME_SEA_ICE if temperature_k < OCEAN_FREEZING_K else BIOME_OCEAN

    # A hideg veg csapadektol FUGGETLEN: a sarkvideki "hideg sivatag" is
    # jeg/tundra, nem homoksivatag.
    if temperature_k < ICE_SHEET_THRESHOLD_K:
        return BIOME_ICE_SHEET
    if temperature_k < TUNDRA_THRESHOLD_K:
        return BIOME_TUNDRA

    warm = temperature_k >= TEMPERATE_THRESHOLD_K

    if precipitation <= arid:
        return BIOME_DESERT
    if precipitation <= semi_arid:
        return BIOME_GRASSLAND
    if precipitation <= moist:
        return BIOME_SAVANNA if warm else BIOME_TEMPERATE_FOREST
    return BIOME_RAINFOREST


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    TH = (1.0, 2.0, 3.0)  # rogzitett vagopontok a hataresetekhez

    print("Kuszob-hataresetek:")
    test_cases = [
        # homersekleti vegek - csapadektol fuggetlenek
        (260.0, False, 5.0, BIOME_ICE_SHEET),
        (263.15, False, 5.0, BIOME_TUNDRA),
        (270.0, False, 0.0, BIOME_TUNDRA),
        # mersekelt sav, csapadek szerint
        (278.15, False, 0.5, BIOME_DESERT),
        (278.15, False, 1.0, BIOME_DESERT),      # <= arid
        (280.0, False, 1.5, BIOME_GRASSLAND),
        (285.0, False, 2.0, BIOME_GRASSLAND),    # <= semi-arid
        (285.0, False, 2.5, BIOME_TEMPERATE_FOREST),
        (285.0, False, 3.0, BIOME_TEMPERATE_FOREST),  # <= moist
        (285.0, False, 3.5, BIOME_RAINFOREST),
        # tropusi sav - csak a "kozepes" sor ter el
        (293.15, False, 0.5, BIOME_DESERT),
        (300.0, False, 1.5, BIOME_GRASSLAND),
        (300.0, False, 2.5, BIOME_SAVANNA),
        (300.0, False, 3.5, BIOME_RAINFOREST),
        # ocean
        (265.0, True, 0.0, BIOME_SEA_ICE),
        (271.15, True, 0.0, BIOME_OCEAN),
        (280.0, True, 9.0, BIOME_OCEAN),
    ]
    all_ok = True
    for t, oceanic, p, expected in test_cases:
        got = classify_biome(t, oceanic, p, TH)
        ok = got == expected
        all_ok = all_ok and ok
        print(f"  T={t:.2f}K oceanic={oceanic} P={p:.2f}: {got} (vart: {expected}) {'OK' if ok else 'HIBA'}")
    assert all_ok, "Kuszob-hataresetek nem egyeznek a varttal"
    print("OK - minden kuszob-hatareset helyes\n")

    # compute_thresholds ellenorzes ismert eloszlason: 0..99
    known = list(range(100))
    th = compute_thresholds(known)
    assert th == (20.0 - 0.0 + 0, 45, 75), f"varatlan vagopontok: {th}"
    print(f"compute_thresholds(0..99) = {th}  (index = int(q*n), tehat 20/45/75)")
    assert compute_thresholds([]) == (0.0, 0.0, 0.0)
    print("compute_thresholds([]) = (0,0,0) - dokumentalt konvencio")

    # A vegetalt szuro: a hideg mintak csapadeka NEM szamit bele.
    samples = [(250.0, 0.0)] * 100 + [(290.0, float(i)) for i in range(100)]
    th_veg = compute_thresholds_for_vegetated_land(samples)
    assert th_veg == (20, 45, 75), f"varatlan vegetalt vagopontok: {th_veg}"
    th_all = compute_thresholds([p for (_, p) in samples])
    assert th_all == (0, 0, 50), f"varatlan teljes vagopontok: {th_all}"
    print(f"vegetalt szuro: {th_veg} (szures nelkul {th_all} lenne - a hideg 0-k lehuznak)\n")

    # Tesztvektorok a C# porthoz
    vectors = []
    gen_seed = 0xB10E000000001
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 10, 0], 20)
        t_k = 200.0 + (p[0] % 200000) / 1000.0  # 200..400K
        is_oceanic = (p[1] % 2) == 0
        precip = (p[2] % 5000) / 1000.0  # 0..5 nedvesseg-egyseg
        biome = classify_biome(t_k, is_oceanic, precip, TH)
        vectors.append({
            "temperatureK": t_k,
            "isOceanic": is_oceanic,
            "precipitation": precip,
            "biome": biome,
        })

    payload = {
        "thresholds": {"arid": TH[0], "semiArid": TH[1], "moist": TH[2]},
        "vectors": vectors,
    }
    with open("biome_vectors.json", "w", newline="\n") as f:
        json.dump(payload, f, indent=1)
    print(f"{len(vectors)} biome-tesztvektor generalva")
