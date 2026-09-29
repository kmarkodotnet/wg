"""ND-167: páronként konzervatív hőfluxus referencia-orákuluma.

A vezetőképesség itt bemenet (W/K), nem kalibrált klímaparaméter.
"""

import json
import pathlib
import sys


def compute_power(cell_count, edge_i, edge_j, conductance_w_per_k, temperature_k):
    """Cellánkénti teljesítmény (W), kanonikus élsorrendben."""
    if len(temperature_k) != cell_count:
        raise ValueError("Eltérő cellaszám")
    if not (len(edge_i) == len(edge_j) == len(conductance_w_per_k)):
        raise ValueError("Eltérő élszám")
    power = [0.0] * cell_count
    for e in range(len(edge_i)):
        i, j = edge_i[e], edge_j[e]
        if not (0 <= i < j < cell_count):
            raise ValueError("Nem kanonikus él")
        q = conductance_w_per_k[e] * (temperature_k[j] - temperature_k[i])
        power[i] += q
        power[j] -= q
    return power


def cases():
    return [
        dict(cellCount=2, edgeI=[0], edgeJ=[1], conductanceWPerK=[3.0],
             temperatureK=[10.0, 20.0], powerW=[30.0, -30.0]),
        dict(cellCount=4, edgeI=[0, 0, 1, 2], edgeJ=[1, 2, 3, 3],
             conductanceWPerK=[1.25, 2.0, 0.5, 3.0],
             temperatureK=[260.0, 280.0, 275.0, 250.0],
             powerW=[55.0, -40.0, -105.0, 90.0]),
        dict(cellCount=4, edgeI=[0, 0, 1, 2], edgeJ=[1, 2, 3, 3],
             conductanceWPerK=[1.25, 2.0, 0.5, 3.0],
             temperatureK=[270.0, 270.0, 270.0, 270.0],
             powerW=[0.0, 0.0, 0.0, 0.0]),
    ]


def main():
    vectors = cases()
    for case in vectors:
        got = compute_power(case["cellCount"], case["edgeI"], case["edgeJ"],
                            case["conductanceWPerK"], case["temperatureK"])
        assert got == case["powerW"], (got, case["powerW"])
        assert sum(got) == 0.0
    output = json.dumps({"decision": "ND-167", "cases": vectors}, indent=2, ensure_ascii=False) + "\n"
    if len(sys.argv) == 2 and sys.argv[1] in ("--verify-vectors", "--write-vectors"):
        path = pathlib.Path(__file__).resolve().parents[2] / "tests" / "WorldGen.Core.Tests" / "testdata" / "conservative_heat_transport_vectors.json"
        if sys.argv[1] == "--write-vectors":
            path.write_bytes(output.encode("utf-8"))
            print("ND-167 vektorok generálva")
        else:
            assert output.encode("utf-8") == path.read_bytes(), "Az ND-167 vektorok bájtjai eltérnek"
            print("ND-167: 3/3 KAT, vektorok bájtszinten egyeznek")
    elif len(sys.argv) == 1:
        print(output, end="")
    else:
        raise ValueError("Ismeretlen parancssori argumentum")


if __name__ == "__main__":
    main()
