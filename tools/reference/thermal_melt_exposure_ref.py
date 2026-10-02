"""ND-172: foknap-integrál a meglévő kis éves orákulumvilágon."""
import json
from pathlib import Path
import thermal_annual_ref as annual
import thermal_field_ref as tf
from snow_melt_ref import positive_degree_days


def generate():
    annual._patch_globals()
    grid = tf.Grid()
    kinds, elevation = annual.oracle_world(grid)
    field = annual.make_field(grid, kinds, elevation)
    state = annual.State(tf.CELL_COUNT)
    pdd = [0.0] * tf.CELL_COUNT
    warmest = [float('-inf')] * tf.CELL_COUNT
    warmest_day = [0] * tf.CELL_COUNT
    warmest_base = [0.0] * tf.CELL_COUNT
    warmest_anomaly = [0.0] * tf.CELL_COUNT
    days = annual.sample_day_indices()
    ticks = tf.SECONDS_PER_DAY // tf.TICK_SECONDS
    for day in days:
        annual.state_at(field, state, day * ticks)
        daily = [0.0] * tf.CELL_COUNT
        daily_base = [0.0] * tf.CELL_COUNT
        daily_anomaly = [0.0] * tf.CELL_COUNT
        for _ in range(ticks):
            _, base = field.baseline.at(state.tick * tf.TICK_SECONDS)
            for c in range(tf.CELL_COUNT):
                air = base[c] + state.theta_a[c]
                pdd[c] += positive_degree_days(air, 1.0 / ticks)
                daily[c] += air
                daily_base[c] += base[c]
                daily_anomaly[c] += state.theta_a[c]
            state.theta_s, state.theta_a, _ = field.step(state.tick, state.theta_s, state.theta_a)
            state.tick += 1
        for c in range(tf.CELL_COUNT):
            mean = daily[c] / ticks
            if mean > warmest[c]:
                warmest[c] = mean
                warmest_day[c] = day
                warmest_base[c] = daily_base[c] / ticks
                warmest_anomaly[c] = daily_anomaly[c] / ticks
    weight = tf.ORBITAL_PERIOD_DAYS / len(days)
    return dict(days=days, pdd=[x * weight for x in pdd], warmest=warmest,
                warmestDay=warmest_day, warmestBase=warmest_base, warmestAnomaly=warmest_anomaly)


if __name__ == '__main__':
    output = Path(__file__).with_name('thermal_melt_exposure_vectors.json')
    output.write_text(json.dumps(generate(), indent=2) + '\n', encoding='utf-8')
    print(output)
