"""
Tektonikuslemez-overlay (2026-09-13, docs/backlog.md) referencia-
implementacioja: lemezenkenti szin es nev. Tisztan megjelenitesi
tulajdonsagok - NEM fizikai allapot, 1:1 megfeleles a C#
WorldGen.Core.Tectonics.PlatePresentation-nal.

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
from crust_elevation_ref import sample
from features_ref import generate_name

DOMAIN_DECORATIVE = 32
PROPERTY_PLATE_COLOR_HUE = 42

PLATE_FEATURE_ID_BASE = 800000

GOLDEN_RATIO_CONJUGATE = 0.6180339887498949


def plate_feature_id(plate_id):
    return PLATE_FEATURE_ID_BASE + plate_id


def plate_name(world_seed, plate_id, is_oceanic):
    crust_key = "OceanicCrust" if is_oceanic else "ContinentalCrust"
    return generate_name(world_seed, plate_feature_id(plate_id), crust_key)


def _frac01(x):
    f = x - (x // 1.0)
    if f >= 1.0:
        return 0.0
    if f < 0.0:
        return 0.0
    return f


def plate_hue(world_seed, plate_id):
    hue_offset = sample(world_seed, DOMAIN_DECORATIVE, 0, 0, PROPERTY_PLATE_COLOR_HUE)
    hue = hue_offset + plate_id * GOLDEN_RATIO_CONJUGATE
    return _frac01(hue)


def hsv_to_rgb(h, s, v):
    hh = _frac01(h) * 6.0
    i = int(hh // 1.0) % 6
    f = hh - (hh // 1.0)
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    if i == 0:
        return v, t, p
    if i == 1:
        return q, v, p
    if i == 2:
        return p, v, t
    if i == 3:
        return p, q, v
    if i == 4:
        return t, p, v
    return v, p, q


def plate_color_rgb(world_seed, plate_id, saturation=0.62, value=0.88):
    return hsv_to_rgb(plate_hue(world_seed, plate_id), saturation, value)


if __name__ == "__main__":
    import json
    from plate_ref import generate_plate_seeds
    from crust_elevation_ref import is_oceanic

    world_seed = 0xA7C944210000
    plate_count = 20
    seeds = generate_plate_seeds(world_seed, plate_count)

    plates = []
    for pid in range(plate_count):
        oceanic = is_oceanic(world_seed, pid)
        name = plate_name(world_seed, pid, oceanic)
        r, g, b = plate_color_rgb(world_seed, pid)
        plates.append({"plateId": pid, "isOceanic": oceanic, "name": name, "r": r, "g": g, "b": b})
        print(f"  plate {pid:2d} ({'oceanic' if oceanic else 'continental'}): {name} "
              f"rgb=({r:.4f},{g:.4f},{b:.4f})")

    # Determinizmus
    assert plate_color_rgb(world_seed, 5) == plate_color_rgb(world_seed, 5)
    assert plate_name(world_seed, 5, False) == plate_name(world_seed, 5, False)
    print("OK - determinisztikus")

    # Kulonbozo plateId kulonbozo (nagyon valoszinuleg) szint/nevet ad
    assert plate_color_rgb(world_seed, 0) != plate_color_rgb(world_seed, 1)
    assert plate_name(world_seed, 0, False) != plate_name(world_seed, 1, False)
    print("OK - kulonbozo plateId kulonbozo megjelenest ad")

    # Minden RGB komponens [0,1]-ben
    for p in plates:
        assert 0.0 <= p["r"] <= 1.0 and 0.0 <= p["g"] <= 1.0 and 0.0 <= p["b"] <= 1.0
    print("OK - minden RGB [0,1] tartomanyban")

    with open("plate_presentation_vectors.json", "w", newline="\n") as f:
        json.dump({"worldSeed": world_seed, "plateCount": plate_count, "plates": plates}, f, indent=1)
    print(f"\n{len(plates)} lemez-megjelenites tesztvektor elmentve")
