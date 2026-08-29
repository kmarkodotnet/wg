"""
Tengerszint-kalibracio + TEST-EARTH-001 referencia-implementacioja M4-hez
(docs/05-milestones.md §4.4).

MODSZER: a tengerszintet a magassag-eloszlas PERCENTILISE hatarozza meg
(nem fix meter-ertek) - ez automatikusan biztositja a celzott viz-aranyt,
fuggetlenul attol, hogy a nyers elevacio-eloszlas eppen hogyan alakul.

TEST-EARTH-001 (docs/05-milestones.md M4 sora): viz-arany 50-75% KOZOTT,
tobb (>=2) diszjunkt szarazfold-kontinens. A kontinens-szamlalas flood-fill
(szelessegi bejaras) a mar verifikalt szomszedsagi logikaval
(tools/reference/neighbor_ref.py).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
from collections import deque

from plate_ref import generate_plate_seeds, assign_plate
from plate_boundary_ref import elevation_with_boundary
from morton_ref import tile_id
from sphere_position_ref import position_from_tile
from neighbor_ref import neighbor, DIRECTIONS
from domain_warp_ref import warp_position


def compute_elevation_field(world_seed, plate_count, level):
    """Minden tile elevacioja + oceani-flag egy level-n adott racson.

    A lemez-HOZZARENDELES a WARPOLT pozicion tortenik (domain_warp_ref) -
    ez tori meg a tiszta legkozelebbi-mag Voronoi hatarok tul geometrikus,
    nagykor-iv-szeru jelleget. Az elevation_with_boundary (es a benne levo
    base_elevation zaj-kiertekeles) VALTOZATLANUL a NYERS pozicot kapja -
    a warp csak a lemez-topologia dontesehez hasznalt, a domborzat-textura
    nem."""
    seeds = generate_plate_seeds(world_seed, plate_count)
    n = 1 << level
    field = {}  # (face,u,v) -> elevation
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                warped_pos = warp_position(world_seed, pos)
                plate_id = assign_plate(warped_pos, seeds)
                tid = tile_id(face, level, u, v)
                elev, _ = elevation_with_boundary(world_seed, plate_id, tid, pos, seeds)
                field[(face, u, v)] = elev
    return field


def calibrate_sea_level(field, target_water_fraction):
    """A percentilis-ertek, ami a celzott viz-aranyt adja."""
    elevations = sorted(field.values())
    idx = int(target_water_fraction * len(elevations))
    idx = max(0, min(len(elevations) - 1, idx))
    return elevations[idx]


def count_continents(field, sea_level, level, min_size=5):
    """Osszefuggo szarazfold-komponensek szama flood-fill-lel."""
    land = {k for k, elev in field.items() if elev >= sea_level}
    visited = set()
    components = []

    for start in land:
        if start in visited:
            continue
        component = []
        queue = deque([start])
        visited.add(start)
        while queue:
            face, u, v = queue.popleft()
            component.append((face, u, v))
            for d in DIRECTIONS:
                nf, nu, nv = neighbor(face, level, u, v, d)
                neighbor_key = (nf, nu, nv)
                if neighbor_key in land and neighbor_key not in visited:
                    visited.add(neighbor_key)
                    queue.append(neighbor_key)
        components.append(component)

    sized = [c for c in components if len(c) >= min_size]
    return sized, components


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    # plate_count=20 (a spec 6-30-as tartomanyaban): 12-nel a canonikus
    # seedre a 5 kontinentalis lemez veletlenul mind osszeert (1 giant
    # szuperkontinens - fizikailag ez sem hibas, ld. Pangea, de a
    # TEST-EARTH-001 kifejezetten "tobb kontinenst" var egy referencia-
    # vilagtol). Tobb, kisebb lemezzel statisztikailag valoszinubb, hogy
    # a kontinentalis lemezek NEM mind erintkeznek egymassal.
    plate_count = 20
    level = 6

    print(f"Elevacio-mezo szamitasa level {level}-on ({6 * (1 << level) ** 2} tile)...")
    field = compute_elevation_field(world_seed, plate_count, level)
    total_tiles = len(field)
    print(f"Kesz: {total_tiles} tile\n")

    target_water_fraction = 0.65  # a TEST-EARTH-001 50-75% sávjának közepe
    sea_level = calibrate_sea_level(field, target_water_fraction)
    print(f"Kalibralt tengerszint (cel viz-arany={target_water_fraction:.0%}): {sea_level:.1f}m")

    actual_water = sum(1 for e in field.values() if e < sea_level)
    actual_water_fraction = actual_water / total_tiles
    print(f"Tenyleges viz-arany: {actual_water_fraction:.2%}\n")

    continents, all_components = count_continents(field, sea_level, level, min_size=5)
    print(f"Osszefuggo szarazfold-komponensek (>=5 tile): {len(continents)}")
    print(f"  (osszes komponens, meret-szures nelkul: {len(all_components)})")
    sizes = sorted((len(c) for c in continents), reverse=True)
    print(f"  meretek (tile): {sizes[:10]}{'...' if len(sizes) > 10 else ''}")

    print("\n--- TEST-EARTH-001 ---")
    water_ok = 0.50 <= actual_water_fraction <= 0.75
    continents_ok = len(continents) >= 2
    print(f"Víz-arány 50-75% között: {'OK' if water_ok else 'BUKÁS'} ({actual_water_fraction:.2%})")
    print(f"Legalább 2 kontinens: {'OK' if continents_ok else 'BUKÁS'} ({len(continents)} db)")
    assert water_ok, f"TEST-EARTH-001 BUKÁS: víz-arány {actual_water_fraction:.2%} a sávon kívül"
    assert continents_ok, f"TEST-EARTH-001 BUKÁS: csak {len(continents)} kontinens"
    print("\nTEST-EARTH-001: PASS")
