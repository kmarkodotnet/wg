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
from plate_motion_ref import plate_seed_at_time


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


def compute_elevation_field_at_time(world_seed, plate_count, level, time_myr):
    """Az elevacio-mezo egy deep-time idopontban - a lemez-magok
    plate_motion_ref.plate_seed_at_time szerint elmozdulva. time_myr=0-nal
    bitre megegyezik a statikus compute_elevation_field ereedmenyevel."""
    seeds0 = generate_plate_seeds(world_seed, plate_count)
    moved_seeds = [
        plate_seed_at_time(world_seed, i, s0, time_myr)
        for i, s0 in enumerate(seeds0)
    ]
    n = 1 << level
    field = {}
    for face in range(6):
        for u in range(n):
            for v in range(n):
                pos = position_from_tile(face, level, u, v)
                warped_pos = warp_position(world_seed, pos)
                plate_id = assign_plate(warped_pos, moved_seeds)
                tid = tile_id(face, level, u, v)
                elev, _ = elevation_with_boundary(world_seed, plate_id, tid, pos, moved_seeds)
                field[(face, u, v)] = elev
    return field


def flooded_volume_proxy(elevations, sea_level):
    """Dimenziomentes viz-terfogat PROXY egy adott tengerszinthez: minden
    tile terulet-suly nelkul (egyenletes tile-terulet-kozelites, ld. a mar
    bevett FeatureMetrics.AreaTiles precedenst - ND-24) hozzajarul
    max(0, sea_level - elevation)-vel. Monoton NOVEKVO fuggvenye
    sea_level-nek."""
    total = 0.0
    for e in elevations:
        depth = sea_level - e
        if depth > 0.0:
            total += depth
    return total


def calibrate_sea_level_by_volume(elevations, target_volume, iterations=60):
    """A tengerszint, amire flooded_volume_proxy(elevations, H) == target_volume,
    FIX iteracioszamu binaris keresessel (nem tolerancia-alapu leallas - ld.
    CLAUDE.md I1, determinizmus platformok kozott). A flooded_volume_proxy
    monoton novekvo H-ban, ezert a binaris kereses egyertelmuen konvergal."""
    lo = min(elevations)
    hi = max(elevations)
    for _ in range(iterations):
        mid = (lo + hi) / 2.0
        vol = flooded_volume_proxy(elevations, mid)
        if vol < target_volume:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


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

    # --- ND-38: terfogat-megmaradas alapu tengerszint ---
    print("\n--- ND-38: terfogat-alapu tengerszint (t=0 visszaoldas) ---")
    elevations0 = list(field.values())
    v0 = flooded_volume_proxy(elevations0, sea_level)
    print(f"V0 (proxy-terfogat, percentilis-kalibralt sea_level={sea_level:.3f}m-nel): {v0:.3f}")

    sea_level_from_volume = calibrate_sea_level_by_volume(elevations0, v0, iterations=60)
    diff = abs(sea_level_from_volume - sea_level)
    print(f"Visszaoldott tengerszint (60 lepes binaris kereses): {sea_level_from_volume:.6f}m")
    print(f"Elteres a percentilis-kalibralt szinttol: {diff:.2e}m")
    assert diff < 1e-6, f"t=0-nal a terfogat-alapu visszaoldasnak gyakorlatilag egyeznie kell a percentilissel, diff={diff}"
    print("OK - t=0-nal a terfogat-alapu es percentilis-alapu tengerszint gyakorlatilag egyezik")

    water_from_volume = sum(1 for e in elevations0 if e < sea_level_from_volume)
    water_fraction_from_volume = water_from_volume / total_tiles
    print(f"Viz-arany a visszaoldott szinttel: {water_fraction_from_volume:.4%} (eredeti: {actual_water_fraction:.4%})")

    print("\n--- ND-38: viz-arany elmozdulasa deep-time-ban (level 5, olcsobb racs) ---")
    dt_level = 5
    field_dt0 = compute_elevation_field(world_seed, plate_count, dt_level)
    elevations_dt0 = list(field_dt0.values())
    sea_level_dt0 = calibrate_sea_level(field_dt0, target_water_fraction)
    v0_dt = flooded_volume_proxy(elevations_dt0, sea_level_dt0)
    total_dt_tiles = len(elevations_dt0)
    water0 = sum(1 for e in elevations_dt0 if e < sea_level_dt0)
    print(f"t=0 (level {dt_level}): sea_level={sea_level_dt0:.2f}m, viz-arany={water0/total_dt_tiles:.4%}, V0={v0_dt:.3f}")

    for t_myr in (50.0, 100.0, 250.0, 500.0):
        field_t = compute_elevation_field_at_time(world_seed, plate_count, dt_level, t_myr)
        elevations_t = list(field_t.values())
        sea_level_t = calibrate_sea_level_by_volume(elevations_t, v0_dt, iterations=60)
        water_t = sum(1 for e in elevations_t if e < sea_level_t)
        water_fraction_t = water_t / len(elevations_t)
        # ellenorzeskepp: a proxy-terfogat a visszaoldott szintnel gyakorlatilag V0
        vol_check = flooded_volume_proxy(elevations_t, sea_level_t)
        print(f"t={t_myr:>6.1f} Myr: sea_level={sea_level_t:10.2f}m  viz-arany={water_fraction_t:.4%}  "
              f"(cel-tol eltolodas: {(water_fraction_t - target_water_fraction) * 100:+.2f} szazalekpont)  "
              f"V(H)={vol_check:.3f} (V0={v0_dt:.3f}, diff={abs(vol_check - v0_dt):.2e})")
        assert 0.0 < water_fraction_t < 1.0, f"t={t_myr}: fizikailag lehetetlen viz-arany ({water_fraction_t})"
