#!/usr/bin/env python3
"""Renders a picked preset into the clips the game plays (Audio/CC0/<preset>/, CastSoundTable, SoundBankBuilder).
A sound preset gives three takes, seeded, for the bank to pick among. A cast preset gives what follows.

synth.py renders a whole take on one timeline; the game needs the same sound in parts it can hold for as
long as a cast lasts, so this renders, from the same words: a seamless loop per element for each stage of a
cast (gathering, then charge levels 1 to 3), the thump and a few whooshes. Each is left at the preset's own
balance and SoundMastering brings it to its level when the bank is built. Python's standard library, plus
ffmpeg for the .ogg: `python render_game.py presets/cast-deep-hum-pulse.json --out <folder>`."""
import argparse, json, math, os, random, subprocess, tempfile

import families
import synth

RATE = synth.RATE
LOOP_SECONDS = 3      # every partial is rounded to whole cycles in this, so the loop has no seam
NOISE_FADE = 0.25     # s of noise crossfaded over the seam
WHOOSHES = 3          # variations, seeded
WHOOSH_RISE = 0.167   # s, the tap's let-go to hands landing in the cast series
PLAIN_HZ = 61.7       # a cast with no element (the d-pad's): between earth and water, no colour
QUIET = -60.0


def whole_cycles(hz):
    return max(1, round(hz * LOOP_SECONDS)) / LOOP_SECONDS


def drone_loop(p, element, stage, r):
    """One seamless loop: stage 0 is gathering, 1 the elements combined, 2 and 3 the charge steps above that."""
    rising, esc, pulse = synth.options(p, 'rising'), synth.options(p, 'escalate'), synth.options(p, 'pulse')
    semis = 0 if stage == 0 else rising.get('semitones', 0)
    if stage > 1: semis += esc['semitones'][stage - 2]
    root = p['root_hz'].get(element, PLAIN_HZ) * 2 ** (semis / 12)
    colour = p.get('colour', {}).get(element, {})
    partials = [(whole_cycles(root * ratio), amp, r.random() * synth.TAU) for ratio, amp in synth.drone_partials(p, colour)]
    level = max(0, stage - 1)
    gain = esc.get('gain', 1) ** level
    pulse_depth = synth.amount(p, 'pulse') * pulse.get('depth', synth.C_PULSE['depth'])
    pulse_hz = whole_cycles(pulse.get('hz', synth.C_PULSE['hz']) + (pulse.get('step_hz', synth.C_PULSE['step_hz']) if pulse.get('accelerating') else 0) * level)
    wobble = colour.get('wobble', 0)
    n = LOOP_SECONDS * RATE
    out = [0.0] * n
    for hz, amp, phase in partials:
        # The wobble is a frequency swing of 1.2%, written as its phase so that it closes over the loop.
        swing = 0.012 * hz / wobble if wobble else 0
        for i in range(n):
            t = i / RATE
            out[i] += amp * math.sin(synth.TAU * hz * t + phase + (swing * math.sin(synth.TAU * wobble * t) if wobble else 0))
    amount = synth.amount(p, 'rumble') * colour.get('rumble', 1)
    k = 1 - math.exp(-synth.TAU * synth.options(p, 'rumble').get('hz', 140) / RATE)
    fade = int(NOISE_FADE * RATE)
    noise, lp, lp2 = [], 0.0, 0.0
    for _ in range(n + fade):
        lp += k * (r.uniform(-1, 1) - lp)
        lp2 += k * (lp - lp2)
        noise.append(lp2)
    for i in range(fade):  # the run past the end fades in under the start, equal power
        x = i / fade * math.pi / 2
        noise[i] = noise[i] * math.sin(x) + noise[n + i] * math.cos(x)
    for i in range(n):
        s = out[i] + amount * noise[i] * 9
        t = i / RATE
        out[i] = s * (1 - pulse_depth * (0.5 + 0.5 * math.sin(synth.TAU * pulse_hz * t))) * gain * synth.HUM_GAIN
    return out


def one_shot(seconds, add):
    mix = [0.0] * int(seconds * RATE)
    add(mix)
    return mix


def write_ogg(path, samples, quality):
    with tempfile.TemporaryDirectory() as folder:
        wav = os.path.join(folder, 'in.wav')
        synth.write_wav(wav, samples)
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', wav, '-c:a', 'libvorbis', '-q:a', str(quality), path], check=True)


def render_sound_preset(p, out, quality):
    """The sound's takes at their pitch of 1: the bank gives each its own pitch as it plays."""
    takes, _ = families.render({**p, 'unpitched': True})
    print(families.measure(p, takes))
    for i, samples in enumerate(takes):
        write_ogg(os.path.join(out, f'{p["name"]}-{i + 1}.ogg'), samples, quality)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('preset')
    ap.add_argument('--out', required=True)
    ap.add_argument('--quality', type=float, default=3, help='Vorbis quality (0-10)')
    args = ap.parse_args()
    with open(args.preset, encoding='utf8') as f:
        p = json.load(f)
    r = random.Random(p.get('seed', 1))
    os.makedirs(args.out, exist_ok=True)
    if 'sound' in p:
        render_sound_preset(p, args.out, args.quality)
        return
    clips = {}
    for element in list(p['root_hz']) + ['plain']:
        clips[f'gather-{element}'] = drone_loop(p, element, 0, r)
        for level in range(1, 4):
            clips[f'charge-{element}-{level}'] = drone_loop(p, element, level, r)
    clips['slam'] = one_shot(0.5, lambda mix: synth.add_thump(mix, p, 0.0))
    tail = p['whoosh']['tail_ms'] / 1000
    for i in range(WHOOSHES):
        clips[f'whoosh-{i + 1}'] = one_shot(WHOOSH_RISE + tail + 0.01, lambda mix: synth.add_whoosh(mix, p, 0.0, WHOOSH_RISE, r))
    peak = max(max(map(abs, c)) for c in clips.values())
    for name, samples in clips.items():
        scaled = [s * 0.5 / peak for s in samples]
        write_ogg(os.path.join(args.out, name + '.ogg'), scaled, args.quality)
        print(f'{name}: rms {synth.db(synth.rms(scaled)):.1f} dBFS, peak {max(map(abs, scaled)):.3f}')


if __name__ == '__main__':
    main()
