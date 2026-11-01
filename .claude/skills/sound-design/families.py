"""Every game sound outside the cast series (docs/SOUNDS.md), its family's standard series, and its measures.

A family's series is the moments every preset of that family renders, so variants compare like for like: the
same takes, pitches and gaps. Each sound is brought to today's loudness (SOUNDS.md, LUFS) so a variant is
never picked for being louder, then soft-limited as the bank does."""
from dsp import RATE, attack_ms, band_share, decay_ms, flatness, lowpass, lufs, peak_db, resample, soft_limit
from layers import render_sound

# id: (length s, LUFS, peak dBFS, what it is). Today's figures from SOUNDS.md (a range's middle for length);
# a sound the game hasn't got yet has a proposed target, and None for peak. `loop` marks the loops.
FAMILIES = {
    'casting': {
        'fizzle': (0.45, -21.8, -8.3, 'a cast cancelled: a falling sputter'),
    },
    'spells': {
        'fire': (1.20, -22.3, -2.0, 'fire catching where a spell lands'),
        'explosion': (0.90, -16.1, 0.0, 'a fire burst: Fireball, Falling Star, Meteor, a barrel'),
        'frost': (1.35, -20.0, 0.0, 'cold landing: Frost, Sleet Storm, Ray of Frost'),
        'splash': (0.60, -18.1, 0.0, 'water landing, a Wave breaking'),
        'grease': (0.45, -19.4, -2.4, 'Grease landing: a wet, low squelch'),
        'zap': (0.70, -19.0, 0.0, 'lightning, Shocking Grasp, a storm cloud\'s strike'),
        'gust': (1.00, -19.1, -8.2, 'air landing: Gust of Wind, Whirlwind'),
        'thunder': (1.40, -17.4, 0.0, 'Thunderwave landing'),
        'earth': (1.20, -18.0, -1.9, 'the ground moved: Raise Earth, Wall of Stone, ...'),
        'arcane': (0.60, -21.6, -2.2, 'every other spell\'s landing: a soft whoosh'),
        'blink': (1.00, -14.0, -1.9, 'Misty Step'),
        'hiss': (1.20, -15.6, -1.9, 'Acid Splash eating in: a sizzle and pops'),
        'shatter': (0.80, -24.1, -1.9, 'a frozen raider struck'),
    },
    'world': {
        'fire-loop': (5.70, -27.0, -9.0, 'a fire burning: roar, hiss and crackle', 'loop'),
        'rain-loop': (3.80, -13.5, -1.9, 'rain: fine hiss with drops', 'loop'),
        'wind-loop': (5.80, -18.7, -1.9, 'wind gusting up and down', 'loop'),
        'falls-roar': (6.91, -14.0, -1.9, 'the waterfall\'s roar', 'loop'),
        'thunder-clap': (7.90, -20.1, -0.4, 'thunder after a sky strike'),
        'break': (0.35, -19.0, 0.0, 'a crate or prop breaking, a window pane'),
    },
    'creatures': {
        'horn': (4.50, -11.4, -1.9, 'a war horn from the mountains: two low brass notes'),
        'roar': (3.20, -14.3, -1.9, 'the dragon\'s roar: a growl sliding down'),
    },
    'wizard': {  # none of these exist yet: today a punch, the pick and a hit play earth (-18.0, 1.2 s)
        'punch': (0.35, -20.0, None, 'a punch landing'),
        'pick': (0.40, -20.0, None, 'the pick biting the ground'),
        'pick-up': (0.25, -26.0, None, 'an item picked up'),
        'hit': (0.45, -18.0, None, 'the wizard taking a hit'),
        'footsteps': (0.25, -30.0, None, 'one footstep on grass'),
    },
    'ui': {  # none yet
        'confirm': (0.18, -28.0, None, 'a soft confirm'),
        'refusal': (0.30, -26.0, None, 'a refusal: you can\'t do that'),
    },
    'minigames': {  # golf's hole plays splash today (-18.1)
        'golf-strike': (0.30, -20.0, None, 'the golf ball struck'),
        'hole': (0.80, -19.0, None, 'the golf ball dropping in the hole'),
    },
}
SOUNDS = {sound: (family,) + row for family, rows in FAMILIES.items() for sound, row in rows.items()}

# A take is (seed offset, pitch, far): far low-passes it at that Hz and drops it 10 dB, as heard from afar.
# A loop plays twice end to end instead (so the seam is heard), once.
SERIES = {
    'landing': dict(says='three takes, as the bank picks one of three clips with a little pitch jitter', families=['casting', 'spells'],
                    takes=[(0, 1.0, None), (1, 0.95, None), (2, 1.05, None)], gap=0.6),
    'world': dict(says='a loop twice end to end; a one-shot near, again, and far (1 kHz, -10 dB)', families=['world'],
                  takes=[(0, 1.0, None), (1, 0.97, None), (2, 1.0, 1000)], gap=0.6),
    'creature': dict(says='near, then far over the valley (900 Hz, -10 dB)', families=['creatures'],
                     takes=[(0, 1.0, None), (1, 1.0, 900)], gap=1.0),
    'wizard': dict(says='four takes at a walking pace, each a little different', families=['wizard'],
                   takes=[(0, 1.0, None), (1, 0.97, None), (2, 1.03, None), (3, 0.99, None)], gap=0.25),
    'feedback': dict(says='twice fast (pressed twice), then once alone: a UI sound must bear repeating', families=['ui'],
                     takes=[(0, 1.0, None), (1, 1.0, None), (2, 1.0, None)], gaps=[0.08, 0.8, 0.8]),
    'golf': dict(says='three takes with pitch jitter', families=['minigames'],
                 takes=[(0, 1.0, None), (1, 0.96, None), (2, 1.04, None)], gap=0.6),
}
SERIES_OF = {family: name for name, s in SERIES.items() for family in s['families']}
JOIN_GAP = 1.2  # between sounds in a family's joined file, as in the audition renders


def describe():
    return '\n'.join(f'  {name}: {s["says"]}\n    families: ' + ', '.join(
        f'{f} ({", ".join(FAMILIES[f])})' for f in s['families']) for name, s in SERIES.items())


def take(p, length, loop, seed, pitch, far):
    x = render_sound(p, length, loop, seed)
    if loop: x = x + x
    x = resample(x, pitch)
    if far: x = [v * 10 ** (-10 / 20) for v in lowpass(lowpass(x, far), far)]
    return x


def render(p):
    """The sound's series: its takes, each at today's loudness (the first take sets the gain), and the file
    that joins them."""
    family, length, target = SOUNDS[p['sound']][:3]
    length = p.get('length', length)
    loop = len(SOUNDS[p['sound']]) > 5
    series = SERIES[SERIES_OF[family]]
    specs = series['takes'][:1] if loop else series['takes']
    raw = [take(p, length, loop, p.get('seed', 1) * 1000 + s, 1.0 if p.get('unpitched') else pitch, far) for s, pitch, far in specs]
    ref = raw[0][:int(length * RATE)]
    gain = 10 ** ((target - lufs(ref)) / 20)
    for _ in range(2):  # the limiter takes a little off a loud sound; give it back
        gain *= 10 ** ((target - lufs(soft_limit([v * gain for v in ref]))) / 20)
    takes = [soft_limit([v * gain for v in x]) for x in raw]
    gaps = series.get('gaps', [series.get('gap', 0.6)] * len(takes))
    joined = []
    for x, gap in zip(takes, gaps):
        joined += x + [0.0] * int(gap * RATE)
    return takes, joined[:len(joined) - int(gaps[len(takes) - 1] * RATE)]


def measure(p, takes):
    """One line per sound: length, loudness and peak against today's, and the shape and colour measures."""
    family, length, target, peak, _ = SOUNDS[p['sound']][:5]
    x = takes[0][:int(p.get('length', length) * RATE)]
    s = len(x) / RATE
    new = peak is None
    attack = attack_ms(x, 0, s)
    decay = decay_ms(x, attack / 1000, s, 10)
    return (f'{p["sound"]}: {s:.2f} s ({"proposed" if new else "today"} {length:.2f}), {lufs(x):.1f} LUFS '
            f'({"proposed" if new else "today"} {target:.1f}), peak {peak_db(x):.1f} dBFS ({"new" if new else f"today {peak:.1f}"}); attack {attack:.0f} ms, down 20 dB in {decay:.0f} ms; under 200 Hz {100 * band_share(x, 200):.0f}%, '
            f'over 4 kHz {100 * (1 - band_share(x, 4000)):.0f}%; flatness {flatness(x):.2f}')


def loudness_line(name, x):
    return f'{name}: {lufs(x):.1f} LUFS, peak {peak_db(x):.1f} dBFS, {len(x) / RATE:.2f} s'


def order(sound):
    family = SOUNDS[sound][0]
    return list(FAMILIES).index(family), list(FAMILIES[family]).index(sound)
