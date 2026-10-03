"""ND-175: m vízegyenérték/év kalibráció és évfázisos hó/fagyás KAT."""
import json
from pathlib import Path
from snow_melt_ref import positive_degree_days


def budget(temperature, period, precipitation, kind):
    snow = sum(precipitation/len(temperature)*max(0.0,min(1.0,(275.15-t)/2.0)) for t in temperature)
    pdd = sum(positive_degree_days(t,period/len(temperature)) for t in temperature)
    melt = pdd*0.003
    freezing = 271.35 if kind == 1 else 273.15
    water = kind in (1,2)
    margin = max(temperature)-freezing if water else (melt-snow)/(0.003*period)
    classification = 2 if margin<0 else 1 if min(temperature)<freezing and (water or snow>0) else 0
    return dict(snow=snow,melt=melt,margin=margin,classification=classification)


if __name__ == '__main__':
    cases=[]
    for temperature in ([263.15]*4,[283.15]*4,[263.15,275.15,277.15,265.15],[270.15]*4):
        for kind in (0,1,2):
            for precipitation in (0.0,0.1,1.0,3.0):
                case=dict(temperature=temperature,period=365.25,precipitation=precipitation,kind=kind)
                case['expected']=budget(**case)
                cases.append(case)
    cold=budget([263.15]*4,365.25,1,0)
    assert cold['snow']==1 and cold['melt']==0 and cold['classification']==2
    assert budget([263.15]*4,365.25,0,0)['classification']==0
    assert budget([270.15]*4,365.25,0,1)['classification']==2
    assert budget([270.15,274.15],365.25,0,1)['classification']==1
    Path(__file__).with_name('physical_ice_budget_vectors.json').write_text(json.dumps(cases,indent=2)+'\n',encoding='utf-8')
    print('Physical ice budget KAT OK; 48 vectors')
