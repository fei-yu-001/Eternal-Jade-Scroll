"""Verify actual baked source and installed world files, including generation-time inputs."""
import json,sys
from pathlib import Path
import world_build_manifest as provenance
ROOT=Path(__file__).resolve().parents[1]
CFG=ROOT/'GameClient/TwelveJade/Assets/Resources/Config'
def main():
    try:
        compose=json.loads((CFG/'world-compose.json').read_text('utf-8'))
        source=ROOT/compose['outputDir']
        generated=provenance.validate(source)
        runtime=CFG.parent/'Art/World'
        installed=json.loads((runtime/'world-build-manifest.json').read_text('utf-8'))
        if installed!=generated:raise ValueError('Installed manifest differs from completed source generation')
        for row in generated['chunks']:
            if provenance.sha(runtime/'Chunks'/row['file'])!=row['sha256']:
                raise ValueError('Installed chunk differs from signed generation: '+row['file'])
        if provenance.sha(runtime/'world-overview.png')!=generated['overview']['sha256']:
            raise ValueError('Installed overview differs from signed generation')
        print('Generation input SHA256: '+generated['generation_input']['sha256'])
        print('RESULT: PASS (64 source chunks + 64 runtime chunks + overview; captured inputs unchanged)')
        return 0
    except (OSError,ValueError,KeyError,TypeError) as error:
        print('RESULT: FAIL ('+str(error)+')');return 1
if __name__=='__main__':sys.exit(main())
