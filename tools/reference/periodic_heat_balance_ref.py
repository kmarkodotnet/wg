"""ND-174: periodikus visszalépő Euler, Fourier + diagonálisan előkondicionált COCG.

COCG: https://doi.org/10.1155/2015/548609 (Algorithm 2).
A bilineáris szorzatokban szándékosan NINCS komplex konjugálás.
"""
import json
import math
from pathlib import Path
import deterministic_math_ref as dm


def solve(area, edge_i, edge_j, conductance, capacity, forcing, period, feedback=2.09):
    phases, count = len(forcing), len(area)
    dt = period / phases
    result = [[0.0] * count for _ in range(phases)]
    for harmonic in range(phases // 2 + 1):
        trig = [dm.sin_cos(2.0 * math.pi * harmonic * j / phases) for j in range(phases)]
        source = [sum(complex(trig[j][1], -trig[j][0]) * forcing[j][c]
                      for j in range(phases)) * area[c] / phases for c in range(count)]
        sine, cosine = dm.sin_cos(2.0 * math.pi * harmonic / phases)
        shift = complex(1.0 - cosine, sine) / dt
        mass = [area[c] * (feedback + capacity[c] * shift) for c in range(count)]
        diagonal = mass[:]
        for i, j, g in zip(edge_i, edge_j, conductance):
            diagonal[i] += g
            diagonal[j] += g

        def apply(x):
            y = [mass[c] * x[c] for c in range(count)]
            for i, j, g in zip(edge_i, edge_j, conductance):
                flux = g * (x[i] - x[j])
                y[i] += flux
                y[j] -= flux
            return y

        x = [0j] * count
        r = source[:]
        z = [r[c] / diagonal[c] for c in range(count)]
        direction = z[:]
        rz = sum(r[c] * z[c] for c in range(count))
        initial = sum(abs(v)**2 for v in r)
        for iteration in range(1024):
            if max(abs(r[c]) / area[c] for c in range(count)) <= 1e-11 or sum(abs(v)**2 for v in r) <= initial * 1e-28 or rz == 0:
                break
            applied = apply(direction)
            denominator = sum(direction[c] * applied[c] for c in range(count))
            if denominator == 0:
                break
            alpha = rz / denominator
            for c in range(count):
                x[c] += alpha * direction[c]
                r[c] -= alpha * applied[c]
            z = [r[c] / diagonal[c] for c in range(count)]
            next_rz = sum(r[c] * z[c] for c in range(count))
            beta = next_rz / rz
            direction = [z[c] + beta * direction[c] for c in range(count)]
            rz = next_rz
        error = max(abs(a-b) / area[c] for c, (a,b) in enumerate(zip(apply(x), source)))
        if not math.isfinite(error) or error > 1e-5:
            # A COCG bilineáris szorzata nem nulla reziduumnál is eltűnhet.
            # Szigorúan diagonáldomináns rendszer: Jacobi tartalékmegoldás.
            x = [0j] * count
            for iteration in range(8192):
                applied = apply(x)
                error = max(abs(applied[c]-source[c])/area[c] for c in range(count))
                if error <= 1e-9:
                    break
                x = [x[c] + (source[c]-applied[c])/diagonal[c] for c in range(count)]
        if error > 1e-5:
            raise ArithmeticError(f'Harmonic residual {error}')
        multiplicity = 1 if harmonic == 0 or 2 * harmonic == phases else 2
        for j in range(phases):
            for c in range(count):
                result[j][c] += multiplicity * (x[c].real * trig[j][1] - x[c].imag * trig[j][0])
    return result


def residual(area, edge_i, edge_j, conductance, capacity, forcing, period, feedback, result):
    dt = period / len(forcing)
    maximum = 0.0
    for phase, current in enumerate(result):
        power = [0.0] * len(area)
        for i, j, g in zip(edge_i, edge_j, conductance):
            flux = g * (current[j] - current[i])
            power[i] += flux
            power[j] -= flux
        for c in range(len(area)):
            value = capacity[c] * (current[c] - result[phase-1][c]) / dt + feedback * current[c] - power[c] / area[c] - forcing[phase][c]
            maximum = max(maximum, abs(value))
    return maximum


def vectors():
    # Állandó kétcellás KAT: B=2, G=1 -> [275,225] egyensúly.
    cases = [dict(area=[1.0,1.0], edge_i=[0], edge_j=[1], conductance=[1.0],
                  capacity=[10.0,20.0], forcing=[[600.0,400.0]]*8, period=100.0, feedback=2.0)]
    # Egycella, 4 fázis: C/dt=B=1 -> 2*Tj-Tj-1=Qj.
    cases.append(dict(area=[1.0], edge_i=[], edge_j=[], conductance=[],
                      capacity=[1.0], forcing=[[1.0],[0.0],[-1.0],[0.0]], period=4.0, feedback=1.0))
    cases.append(dict(area=[2.0,1.0,3.0], edge_i=[0,1], edge_j=[1,2], conductance=[3.0,2.0],
                      capacity=[2.0,20.0,5.0], forcing=[[10+2*j, 30-3*j, -5+j] for j in range(8)],
                      period=100.0, feedback=2.09))
    for case in cases:
        case['expected'] = solve(**case)
        assert residual(**case_without_expected(case), result=case['expected']) < 1e-10
    assert max(abs(row[0]-275) + abs(row[1]-225) for row in cases[0]['expected']) < 1e-10
    assert max(abs(row[0]-expected) for row,expected in zip(cases[1]['expected'],[0.4,0.2,-0.4,-0.2])) < 1e-12
    return cases


def case_without_expected(case):
    return {key:value for key,value in case.items() if key != 'expected'}


if __name__ == '__main__':
    data = vectors()
    output = Path(__file__).with_name('periodic_heat_balance_vectors.json')
    output.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    print('Periodic heat balance: analytic KAT and residuals OK')
