"""The layers a sound is built from, one per vocabulary word, and the renderer that sums them into one sound.

Two kinds of layer. Struck ones (thump, crack, ring, whoosh) have their own shape, starting at their `at_ms`.
Held ones (hum, sub, rumble, hiss, buzz, growl, grit, drops) follow the sound's envelope: `swell` in, `tail`
out, and are moved by `pulse` and `gusting`. `rising` bends every pitched layer; `dull` low-passes the lot;
`cut off` ends everything at the sound's length. A loop has no envelope and closes on itself seamlessly."""
import math, random

from dsp import RATE, TAU, amount, instances, lowpass, one_pole, options

LOOP_FADE = 0.4  # s of the run past a loop's end, crossfaded under its start


def thump_into(mix, th, at, length=0.5):
    """A pitched-down sine: the cast series' slam."""
    fade_out, phase = 0.03, 0.0  # the fade stops the end clicking
    decay = th['decay_ms'] / 1000
    for i in range(int(at * RATE), min(len(mix), int((at + length) * RATE))):
        t = i / RATE - at
        phase += TAU * (th['to_hz'] + (th['from_hz'] - th['to_hz']) * math.exp(-t / 0.035)) / RATE
        mix[i] += math.sin(phase) * math.exp(-t / decay) * th.get('amount', 0) * min(1.0, (length - t) / fade_out)


def whoosh_into(mix, w, start, home, r):
    """Noise whose low-pass sweeps up from the start to `home`, and dies in the tail."""
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
        mix[i] += lp2 * 3.2 * w.get('amount', 0) * env


def add_thump(mix, p, at):
    thump_into(mix, p['thump'], at)


def add_whoosh(mix, p, start, home, r):
    whoosh_into(mix, p['whoosh'], start, home, r)


class Sound:
    """What every layer of one take shares: its random numbers, its length and the per-sample curves."""

    def __init__(self, p, n, length, r):
        self.p, self.n, self.length, self.r = p, n, length, r
        rise = options(p, 'rising')
        semis, glide, at = rise.get('semitones', 0), max(1, rise.get('glide_ms', 150)) / 1000, rise.get('at_ms', 0) / 1000
        self.bend = [2 ** (semis * min(1.0, max(0.0, (i / RATE - at) / glide)) / 12) for i in range(n)] if semis else None
        # A gust is three slow sines at unrelated rates, each a whole number of cycles in the sound so a loop closes.
        g = options(p, 'gusting')
        self.gust = None
        if amount(p, 'gusting'):
            hz = g.get('hz', 0.3)
            parts = [(max(1, round(hz * f * length)) / length, r.random() * TAU) for f in (1, 1.618, 2.414)]
            self.gust = [0.5 + sum(math.sin(TAU * f * i / RATE + ph) for f, ph in parts) / 6 for i in range(n)]

    def hz(self, base, i):
        return base * self.bend[i] if self.bend else base

    def span(self, s):
        return int(s.get('at_ms', 0) / 1000 * RATE), self.n

    def noise_cut(self, hz, i):
        """A gust opens a noise layer's filter as it rises, so wind gets brighter as well as louder."""
        return hz * (0.6 + 0.8 * self.gust[i]) if self.gust else hz


# Struck layers --------------------------------------------------------------------------------------------

def struck_thump(mix, s, c):
    thump_into(mix, dict(dict(from_hz=95, to_hz=50, decay_ms=200), **s), s.get('at_ms', 0) / 1000,
               max(0.5, 6 * s.get('decay_ms', 200) / 1000))


def struck_whoosh(mix, s, c):
    w = dict(dict(peak_hz=2400, rest_hz=420, tail_ms=140), **s)
    at = s.get('at_ms', 0) / 1000
    whoosh_into(mix, w, at, at + s.get('rise_ms', 167) / 1000, c.r)


def struck_crack(mix, s, c):
    """A burst of noise in a band around hz: lowpass at 1.5 hz minus lowpass at hz / 1.5."""
    a, hz, decay = s.get('amount', 0), s.get('hz', 3000), s.get('ms', 30) / 1000
    k_hi, k_lo = one_pole(hz * 1.5), one_pole(hz / 1.5)
    hi = hi2 = lo = lo2 = 0.0
    start, end = c.span(s)
    for i in range(start, min(end, start + int(decay * 7 * RATE))):
        t = (i - start) / RATE
        x = c.r.uniform(-1, 1)
        hi += k_hi * (x - hi); hi2 += k_hi * (hi - hi2)
        lo += k_lo * (x - lo); lo2 += k_lo * (lo - lo2)
        mix[i] += (hi2 - lo2) * 2.5 * a * min(1.0, t / 0.001) * math.exp(-t / decay)


def struck_ring(mix, s, c):
    """Struck partials at hz times each ratio, the higher ones dying sooner: a bell, a glass, a knock."""
    a, base, decay = s.get('amount', 0), s.get('hz', 440), s.get('decay_ms', 300) / 1000
    ratios = s.get('ratios', [1])
    levels = s.get('levels', [1 / (j + 1) for j in range(len(ratios))])
    start, end = c.span(s)
    end = min(end, start + int(decay * 7 * RATE))
    for ratio, level in zip(ratios, levels):
        d, phase = decay / math.sqrt(ratio), c.r.random() * TAU
        for i in range(start, end):
            t = (i - start) / RATE
            phase += TAU * c.hz(base * ratio, i) / RATE
            mix[i] += a * level * math.sin(phase) * min(1.0, t / 0.0015) * math.exp(-t / d)


# Held layers ----------------------------------------------------------------------------------------------

def drone_partials(p, col):
    """The hum's notes as (ratio to the root, amount): the root, its choir and the fifths."""
    fifths = amount(p, 'fifths')
    choir = options(p, 'choir')
    voices = choir.get('voices', 1)
    detune = [2 ** (s * choir.get('cents', 0) * (v // 2 + 1) / max(1, (voices + 1) // 2) / 1200)
              for v, s in zip(range(voices), [1, -1] * voices)]
    choir_amp = 0.32 * amount(p, 'choir') * col.get('choir', 1) / math.sqrt(voices)
    partials = [(1, 0.42)] + [(d, choir_amp) for d in detune] + [
        (1.5, 0.22 * fifths * col.get('fifth', 1)), (2, 0.14 * fifths), (3, 0.05 * fifths)]
    hum = amount(p, 'hum')
    return [(ratio, amp * hum) for ratio, amp in partials]


def held_hum(mix, s, c):
    """The cast series' hum and its words (fifths, choir), at root_hz."""
    partials = drone_partials(c.p, {})
    phases = [c.r.random() * TAU for _ in partials]
    root = c.p['root_hz']
    start, end = c.span(s)
    for i in range(start, end):
        hz = c.hz(root, i)
        v = 0.0
        for j, (ratio, amp) in enumerate(partials):
            phases[j] += TAU * hz * ratio / RATE
            v += amp * math.sin(phases[j])
        mix[i] += v


def held_sub(mix, s, c):
    a, hz, phase = s.get('amount', 0), s.get('hz', 45), 0.0
    start, end = c.span(s)
    for i in range(start, end):
        phase += TAU * hz / RATE
        mix[i] += a * math.sin(phase)


def held_rumble(mix, s, c):
    """Noise through two low-passes at hz, as in the cast series."""
    a, hz = s.get('amount', 0), s.get('hz', 140)
    lp = lp2 = 0.0
    k = one_pole(hz)
    start, end = c.span(s)
    for i in range(start, end):
        if c.gust and i % 32 == 0: k = one_pole(c.noise_cut(hz, i))
        lp += k * (c.r.uniform(-1, 1) - lp)
        lp2 += k * (lp - lp2)
        mix[i] += a * lp2 * 9 * math.sqrt(140 / hz)


def held_hiss(mix, s, c):
    """Noise with everything under hz taken out (the noise minus two low-passes at hz)."""
    a, hz = s.get('amount', 0), s.get('hz', 3000)
    lp = lp2 = 0.0
    k = one_pole(hz)
    start, end = c.span(s)
    for i in range(start, end):
        if c.gust and i % 32 == 0: k = one_pole(c.noise_cut(hz, i))
        x = c.r.uniform(-1, 1)
        lp += k * (x - lp)
        lp2 += k * (lp - lp2)
        mix[i] += a * (x - lp2)


def held_buzz(mix, s, c):
    """A sawtooth at hz, low-passed by `bright` (0 dull, 1 raw): brassy at low pitch, electric with grit."""
    a, base, bright = s.get('amount', 0), s.get('hz', 110), s.get('bright', 0.5)
    vib_hz, vib = s.get('vibrato_hz', 0), s.get('vibrato_cents', 0) / 1200
    k = one_pole(base * (2 + 30 * bright))
    phase, lp, lp2 = c.r.random(), 0.0, 0.0
    start, end = c.span(s)
    for i in range(start, end):
        hz = c.hz(base, i) * (2 ** (vib * math.sin(TAU * vib_hz * i / RATE)) if vib else 1)
        phase = (phase + hz / RATE) % 1.0
        lp += k * (2 * phase - 1 - lp)
        lp2 += k * (lp - lp2)
        mix[i] += a * lp2


def held_growl(mix, s, c):
    """Noise chopped at a rough, wandering rate (rough_hz) and band-passed at hz: a throat."""
    a, hz, rough = s.get('amount', 0), s.get('hz', 400), s.get('rough_hz', 40)
    phase, drift = 0.0, 0.0
    hi = hi2 = lo = lo2 = 0.0
    start, end = c.span(s)
    k_hi = k_lo = 0.0
    for i in range(start, end):
        if i % 32 == 0:
            f = c.hz(hz, i)
            k_hi, k_lo = one_pole(f * 1.4), one_pole(f / 1.4)
        drift += 0.002 * (c.r.uniform(-1, 1) - 0.01 * drift)
        phase += TAU * rough * (1 + 0.3 * math.tanh(drift)) / RATE
        x = c.r.uniform(-1, 1) * (0.5 + 0.5 * math.sin(phase)) ** 2
        hi += k_hi * (x - hi); hi2 += k_hi * (hi - hi2)
        lo += k_lo * (x - lo); lo2 += k_lo * (lo - lo2)
        mix[i] += a * (hi2 - lo2) * 6


def events(s, c, per_s):
    """Random moments at per_s a second over the layer's span, as sample indices."""
    start, end = c.span(s)
    t = start / RATE
    while True:
        t += c.r.expovariate(per_s)
        if t * RATE >= end: return
        yield int(t * RATE)


def held_grit(mix, s, c):
    """Crackle: short clicks of noise, 1-4 ms each, at per_s a second."""
    a = s.get('amount', 0)
    for at in events(s, c, s.get('per_s', 40)):
        size, last = c.r.uniform(0.3, 1) * a, 0.0
        n = int(c.r.uniform(0.001, 0.004) * RATE)
        for j in range(min(n, len(mix) - at)):
            x = c.r.uniform(-1, 1)
            mix[at + j] += size * (x - last) * (1 - j / n)
            last = x


def held_drops(mix, s, c):
    """Droplets: tiny sine chirps rising half again in pitch, each at a random pitch between hz[0] and hz[1]."""
    a, (lo, hi), dur = s.get('amount', 0), s.get('hz', [800, 2400]), s.get('ms', 20) / 1000
    for at in events(s, c, s.get('per_s', 30)):
        f0, size, phase = lo * (hi / lo) ** c.r.random(), c.r.uniform(0.4, 1) * a, 0.0
        for j in range(min(int(dur * 5 * RATE), len(mix) - at)):
            t = j / RATE
            phase += TAU * f0 * (1 + 0.5 * min(1.0, t / dur)) / RATE
            mix[at + j] += size * math.sin(phase) * min(1.0, t / 0.0005) * math.exp(-t / dur)


STRUCK = dict(thump=struck_thump, whoosh=struck_whoosh, crack=struck_crack, ring=struck_ring)
HELD = dict(hum=held_hum, sub=held_sub, rumble=held_rumble, hiss=held_hiss, buzz=held_buzz, growl=held_growl,
            grit=held_grit, drops=held_drops)


def envelope_at(p, t):
    swell = options(p, 'swell').get('ms', 0) / 1000
    tail = options(p, 'tail')
    env = min(1.0, t / swell) if swell else 1.0
    hold = tail.get('hold_ms', 0) / 1000
    if tail.get('ms') and t > hold: env *= math.exp(-(t - hold) / (tail['ms'] / 1000))
    return env


def render_sound(p, length, loop, seed):
    """One take of a sound, `length` seconds; a loop closes on itself."""
    r = random.Random(seed)
    n = int((length + (LOOP_FADE if loop else 0)) * RATE)
    c = Sound(p, n, length, r)
    held, struck = [0.0] * n, [0.0] * n
    for word, layer in HELD.items():
        for s in instances(p, word): layer(held, s, c)
    for word, layer in STRUCK.items():
        for s in instances(p, word): layer(struck, s, c)
    pulse = options(p, 'pulse')
    depth = amount(p, 'pulse') * pulse.get('depth', 0.55)
    pulse_hz = pulse.get('hz', 7.0)
    if loop and depth: pulse_hz = max(1, round(pulse_hz * length)) / length
    gust = amount(p, 'gusting')
    mix = []
    for i in range(n):
        t = i / RATE
        g = 1.0 if loop else envelope_at(p, t)
        if depth: g *= 1 - depth * (0.5 + 0.5 * math.sin(TAU * pulse_hz * t))
        if gust: g *= 1 - gust + gust * c.gust[i]
        mix.append(held[i] * g + struck[i])
    if 'dull' in p:
        mix = lowpass(lowpass(mix, options(p, 'dull').get('hz', 3000)), options(p, 'dull').get('hz', 3000))
    end = int(length * RATE)
    if loop:  # the run past the end fades in under the start, equal power
        fade = n - end
        for i in range(fade):
            x = i / fade * math.pi / 2
            mix[i] = mix[i] * math.sin(x) + mix[end + i] * math.cos(x)
        return mix[:end]
    fade = max(1, int(options(p, 'cut off').get('ms', 20) / 1000 * RATE))
    for i in range(max(0, end - fade), end):
        mix[i] *= 0.5 + 0.5 * math.cos(math.pi * (i - end + fade) / fade)
    return mix[:end]
