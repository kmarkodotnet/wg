"""ND-174: az AKTÍV szezonális bázis + LS-szél + napi solver integrációs orákuluma."""
import json
from pathlib import Path
import thermal_annual_ref as annual
import thermal_field_ref as tf


def generate():
    annual._patch_globals()
    tf.USE_SEASONAL_ENERGY_BALANCE=True
    grid=tf.Grid()
    kinds,elevation=annual.oracle_world(grid)
    field=annual.make_field(grid,kinds,elevation)
    state=annual.State(tf.CELL_COUNT)
    annual.state_at(field,state,100)
    _,baseline=field.baseline.at(100*tf.TICK_SECONDS)
    edge,speed=field.feedback.at(100*tf.TICK_SECONDS,state.theta_a)
    return dict(tick=100,baselineK=baseline,thetaS=state.theta_s,thetaA=state.theta_a,
                edgeWind=edge,cellSpeed=speed,
                annual=annual.annual_statistics(field,annual.State(tf.CELL_COUNT)))


if __name__=='__main__':
    Path(__file__).with_name('seasonal_coupled_vectors.json').write_text(json.dumps(generate(),indent=2)+'\n',encoding='utf-8')
    print('Active seasonal coupled oracle generated')
