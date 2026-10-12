#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1980)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def noise(seconds, release=1.0, smooth=0.0):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


os.makedirs(OUT, exist_ok=True)

# Player shot: a quick falling zap.
write("Fire.wav", sweep(0.11, 1400, 300, square, release=1.5), 0.35)

# A ring segment takes a hit but survives.
write("Hit.wav", sweep(0.05, 900, 700, tri, release=2), 0.45)

# A ring segment is destroyed.
write("Break.wav", mix(noise(0.16, release=2, smooth=0.3), sweep(0.16, 500, 120, square, release=2)), 0.5)

# The cannon's fireball: a low, wobbling growl that falls away.
total = n(0.7)
growl, phase = [], 0.0
for i in range(total):
    f = 160 - 90 * i / total + 25 * math.sin(2 * math.pi * 14 * i / RATE)
    phase += f / RATE
    growl.append(square(phase) * env(i, total, attack=0.02, release=1.2))
write("Fireball.wav", growl, 0.55)

# The ship explodes.
write("Explosion.wav", noise(0.9, release=2.2, smooth=0.6), 0.75)

# The castle's cannon is destroyed: a long, deep rumble with a falling tone under it.
write("CastleBoom.wav", mix(noise(2.4, release=1.6, smooth=0.85), sweep(2.4, 110, 30, tri, release=1.5)), 0.85)

# The rings rebuild (a ring was cleared, or a new castle appears): a rising sweep.
write("Regenerate.wav", sweep(0.7, 180, 900, tri, release=0.6), 0.4)

# A mine leaves the core.
write("Mine.wav", sweep(0.18, 300, 1200, tri, release=1.0), 0.3)

# Extra ship: a short arpeggio.
notes = []
for f in (523, 659, 784, 1047):
    notes += sweep(0.09, f, f, square, release=0.3)
write("ExtraLife.wav", notes, 0.35)

# The castle's background drone, one file per stage: a low hum that throbs, and climbs in pitch and
# throbs faster at later castles. Each file holds a whole number of throbs and every throb falls to
# silence, so the small gap between loops (the player restarts the file) lands where nothing is heard.
for stage, (freq, rate) in enumerate([(55, 2.0), (62, 2.4), (70, 2.8), (79, 3.3)], start=1):
    pulses = round(3.2 * rate)
    total = n(pulses / rate)
    hum, p1, p2 = [], 0.0, 0.0
    for i in range(total):
        p1 += freq / RATE
        p2 += freq * 1.007 / RATE  # a slightly detuned twin, for a slow beating
        tone = (0.55 * tri(p1) + 0.45 * tri(p2)
                + 0.5 * math.sin(2 * math.pi * 2 * p1) + 0.3 * math.sin(2 * math.pi * 3 * p2))
        beat = (0.5 - 0.5 * math.cos(2 * math.pi * rate * i / RATE)) ** 1.5
        hum.append(tone * beat)
    write(f"Drone{stage}.wav", hum, 0.32)

# Thrust: soft rumbling noise, flat so it loops cleanly.
total = n(0.5)
last, rumble = 0.0, []
for i in range(total):
    last = last * 0.9 + random.uniform(-1, 1) * 0.1
    rumble.append(last)
write("Thruster.wav", rumble, 0.4)

print("Wrote sounds to", os.path.normpath(OUT))
