"""Generate one four-shot Qingshi storyboard using the user's image-studio skill."""
import argparse,contextlib,hashlib,importlib.util,json
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
SHOTS={
'home':[
('home-sewing','Intimate shot of the kind tired middle-aged mother sewing an INDIGO BLUE patched padded coat by a shallow ceramic oil dish with a short cotton wick, NO glass chimney. Rough careful hands and loving face.'),
('home-father','Disabled father reclines on a straw bed, legs covered, whittling a SMALL handspan elm wooden knife handle, NOT a long stick. His worn hands offer the unfinished handle toward viewer, a quiet shy smile.'),
('home-egg','The mother gently pushes the ONLY boiled egg in a bowl toward an EMPTY seat at a poor wooden table. Two steamed sweet potatoes beside it. Father watches fondly from bed. NO protagonist face or hands.'),
('home-tax','View through poor cottage doorway at dawn: stern official strikes a wooden clapper in lane; Shitou, tall lean eighteen-year-old ADULT in patched clay-brown short hemp tunic and dark trousers, gestures reassuringly toward viewer to come find work.')],
'livelihood':[
('livelihood-work','Poor blacksmith courtyard. Shitou, tall lean eighteen-year-old ADULT in patched clay-brown short hemp tunic, hauls a heavy grain sack past a forge, medicine grinding stone and herb baskets. Sweat and honest effort, no magic.'),
('livelihood-coins','Shitou sits at sunset on a low stone bridge, dirty tired adult face smiling, breaking a tiny piece of salted pork into two portions. A few copper coins on coarse cloth, EMPTY space beside him for player.'),
('livelihood-lovers','Young physician Lu Wuji, plain pale-grey hemp robe and long black hair tied with dark strip; A Wan, faded ochre-brown long dress and practical hair bun. They tend wounded refugees at a kettle shelter. She looks toward a tiny cottage; he glances at her with restrained love.'),
('livelihood-hermit','Shabby ONE-ARMED elderly wandering cultivator, left sleeve visibly empty and tied, untidy grey beard, worn straw cloak, little wine gourd, at a repaired thatched earth-god shrine. Knowing glance toward EMPTY foreground, reed bundles and tools, no glowing magic.')],
'tiger':[
('tiger-tracks','Old hunter in worn straw cape inspects large ordinary tiger paw print in mud beside trampled farm and broken fence. Shitou holds plain wooden spear with apprehension. No beast horns or magic.'),
('tiger-plan','Old hunter explains rope snare and concealed pit to frightened determined adult villagers in patched hemp. Shitou checks wooden spear, looks toward EMPTY place in hunting line for player. Natural dusk forest.'),
('tiger-struggle','Ordinary striped tiger leaps sideways toward concealed pit. Adult villagers hold taut rope together and raise burning torches at safe distance, Shitou braces feet and pulls. Collective human effort, no gore or powers.'),
('tiger-levy','Exhausted bandaged villagers with folded striped tiger pelt on rough cart in village courtyard. Arrogant officials seize cart and demand meagre coins, Shitou clenches fist helplessly. Folded pelt, no corpse or blood.')],
'undercurrent':[
('undercurrent-water','Close-up of bucket by village well, dirty dark foam on water, worried elderly villager draws back hand. Twilight, ordinary dirt, no glow.'),
('undercurrent-missing','Shitou and elder examine abandoned refugee straw hut: single dropped cloth shoe, overturned rice bowl, bootprints in mud leading to dark alley. No victims shown.'),
('undercurrent-letter','A Wan alone in humble medicine room hides folded unsent letter beneath clay medicine pot. Lu Wuji returns late in plain pale-grey hemp robe, dusty hem and tired face. Worried gaze, silence and distance.'),
('undercurrent-caravan','Grey morning supply convoy on winding mountain road to ancient tomb ridge. Shitou carries heavy sack, village guard gestures hurry, ordinary workers in patched hemp, dangerous low clouds, no magic.')],
'tomb':[
('tomb-command','Ancient mountain tomb stone door. Physician Lu Wuji in pale-grey hemp robe faces A Wan in faded ochre-brown dress. Opposing armed adult groups at far left and right demand they kill each other. Lovers trapped, holding eye contact.'),
('tomb-hands','Close-up of Lu Wuji and A Wan clasping both hands, frightened relieved tender faces near one another, simple broken wooden hairpin at feet. Same grey and ochre clothes, stone door behind.'),
('tomb-farewell','Adult lovers hold each other motionless before tomb door, tragic stillness, no visible wounds. TWO tiny red drops meet above stone groove in foreground. Warm seam of light opens beyond, restrained symbolism.'),
('tomb-box','In disorderly tomb chamber furious black-robed adult cultivator throws aside small battered black WOODEN BOX with broken hairpins and faded cloth scraps. Shitou in patched clay-brown tunic collects old cloth and unremarkable rough STONE into discarded box. Treasure overlooked, not a gift.')],
'calamity':[
('calamity-rescue','Rainy village lane night: Shitou with small battered black wooden box tucked to chest runs toward warm window of player family cottage. A scream from neighboring courtyard makes him turn toward it. Good-hearted hesitation.'),
('calamity-window','Low close shot exhausted Shitou lying in mud, adult face with one tear, hand reaching weakly toward SMALL WARM cottage window a few houses away. Wounded black-robed adult looms out of focus behind, no graphic injuries.'),
('calamity-door','Poor family cottage: caring mother urgently takes Shitou by hand and pulls him in for shelter. His eyes cold and unfamiliar; disabled father on straw bed beyond. Oil dish casts tense warm light. Violence only implied, no wounds.'),
('calamity-box','Close dramatic shot: tearful tortured Shitou in patched clay-brown tunic flings BLACK WOODEN BOX into muddy corner inside collapsed cottage. Face twisting between grief and coldness; overturned oil dish and indigo blue coat on floor, no explicit gore or bodies.')],
'departure':[
('departure-ruins','Grey dawn inside silent ruined cottage. INDIGO BLUE patched coat lies across broken chair; SMALL unfinished handspan elm knife handle by empty straw bed. Empty foreground, loss through beloved objects, no bodies.'),
('departure-witness','Only TWO surviving elderly neighbors emerge from deep cellar under broken floor and behind fire-blackened straw stack. One trembling elder points toward mountain forest, grief and fear, morning ashes. No crowd.'),
('departure-box','Opened battered BLACK WOODEN BOX in family cottage muddy corner. Several faded paper scraps and ONE coarse grey stone wrapped in old cloth. No jewelry, glowing treasure or sword. Narrow cold dawn illuminates evidence.'),
('departure-road','Two modest fresh earth graves with simple unlettered wood markers by ruined village at dawn. Dirt road leads to uncertain mountain horizon. Plain patched travel bundle by graves, NO protagonist, suitable both player genders. Quiet grief, no fantasy city.')]
}
SHOTS['turning'] = [
('calamity-possession', 'Rainy village lane at night. Shitou, lean eighteen-year-old ADULT with messy topknot, patched clay-brown short hemp tunic and dark trousers, kneels helpless in mud. A black-robed wounded villain dissolves into a vague ASH AND EMBER SILHOUETTE behind him as thin ominous dark mist enters Shitou. His hand clenches involuntarily, tears on face. Warm cottage window far away. Visualize supernatural possession, not an attack by the hero. No explicit injuries or gore, no new glowing costume, no identity labels.'),
('calamity-flight', 'Wide night scene from ruined village lane toward deep mountain forest. Same patched clay-brown adult Shitou runs away toward the distant trees, only his back visible, unmistakable hunched silhouette. Rain and overturned baskets in foreground. No box in his hands. The possessed body has left the wooden box in the cottage. No gore or victims.'),
('tomb-road', 'First-person view of a mountain road cut off by fallen boulders and broken branches at night. Distant village warm lights and smoke beyond the blocked path. A small torn plain cloth bundle in foreground with a subtle red stain, no body or hero visible, gender-neutral. Sense of injured traveler desperate to get home, no magic.'),
('departure-interior', 'Quiet close view inside the ruined family cottage at grey dawn. On an EMPTY straw bed lies a SMALL unfinished handspan piece of ELM WOOD, a rough partially whittled handle with a shallow notch, ALL WOOD, NO METAL, NO BLADE, NO assembled knife. A few wood shavings beside it. INDIGO BLUE patched padded coat folded on broken chair in foreground. No characters, bodies or hands. Show only the two unfinished gifts that survive the parents. Intimate restrained grief.')
]
def main():
 ap=argparse.ArgumentParser();ap.add_argument('scene',choices=SHOTS);args=ap.parse_args()
 out=ROOT/'Tools/logs/qingshi-storyboards-20261123'/args.scene;out.mkdir(parents=True,exist_ok=True);target=out/'source.png'
 if target.exists():raise SystemExit('Existing source preserved')
 style='Create a coherent FOUR-SHOT cinematic storyboard of Chinese historical rural tragedy. EXACTLY 2 columns and 2 rows of equal-size rectangular panels, all fully painted, straight divisions at half-width and half-height. NO captions NO text NO numbers NO speech bubbles NO blank gutters NO decorative borders NO inset panels. Consistent faces, colors, costume, village and props in four shots. Fine ink drawing, nuanced muted watercolor, expressive natural adult anatomy, tactile coarse hemp, worn wood, restrained cinematic light, beautiful serious narrative illustration, not chibi, not photograph, no extravagant fantasy clothes. Fictional late Yuan/early Ming poor village in wartime. Keep bottom quarter of EACH panel relatively uncluttered for later dialogue. No fixed protagonist shown: point of view and empty space work for male/female player. All young people are eighteen-year-old ADULTS. Four panels LEFT TO RIGHT then TOP TO BOTTOM. '
 prompt=style+' '.join(f'PANEL {i+1}: {desc}' for i,(_,desc) in enumerate(SHOTS[args.scene]))
 spec=importlib.util.spec_from_file_location('user_image_studio','C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py');skill=importlib.util.module_from_spec(spec);spec.loader.exec_module(skill)
 with (out/'generation.log').open('w',encoding='utf-8') as log,contextlib.redirect_stdout(log),contextlib.redirect_stderr(log):
  result=skill.generate_image(prompt,channel_id='chuanapi',output=str(target),size='1536x1024')
 if not result or not target.exists():raise SystemExit('Generation failed; inspect local log privately')
 (out/'source.json').write_text(json.dumps({'scene':args.scene,'shots':SHOTS[args.scene],'prompt':prompt,'skill':'image-studio','sha256':hashlib.sha256(target.read_bytes()).hexdigest()},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 im=Image.open(target).convert('RGB');im.thumbnail((1440,1000));im.save(out/'review.jpg',quality=76,optimize=True)
 print('GENERATED',args.scene,target.stat().st_size,flush=True)
if __name__=='__main__':main()
