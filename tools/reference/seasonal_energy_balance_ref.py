"""ND-174: a periodikus EBM bolygó-rácsra kötése, kisrács-orákulum."""
import json
from pathlib import Path
import thermal_field_ref as tf
from periodic_heat_balance_ref import solve, residual
from meridional_energy_balance_ref import conductance

PHASES = 48
OLR_INTERCEPT = 210.0
FEEDBACK = 2.09


def build(grid, kinds, elevation, sea_level, cycle, phases=PHASES):
    count = len(kinds)
    capacity = [tf.SURFACE_HEAT_CAPACITY[tf.OCEAN if k==tf.ICE and elevation[c]<sea_level else k]
                + tf.AIR_HEAT_CAPACITY for c,k in enumerate(kinds)]
    forcing = []
    for phase in range(phases):
        samples = tf.daily_sample_directions(phase * tf.ORBITAL_PERIOD_DAYS / phases - 0.5)
        row = []
        for c in range(count):
            z = grid.center[c][2]
            albedo = 0.62 if kinds[c] == tf.ICE else 0.30 + 0.078 * (1.5*z*z - 0.5)
            absorbed = tf.F_PEAK * tf.average_factor(grid.center[c], samples) * (1.0-albedo)
            offset = cycle - tf.LAPSE_RATE_K_PER_M * max(0.0, elevation[c]-sea_level)
            row.append(absorbed - OLR_INTERCEPT + FEEDBACK * offset)
        forcing.append(row)
    g = conductance(grid)
    result = solve(grid.area, grid.edge_i, grid.edge_j, g, capacity, forcing,
                   tf.ORBITAL_PERIOD_DAYS*86400.0, FEEDBACK)
    error = residual(grid.area, grid.edge_i, grid.edge_j, g, capacity, forcing,
                     tf.ORBITAL_PERIOD_DAYS*86400.0, FEEDBACK, result)
    assert error < 0.001, error
    return result, forcing, error


if __name__ == '__main__':
    import thermal_annual_ref as annual
    annual._patch_globals()
    tf.ORBITAL_PERIOD_DAYS = 365.25
    grid = tf.Grid()
    kinds, elevation = annual.oracle_world(grid)
    cycle = tf.climate_cycle_temperature_k(annual.WORLD_SEED, 0.0)
    values, forcing, error = build(grid, kinds, elevation, 0.0, cycle)
    data = dict(level=tf.LEVEL, phases=PHASES, cycle=cycle, temperatureC=values,
                forcing=forcing, residual=error)
    Path(__file__).with_name('seasonal_energy_balance_vectors.json').write_text(
        json.dumps(data, indent=2)+'\n', encoding='utf-8')
    print('Seasonal EBM oracle residual:', error)
