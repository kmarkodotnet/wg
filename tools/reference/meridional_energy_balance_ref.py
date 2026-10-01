"""ND-168: éves meridionális energiaegyensúly referencia-orákuluma.

Az élfluxus ND-167 szerinti, páronként konzervatív. A kisrács KAT analitikus:
λ=2, A=[1,1], G=1, T0=[300,200] -> B=[275,225] K.
"""

import math
import json
import pathlib
import sys

from conservative_heat_transport_ref import compute_power

DIFFUSION_W_M2_K = 0.555
RADIATIVE_FEEDBACK_W_M2_K = 2.09
ITERATIONS = 256


def conductance(grid, diffusion=DIFFUSION_W_M2_K):
    radius = grid.radius_m
    result = []
    for e, (i, j) in enumerate(zip(grid.edge_i, grid.edge_j)):
        a, b, mid, normal = grid.center[i], grid.center[j], grid.edge_mid[e], grid.edge_normal[e]
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        distance = radius * math.sqrt(dx * dx + dy * dy + dz * dz)
        rho = math.sqrt(mid[0] * mid[0] + mid[1] * mid[1])
        if rho > 1e-12:
            north = (-mid[2] * mid[0] / rho, -mid[2] * mid[1] / rho, rho)
            projection = normal[0] * north[0] + normal[1] * north[1] + normal[2] * north[2]
            result.append(diffusion * radius * radius * (grid.edge_len[e] / distance)
                          * projection * projection)
        else:
            result.append(0.0)
    return result


def solve_correction(area, edge_i, edge_j, g, target, feedback=RADIATIVE_FEEDBACK_W_M2_K,
                     iterations=ITERATIONS):
    count = len(area)
    diagonal = [feedback * a for a in area]
    for e, (i, j) in enumerate(zip(edge_i, edge_j)):
        diagonal[i] += g[e]
        diagonal[j] += g[e]
    source = compute_power(count, edge_i, edge_j, g, target)
    correction = [0.0] * count
    residual = source[:]
    z = [residual[c] / diagonal[c] for c in range(count)]
    direction = z[:]
    rz = sum(residual[c] * z[c] for c in range(count))
    if rz == 0.0:
        return correction
    for _ in range(iterations):
        power = compute_power(count, edge_i, edge_j, g, direction)
        applied = [feedback * area[c] * direction[c] - power[c] for c in range(count)]
        denominator = sum(direction[c] * applied[c] for c in range(count))
        if denominator == 0.0:
            break
        alpha = rz / denominator
        for c in range(count):
            correction[c] += alpha * direction[c]
            residual[c] -= alpha * applied[c]
        z = [residual[c] / diagonal[c] for c in range(count)]
        rz_new = sum(residual[c] * z[c] for c in range(count))
        if rz_new == 0.0:
            break
        beta = rz_new / rz
        direction = [z[c] + beta * direction[c] for c in range(count)]
        rz = rz_new
    mean = sum(area[c] * correction[c] for c in range(count)) / sum(area)
    return [value - mean for value in correction]


def analytic_kat():
    delta = solve_correction([1.0, 1.0], [0], [1], [1.0], [300.0, 200.0],
                             feedback=2.0, iterations=8)
    assert abs(delta[0] + 25.0) < 1e-12
    assert abs(delta[1] - 25.0) < 1e-12
    return delta


if __name__ == "__main__":
    if "--verify-kat" in sys.argv:
        print("ND-168 kétcellás KAT:", analytic_kat())
        sys.exit(0)
    import thermal_field_ref as ref
    ref.LEVEL, ref.N, ref.CELL_COUNT = 1, 2, 24
    grid = ref.Grid()
    target = [280.0 + 20.0 * p[2] for p in grid.center]
    g = conductance(grid)
    correction = solve_correction(grid.area, grid.edge_i, grid.edge_j, g, target)
    vectors = {"decision": "ND-168", "level": 1, "targetK": target,
               "conductanceWPerK": g, "correctionK": correction}
    path = pathlib.Path(__file__).resolve().parents[2] / "tests" / "WorldGen.Core.Tests" / "testdata" / "meridional_energy_balance_vectors.json"
    path.write_bytes((json.dumps(vectors, indent=2) + "\n").encode("utf-8"))
    print("ND-168: level-1 vektor generálva")
