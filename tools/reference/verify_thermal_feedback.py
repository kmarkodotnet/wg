"""ND-142: független analitikus gradiens- és időlépés-konvergencia ellenőrzés."""
import math
import json
import sys
import thermal_field_ref as ref


def main():
    ref.LEVEL = 3
    ref.N = 1 << ref.LEVEL
    ref.CELL_COUNT = 6 * ref.N * ref.N
    field = ref.Field()
    feedback = field.feedback
    assert all(v == (0.0, 0.0, 0.0) for v in feedback.gradients([7.0] * ref.CELL_COUNT))
    # A gömbön a lineáris 3D mező analitikus gradiense a tangenciális vetület.
    a = (10.0, 3.0, -2.0)
    theta = [ref.dot(a, p) for p in field.grid.center]
    gradients = feedback.gradients(theta)
    error = max(ref.length(ref.sub(v, tuple(a[k] - ref.dot(a, p) * p[k] for k in range(3))))
                for p, v in zip(field.grid.center, gradients))
    assert error < 1.2, error  # Durva, level-3 rács; varratokat/pólusokat is tartalmaz.
    u, speed = feedback.at(3 * 86400, theta)
    assert all(math.isfinite(v) and abs(v) <= 40.0000000001 for v in u + speed)
    u0, s0 = feedback.at(3 * 86400, [0.0] * ref.CELL_COUNT)
    ub, sb = field.wind.at(3 * 86400)
    assert max(abs(a - b) for a, b in zip(u0 + s0, ub + sb)) < 1e-12
    assert max(abs(a - b) for a, b in zip(u + speed, u0 + s0)) > 0.1
    # Tört napon a nyers gradiens interpolációját is ellenőrizzük.
    seconds = 3 * 86400 + 7 * 3600 + 450
    u, speed = feedback.at(seconds, theta)
    vectors = {
        "modelVersion": ref.MODEL_VERSION, "level": ref.LEVEL, "seconds": seconds,
        "airFeedbackStrength": ref.AIR_FEEDBACK_STRENGTH,
        "edges": [{"index": e, "velocity": u[e]} for e in range(len(u))],
        "cells": [{"index": c, "speed": speed[c]} for c in range(len(speed))],
    }

    states = []
    for dt in (900, 450, 225):
        ts, ta = [0.0] * ref.CELL_COUNT, [0.0] * ref.CELL_COUNT
        for seconds in range(3 * 86400, 4 * 86400, dt):
            ts, ta, _ = field.step(seconds / 900.0, ts, ta, dt)
        assert all(math.isfinite(v) for v in ts + ta)
        states.append(ts + ta)
    errors = [math.sqrt(sum((a - b) ** 2 for a, b in zip(states[i], states[2])) / len(states[2]))
              for i in range(2)]
    assert errors[1] < errors[0] * 0.75, errors
    print(f"PASS: konstans mező, analitikus gradiens (max {error:.6f} K/rad), szélhatár, hatás.")
    print(f"PASS: 24 h időintegráció RMS hibája a 225 s referenciához: 900 s={errors[0]:.8f} K, "
          f"450 s={errors[1]:.8f} K.")
    with open("thermal_feedback_vectors.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(vectors, f, indent=1)
    if "--check" in sys.argv:
        path = sys.argv[sys.argv.index("--check") + 1]
        with open(path, encoding="utf-8") as f:
            expected = json.load(f)
        for key in ("modelVersion", "level", "seconds", "airFeedbackStrength"):
            assert vectors[key] == expected[key], key
        # ND-24: a rács tan-warp konstrukciója natív libm-et használ.
        # A C# vektorteszttel egyező tolerancia; a random-orákulum továbbra is byte-egzakt.
        for key, value in (("edges", "velocity"), ("cells", "speed")):
            assert len(vectors[key]) == len(expected[key]), key
            for got, want in zip(vectors[key], expected[key]):
                assert got["index"] == want["index"]
                assert abs(got[value] - want[value]) <= 1e-9, (key, got, want)
        print("PASS: az újragenerált szélvektorok egyeznek a verziózott orákulummal (ND-24 tolerancia).")


if __name__ == '__main__':
    main()
