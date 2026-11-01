"""What every series shares: the sample rate, reading a preset's words, writing WAVs and measuring.

Python 3 standard library only, like the rest of the skill."""
import cmath, math, wave
from array import array

RATE = 48000
TAU = 2 * math.pi


def amount(preset, word, default=0.0):
    v = preset.get(word, default)
    return v.get('amount', default) if isinstance(v, dict) else v


def options(preset, word):
    v = preset.get(word)
    return v if isinstance(v, dict) else {}


def instances(preset, word):
    """A word's value as a list of dicts: a number is {"amount": n}, a list is several of that layer."""
    v = preset.get(word)
    if v is None: return []
    return [x if isinstance(x, dict) else {'amount': x} for x in (v if isinstance(v, list) else [v])]


def write_wav(path, samples):
    out = array('h', (round(max(-1.0, min(1.0, x)) * 32767) for x in samples))
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(out.tobytes())


def rms(x):
    return math.sqrt(sum(v * v for v in x) / len(x))


def db(x):
    return 20 * math.log10(max(x, 1e-12))


def peak_db(x):
    return db(max(map(abs, x)))


def one_pole(hz):
    """The step of a one-pole low-pass at hz: lp += k * (x - lp)."""
    return 1 - math.exp(-TAU * min(hz, RATE / 2.5) / RATE)


def biquad(x, b, a):
    out, x1, x2, y1, y2 = [], 0.0, 0.0, 0.0, 0.0
    b0, b1, b2 = b
    _, a1, a2 = a
    for v in x:
        y = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        out.append(y)
        x1, x2, y1, y2 = v, x1, y, y1
    return out


def lowpass(x, hz, q=0.707):
    w = TAU * hz / RATE
    alpha = math.sin(w) / (2 * q)
    a0 = 1 + alpha
    b0 = (1 - math.cos(w)) / 2 / a0
    return biquad(x, (b0, 2 * b0, b0), (1, -2 * math.cos(w) / a0, (1 - alpha) / a0))


def lowpass3(x, hz):
    """Three cascaded 2-pole Butterworth low-passes: the band filter the first measures used (ffmpeg's lowpass)."""
    for _ in range(3):
        x = lowpass(x, hz)
    return x


def band_share(x, hz):
    """Energy share under hz, as a fraction of the whole."""
    return (rms(lowpass3(x, hz)) / rms(x)) ** 2


def lufs(x):
    """Integrated loudness, BS.1770-4 (K-weighted, 400 ms blocks, gated), as the audition script measured
    SOUNDS.md. A sound shorter than a block is one block."""
    k = biquad(x, (1.53512485958697, -2.69169618940638, 1.19839281085285), (1, -1.69065929318241, 0.73248077421585))
    k = biquad(k, (1.0, -2.0, 1.0), (1, -1.99004745483398, 0.99007225036621))
    block, hop = min(len(k), int(0.4 * RATE)), int(0.1 * RATE)
    sq = [v * v for v in k]
    z = [sum(sq[i:i + block]) / block for i in range(0, len(sq) - block + 1, hop)]
    z = [v for v in z if v > 0 and -0.691 + 10 * math.log10(v) > -70]
    if not z: return -70.0
    gate = -0.691 + 10 * math.log10(sum(z) / len(z)) - 10
    z = [v for v in z if -0.691 + 10 * math.log10(v) > gate]
    return -0.691 + 10 * math.log10(sum(z) / len(z))


def fft(x):
    n = len(x)
    if n == 1: return list(x)
    even, odd = fft(x[0::2]), fft(x[1::2])
    tw = [cmath.exp(-2j * math.pi * k / n) * odd[k] for k in range(n // 2)]
    return [even[k] + tw[k] for k in range(n // 2)] + [even[k] - tw[k] for k in range(n // 2)]


def flatness(x, size=4096):
    """Spectral flatness over the loudest 85 ms, 50 Hz-16 kHz: 0 is a pure tone, 1 is noise (SOUNDS.md, "The chime")."""
    if len(x) < size: x = list(x) + [0.0] * (size - len(x))
    hop = size // 4
    start = max(range(0, len(x) - size + 1, hop), key=lambda i: sum(v * v for v in x[i:i + size:8]))
    seg = [v * (0.5 - 0.5 * math.cos(TAU * i / size)) for i, v in enumerate(x[start:start + size])]
    spec = fft(seg)
    power = [abs(c) ** 2 + 1e-20 for c in spec[int(50 * size / RATE):int(16000 * size / RATE)]]
    return math.exp(sum(map(math.log, power)) / len(power)) / (sum(power) / len(power))


def soft_limit(x, knee=0.8):
    """The bank's soft limiter, roughly: untouched under the knee, peaks bent to just under full scale."""
    room = 1 - knee
    return [v if abs(v) <= knee else math.copysign(knee + room * math.tanh((abs(v) - knee) / room), v) for v in x]


def resample(x, pitch):
    """Played at a pitch, as Unity does: higher and shorter above 1."""
    if pitch == 1: return list(x)
    n = int((len(x) - 1) / pitch)
    out = []
    for i in range(n):
        p = i * pitch
        j = int(p)
        out.append(x[j] + (x[j + 1] - x[j]) * (p - j))
    return out


def envelope(x, a, b, hold=0.025):
    """A peak-held envelope from sample a to b, so a low tone's cycles don't read as motion."""
    n = max(1, int(hold * RATE))
    return [max(map(abs, x[i:i + n])) for i in range(a, b, n // 5 or 1)], (n // 5 or 1) / RATE


def attack_ms(x, at, until):
    env, dt = envelope(x, int(at * RATE), int(until * RATE))
    top = max(env)
    return 1000 * dt * next(i for i, v in enumerate(env) if v >= 0.9 * top)


def decay_ms(x, at, until, fall=math.e):
    """From the peak until it is `fall` times under it (1/e by default)."""
    env, dt = envelope(x, int(at * RATE), int(until * RATE))
    top = max(env)
    peak = env.index(top)
    return 1000 * dt * (next((i for i in range(peak, len(env)) if env[i] < top / fall), len(env)) - peak)
