"""
Világállapot determinisztikus hash-eleseneke referencia-implementacioja
M12-hoz (docs/00-spec-v1.0.md Sz.60 "World State hash").

HATOKOR (tudatosan szukitve - a teljes esemenysourcing+checkpoint+worldpkg
rendszerhez kepest, ld. docs/04-decisions.md ND-30): ez a lepes CSAK a
hash-fuggvenyt adja - a .worldpkg fajlformatum, CLI (`worldgen verify`),
event-sourcing/replay HALASZTVA. Indoklas: a Core minden resze mar most
is TISZTA FUGGVENYE a (worldSeed, parameterek, ido)-nak - nincs
"irreverzibilis" allapot, amit event-sourcing-nal kellene tarolni. A
hash a "ugyanaz a definicio -> ugyanaz a vilag minden platformon"
(I1) automatizalt ellenorzesehez kell, ami MAR MOST ertekes M12 elott is.

MODSZER: a mezot (TileId -> elevacio) KANONIKUS sorrendben (TileId.Value
szerint novekvo, NEM dict-bejarasi sorrend) big-endian bajtsorrendbe
irjuk, majd SHA-256. A big-endian VALASZTOTT, nem a platform natív
byte-sorrendjere tamaszkodva (elmeletben ARM64 eltero lehetne, bar a
gyakorlatban minden CI-celplatform little-endian - a projekt fentebbi
"ne bizz implicit platform-feltetelezesben" elve szerint mindig
explicit sorrendet hasznalunk).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
import hashlib
import struct


def _u64_be(value):
    return struct.pack('>Q', value & 0xFFFFFFFFFFFFFFFF)


def _double_bits_be(value):
    bits = struct.unpack('>Q', struct.pack('>d', value))[0]
    return struct.pack('>Q', bits)


def compute_field_hash(field):
    """field: dict[tile_id_value(int)] -> elevation(float). Visszaad egy
    64 karakteres hex string-et (SHA-256)."""
    buf = bytearray()
    for tile_id_value in sorted(field.keys()):
        buf += _u64_be(tile_id_value)
        buf += _double_bits_be(field[tile_id_value])
    return hashlib.sha256(bytes(buf)).hexdigest()


if __name__ == "__main__":
    from hydrology_ref import compute_elevation_and_ocean_field
    from morton_ref import tile_id

    world_seed = 0xA7C944210000
    plate_count = 20
    level = 5

    print("Elevacio-mezo szamitasa (level=5, gyors teszteleshez)...")
    field_by_tuple, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    field = {tile_id(face, level, u, v): elevation for (face, u, v), elevation in field_by_tuple.items()}
    print(f"{len(field)} tile\n")

    print("--- Tisztasag es sorrend-fuggetlenseg ---")
    h1 = compute_field_hash(field)
    h2 = compute_field_hash(field)
    assert h1 == h2, "A hash nem determinisztikus (ismetelt hivasra elter)!"

    # Ugyanaz a tartalom, MAS beszurasi sorrendben (dict rekonstrualva
    # visszafele) - a hash-nek ugyanannak kell lennie, mert a fuggveny
    # BELUL rendez, nem a dict bejarasi sorrendjere tamaszkodik.
    reversed_field = dict(reversed(list(field.items())))
    h3 = compute_field_hash(reversed_field)
    assert h1 == h3, "A hash fugg a dict beszurasi sorrendjetol - HIBA!"
    print(f"OK - hash: {h1}\n")

    print("--- Erzekenyseg: egyetlen ertek megvaltoztatasa mas hash-t ad ---")
    any_key = next(iter(field))
    perturbed = dict(field)
    perturbed[any_key] = perturbed[any_key] + 1e-9
    h4 = compute_field_hash(perturbed)
    assert h4 != h1, "A hash nem erzekeny egyetlen ertek valtozasara!"
    print("OK\n")

    print("--- Kulonbozo seed mas hash-t ad ---")
    field2_by_tuple, _, _ = compute_elevation_and_ocean_field(world_seed + 1, plate_count, level)
    field2 = {tile_id(face, level, u, v): elevation for (face, u, v), elevation in field2_by_tuple.items()}
    h5 = compute_field_hash(field2)
    assert h5 != h1, "Ket kulonbozo seed ugyanazt a hash-t adta - gyanus!"
    print("OK\n")

    print("--- Tesztvektor generalasa (C# porthoz) ---")
    import json
    vector = {
        "worldSeed": world_seed, "plateCount": plate_count, "level": level,
        "expectedHash": h1,
        "tileCount": len(field),
    }
    with open("state_hash_vectors.json", "w", newline="\n") as f:
        json.dump(vector, f, indent=1)
    print(f"Tesztvektor elmentve: {h1}")
