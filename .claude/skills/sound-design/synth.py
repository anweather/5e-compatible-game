#!/usr/bin/env python3
"""Renders a sound preset to WAV and prints the vocabulary's measures. Python 3 standard library only.

Each render is seeded, so it comes out the same every run. See SKILL.md for the loop, vocabulary.md for
the words. Loudness is set here, not in presets, so that every variant is compared at the same level."""
import argparse, json, math, os, random, time, wave
from array import array

RATE = 48000
TAU = 2 * math.pi
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
EPILOG = 'series:\n' + '\n'.join(f'  {k}: {v["says"]}\n    files: ' + ', '.join(c['file'] for c in v['casts']) + ', joined'
                                 for k, v in SERIES.items()) + '''

preset words (vocabulary.md), amounts 0-1 unless a unit is given; a number or {"amount": n, ...}:
  root_hz   {element: Hz}          hum 1 is option B's level; fifths scales the 1.5x, 2x and 3x partials
  choir     {amount, cents, voices}    detuned copies of the root
  rumble    {amount, hz}           low-passed noise       sub {amount, hz}   sine under the hum
  grit      crackle of noise pops
  pulse     {amount, accelerating, depth?, hz?, step_hz?}   amount is the share of option C's depth
  rising    {semitones, glide_ms}  pitch step when the elements combine
  escalate  {semitones: [per step], gain}    on top of rising, each charge step louder by gain
  layer     level of the second element over the first
  swell {ms}, "cut off" {ms}, whoosh {amount, peak_hz, rest_hz, tail_ms}, thump {amount, from_hz, to_hz, decay_ms}
  colour    {element: {rumble, choir, fifth (multipliers), wobble (Hz), grit (amount)}}'''


def amount(preset, word, default=0.0):
    v = preset.get(word, default)
    return v.get('amount', default) if isinstance(v, dict) else v


def options(preset, word):
    v = preset.get(word)
    return v if isinstance(v, dict) else {}


def add_drone(mix, p, element, start, cut, combine_at, steps, gain_scale, r):
    col = p.get('colour', {}).get(element, {})
    fifths = amount(p, 'fifths')
    choir = options(p, 'choir')
    voices = choir.get('voices', 1)
    detune = [2 ** (s * choir.get('cents', 0) * (v // 2 + 1) / max(1, (voices + 1) // 2) / 1200)
              for v, s in zip(range(voices), [1, -1] * voices)]
    choir_amp = 0.32 * amount(p, 'choir') * col.get('choir', 1) / math.sqrt(voices)
    partials = [(1, 0.42)] + [(d, choir_amp) for d in detune] + [
        (1.5, 0.22 * fifths * col.get('fifth', 1)), (2, 0.14 * fifths), (3, 0.05 * fifths)]
    hum = amount(p, 'hum')
    partials = [(ratio, amp * hum) for ratio, amp in partials]
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


def add_thump(mix, p, at):
    th = p['thump']
    length, fade_out, phase = 0.5, 0.03, 0.0  # the fade stops the end clicking
    for i in range(int(at * RATE), min(len(mix), int((at + length) * RATE))):
        t = i / RATE - at
        phase += TAU * (th['to_hz'] + (th['from_hz'] - th['to_hz']) * math.exp(-t / 0.035)) / RATE
        mix[i] += math.sin(phase) * math.exp(-t / (th['decay_ms'] / 1000)) * amount(p, 'thump') * min(1.0, (length - t) / fade_out)


def add_whoosh(mix, p, start, home, r):
    """Swells from the cut to the hands landing (home), and dies in the tail."""
    w = p['whoosh']
    tail = w['tail_ms'] / 1000
    lp = lp2 = 0.0
    rise = max(0.02, home - start)
    for i in range(int(start * RATE), min(len(mix), int((home + tail) * RATE))):
        t = i / RATE
        x = (t - start) / rise
        env = x * x if t < home else (1 - (t - home) / tail) ** 2
        k = 1 - math.exp(-TAU * (w['peak_hz'] * env + w['rest_hz'] * (1 - env)) / RATE)
        lp += k * (r.uniform(-1, 1) - lp)
        lp2 += k * (lp - lp2)
        mix[i] += lp2 * 3.2 * amount(p, 'whoosh') * env


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


def write_wav(path, samples):
    out = array('h', (round(x * 32767) for x in samples))
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(out.tobytes())


def rms(x):
    return math.sqrt(sum(v * v for v in x) / len(x))


def db(x):
    return 20 * math.log10(max(x, 1e-12))


def lowpass3(x, hz):
    """Three cascaded 2-pole Butterworth low-passes: the band filter the first measures used (ffmpeg's lowpass)."""
    w = TAU * hz / RATE
    alpha = math.sin(w) / (2 * 0.707)
    a0 = 1 + alpha
    b0 = (1 - math.cos(w)) / 2 / a0
    b1, a1, a2 = 2 * b0, -2 * math.cos(w) / a0, (1 - alpha) / a0
    for _ in range(3):
        out, x1, x2, y1, y2 = [], 0.0, 0.0, 0.0, 0.0
        for v in x:
            y = b0 * v + b1 * x1 + b0 * x2 - a1 * y1 - a2 * y2
            out.append(y)
            x1, x2, y1, y2 = v, x1, y, y1
        x = out
    return x


def band_share(x, hz):
    """Energy share under hz, as a fraction of the whole."""
    return (rms(lowpass3(x, hz)) / rms(x)) ** 2


def envelope(x, a, b, hold=0.025):
    """A peak-held envelope from sample a to b, so a low tone's cycles don't read as motion."""
    n = max(1, int(hold * RATE))
    return [max(map(abs, x[i:i + n])) for i in range(a, b, n // 5 or 1)], (n // 5 or 1) / RATE


def attack_ms(x, at, until):
    env, dt = envelope(x, int(at * RATE), int(until * RATE))
    top = max(env)
    return 1000 * dt * next(i for i, v in enumerate(env) if v >= 0.9 * top)


def decay_ms(x, at, until):
    """From the peak to 1/e of it."""
    env, dt = envelope(x, int(at * RATE), int(until * RATE))
    top = max(env)
    peak = env.index(top)
    return 1000 * dt * (next((i for i in range(peak, len(env)) if env[i] < top / math.e), len(env)) - peak)


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


def main():
    ap = argparse.ArgumentParser(description='Render a sound preset to WAV and print its measures.', epilog=EPILOG,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('preset', help='presets/<name>.json')
    ap.add_argument('--out', required=True, help='folder for the WAVs (made if missing)')
    args = ap.parse_args()
    with open(args.preset, encoding='utf8') as f:
        preset = json.load(f)
    unknown = set(preset) - WORDS
    if unknown:
        ap.error(f'not in the vocabulary here: {sorted(unknown)}')
    missing = REQUIRED - set(preset)
    if missing:
        ap.error(f'a preset needs: {sorted(missing)}; copy one from presets/')
    series = SERIES[preset.get('series', 'cast')]
    began = time.perf_counter()
    stems, gain = render(preset, series)
    os.makedirs(args.out, exist_ok=True)
    mix = stems['mix']
    for name, a, b in clips(series):
        seg = mix[int(a * RATE):int(b * RATE)]
        write_wav(os.path.join(args.out, name + '.wav'), seg)
        print(f'{name}.wav {len(seg) / RATE:.2f} s')
    write_wav(os.path.join(args.out, 'joined.wav'), mix)
    print(f'joined.wav {len(mix) / RATE:.2f} s')
    rendered = time.perf_counter() - began
    print(preset.get('name', args.preset), f'| gain {gain:.3f}', '' if abs(db(rms(mix)) - TARGET_DB) < 0.01 else '| HELD BY PEAK, NOT AT TARGET LOUDNESS')
    for line in measure(stems, series, preset):
        print(' ', line)
    print(f'  render {rendered:.1f} s, with measures {time.perf_counter() - began:.1f} s')


if __name__ == '__main__':
    main()
