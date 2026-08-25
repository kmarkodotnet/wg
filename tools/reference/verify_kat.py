"""A hivatalos Random123 kat_vectors fájl alapján verifikál — nem emlékezetből."""
from threefry_ref import threefry4x64

total = passed = 0
with open("kat_vectors") as f:
    for line in f:
        parts = line.split()
        if len(parts) < 2 or parts[0] != "threefry4x64":
            continue
        rounds = int(parts[1])
        vals = [int(v, 16) for v in parts[2:]]
        if len(vals) != 12:
            continue
        ctr, key, expected = vals[0:4], vals[4:8], vals[8:12]
        got = threefry4x64(ctr, key, rounds)
        total += 1
        if got == expected:
            passed += 1
            print(f"[OK ] threefry4x64-{rounds}")
        else:
            print(f"[HIBA] threefry4x64-{rounds}")
            print("   várt  :", " ".join(f"{v:016x}" for v in expected))
            print("   kapott:", " ".join(f"{v:016x}" for v in got))

print(f"\n{passed}/{total} vektor egyezik")
