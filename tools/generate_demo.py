"""Create the original 36-second demo track and EASY/HARD charts.

The quarter-note chord-focused NORMAL chart is authored separately and preserved.
"""
from pathlib import Path
import math
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
RATE = 44100
DURATION = 36
audio = np.zeros((RATE * DURATION, 2), dtype=np.float64)
rng = np.random.default_rng(90210)


def add(at, samples, gain=1.0, pan=0.0):
    start = round(at * RATE)
    end = min(len(audio), start + len(samples))
    if end <= start:
        return
    samples = samples[: end - start] * gain
    audio[start:end, 0] += samples * math.sqrt((1 - pan) / 2)
    audio[start:end, 1] += samples * math.sqrt((1 + pan) / 2)


def tone(midi, duration, kind="pluck"):
    t = np.arange(round(duration * RATE)) / RATE
    hz = 440 * 2 ** ((midi - 69) / 12)
    attack = np.minimum(t / 0.008, 1)
    if kind == "bass":
        return attack * np.exp(-t * 4.8) * (np.sin(2 * np.pi * hz * t) + 0.18 * np.sin(4 * np.pi * hz * t))
    return attack * np.exp(-t * 5.5) * (np.sin(2 * np.pi * hz * t) + 0.28 * np.sin(4 * np.pi * hz * t))


chords = [(62, 65, 69, 72), (58, 62, 65, 69), (60, 64, 67, 70), (57, 60, 64, 67)]
melody = [0, 2, 1, 3, 2, 1, 2, 0]
for bar in range(16):
    chord = chords[bar % 4]
    at = 2 + bar * 2
    for beat in range(4):
        t = np.arange(round(0.38 * RATE)) / RATE
        kick = np.sin(2 * np.pi * (48 * t + 16 * (1 - np.exp(-t * 32)))) * np.exp(-t * 15)
        if beat in (0, 2):
            add(at + beat * 0.5, kick, 0.36)
        if beat in (1, 3):
            noise = rng.normal(0, 1, len(t))
            snare = (noise * 0.38 + np.sin(2 * np.pi * 185 * t) * 0.35) * np.exp(-t * 24)
            add(at + beat * 0.5, snare, 0.16, -0.12)
        add(at + beat * 0.5, tone(chord[0] - 24, 0.45, "bass"), 0.23)
    for eighth in range(8):
        t = np.arange(round(0.08 * RATE)) / RATE
        noise = rng.normal(0, 1, len(t))
        hat = (noise - np.roll(noise, 1)) * np.exp(-t * 70)
        add(at + eighth * 0.25, hat, 0.035 if eighth % 2 else 0.055, 0.25)
        note = chord[melody[(eighth + bar) % 8]] + (12 if bar >= 8 else 0)
        add(at + eighth * 0.25, tone(note, 0.6), 0.14, math.sin(eighth) * 0.35)
    if bar % 4 == 0:
        for i, note in enumerate(chord):
            add(at, tone(note - 12, 1.5), 0.09, (i - 1.5) / 4)

# A short opening chord and a natural release at the end.
for note in chords[0]:
    add(0.2, tone(note, 1.4), 0.10)
    add(34, tone(note, 1.8), 0.11)
fade = np.minimum(np.arange(len(audio)) / (RATE * 0.03), 1)
fade *= np.minimum((len(audio) - 1 - np.arange(len(audio))) / (RATE * 0.4), 1)
audio *= fade[:, None]
audio = np.tanh(audio * 1.3)
audio *= 0.88 / max(np.max(np.abs(audio)), 1e-6)
wav_path = ROOT / "content/audio/first-circuit.wav"
wav_path.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(wav_path), "wb") as out:
    out.setnchannels(2)
    out.setsampwidth(2)
    out.setframerate(RATE)
    out.writeframes((audio * 32767).astype("<i2").tobytes())


def make_chart(hard):
    divider = 8 if hard else 4
    slots = 128 if hard else 64
    step = 0.5 if hard else 1
    blocked = {k: -1 for k in range(1, 10)}
    lines = []
    sequence = [7, 8, 9, 6, 5, 4, 1, 2, 3, 5, 6, 8, 4, 2, 9, 7]
    holds = {16: (7, "C", 4), 17: (4, "C", 2), 32: (6, "R", 4),
             33: (5, "R", 2), 48: (2, "C", 3), 49: (8, "C", 1)}
    for slot in range(slots):
        beat = slot * step
        notes = []
        seen = set()

        def put(key, axis, duration=None):
            if key in seen or beat <= blocked[key]:
                return
            seen.add(key)
            if duration is not None:
                blocked[key] = beat + duration
                notes.append(f"{key}{axis}h[4:{duration}]")
            else:
                notes.append(f"{key}{axis}")

        if beat in holds:
            key, axis, duration = holds[beat]
            if hard or beat in (16, 32, 48):
                put(key, axis, duration)
        if beat != int(beat):
            if hard and int(beat) % 4 in (1, 3):
                put(sequence[(slot + 5) % len(sequence)], "R" if slot % 4 else "C")
        elif int(beat) % 8 != 7:
            index = int(beat)
            axis = "C" if index < 8 or (index // 4) % 2 == 0 else "R"
            if hard and index >= 8 and index % 4 == 0:
                keys = ([7, 4, 1], [8, 5, 2], [9, 6, 3])[(index // 4) % 3] if axis == "C" else ([7, 8, 9], [4, 5, 6], [1, 2, 3])[(index // 4) % 3]
                for key in keys[: (3 if index >= 24 and index % 8 == 0 else 2)]:
                    put(key, axis)
            else:
                put(sequence[index % len(sequence)], axis)
            if hard and index >= 24 and index % 4 == 2:
                put(sequence[(index + 7) % len(sequence)], "R" if axis == "C" else "C")
        lines.append("/".join(notes) + ",")
    # Final rest leaves time for the last judgment and audio release.
    lines += [",", ",", ",", "E"]
    text = "\n".join([
        "&format=numpad-simai-v1", "&title=첫 회로", "&artist=Numpad Nine · Original Demo",
        f"&difficulty={'HARD' if hard else 'EASY'}", "&audio=../audio/first-circuit.wav",
        "&first=2", "&inote=", f"(120){{{divider}}}", *lines, ""
    ])
    path = ROOT / f"content/charts/first-circuit-{'hard' if hard else 'easy'}.chart.txt"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    print(path.relative_to(ROOT))


make_chart(False)
make_chart(True)
print(f"{wav_path.relative_to(ROOT)}: {DURATION}s, stereo {RATE}Hz")
