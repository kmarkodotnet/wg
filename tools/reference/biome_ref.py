"""
Biome/jeg-osztalyozas referencia-implementacioja M5-hoz (docs/05-
milestones.md §5.2) - ND-10 (Biome-kuszobok) javasolt alapertelmezese:
fix Fold-szeru kuszobok, nem bolygoparameter-skalazott (v1.0-ban).

HATOKOR: csak HOMERSEKLET + viz/szarazfold alapjan osztalyoz - a
csapadek/nedvesseg (§31) halasztva van (ld. M5 hatokor), ezert NEM
kulonboztetunk meg pl. sivatagot/esoerdot (ahhoz nedvesseg-adat kellene)
- csak homersekleti savokat + jeg/oceann.

Nincs transzcendens fuggveny itt - csak kuszob-osszehasonlitas a mar
kiszamolt homersekleten, tehat BITPONTOS (nem ad ND-kockazatot).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""

# Kuszobok Kelvinben (dokumentalt, fix ertekek - ND-10 "fix Fold-kuszobok")
OCEAN_FREEZING_K = 271.15  # ~ -2C, sos viz fagyaspontja
ICE_SHEET_THRESHOLD_K = 263.15  # -10C
TUNDRA_THRESHOLD_K = 278.15  # 5C
TEMPERATE_THRESHOLD_K = 293.15  # 20C

BIOME_OCEAN = "Ocean"
BIOME_SEA_ICE = "SeaIce"
BIOME_ICE_SHEET = "IceSheet"
BIOME_TUNDRA = "Tundra"
BIOME_TEMPERATE = "Temperate"
BIOME_TROPICAL = "Tropical"


def classify_biome(temperature_k, is_oceanic):
    if is_oceanic:
        return BIOME_SEA_ICE if temperature_k < OCEAN_FREEZING_K else BIOME_OCEAN

    if temperature_k < ICE_SHEET_THRESHOLD_K:
        return BIOME_ICE_SHEET
    if temperature_k < TUNDRA_THRESHOLD_K:
        return BIOME_TUNDRA
    if temperature_k < TEMPERATE_THRESHOLD_K:
        return BIOME_TEMPERATE
    return BIOME_TROPICAL


if __name__ == "__main__":
    import json
    from threefry_ref import threefry4x64

    print("Kuszob-hataresetek:")
    test_cases = [
        (260.0, False, BIOME_ICE_SHEET), (263.15, False, BIOME_TUNDRA),
        (270.0, False, BIOME_TUNDRA), (278.15, False, BIOME_TEMPERATE),
        (285.0, False, BIOME_TEMPERATE), (293.15, False, BIOME_TROPICAL),
        (300.0, False, BIOME_TROPICAL),
        (265.0, True, BIOME_SEA_ICE), (271.15, True, BIOME_OCEAN),
        (280.0, True, BIOME_OCEAN),
    ]
    all_ok = True
    for t, oceanic, expected in test_cases:
        got = classify_biome(t, oceanic)
        ok = got == expected
        all_ok = all_ok and ok
        print(f"  T={t:.2f}K oceanic={oceanic}: {got} (vart: {expected}) {'OK' if ok else 'HIBA'}")
    assert all_ok, "Kuszob-hataresetek nem egyeznek a varttal"
    print("OK - minden kuszob-hataresetek helyesek\n")

    # Tesztvektorok a C# porthoz
    vectors = []
    gen_seed = 0xB10E000000001
    for i in range(200):
        p = threefry4x64([i, 0, 0, 0], [gen_seed, 0, 10, 0], 20)
        t_k = 200.0 + (p[0] % 200000) / 1000.0  # 200..400K
        is_oceanic = (p[1] % 2) == 0
        biome = classify_biome(t_k, is_oceanic)
        vectors.append({"temperatureK": t_k, "isOceanic": is_oceanic, "biome": biome})

    with open("biome_vectors.json", "w", newline="\n") as f:
        json.dump({"vectors": vectors}, f, indent=1)
    print(f"{len(vectors)} biome-tesztvektor generalva")
