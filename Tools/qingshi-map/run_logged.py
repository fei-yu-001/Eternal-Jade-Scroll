"""Run a checkpoint without replacing raw evidence. Usage: run_logged.py LABEL COMMAND ..."""
import datetime,json,os,pathlib,subprocess,sys
sys.stdout.reconfigure(encoding="utf-8",errors="replace")
ROOT=pathlib.Path(__file__).resolve().parents[2]
def main():
    label=sys.argv[1]
    target=ROOT/'Tools/logs/qingshi-map-20261105'/label
    target.mkdir(parents=True,exist_ok=False)
    start=datetime.datetime.now().astimezone().isoformat()
    command=sys.argv[2:]
    with (target/'stdout.log').open('w',encoding='utf-8') as out:
        process=subprocess.run(command,cwd=ROOT,stdout=out,stderr=subprocess.STDOUT,env={**os.environ,"PYTHONUTF8":"1"})
    report={'command':command,'cwd':str(ROOT),'start':start,'finish':datetime.datetime.now().astimezone().isoformat(),'exitCode':process.returncode}
    (target/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False))
    print('\n'.join((target/'stdout.log').read_text('utf-8',errors='replace').splitlines()[-14:]))
    return process.returncode
if __name__=='__main__':sys.exit(main())
