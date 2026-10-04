"""Small fixed-ROI CPU recognizer for calibrated saved RGB frames.

No screenshot capture, input, network, or model downloads. Profiles and templates
must be calibrated against the actual HUD; scores are similarities, not calibrated
probabilities. Numeric OCR uses supplied glyph templates, not a language model.
"""
import json
from pathlib import Path
import time

import numpy as np
from PIL import Image

from .combat_state import CombatSnapshot, Signal, FIELDS


def binary(image, threshold):
    return np.asarray(image.convert('L')) >= threshold


def glyph_shape(mask):
    yy, xx = np.where(mask)
    if len(xx) == 0:
        return None
    # Preserve the baseline/full ROI height: otherwise a decimal point and a
    # narrow vertical digit both normalize into a solid rectangle.
    mask = mask[:, xx.min():xx.max()+1]
    return np.asarray(Image.fromarray(mask).resize((16, 24), Image.Resampling.NEAREST), dtype=bool)


def number(image, glyphs, threshold, minimum, margin):
    mask = binary(image, threshold)
    columns = np.any(mask, axis=0)
    edges = np.diff(np.r_[False, columns, False].astype(int))
    runs = list(zip(np.flatnonzero(edges == 1), np.flatnonzero(edges == -1)))
    if not 1 <= len(runs) <= 6:
        return None, 0.0
    text, qualities = '', []
    for start, end in runs:
        sample = glyph_shape(mask[:, start:end])
        scores = []
        for label, reference in glyphs:
            union = np.count_nonzero(sample | reference)
            score = np.count_nonzero(sample & reference) / union if union else 0
            scores.append((score, label))
        scores.sort(reverse=True)
        if scores[0][0] < minimum or scores[0][0] - scores[1][0] < margin:
            return None, scores[0][0]
        text += scores[0][1]
        qualities.append(scores[0][0])
    if text.count('.') > 1 or text.startswith('.') or text.endswith('.'):
        return None, 0.0
    return float(text), min(qualities)


class HudReader:
    def __init__(self, profile, root):
        self.profile = profile
        self.size = tuple(profile['size'])
        if len(self.size) != 2 or any(type(v) is not int or v <= 0 for v in self.size):
            raise ValueError('Invalid profile size')
        self.detectors = []
        if type(profile.get('derive_alive_from_hp', False)) is not bool:
            raise ValueError('derive_alive_from_hp must be boolean')
        if profile.get('derive_alive_from_hp') and any(d.get('signal') == 'dead' for d in profile['detectors']):
            raise ValueError('Choose one canonical death evidence source')
        names = set()
        for original in profile['detectors']:
            d = dict(original)
            name, kind = d['signal'], d['kind']
            if name not in FIELDS or name in names:
                raise ValueError('Unknown or duplicate signal')
            names.add(name)
            x, y, w, h = d['roi']
            if (any(type(v) is not int for v in (x, y, w, h)) or
                    min(x, y) < 0 or min(w, h) <= 0 or x+w > self.size[0] or y+h > self.size[1]):
                raise ValueError('ROI outside profile image')
            d['minimum'] = d.get('minimum', .95)
            d['margin'] = d.get('margin', .05)
            if not 0 < d['minimum'] <= 1 or not 0 < d['margin'] <= 1:
                raise ValueError('Invalid match thresholds')
            if kind in ('classify', 'number'):
                loaded = []
                for item in d['templates']:
                    with Image.open(Path(root) / item['path']) as im:
                        im = im.convert('RGB')
                        if kind == 'classify':
                            if im.size != (w, h):
                                raise ValueError('Classification template dimensions differ from ROI')
                            array = np.asarray(im, dtype=np.float32)
                            if float(array.std()) < 5:
                                raise ValueError('Flat template is ambiguous')
                            # Validate labels against the canonical field type.
                            CombatSnapshot(((name, Signal(item['value'], 0, 1, 'template')),))
                        else:
                            if item['value'] not in '0123456789.' or len(item['value']) != 1:
                                raise ValueError('Invalid numeric glyph')
                            array = glyph_shape(binary(im, d.get('threshold', 200)))
                            if array is None:
                                raise ValueError('Empty numeric glyph')
                        loaded.append((item['value'], array))
                if len(loaded) < 2 or len({str(v) for v, _ in loaded}) != len(loaded):
                    raise ValueError('At least two distinct template labels are required')
                if kind == 'number' and name not in ('e_seconds', 'retry_seconds'):
                    raise ValueError('Numeric OCR only supports cooldown/deadline fields')
                d['loaded'] = loaded
            elif kind == 'bar':
                if name != 'hp_ratio':
                    raise ValueError('Bar detector is only for HP')
                for bounds in d['colors']:
                    low, high = np.asarray(bounds['low']), np.asarray(bounds['high'])
                    if low.shape != (3,) or high.shape != (3,) or np.any(low < 0) or np.any(high > 255) or np.any(low > high):
                        raise ValueError('Invalid RGB range')
                if not d['colors']:
                    raise ValueError('HP colors required')
                d['occluded_columns'] = d.get('occluded_columns', [])
                for start, end in d['occluded_columns']:
                    if (type(start) is not int or type(end) is not int or
                            not 0 < start < end < w):
                        raise ValueError('Occlusion must be strictly inside the HP bar')
            else:
                raise ValueError('Unknown detector kind')
            self.detectors.append(d)

    @classmethod
    def load(cls, path):
        path = Path(path)
        return cls(json.loads(path.read_text(encoding='utf-8')), path.parent)

    def read(self, image, captured_ms):
        started = time.perf_counter()
        if image.size != self.size or image.mode != 'RGB':
            raise ValueError('Expected calibrated RGB image at exact profile size')
        signals = []
        for d in self.detectors:
            x, y, w, h = d['roi']
            crop = image.crop((x, y, x+w, y+h))
            value, quality = None, 0.0
            if d['kind'] == 'classify':
                pixels = np.asarray(crop, dtype=np.float32)
                scores = sorted([(1-float(np.abs(pixels-ref).mean())/255, i)
                                 for i, (_, ref) in enumerate(d['loaded'])], reverse=True)
                quality = scores[0][0]
                if quality >= d['minimum'] and quality-scores[1][0] >= d['margin']:
                    value = d['loaded'][scores[0][1]][0]
            elif d['kind'] == 'number':
                value, quality = number(crop, d['loaded'], d.get('threshold', 200), d['minimum'], d['margin'])
                if value is not None and value > 3600:
                    value = None
            else:
                pixels = np.asarray(crop)
                mask = np.zeros((h, w), dtype=bool)
                for bounds in d['colors']:
                    mask |= np.all((pixels >= bounds['low']) & (pixels <= bounds['high']), axis=2)
                filled = mask.mean(axis=0) >= .6
                visible = np.ones(w, dtype=bool)
                for start, stop in d['occluded_columns']:
                    visible[start:stop] = False
                    # Bridge fixed numeric overlays only if the fill is visible
                    # on BOTH sides. A hidden endpoint cannot yield an exact HP.
                    if filled[start-1] and filled[stop]:
                        filled[start:stop] = True
                end = next((i for i, present in enumerate(filled) if not present), w)
                # Require a contiguous left-anchored fill. Empty/occluded bars
                # are unknown, not death. Profile is interior of the bar only.
                if end > 0 and not np.any(filled[end:]):
                    if any(start <= end <= stop for start, stop in d['occluded_columns']):
                        signals.append((d['signal'], Signal(None, captured_ms, 0., 'hud_bar')))
                        continue
                    quality = float(mask[:, :end][:, visible[:end]].mean())
                    if quality >= d['minimum']:
                        value = end / w
            signals.append((d['signal'], Signal(value, captured_ms, float(quality), 'hud_' + d['kind'])))
        if self.profile.get('derive_alive_from_hp'):
            hp = dict(signals).get('hp_ratio')
            alive = hp is not None and hp.value is not None and hp.value > 0
            signals.append(('dead', Signal(False if alive else None, captured_ms,
                                          hp.quality if alive else 0., 'positive_hp_evidence')))
        return CombatSnapshot(tuple(signals)), (time.perf_counter()-started)*1000
