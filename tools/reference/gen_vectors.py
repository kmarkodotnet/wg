"""
A WorldGen Sample() API referencia-implementációja + tesztvektor-generálás.
A Threefry mag már hitelesítve a hivatalos Random123 KAT-okhoz.
"""
import json
from threefry_ref import threefry4x64, M64

ALGORITHM_VERSION = 1


def _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    """A kulcs/counter leképezés. Ez a szerződés lényege — ha ez változik, breaking change."""
    key = [
        world_seed & M64,
        ((domain_id & 0xFFFFFFFF) << 32) | (property_id & 0xFFFFFFFF),
        ALGORITHM_VERSION,
        0,
    ]
    ctr = [
        spatial_id & M64,
        time_bucket & M64,
        sample_index & M64,
        0,
    ]
    return threefry4x64(ctr, key, 20)


def sample(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    """-> [0,1) double. A felső 53 bit használata, FPU-független."""
    x0 = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)[0]
    return (x0 >> 11) * (2.0 ** -53)


def sample4(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index):
    """4 független double egy blokkból — gradiensvektorokhoz hatékony."""
    xs = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, sample_index)
    return [(x >> 11) * (2.0 ** -53) for x in xs]


def sample_int(world_seed, domain_id, spatial_id, time_bucket, property_id,
               sample_index, min_incl, max_excl):
    """Torzításmentes egész, Lemire-féle elutasításos módszerrel."""
    span = max_excl - min_incl
    assert span > 0
    # elutasítási küszöb: a 2^64 span-nel nem osztható maradéka
    threshold = (2 ** 64) % span
    i = sample_index
    while True:
        x = _block(world_seed, domain_id, spatial_id, time_bucket, property_id, i)[0]
        if x >= threshold:
            return min_incl + (x % span)
        i += 1  # elutasítva, következő blokk — determinisztikus


DOMAINS = {
    "TERRAIN": 1,
    "TECTONICS": 2,
    "CLIMATE": 3,
    "HYDROLOGY": 4,
    "EVENTS": 5,
    "NAMING": 6,
    "CYCLONE": 7,
    "NEURAL_TEX": 8,
}

if __name__ == "__main__":
    vectors = []

    # 1. Determinált, kézzel választott élesetek
    edge_cases = [
        (0, 0, 0, 0, 0, 0),
        (1, 1, 1, 1, 1, 1),
        (M64, 0xFFFFFFFF, M64, M64, 0xFFFFFFFF, M64),
        (0xA7C944210000, DOMAINS["TERRAIN"], 0, 0, 0, 0),
        (0xA7C944210000, DOMAINS["CLIMATE"], 0, 0, 0, 0),
        (0xA7C944210000, DOMAINS["TERRAIN"], 1, 0, 0, 0),
        (0xA7C944210000, DOMAINS["TERRAIN"], 0, 1, 0, 0),
        (0xA7C944210000, DOMAINS["TERRAIN"], 0, 0, 1, 0),
        (0xA7C944210000, DOMAINS["TERRAIN"], 0, 0, 0, 1),
    ]
    for ec in edge_cases:
        vectors.append({"in": list(ec), "block": [f"{v:016x}" for v in _block(*ec)]})

    # 2. Determinisztikusan generált tömeg — a generálás maga is reprodukálható
    gen_seed = 0xDEADBEEFCAFEBABE
    for i in range(503):
        p = [_block(gen_seed, 999, i, k, 0, 0)[0] for k in range(6)]
        args = (p[0], p[1] & 0xFFFFFFFF, p[2], p[3], p[4] & 0xFFFFFFFF, p[5] & 0xFFFF)
        vectors.append({"in": list(args), "block": [f"{v:016x}" for v in _block(*args)]})

    out = {
        "algorithmVersion": ALGORITHM_VERSION,
        "algorithm": "Threefry-4x64-20",
        "note": "A Threefry mag a hivatalos Random123 kat_vectors fajlhoz hitelesitve (9/9).",
        "inputOrder": ["worldSeed", "domainId", "spatialId", "timeBucket",
                       "propertyId", "sampleIndex"],
        "domains": DOMAINS,
        "vectors": vectors,
    }
    with open("testvectors.json", "w", newline="\n") as f:
        json.dump(out, f, indent=1)

    print(f"{len(vectors)} tesztvektor generálva")
    print()
    print("Mintaértékek (Nereida-7 seed):")
    s = 0xA7C944210000
    for d in ["TERRAIN", "CLIMATE", "NAMING"]:
        v = sample(s, DOMAINS[d], 12345, 0, 0, 0)
        print(f"  {d:10s} -> {v:.17f}")

    # Épkézláb-ellenőrzés: eloszlás
    vals = [sample(s, 1, i, 0, 0, 0) for i in range(200000)]
    mean = sum(vals) / len(vals)
    var = sum((v - mean) ** 2 for v in vals) / len(vals)
    buckets = [0] * 10
    for v in vals:
        buckets[min(9, int(v * 10))] += 1
    print()
    print(f"Eloszlás 200k mintán: átlag={mean:.5f} (várt 0.5)  szórásnégyzet={var:.5f} (várt 0.08333)")
    print(f"Decilis-hisztogram (várt ~20000): {buckets}")
    print(f"Min={min(vals):.6f}  Max={max(vals):.6f}")
