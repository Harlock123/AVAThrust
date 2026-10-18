#!/usr/bin/env python3
"""Synthesises AVAThrust's sound effects and tunes into ../src/AVAThrust/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the tunes
are new compositions: a driving theme for the title and a low pulse for the caverns. The ship's computer speaks
through the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAThrust", "Sounds")
RNG = random.Random(1986)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def computer(words):
    voice = say(words, rate=RATE, pitch=100, speed=0.95, size=1.05)
    hum = say(words, rate=RATE, pitch=150, speed=0.95, size=1.05, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Fire.wav", tone(0.1, 1500, 500, "square", attack=0.001, release=1.0, gain=0.5), 0.14)
    write("GunFire.wav", tone(0.12, 500, 180, "square", attack=0.001, release=1.0, gain=0.5), 0.16)
    write("GunDown.wav", mix(tone(0.4, 1800, 120, "noise", attack=0.001, release=0.9), tone(0.3, 300, 60, "square", release=0.9, gain=0.5)), 0.34)
    write("Boom.wav", mix(tone(0.5, 1500, 100, "noise", attack=0.001, release=0.9), tone(0.4, 140, 40, "sine", release=0.8, gain=0.8)), 0.36)
    write("Explode.wav", cat(mix(tone(1.2, 2200, 50, "noise", attack=0.001, release=0.5), tone(1.0, 130, 25, "sine", release=0.6, gain=0.9)),
                             tone(0.6, 500, 70, "saw", vibrato=0.2, vib_rate=10, release=0.6, gain=0.3)), 0.55)
    write("Tractor.wav", cat(*[tone(0.05, 400 + i * 120, kind="tri", release=0.3, gain=0.6) for i in range(8)]), 0.24)
    write("Fuel.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6"]]), 0.24)
    write("ReactorHit.wav", mix(tone(0.25, 900, 200, "noise", attack=0.001, release=1.0, gain=0.6), tone(0.25, 90, 60, "square", release=0.8, gain=0.6)), 0.3)
    write("Critical.wav", cat(*[cat(tone(0.18, 880, kind="square", release=0.2, gain=0.5), tone(0.18, 660, kind="square", release=0.2, gain=0.5)) for _ in range(3)]), 0.26)
    write("Deflect.wav", tone(0.12, 2400, 900, "tri", attack=0.001, release=0.8, gain=0.5), 0.16)
    write("Escape.wav", tone(1.0, 200, 1400, "saw", attack=0.05, vibrato=0.03, vib_rate=20, release=0.6, gain=0.4), 0.26)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)
    write("LowFuel.wav", cat(*[tone(0.12, 1000, kind="square", release=0.2, gain=0.5) for _ in range(3)]), 0.22)
    write("Incomplete.wav", cat(tone(0.2, 400, kind="square", release=0.3, gain=0.5), tone(0.35, 300, kind="square", release=0.4, gain=0.5)), 0.24)
    write("Cleared.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.36)
    # "Reactor critical!"
    write("SayCritical.wav", computer("R IY AE1 K T ER , K R IH1 T IH K AH L !"), 0.5)

    # The caverns (an original composition): a low, steady pulse.
    e = 0.5
    lead = [("A3", 1), ("-", e), ("A3", e), ("C4", 1), ("B3", 1), ("A3", 1), ("-", e), ("E3", e), ("G3", 1), ("A3", 1),
            ("A3", 1), ("-", e), ("A3", e), ("D4", 1), ("C4", 1), ("B3", 1), ("G3", 1), ("A3", 2)]
    bass = [("A1", e), ("A1", e), ("A2", e), ("A1", e)] * 4 + [("D2", e), ("D2", e), ("D3", e), ("D2", e)] * 2 + [("E2", e), ("E2", e), ("E3", e), ("A1", e)] * 2
    write("Music.wav", tune(118, lead * 2, bass * 2, kind="tri"), 0.24)
    # The title (an original composition): driving, in A minor.
    title = [("A4", e), ("A4", e), ("C5", e), ("A4", e), ("E5", 1), ("D5", e), ("C5", e), ("B4", e), ("G4", e), ("B4", e), ("D5", e), ("E5", 2),
             ("A4", e), ("A4", e), ("C5", e), ("E5", e), ("A5", 1), ("G5", e), ("E5", e), ("F5", e), ("E5", e), ("D5", e), ("B4", e), ("A4", 2)]
    tbass = [("A2", e), ("E3", e)] * 4 + [("G2", e), ("D3", e)] * 4 + [("A2", e), ("E3", e)] * 4 + [("F2", e), ("C3", e)] * 2 + [("E2", e), ("B2", e), ("A2", 1)]
    write("TitleTune.wav", cat(tune(140, title * 2, tbass * 2, kind="square"), silence(0.8)), 0.34)
    write("Victory.wav", tune(150, [("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 1.5), ("G#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 3)],
                              [("A2", 1), ("E2", 1), ("F#2", 1), ("D2", 1), ("E2", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("A4", 1), ("F4", 1), ("D4", 1), ("A3", 1), ("D4", 3)], [("D3", 2), ("A#2", 2), ("D2", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
