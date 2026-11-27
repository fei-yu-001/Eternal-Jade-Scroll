"""One-shot outline corrections, through the authorized image-studio skill.
Sources stay in ignored logs. Review each preview before installing the artwork.
"""
import argparse
import contextlib
import hashlib
import importlib.util
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SKILL = Path('C:/Users/DDyu/.agents/skills/image-studio/scripts/generate.py')
STYLE = ('Single continuous widescreen cinematic narrative illustration, NO montage, NO panels, NO text, '
         'NO letters, NO speech bubbles. A poor Chinese village during wartime, late Yuan/early Ming inspired. '
         'Fine expressive ink contours, detailed muted watercolor and dry brush, worn hemp and rough timber, '
         'natural adult anatomy, intimate humane emotion. Same grounded serious storybook style across shots. '
         'No chibi, no 3D, no photorealism, no ornate fantasy costume, no shiny silk, no decorative jewelry. '
         'Composition: wide 3:2 image, key action and faces stay in the middle horizontal half, generous scene '
         'space above and below to allow a 16:9 crop. Bottom quarter subdued for later subtitles. ')
MOTHER = ('The mother is a thin Chinese woman around forty-five, oval face, gently tired eyes, sparse grey '
          'strands in a loose practical black bun, no cosmetics. She wears a faded INDIGO patched hemp jacket '
          'with closed crossed collar, no ornament. ')
FATHER = ('The father is a thin Chinese man around fifty, short greying beard, weathered kind face, tied '
          'greying black hair. He remains half-reclined on the SAME low straw bed, legs immobile under '
          'a plain brown quilt, faded grey-brown coarse shirt. He never stands or walks. ')
PROMPTS = {
 'livelihood-hermit-one-arm': ('At a dilapidated rural earth-god shrine, a disheveled elderly Chinese hermit '
     'with scruffy grey beard and loose grey hair stands slightly to the LEFT of center. He has ONLY ONE ARM: '
     'his LEFT ARM IS ABSENT, the left sleeve is EMPTY from the shoulder, visibly flat and tied in a knot '
     'against his belt. No stump, injury or blood. His one remaining RIGHT hand holds a small plain gourd. '
     'The silhouette clearly shows ONE hand only. No hidden second hand, no left hand near the waist. '
     'Frayed dull grey-brown short robe, straw rain cape resting on shoulders, patched trousers, worn shoes. '
     'He is a poor wry drunk with warm shrewd eyes, not a deity. Broken thatch and earthy clay shrine to the '
     'right, dry reeds and tools for roof repair nearby. Quiet late afternoon, no magic glow, no identity symbols.'),
 'home-coming-of-age': (MOTHER + FATHER + 'An intimate FIRST-PERSON viewpoint from the seated eighteen-year-old '
     'adult protagonist whose body and face are OUT OF FRAME (works for either gender). Mother sits slightly '
     'LEFT of center, tenderly leaning forward with a small warm pale brown UNPEELED boiled EGG held between '
     'her fingers at eye level close to the viewer, as if rolling it across the unseen viewer forehead to '
     'bless a coming-of-age birthday. A subtle wisp of steam, one simple egg only. Her loving face is visible '
     'above her hand. Father watches from the bed behind at the right with a small smile. Poor clay walls, '
     'one low table with half a sweet potato, no feast, no crown, no ceremonial fine clothes.'),
 'livelihood-forge': ('A humble village smithy, low timber roof, charcoal forge. An older Chinese blacksmith '
     'wearing a coarse dark short tunic and worn leather apron shows how to sharpen an ordinary SHORT woodcutting '
     'blade on a whetstone, edge deliberately stout. Near the forge a wooden bellows and simple hammer on the '
     'anvil show the day work. Viewpoint is the unseen young adult apprentice, NO fixed protagonist body visible. '
     'Warm orange reflected fire, sweat and soot, tangible labour, no swords, no armor, no magic.'),
 'livelihood-herbs': ('Inside a plain rural herbal shop, an elderly Chinese apothecary with tied grey hair '
     'and a faded grey cotton jacket scoops coarse damp ordinary spent medicinal herb residue from an iron pot '
     'onto a piece of brown cloth at center. Wooden foot-operated grinding trough and baskets of common leaves '
     'behind. His calm weathered face and both natural hands are clear, plain pottery, no labeled jars or text. '
     'The unseen apprentice is the viewpoint, no fixed protagonist. This is affordable herbal care, not alchemy. '
     'No pills, colored potions, smoke magic, crystal bottles, luxurious shop or gold.'),
 'livelihood-father-care': (MOTHER + FATHER + 'Inside the poor clay-walled family cottage at night. Mother sits '
     'beside the straw bed and gently lays a STEAMING BROWN HERBAL CLOTH PACK across the father clothed knee, '
     'her other hand steadies his quilt. A plain earthen cup of cheap clear liquor, common ground herbs and '
     'small basin are on a low stool. The ONLY lamp is a tiny shallow OPEN earthen oil dish with exposed cloth wick. '
     'NO glass lamp, NO lantern chimney, NO kerosene lamp, NO metal lantern, NO modern object. Father releases a held breath, hand unclenches on the blanket, quiet relief '
     'without miraculous cure. He is still bedridden. No medical apparatus, no naked body, no glow.'),
 'livelihood-roof': ('First-person view from a low wooden ladder looking across the eaves of a tiny shabby '
     'earth-god shrine. Only two rough working hands in faded brown hemp sleeves belonging to an ADULT '
     'protagonist of unspecified gender pull a plain hemp cord tight around a bundle of new straw thatch. '
     'Hands natural and correct, no jewelry. Rainwater drips through the still-open patch beside the repair, '
     'old broken reeds and a plain straw stack on the ground below, quiet village trees. This is small kindness '
     'and hard repair work, not a sacred ritual. No visible protagonist face, no statues glowing or monks.'),
 'livelihood-tracking': ('A lean elderly village hunter with rough salt-and-pepper beard and plain dark brown '
     'patched jacket crouches at left on a forest path, using one fingertip to point out a small ordinary '
     'animal print in damp earth near center. At the right lies an INERT untriggered simple hemp slip loop '
     'beside a short wooden peg, used as a tracking lesson; no trapped or hurt animal. His plain wooden hunting '
     'bow is slung on his back. The viewer is the unseen adult apprentice. Woodland autumn grey-green light. '
     'No oversized beast, no monster, no fine armor, no magic.'),
 'tomb-blood-seal': ('EXTREME CLOSE-UP, very low camera at an ancient stone threshold. The main focal point '
     'in the CENTER of the image is EXACTLY TWO small blood drops TOUCHING SIDE BY SIDE in midair to begin '
     'merging into a SINGLE horizontal dumbbell shaped droplet, like two red pearls softly joining. '
     'ONLY TWO LOBES, ONLY THIS SINGLE CONNECTED PAIR, no third drop, no splash, no stream of falling drops, '
     'no droplets in a vertical line. The joined pair hovers immediately over the shallow groove at center. '
     'Place this important shape in the MIDDLE THIRD of the image height, never near the bottom. '
     'At the very far LEFT edge lie two gently clasped ADULT hands RESTING ON THE GROUND, grey and dusty '
     'plum sleeves visible, implying the lovers have fallen together. Only hands and fabric: NO upright '
     'people, NO legs, NO faces, NO visible bodies, NO weapons, NO wounds, no gore. Beyond the pair of tiny '
     'blood drops the stone doorway is dark, with just one narrow dim golden seam at right. Fine ink and '
     'watercolor with visible paper grain, muted brown and grey, intimate sorrow. Not photorealistic. '
     'This exact TWO-drop fusion is the narrative focus, very clear against pale worn stone.')
}

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('shot', choices=PROMPTS)
    parser.add_argument('--channel', default='chuanapi')
    parser.add_argument('--revision', default='r1', choices=['r1','r2','r3'])
    args=parser.parse_args()
    folder=ROOT/'Tools/logs/qingshi-outline-art-20261127'/args.shot/args.revision
    folder.mkdir(parents=True,exist_ok=True)
    target=folder/'source.png'
    if target.exists(): raise SystemExit('Existing source preserved; choose a new revision')
    prompt=STYLE+PROMPTS[args.shot]
    spec=importlib.util.spec_from_file_location('authorized_image_studio',SKILL)
    skill=importlib.util.module_from_spec(spec);spec.loader.exec_module(skill)
    with (folder/'generation.log').open('w',encoding='utf-8') as log, contextlib.redirect_stdout(log), contextlib.redirect_stderr(log):
        result=skill.generate_image(prompt,channel_id=args.channel,output=str(target),size='1536x1024')
    if not result or not target.exists(): raise SystemExit('No image; inspect local log privately')
    receipt=dict(art=args.shot,skill='image-studio',channel=args.channel,revision=args.revision,prompt=prompt,
                 sha256=hashlib.sha256(target.read_bytes()).hexdigest())
    (folder/'source.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    im=Image.open(target).convert('RGB'); im.thumbnail((1280,840))
    im.save(folder/'review.jpg',quality=77,optimize=True)
    print('GENERATED',args.shot,str(folder/'review.jpg'),flush=True)
if __name__=='__main__': main()
