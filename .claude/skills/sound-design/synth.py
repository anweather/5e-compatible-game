#!/usr/bin/env python3
"""Renders sound presets to WAV and prints the vocabulary's measures. Python 3 standard library only.

Each render is seeded, so it comes out the same every run. See SKILL.md for the loop, vocabulary.md for
the words. Loudness is set here, not in presets, so that every variant is compared at the same level.
The cast series lives in this file; every other family's series is in families.py, its layers in layers.py."""
import argparse, json, math, os, random, time

import families
from dsp import RATE, TAU, amount, attack_ms, band_share, db, decay_ms, options, rms, write_wav
from layers import HELD, STRUCK, add_thump, add_whoosh, drone_partials

HUM_GAIN = 0.3        # a hum of 1 is the picked option B's level
TARGET_DB = -25.0     # every variant's whole series, dBFS RMS
PEAK = 0.6            # never above this (leaves room for the world's sound)
GAME_CAST_DB = -28.8  # the game's current cast sounds on the same take, world's sound taken out
C_PULSE = dict(depth=0.55, hz=7.0, step_hz=4.0)  # option C's pulse: a pulse amount of 1 is this

# A series is the moments every variant renders, as times from a real cast take (J-040), so variants
# compare like for like. Each entry of `casts` becomes a file; `joined` is all of them on the one timeline.
SERIES = {
    'cast': dict(
        says='cast sounds: earth single, water + air pair, charged fire (two charge steps), each with its release',
        samples=519168,
        casts=[
            dict(file='earth-single', first='earth', start=0.4, slam=1.167, cut=1.733, home=1.9),
            dict(file='water-air-pair', first='water', second='air', second_at=3.767, start=3.4, slam=4.5, cut=4.7, home=4.867),
            dict(file='charged-fire', first='fire', start=6.367, slam=7.3, steps=[7.7, 8.167], cut=9.0, home=9.367),
        ]),
}

# A word left out of a preset is off, except REQUIRED, which every sound needs.
REQUIRED = {'root_hz', 'swell', 'cut off', 'whoosh', 'thump'}
WORDS = {'name', 'says', 'seed', 'series', 'root_hz', 'hum', 'fifths', 'choir', 'rumble', 'sub', 'grit', 'pulse',
         'rising', 'escalate', 'layer', 'swell', 'cut off', 'whoosh', 'thump', 'colour'}
# Every other family's preset names its game sound (`sound`, a SOUNDS.md id) and builds it from layers.
SOUND_WORDS = {'name', 'says', 'seed', 'sound', 'length', 'unpitched', 'root_hz', 'fifths', 'choir', 'pulse', 'rising',
               'swell', 'tail', 'cut off', 'gusting', 'dull'} | set(HELD) | set(STRUCK)
EPILOG = 'series:\n' + '\n'.join(f'  {k}: {v["says"]}\n    files: ' + ', '.join(c['file'] for c in v['casts']) + ', joined'
                                 for k, v in SERIES.items()) + '\n' + families.describe() + '''
  (a family's sounds render to <out>/<family>/<id>.wav, every take 0.6 s apart unless the series says,
   and _joined-<family>.wav: every sound of the family rendered at once, 1.2 s apart)

cast preset words (vocabulary.md), amounts 0-1 unless a unit is given; a number or {"amount": n, ...}:
  root_hz   {element: Hz}          hum 1 is option B's level; fifths scales the 1.5x, 2x and 3x partials
  choir     {amount, cents, voices}    detuned copies of the root
  rumble    {amount, hz}           low-passed noise       sub {amount, hz}   sine under the hum
  grit      crackle of noise pops
  pulse     {amount, accelerating, depth?, hz?, step_hz?}   amount is the share of option C's depth
  rising    {semitones, glide_ms}  pitch step when the elements combine
  escalate  {semitones: [per step], gain}    on top of rising, each charge step louder by gain
  layer     level of the second element over the first
  swell {ms}, "cut off" {ms}, whoosh {amount, peak_hz, rest_hz, tail_ms}, thump {amount, from_hz, to_hz, decay_ms}
  colour    {element: {rumble, choir, fifth (multipliers), wobble (Hz), grit (amount)}}

sound preset words (every other family): "sound" (its SOUNDS.md id) and any layers; length defaults to today's.
A layer is a number (its amount), {amount, ...} or a list of those; every layer takes at_ms (when it starts).
  struck, each with its own shape:
  thump     {amount, from_hz, to_hz, decay_ms}     crack {amount, hz, ms}   a burst of noise around hz
  ring      {amount, hz, ratios, levels?, decay_ms}  struck partials, higher ones dying sooner
  whoosh    {amount, peak_hz, rest_hz, rise_ms, tail_ms}
  held, under swell and tail:
  hum (root_hz Hz, fifths, choir as above)   sub {amount, hz}   rumble {amount, hz}   hiss {amount, hz}
  buzz      {amount, hz, bright, vibrato_hz, vibrato_cents}   a sawtooth: brass, or electric
  growl     {amount, hz, rough_hz}   noise chopped roughly, band-passed at hz: a throat
  grit      {amount, per_s}   crackle          drops {amount, per_s, hz: [low, high], ms}   droplets
  whole sound:
  swell {ms}  tail {ms, hold_ms}  "cut off" {ms}  rising {semitones (minus falls), glide_ms, at_ms}
  pulse {amount, hz, depth}  gusting {amount, hz}  slow wandering level and brightness   dull {hz}  low-pass'''


def add_drone(mix, p, element, start, cut, combine_at, steps, gain_scale, r):
    col = p.get('colour', {}).get(element, {})
    partials = drone_partials(p, col)
    sub_amp, sub_hz = amount(p, 'sub'), options(p, 'sub').get('hz', 45)
    if sub_amp: partials.append((None, sub_amp))
    phases = [r.random() * TAU for _ in partials]

    noise_amt = amount(p, 'rumble') * col.get('rumble', 1)
    noise_k = 1 - math.exp(-TAU * options(p, 'rumble').get('hz', 140) / RATE)
    grit = col.get('grit', amount(p, 'grit'))
    wobble = col.get('wobble', 0)
    pulse = options(p, 'pulse')
    depth = amount(p, 'pulse') * pulse.get('depth', C_PULSE['depth'])
    pulse_hz = pulse.get('hz', C_PULSE['hz'])
    pulse_step = pulse.get('step_hz', C_PULSE['step_hz']) if pulse.get('accelerating') else 0
    swell = p['swell']['ms'] / 1000
    fade = p['cut off']['ms'] / 1000
    rising, esc = options(p, 'rising'), options(p, 'escalate')
    glide = 4 / (rising.get('glide_ms', 150) / 1000 * RATE)

    combine = rising.get('semitones', 0)
    changes = [(combine_at, combine, 1.0, 0)] if combine_at is not None else []
    semis = esc.get('semitones', [])
    for i, at in enumerate(steps):
        changes.append((at, combine + semis[min(i, len(semis) - 1)], esc.get('gain', 1) ** (i + 1), i + 1))

    am_phase = lp = lp2 = pop = semis_now = 0.0
    base = p['root_hz'][element]
    for i in range(int(start * RATE), min(len(mix), int((cut + fade) * RATE))):
        t = i / RATE
        target, gain, level = 0.0, 1.0, 0
        for at, s, g, l in changes:
            if t >= at: target, gain, level = s, g, l
        semis_now += (target - semis_now) * min(1.0, glide)
        hz = base * 2 ** (semis_now / 12)
        if wobble: hz *= 1 + 0.012 * math.sin(TAU * wobble * t)
        s = 0.0
        for j, (ratio, amp) in enumerate(partials):
            phases[j] += TAU * (sub_hz if ratio is None else hz * ratio) / RATE
            s += amp * math.sin(phases[j])
        lp += noise_k * (r.uniform(-1, 1) - lp)
        lp2 += noise_k * (lp - lp2)
        s += noise_amt * lp2 * 9
        if grit:
            if r.random() < 0.0008: pop = 1.0
            pop *= 0.996
            s += grit * pop * r.uniform(-1, 1)
        if depth:
            am_phase += TAU * (pulse_hz + pulse_step * level) / RATE
            s *= 1 - depth * (0.5 + 0.5 * math.sin(am_phase))
        env = min(1.0, (t - start) / swell)
        if t >= cut: env *= 0.5 + 0.5 * math.cos(math.pi * min(1.0, (t - cut) / fade))
        mix[i] += s * env * gain * HUM_GAIN * gain_scale



def render(preset, series):
    """Returns the series' stems, scaled to the shared loudness, and the gain that took."""
    r = random.Random(preset.get('seed', 1))
    n = series['samples']
    drones, thumps, whooshes = [0.0] * n, [0.0] * n, [0.0] * n
    for c in series['casts']:
        steps = c.get('steps', [])
        add_drone(drones, preset, c['first'], c['start'], c['cut'], c['slam'], steps, 1.0, r)
        if 'second' in c:
            add_drone(drones, preset, c['second'], c['second_at'], c['cut'], c['slam'], steps, amount(preset, 'layer', 1.0), r)
        add_thump(thumps, preset, c['slam'])
        add_whoosh(whooshes, preset, c['cut'], c['home'], r)
    mix = [a + b + c for a, b, c in zip(drones, thumps, whooshes)]
    g = min(10 ** (TARGET_DB / 20) / rms(mix), PEAK / max(map(abs, mix)))
    return dict(mix=[x * g for x in mix], drones=[x * g for x in drones], thumps=[x * g for x in thumps],
                whooshes=[x * g for x in whooshes]), g









def cut_ms(drones, at):
    """Ms after `at` until the charge stays 40 dB under its level just before."""
    n = RATE // 200
    i = int(at * RATE)
    ref = max(map(abs, drones[i - 2 * n:i])) or 1e-9
    last = i
    for j in range(i, min(len(drones), i + RATE)):
        if abs(drones[j]) > ref * 0.01: last = j
    return (last - i) * 1000 / RATE


def span_ms(x, a, b):
    """How long the sound in a window is above 40 dB under its peak."""
    seg = x[int(a * RATE):int(b * RATE)]
    floor = max(map(abs, seg)) * 0.01
    hits = [i for i, v in enumerate(seg) if abs(v) > floor]
    return (hits[-1] - hits[0]) * 1000 / RATE


def find_pulse(x, a, b):
    """The strongest loudness beat at 3-25 Hz between times a and b: (rate Hz, depth 0-1)."""
    hop, box = RATE // 100, 3 * RATE // 100
    energy = [v * v for v in x[int(a * RATE):int(b * RATE)]]
    env = [math.sqrt(sum(energy[i:i + box]) / box) for i in range(0, len(energy) - box, hop)]
    m = len(env)
    mean = sum(env) / m
    # Take out the straight-line drift (the slow choir beat) before looking for a rhythm.
    centre = (m - 1) / 2
    slope = sum((i - centre) * (v - mean) for i, v in enumerate(env)) / sum((i - centre) ** 2 for i in range(m))
    flat = [(v - mean - slope * (i - centre)) * (0.5 - 0.5 * math.cos(TAU * i / (m - 1))) for i, v in enumerate(env)]
    best = (0.0, 0.0)
    for k in range(12, 101):
        f = k / 4
        re = sum(v * math.cos(TAU * f * i * hop / RATE) for i, v in enumerate(flat))
        im = sum(v * math.sin(TAU * f * i * hop / RATE) for i, v in enumerate(flat))
        best = max(best, (4 * math.hypot(re, im) / m, f))
    size, f = best
    u = math.pi * f * box / RATE
    size /= math.sin(u) / u  # what the averaging window took off
    return f, 2 * size / (mean + size)


def measure(stems, series, preset):
    """The measures vocabulary.md asks of every variant, as printable lines."""
    mix, drones, thumps, whooshes = stems['mix'], stems['drones'], stems['thumps'], stems['whooshes']
    level = db(rms(mix))
    lines = [f'loudness {level:.2f} dBFS RMS, {level - GAME_CAST_DB:+.1f} dB against the game\'s current cast sounds; peak {max(map(abs, mix)):.3f}',
             f'low energy: {100 * band_share(mix, 200):.1f}% under 200 Hz, {100 * band_share(mix, 80):.1f}% under 80 Hz']
    fade = preset['cut off']['ms'] / 1000
    for c in series['casts']:
        name = c['file']
        lines.append(f"{name}: drone attack {attack_ms(drones, c['start'], c.get('second_at', c['slam'])):.0f} ms; "
                     f"thump attack {attack_ms(thumps, c['slam'], c['slam'] + 0.5):.0f} ms, decay {decay_ms(thumps, c['slam'], c['slam'] + 0.5):.0f} ms; "
                     f"charge cut off {cut_ms(drones, c['cut']):.0f} ms after the event (fade {fade * 1000:.0f} ms); "
                     f"whoosh {span_ms(whooshes, c['cut'], c['home'] + 0.3):.0f} ms")
    if amount(preset, 'pulse'):
        for c in series['casts']:
            times = [c['slam']] + c.get('steps', [])
            if len(times) < 2: continue
            stages = zip(times, times[1:] + [c['cut']])
            found = [find_pulse(drones, a + 0.1, b) for a, b in stages]
            lines.append(f"pulse on {c['file']}: " + ', '.join(f'{f:.1f} Hz depth {d:.2f}' for f, d in found) + ' (one per charge step)')
    return lines


def clips(series):
    """The window of each cast in the joined timeline, in seconds."""
    return [(c['file'], c['start'] - 0.2, max(c['home'] + 0.14, c['slam'] + 0.5) + 0.25) for c in series['casts']]


def check(ap, preset, path):
    if 'sound' in preset:
        unknown, missing = set(preset) - SOUND_WORDS, set()
        if preset['sound'] not in families.SOUNDS:
            ap.error(f'{path}: no game sound {preset["sound"]!r}; one of {sorted(families.SOUNDS)}')
        if 'hum' in preset and not isinstance(preset.get('root_hz'), (int, float)):
            ap.error(f'{path}: a hum needs root_hz in Hz')
    else:
        unknown, missing = set(preset) - WORDS, REQUIRED - set(preset)
    if unknown:
        ap.error(f'{path}: not in the vocabulary here: {sorted(unknown)}')
    if missing:
        ap.error(f'{path}: a preset needs: {sorted(missing)}; copy one from presets/')


def render_cast(preset, name, out):
    series = SERIES[preset.get('series', 'cast')]
    began = time.perf_counter()
    stems, gain = render(preset, series)
    os.makedirs(out, exist_ok=True)
    mix = stems['mix']
    for file, a, b in clips(series):
        seg = mix[int(a * RATE):int(b * RATE)]
        write_wav(os.path.join(out, file + '.wav'), seg)
        print(f'{file}.wav {len(seg) / RATE:.2f} s')
    write_wav(os.path.join(out, 'joined.wav'), mix)
    print(f'joined.wav {len(mix) / RATE:.2f} s')
    rendered = time.perf_counter() - began
    print(preset.get('name', name), f'| gain {gain:.3f}', '' if abs(db(rms(mix)) - TARGET_DB) < 0.01 else '| HELD BY PEAK, NOT AT TARGET LOUDNESS')
    for line in measure(stems, series, preset):
        print(' ', line)
    print(f'  render {rendered:.1f} s, with measures {time.perf_counter() - began:.1f} s')


def render_sounds(presets, out):
    """Every sound preset into <out>/<family>/<sound>.wav, then a joined file per family, in SOUNDS.md's order."""
    joined = {}
    for path, p in sorted(presets, key=lambda pp: families.order(pp[1]['sound'])):
        family = families.SOUNDS[p['sound']][0]
        began = time.perf_counter()
        takes, whole = families.render(p)
        os.makedirs(os.path.join(out, family), exist_ok=True)
        write_wav(os.path.join(out, family, p['sound'] + '.wav'), whole)
        joined.setdefault(family, []).append(whole)
        print(f'{family}/{p["sound"]}.wav  {p.get("name", path)}  ({len(takes)} takes, {time.perf_counter() - began:.1f} s)')
        print('  ' + families.measure(p, takes))
    for family, parts in joined.items():
        whole = []
        for x in parts:
            whole += x + [0.0] * int(families.JOIN_GAP * RATE)
        whole = whole[:-int(families.JOIN_GAP * RATE)]
        write_wav(os.path.join(out, family, f'_joined-{family}.wav'), whole)
        print(f'{family}/_joined-{family}.wav  ' + families.loudness_line(f'{len(parts)} sound' + 's' * (len(parts) > 1), whole))


def main():
    ap = argparse.ArgumentParser(description='Render sound presets to WAV and print their measures.', epilog=EPILOG,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('presets', nargs='+', help='presets/<name>.json: one cast preset, or any number of sound presets')
    ap.add_argument('--out', required=True, help='folder for the WAVs (made if missing)')
    args = ap.parse_args()
    loaded = []
    for path in args.presets:
        with open(path, encoding='utf8') as f:
            loaded.append((path, json.load(f)))
        check(ap, loaded[-1][1], path)
    sounds = [(path, p) for path, p in loaded if 'sound' in p]
    casts = [(path, p) for path, p in loaded if 'sound' not in p]
    if len(casts) > 1 or (casts and sounds):
        ap.error('a cast preset renders alone (its files would overwrite each other)')
    ids = [p['sound'] for _, p in sounds]
    if len(set(ids)) < len(ids):
        ap.error('two presets of the same sound: render variants into different --out folders')
    if casts:
        render_cast(casts[0][1], casts[0][0], args.out)
    else:
        render_sounds(sounds, args.out)


if __name__ == '__main__':
    main()
