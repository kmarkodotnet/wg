"""ND-172: pozitív foknap és explicit hótároló, m vízegyenértékben.

Forrás: https://www.pism.io/docs/climate_forcing/surface.html
A 3 mm/PDD tényező hóra vonatkozik; nincs visszafagyás vagy jégolvadás.
"""
import json
import math
from pathlib import Path


def nonnegative(value):
    if not math.isfinite(value) or value < 0:
        raise ValueError("Véges, nemnegatív bemenet szükséges")
    return value


def positive_degree_days(air_k, days):
    nonnegative(air_k)
    nonnegative(days)
    return nonnegative(max(air_k - 273.15, 0.0) * days)


def snow_step(snow, snowfall, pdd, factor):
    for value in (snow, snowfall, pdd, factor):
        nonnegative(value)
    available = nonnegative(snow + snowfall)
    potential = nonnegative(pdd * factor)
    melt = min(available, potential)
    return available - melt, melt, potential


def vectors():
    # Független kézi KAT: +2 C három napig = 6 K·nap;
    # 3 mm/K·nap mellett 18 mm olvadás, 120 mm készletből 102 mm marad.
    assert positive_degree_days(275.15, 3.0) == 6.0
    remaining, melt, potential = snow_step(0.1, 0.02, 6.0, 0.003)
    assert abs(remaining - 0.102) < 1e-15
    assert abs(melt - 0.018) < 1e-15 and abs(potential - 0.018) < 1e-15
    remaining, melt, potential = snow_step(0.01, 0, 6, 0.003)
    assert remaining == 0.0 and melt == 0.01 and abs(potential - 0.018) < 1e-15
    cases = []
    for air in (0.0, 263.15, 273.15, 273.16, 275.15, 303.15):
        for days in (0.0, 1.0 / 96, 3.0, 365.25):
            pdd = positive_degree_days(air, days)
            for snow, snowfall, factor in ((0, 0, 0.003), (0.1, 0.02, 0.003),
                                            (0.001, 0.0, 0.006), (1, 2, 0)):
                remaining, melt, potential = snow_step(snow, snowfall, pdd, factor)
                cases.append(dict(airK=air, days=days, snow=snow, snowfall=snowfall,
                                  factor=factor, pdd=pdd, remaining=remaining,
                                  melt=melt, potential=potential))
    return cases


if __name__ == "__main__":
    output = Path(__file__).with_name("snow_melt_vectors.json")
    output.write_text(json.dumps(vectors(), indent=2) + "\n", encoding="utf-8")
    print("KAT OK; 96 vektor:", output)
