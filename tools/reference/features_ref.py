"""
Features modul referencia-implementacioja M8-hoz (docs/05-milestones.md,
docs/01-architecture.md §6): kontinens/regio-szegmentalas, nevgeneralas,
aggregalt metrikak.

HATOKOR (tudatosan szukitve - ld. milestones "M8 hatokor"):
  - Regio = VIZGYUJTO-alapu csoportositas (a FlowNetwork szulo-fajabol -
    minden szarazfold-tile ahhoz az ocean-tile "gyokerhez" tartozik,
    amihez vegul lefolyik). NEM a teljes vizgyujto ∪ biome-klaszter ∪
    domborzati-tores hibrid (ND-05).
  - Nevgeneralas: szotag-to + BIOME-ALAPU utotag (nem teljes
    morfologiai tipizalas - delta/hegylanc/medence felismeres nelkul).
  - "Area": TILE-SZAMLALAS, nem valodi km^2 (a cubed-sphere tile-teruletek
    kb. 1.3-1.4x aranyban valtoznak, ld. ND-24 - ez dokumentalt
    kozelites, nem hianyossag).

NEM produkcios kod - csak orakulum, a python-reference skill szerint.
"""
from collections import deque

from plate_ref import generate_plate_seeds, assign_plate
from plate_boundary_ref import elevation_with_boundary
from sphere_position_ref import position_from_tile
from morton_ref import tile_id
from neighbor_ref import neighbor, DIRECTIONS
from temperature_ref import temperature_kelvin
from biome_ref import classify_biome, BIOME_OCEAN, BIOME_SEA_ICE
from hydrology_ref import (
    compute_elevation_and_ocean_field, priority_flood,
    flow_accumulation, select_river_tiles, river_mouth_count,
)
from threefry_ref import threefry4x64

M64 = (1 << 64) - 1
SCALE53 = 2.0 ** -53
DOMAIN_NAMING = 6
PROPERTY_SYLLABLE_CHOICE = 30
PROPERTY_SUFFIX_CHOICE = 31

SYLLABLES = [
    "Au", "Rel", "Ion", "Nor", "Wat", "Ver", "Del", "Rin", "Hal", "Cy",
    "Fros", "Sil", "Tide", "Mar", "Light", "South", "East", "Vel", "Dor",
    "Ka", "Lu", "Mir", "Os", "Pyr", "Quel", "Rha", "Syl", "Thal", "Um",
]

# biome-alapu "hangulati" utotag-keszlet (§6.3 mintajara)
BIOME_SUFFIXES = {
    "IceSheet": ["Frost", "Rime", "Ice", "Glacier"],
    "SeaIce": ["Frost", "Rime", "Ice"],
    "Tundra": ["Tundra", "Barrens", "Waste"],
    "Temperate": ["Forest", "Woods", "Vale", "Downs"],
    "Tropical": ["Isles", "Verdant", "Reach", "Coast"],
}
DEFAULT_SUFFIXES = ["Land", "Reach", "Expanse"]


def _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    key = [world_seed & M64, ((domain_id & 0xFFFFFFFF) << 32) | (property_id & 0xFFFFFFFF), 1, 0]
    ctr = [spatial_id & M64, time_bucket & M64, sample_index & M64, 0]
    return threefry4x64(ctr, key, 20)


def sample_int(world_seed, domain_id, spatial_id, time_bucket, property_id, max_exclusive):
    x = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, 0)[0]
    return x % max_exclusive


def generate_name(world_seed, feature_id, dominant_biome):
    s1 = SYLLABLES[sample_int(world_seed, DOMAIN_NAMING, feature_id, 0, PROPERTY_SYLLABLE_CHOICE, len(SYLLABLES))]
    s2_idx = sample_int(world_seed, DOMAIN_NAMING, feature_id, 1, PROPERTY_SYLLABLE_CHOICE, len(SYLLABLES))
    s2 = SYLLABLES[s2_idx]
    stem = s1 + s2.lower()

    suffixes = BIOME_SUFFIXES.get(dominant_biome, DEFAULT_SUFFIXES)
    suffix = suffixes[sample_int(world_seed, DOMAIN_NAMING, feature_id, 0, PROPERTY_SUFFIX_CHOICE, len(suffixes))]
    return f"{stem} {suffix}"


def find_continents(field, is_ocean, level, min_size=5):
    """Osszefuggo szarazfold-komponensek (ugyanaz, mint sea_level_ref.count_continents)."""
    land = {k for k, v in is_ocean.items() if not v}
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
                nb = neighbor(face, level, u, v, d)
                if nb in land and nb not in visited:
                    visited.add(nb)
                    queue.append(nb)
        if len(component) >= min_size:
            components.append(component)
    return components


def find_watershed_regions(parent, is_ocean):
    """Minden szarazfold-tile-t csoportosit az ocean-"gyoker" szerint, amihez lefolyik."""
    regions = {}  # root_tile -> [tile, ...]
    for tile, is_oc in is_ocean.items():
        if is_oc:
            continue
        current = tile
        while parent[current] is not None and not is_ocean[parent[current]]:
            current = parent[current]
        root = parent[current]  # az elso ocean-tile a lancon (vagy None, ha current mar gyoker - nem lehet, mert current szarazfold)
        if root is None:
            continue  # elvileg nem fordulhat elo szarazfold-tile-nal (ld. hydrology_ref verify)
        regions.setdefault(root, []).append(tile)
    return regions


def dominant_biome(tiles, biome_of):
    counts = {}
    for t in tiles:
        b = biome_of[t]
        counts[b] = counts.get(b, 0) + 1
    return max(counts.items(), key=lambda kv: kv[1])[0]


def ocean_coverage_fraction(is_ocean):
    if not is_ocean:
        return 0.0
    ocean_count = sum(1 for v in is_ocean.values() if v)
    return ocean_count / len(is_ocean)


def river_basin_count(tiles, regions):
    """Hany DISTINCT vizgyujto-regio metsz bele egy tile-halmazba."""
    tile_set = set(tiles)
    count = 0
    for region_tiles in regions.values():
        if any(t in tile_set for t in region_tiles):
            count += 1
    return count


if __name__ == "__main__":
    world_seed = 0xA7C944210000
    plate_count = 20
    level = 6

    print("Elevacio + tengerszint + folyohalozat szamitasa...")
    field, sea_level, is_ocean = compute_elevation_and_ocean_field(world_seed, plate_count, level)
    filled, parent, flood_order = priority_flood(field, is_ocean, level)
    print(f"Kesz: {len(field)} tile\n")

    print("Biome-osztalyozas minden tile-ra...")
    seeds = generate_plate_seeds(world_seed, plate_count)
    orbital_period, rotation_period = 365.25, 1.0
    import math
    axial_tilt = math.radians(23.44)
    biome_of = {}
    for key in field:
        face, u, v = key
        pos = position_from_tile(face, level, u, v)
        t_k = temperature_kelvin(pos, 0.0, orbital_period, rotation_period, axial_tilt,
                                   is_ocean[key], field[key], sea_level)
        biome_of[key] = classify_biome(t_k, is_ocean[key])
    print("Kesz\n")

    print("Folyohalozat (torkolat-szamlalashoz)...")
    accumulation = flow_accumulation(field, parent, flood_order)
    river_target_fraction = 0.03  # ugyanaz, mint a Unity riverTargetFraction alapertelmezese
    river_tiles = select_river_tiles(is_ocean, accumulation, river_target_fraction)
    print(f"{len(river_tiles)} folyo-tile\n")

    print("Kontinens-szegmentalas...")
    continents = find_continents(field, is_ocean, level, min_size=5)
    print(f"{len(continents)} kontinens talalhato\n")

    print("Vizgyujto-regiok...")
    regions = find_watershed_regions(parent, is_ocean)
    sized_regions = {k: v for k, v in regions.items() if len(v) >= 5}
    print(f"{len(sized_regions)} regio (>=5 tile)\n")

    print("--- Kontinensek ---")
    continent_results = []
    # Explicit, hordozhato rendezes: meret csokkeno, majd a komponens
    # legkisebb (face,u,v) tile-ja mint masodlagos kulcs - NEM a Python
    # set/dict bejarasi sorrendjere tamaszkodik (ami C#-ban nem
    # reprodukalhato azonosan).
    for i, comp in enumerate(sorted(continents, key=lambda c: (-len(c), min(c)))):
        biomes_present = {biome_of[t] for t in comp}
        dom = dominant_biome(comp, biome_of)
        name = generate_name(world_seed, feature_id=i, dominant_biome=dom)
        mouths = river_mouth_count(comp, parent, is_ocean, river_tiles)
        basins = river_basin_count(comp, sized_regions)  # csak a "named" (>=5 tile) regiok szamitanak
        result = {
            "name": name, "areaTiles": len(comp), "biomeCount": len(biomes_present),
            "dominantBiome": dom, "riverMouthCount": mouths, "riverBasinCount": basins,
        }
        continent_results.append(result)
        print(f"  {name}: {len(comp)} tile, {len(biomes_present)} biome, "
              f"dominans={dom}, {mouths} folyo-torkolat, {basins} vizgyujto")

    print("\n--- Regiok (top 10 meret szerint) ---")
    region_results = []
    # Explicit, hordozhato rendezes: meret csokkeno, majd a kifolyas
    # (root) tile mint masodlagos kulcs.
    for i, (root, tiles) in enumerate(sorted(sized_regions.items(), key=lambda kv: (-len(kv[1]), kv[0]))):
        dom = dominant_biome(tiles, biome_of)
        name = generate_name(world_seed, feature_id=10000 + i, dominant_biome=dom)
        mouths = river_mouth_count(tiles, parent, is_ocean, river_tiles)
        result = {
            "name": name, "areaTiles": len(tiles), "dominantBiome": dom,
            "outlet": root, "riverMouthCount": mouths,
        }
        region_results.append(result)
        if i < 10:
            print(f"  {name}: {len(tiles)} tile, dominans={dom}, kifolyas={root}, {mouths} torkolat")

    # Plauzibilitas
    assert len(continents) >= 2, "Legalabb 2 kontinensnek kell lennie (TEST-EARTH-001 utan varhato)"
    assert len(sized_regions) >= 2, "Legalabb 2 regionak kell lennie"
    total_region_tiles = sum(len(v) for v in sized_regions.values())
    total_land_tiles = sum(1 for v in is_ocean.values() if not v)
    print(f"\nRegiok altal lefedett szarazfold: {total_region_tiles}/{total_land_tiles} "
          f"({100.0*total_region_tiles/total_land_tiles:.1f}%)")
    print("OK - a szegmentalas plauzibilis")

    # Determinizmus
    name1 = generate_name(world_seed, 42, "Temperate")
    name2 = generate_name(world_seed, 42, "Temperate")
    assert name1 == name2, "A nevgeneralas nem tiszta fuggveny!"
    print("OK - determinisztikus nevgeneralas")

    # Kulonbozo feature_id mas nevet ad
    name3 = generate_name(world_seed, 43, "Temperate")
    assert name1 != name3, "Kulonbozo feature_id ugyanazt a nevet adta - gyanus"
    print("OK - kulonbozo feature_id mas nevet ad")

    # Nevgeneralas tesztvektorok - szelesebb minta, kulonbozo biome-okkal.
    name_vectors = []
    biome_options = list(BIOME_SUFFIXES.keys()) + ["Unknown"]
    for i in range(300):
        p = threefry4x64([i, 0, 0, 0], [0xFEA70000000001, 0, 12, 0], 20)
        feature_id = p[0] % 100000
        biome = biome_options[p[1] % len(biome_options)]
        name = generate_name(world_seed, feature_id, biome)
        name_vectors.append({"featureId": feature_id, "biome": biome, "name": name})

    world_ocean_coverage = ocean_coverage_fraction(is_ocean)
    print(f"\nVilag ocean-borítottság: {world_ocean_coverage*100:.1f}%")

    import json
    with open("features_vectors.json", "w", newline="\n") as f:
        json.dump({
            "worldSeed": world_seed, "plateCount": plate_count, "level": level,
            "worldOceanCoverage": world_ocean_coverage,
            "continents": continent_results, "regions": region_results,
            "nameVectors": name_vectors,
        }, f, indent=1)
    print(f"\n{len(continent_results)} kontinens + {len(region_results)} regio + "
          f"{len(name_vectors)} nev-tesztvektor elmentve")
