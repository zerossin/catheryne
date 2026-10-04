"""Generate artificial HUD fixtures. These are NOT Genshin screenshots."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


def create(root):
    root = Path(root)
    root.mkdir(parents=True, exist_ok=True)
    frame = Image.new('RGB', (3440, 1440), (15, 15, 15))
    detectors = []
    fields = [('combat_ui', True), ('dead', False), ('active_slot', 2),
              ('e_ready', False), ('q_ready', True), ('world_paused', False)]
    for index, (name, value) in enumerate(fields):
        pattern = np.zeros((24, 24, 3), dtype=np.uint8)
        pattern[::2, :, :] = 255
        a = Image.fromarray(pattern)
        b = Image.fromarray(255-pattern)
        a.save(root / (name+'-a.png'))
        b.save(root / (name+'-b.png'))
        roi = [2800+index*30, 1200, 24, 24]
        frame.paste(a, tuple(roi[:2]))
        other = 1 if name == 'active_slot' else not value
        detectors.append(dict(signal=name,kind='classify',roi=roi,templates=[
            dict(value=value,path=name+'-a.png'),dict(value=other,path=name+'-b.png')]))
    roi = [1600, 1350, 200, 8]
    ImageDraw.Draw(frame).rectangle((1600,1350,1719,1357),fill=(40,220,60))
    detectors.append(dict(signal='hp_ratio',kind='bar',roi=roi,colors=[
        dict(low=[20,180,30],high=[70,255,100])]))
    glyphs = []
    prepared = {}
    for char in '0123456789.':
        im = Image.new('RGB',(14,20))
        ImageDraw.Draw(im).text((1,2),char,fill='white',stroke_width=0)
        # Match full ROI height; trim only horizontal blank space.
        arr = np.asarray(im.convert('L')) >= 100
        xs = np.where(arr)[1]
        im = im.crop((int(xs.min()),0,int(xs.max()+1),20))
        filename = 'glyph-'+('dot' if char=='.' else char)+'.png'
        im.save(root/filename)
        prepared[char] = im
        glyphs.append(dict(value=char,path=filename))
    num = Image.new('RGB',(60,20))
    x = 0
    for char in '8.2':
        num.paste(prepared[char],(x,0))
        x += prepared[char].width+3
    frame.paste(num,(3100,1300))
    detectors.append(dict(signal='e_seconds',kind='number',roi=[3100,1300,60,20],
                          templates=glyphs,minimum=.95,margin=.02,threshold=100))
    profile = dict(name='SYNTHETIC ONLY - not calibrated for Genshin',size=[3440,1440],detectors=detectors)
    (root/'profile.json').write_text(json.dumps(profile,indent=2)+'\n',encoding='utf-8')
    (root/'plan.json').write_text(json.dumps(dict(context='synthetic-party',expires_ms=10000,
                                                allowed_slots=[1,2],rotation=['e','q','attack']),indent=2)+'\n',encoding='utf-8')
    frame.save(root/'frame.png')
    return profile, frame


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path)
    args = parser.parse_args()
    if args.directory.exists():
        parser.error('Choose a new directory; existing fixtures are never overwritten')
    create(args.directory)
    print('Synthetic fixtures created; not calibrated game assets.')
