"""Generate one nine-pose locomotion sheet with the user's image-studio skill."""
import argparse, contextlib, hashlib, importlib.util, json
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
SKILL = Path('C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py')
PEOPLE = {
 'male': 'handsome eighteen-year-old ADULT Chinese man, lean natural adult body, warm youthful face, black hair tied in a plain small topknot, faded grey-brown patched short hemp jacket, rolled forearms, cloth sash, charcoal straight trousers with a knee patch, worn flat dark cloth shoes',
 'female': 'beautiful eighteen-year-old ADULT Chinese woman, lean natural adult body, warm youthful face, black hair tied in a practical low bun with grey cloth strip, faded grey-indigo patched short hemp jacket, modest closed collar, rolled forearms, cloth sash, charcoal straight trousers with a knee patch, worn flat dark cloth shoes'
}
VIEWS = {'side': 'STRICT RIGHT-FACING SIDE PROFILE, nose and toes pointing to the RIGHT in ALL NINE CELLS',
 'front': 'STRICT FRONT VIEW, walking directly TOWARD the viewer, never side view; correct foreshortening of approaching and trailing legs',
 'back': 'STRICT BACK VIEW, walking directly AWAY from viewer; the back of the head and back of the jacket visible in ALL NINE CELLS, no face'}
def main():
 ap=argparse.ArgumentParser();ap.add_argument('gender',choices=PEOPLE);ap.add_argument('view',choices=VIEWS);ap.add_argument('--revision',default='r1');args=ap.parse_args()
 out=ROOT/'Tools/logs/qingshi-locomotion-20261123'/f'{args.gender}-{args.view}-{args.revision}'
 out.mkdir(parents=True,exist_ok=True); raw=out/'source.png'
 if raw.exists(): raise SystemExit('Existing source preserved.')
 prompt='''Professional 2D historical RPG sprite animation production sheet, tasteful hand-painted semi-realistic Chinese guofeng concept art, fine expressive dark ink outlines, soft restrained shading. EXACTLY 3 columns and 3 rows, nine equally sized SQUARE cells, plain PURE WHITE background. No drawn grid, no captions, no text, no shadows on floor. Same character, same size, same costume, same face and hairstyle repeated in EVERY cell. All nine figures full body from hair to soles with 10 percent white padding on each side, feet on the same baseline within every cell. Consistent perspective, lighting and proportions, eight heads tall, NO chibi, NO photograph, NO armor, NO silk, NO jewelry, NO weapons. This person is a rural villager in wartime: ''' + PEOPLE[args.gender]+'. '+VIEWS[args.view]+'''.
The FIRST EIGHT cells are EIGHT DISTINCT sequential keyframes of ONE complete natural WALK cycle, read left-to-right then top-to-bottom. Anatomically credible alternating limbs with clear knee flexion, heel strike, toe-off, grounded support foot and counter-swinging arms. Do not copy just two poses. Modest natural step length, not a military march or running, not arms held stiffly. Cell 1: LEFT leg leading contact, RIGHT arm forward. Cell 2: left leg taking weight with soft bent knee, right heel lifting into toe-off, body slightly lower. Cell 3: right leg bent in swing passing beneath body, left leg planted, hands crossing waist. Cell 4: right leg swinging ahead with bent knee and toe slightly raised, left heel rising, left arm swinging forward, body slightly higher. Cell 5: RIGHT leg leading contact, LEFT arm forward, clearly opposite of cell 1. Cell 6: right leg taking weight, left heel lifting, body slightly lower. Cell 7: left leg bent in swing passing beneath body, right leg planted, hands crossing waist. Cell 8: left leg swinging ahead, right heel rising, right arm forward. Cell 9: NATURAL IDLE STANDING, both feet firmly planted shoulder width, knees soft, arms relaxed alongside body, same view and scale.
Full character must fit each cell. Keep chest, hips and head proportions stable throughout. Dark outlines around shoes and sleeves, no white clothing or shiny highlights so the white background can be removed cleanly.'''
 spec=importlib.util.spec_from_file_location('user_image_studio',SKILL);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 with (out/'generation.log').open('w',encoding='utf-8') as log,contextlib.redirect_stdout(log),contextlib.redirect_stderr(log):
  result=m.generate_image(prompt,channel_id='chuanapi',output=str(raw),size='1536x1536')
 if not result or not raw.exists(): raise SystemExit('Generation failed; inspect local log privately.')
 (out/'source.json').write_text(json.dumps({'gender':args.gender,'view':args.view,'prompt':prompt,'skill':'image-studio','sha256':hashlib.sha256(raw.read_bytes()).hexdigest()},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('GENERATED',args.gender,args.view,raw.stat().st_size)
if __name__=='__main__':main()
