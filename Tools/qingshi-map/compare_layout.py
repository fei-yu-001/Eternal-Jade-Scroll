"""Audit current layout against the preserved input using identical criteria and per-key changes."""
import importlib.util,json,pathlib,sys,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools'))
spec=importlib.util.spec_from_file_location('layout_gate',ROOT/'Tools/check-town-layout.py');gate=importlib.util.module_from_spec(spec);spec.loader.exec_module(gate)
CFG=ROOT/'GameClient/TwelveJade/Assets/Resources/Config';LOG=ROOT/'Tools/logs/qingshi-map-20261105'
def read(p):return json.loads(p.read_text('utf-8'))
def collect(plots,town):
    checks=gate.run_checks(plots['plots'],town)
    fail,warn,info=gate.arbitrate(checks,gate.BASELINE)
    rows=[]
    for check in checks:
        for v in check.violations:
            baseline=gate.BASELINE.get(v['key'],{})
            raw=v['level']; effective='WARN' if baseline.get('armed') and baseline.get('level')=='red' and raw=='FAIL' else raw
            rows.append({**v,'effective':effective})
    return {'failCount':len(fail),'warnCount':len(warn),'assertions':rows,'zones':{str(k):[p['id'] for p in v] for k,v in gate.zone_index(plots['plots'],town['terrainShape']).items()}}
def main():
    before=collect(read(ROOT/'Tools/qingshi-map/plots-before.json'),read(ROOT/'.batch/qingshi-map-20261105/before/GameClient/TwelveJade/Assets/Resources/Config/town-map.json'))
    after=collect(read(CFG/'building-plots.json'),read(CFG/'town-map.json'))
    a={v['key']:v for v in before['assertions'] if v['effective']=='FAIL'};b={v['key']:v for v in after['assertions'] if v['effective']=='FAIL'}
    report={'criteriaSha256':hashlib.sha256((ROOT/'Tools/check-town-layout.py').read_bytes()).hexdigest(),'classification':'world ground anchor footY+height, applied to both snapshots','before':before,'after':after,'added':[b[k] for k in b.keys()-a.keys()],'resolved':[a[k] for k in a.keys()-b.keys()],'remaining':[{'key':k,'before':a[k]['text'],'after':b[k]['text']} for k in a.keys()&b.keys()]}
    (LOG/'layout-assertion-comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"Same criteria: {len(a)} -> {len(b)} FAIL; added {len(report['added'])}; resolved {len(report['resolved'])}; remaining {len(report['remaining'])}")
if __name__=='__main__':main()
