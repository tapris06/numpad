"""Generate the original fast track '스파크 러시' and sparse-chord tap charts."""
from pathlib import Path
import json
import math
import re
import wave

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
RATE, BPM, DURATION = 44100, 200, 44
BEAT, FIRST_US, BEAT_US = 60 / BPM, 2_000_000, 300_000

EASY_BARS = [
    "7C,,8C,,", "9C,,6C,,", "5C,,4C,,", "1C,2C,3C,,",
    "7R,,4R,,", "8R,,5R,,", "9R,,6R,,", "1R,2R,3R,,",
    "7C,8C,9C,,", "6C,5C,4C,,", "1R,2R,3R,,", "4R,,5R,,",
    "7C,,5R,,", "9C,,4R,,", "1C,2C,3R,,", "5R,,6R,,",
    "7C,4C,1C,,", "8C,5C,2C,,", "9R,6R,3R,,", "4R,,6R,,",
    "7R,8R,9R,,", "4C,5C,6C,,", "1R,3R,2R,,", "8C,,2C,,",
    "7C,8C,9C,,", "6R,5R,4R,,", "1C,2C,3C,,", "4R,5R,6R,,",
    "7C,,5R,,", "9R,,2C,,", "47C,,8C,,", "5R,6R,12R,,",
]

# Four brief three-note eighth-note runs mark the ends of the eight-bar phrases.
NORMAL_BARS = [
    (4, "7C,8C,9C,6C,"), (4, "5C,4C,1C,2C,"),
    (4, "3C,6C,9C,8C,"), (4, "7C,5C,3C,,"),
    (4, "7R,4R,1R,2R,"), (4, "5R,8R,9R,6R,"),
    (4, "3R,2R,1R,45R,"), (8, "7R,8R,9R,,6R,,5R,,"),
    (4, "7C,8C,6R,5R,"), (4, "9C,6C,4R,1R,"),
    (4, "2R,3R,5C,8C,"), (4, "4C,7C,9R,,"),
    (4, "7C,6R,8C,5R,"), (4, "9C,4R,3C,2R,"),
    (4, "1C,8R,5C,47R,"), (8, "4C,5C,6C,,9R,,2R,,"),
    (4, "1C,4C,7C,8C,"), (4, "2C,5C,8C,9C,"),
    (4, "9R,6R,3R,2R,"), (4, "8R,5R,2R,,"),
    (4, "7R,5C,3R,6C,"), (4, "1C,5R,9C,4R,"),
    (4, "8C,2R,6C,7C/5R,"), (8, "1R,2R,3R,,5C,,8C,,"),
    (4, "7C,8R,9C,6R,"), (4, "5C,4R,1C,2R,"),
    (4, "3C,6R,9C,8R,"), (4, "7R,5C,3R,,"),
    (4, "4C,7R,8C,5R,"), (4, "9C,6R,3C,2R,"),
    (4, "1C,4R,7C,8C/6R,"), (8, "7C,8C,9C,,5R,,2C,,"),
]


def write_chart(difficulty, bars):
    events, moments, chords, eighth_bars = [], 0, 0, 0
    if len(bars) != 32:
        raise ValueError("Spark Rush uses 32 four-beat bars")
    for bar, (divider, line) in enumerate(bars):
        slots = line.split(",")
        if divider not in (4, 8) or len(slots) != divider + 1 or slots[-1]:
            raise ValueError(f"{difficulty}: invalid bar {bar + 1}")
        eighth_bars += divider == 8
        for slot, chord in enumerate(slots[:-1]):
            if not chord:
                continue
            start = FIRST_US + bar * 4 * BEAT_US + slot * (4 * BEAT_US // divider)
            keys = set()
            for token in chord.split("/"):
                match = re.fullmatch(r"([1-9]+)([RC])", token)
                if match is None:
                    raise ValueError(f"Invalid tap token: {token}")
                for digit in match[1]:
                    key = int(digit)
                    if key in keys:
                        raise ValueError(f"Duplicate physical key at bar {bar + 1}: {key}")
                    keys.add(key)
                    events.append((key, match[2], start))
            if len(keys) > 2:
                raise ValueError("Spark Rush has no three-key inputs")
            moments += 1
            chords += len(keys) == 2
    chord_limit = 2 if difficulty == "EASY" else 4
    if chords > chord_limit or chords / moments > 0.05:
        raise ValueError("Simultaneous inputs must remain sparse")
    if difficulty == "EASY" and eighth_bars:
        raise ValueError("EASY must stay on the quarter-note grid")
    end_us = FIRST_US + (len(bars) * 4 + 4) * BEAT_US
    if end_us <= max(n[2] + 90_001 for n in events) or end_us > DURATION * 1_000_000:
        raise ValueError("Invalid chart end marker")
    sections = (["4비트와 쉬는 박", "한 방향 단타와 느린 방향 전환", "행·열 단타 패턴", "단타 마무리와 드문 2키 악센트"]
                if difficulty == "EASY" else
                ["4비트 연속 단타", "양방향 전환", "행·열 연속 패턴", "빠른 단타 마무리"])
    lines = ["&format=numpad-simai-v1", "&title=스파크 러시", "&artist=Numpad Nine · Original Demo",
             f"&difficulty={difficulty}", "&audio=../audio/spark-rush.wav", "&first=2", "&inote=", f"({BPM}){{4}}",
             f"// 단타 중심. 2키 동타는 최대 {chord_limit}회, 3키 동타 없음."]
    current_divider = 4
    for bar, (divider, line) in enumerate(bars):
        if bar % 8 == 0:
            lines += ["", f"// {bar + 1}~{bar + 8}마디: {sections[bar // 8]}"]
        prefix = f"{{{divider}}}" if divider != current_divider else ""
        lines.append(prefix + line)
        current_divider = divider
    lines += ["", "// 마지막 입력 이후 음원 릴리스.", "{4},,,,", "E", ""]
    path = ROOT / f"content/charts/spark-rush-{difficulty.lower()}.chart.txt"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"chart": str(path.relative_to(ROOT)), "taps": len(events), "input_moments": moments,
                      "two_key_chords": chords, "chord_percent": round(100 * chords / moments, 2),
                      "eighth_note_bars": eighth_bars, "end_seconds": end_us / 1e6}))


def make_music():
    audio = np.zeros((RATE * DURATION, 2), dtype=np.float64)
    rng = np.random.default_rng(2002026)

    def add(at, samples, gain, pan=0):
        start = round(at * RATE)
        end = min(len(audio), start + len(samples))
        samples = samples[:end - start] * gain
        audio[start:end, 0] += samples * math.sqrt((1 - pan) / 2)
        audio[start:end, 1] += samples * math.sqrt((1 + pan) / 2)

    def synth(midi, duration, kind="lead"):
        t = np.arange(round(duration * RATE)) / RATE
        hz = 440 * 2 ** ((midi - 69) / 12)
        release = np.minimum((duration - t) / 0.025, 1)
        if kind == "pad":
            envelope = np.minimum(t / 0.025, 1) * np.minimum((duration - t) / 0.18, 1)
            return envelope * (np.sin(2 * np.pi * hz * t) + 0.16 * np.sin(4 * np.pi * hz * t))
        if kind == "bass":
            return np.minimum(t / 0.004, 1) * np.exp(-t * 11) * release * (
                np.sin(2 * np.pi * hz * t) + 0.24 * np.sin(4 * np.pi * hz * t)
                + 0.10 * np.sin(6 * np.pi * hz * t))
        return np.minimum(t / 0.004, 1) * np.exp(-t * 7.5) * release * (
            0.73 * np.sin(2 * np.pi * hz * t) + 0.21 * np.sin(6 * np.pi * hz * t)
            + 0.09 * np.sin(10 * np.pi * hz * t))

    chords = [(52, 55, 59, 64), (48, 52, 55, 60), (55, 59, 62, 67), (50, 54, 57, 62)]
    melody = [0, 1, 2, 1, 3, 2, 1, 2]
    for bar in range(32):
        chord = chords[(bar // 2) % 4]
        at = 2 + bar * 4 * BEAT
        breakdown = 12 <= bar < 16
        energy = 0.70 if breakdown else 1.0
        for index, note in enumerate(chord):
            add(at, synth(note, 0.44, "pad"), 0.065, (index - 1.5) * 0.4)
            if not breakdown:
                add(at + 2.5 * BEAT, synth(note, 0.29, "pad"), 0.042, (1.5 - index) * 0.3)
        for beat in range(4):
            time = at + beat * BEAT
            t = np.arange(round(0.22 * RATE)) / RATE
            if not breakdown or beat % 2 == 0:
                kick = np.sin(2 * np.pi * (47 * t + 14 * (1 - np.exp(-t * 42)))) * np.exp(-t * 21)
                add(time, kick, 0.32 * energy)
            if beat % 2:
                noise = rng.normal(0, 1, len(t))
                snare = (noise * 0.28 + np.sin(2 * np.pi * 190 * t) * 0.45) * np.exp(-t * 29)
                add(time, snare, 0.17 * energy, -0.10)
            bass = chord[0] - 12 + (7 if beat in (1, 3) else 0)
            add(time, synth(bass, 0.23, "bass"), 0.23 * energy)
        for eighth in range(8):
            time = at + eighth * BEAT / 2
            t = np.arange(round(0.055 * RATE)) / RATE
            noise = rng.normal(0, 1, len(t))
            hat = (noise - np.roll(noise, 1)) * np.exp(-t * 95)
            add(time, hat, (0.025 if eighth % 2 == 0 else 0.039) * energy, 0.28)
            note = chord[melody[(eighth + (bar % 2) * 2) % 8]] + (24 if bar >= 24 else 12)
            add(time, synth(note, 0.19), 0.13 * energy, math.sin(eighth * 1.3) * 0.28)
    for index, note in enumerate(chords[0]):
        add(0.2, synth(note + 12, 1.1, "pad"), 0.06, (index - 1.5) * 0.3)
        add(40.4, synth(note, 2.8, "pad"), 0.10, (index - 1.5) * 0.3)
    delay = round(0.225 * RATE)
    audio[delay:] += audio[:-delay, ::-1].copy() * 0.10
    fade = np.minimum(np.arange(len(audio)) / (RATE * 0.02), 1)
    fade *= np.minimum((len(audio) - 1 - np.arange(len(audio))) / (RATE * 0.65), 1)
    audio = np.tanh(audio * fade[:, None] * 1.35)
    audio *= 0.88 / max(np.max(np.abs(audio)), 1e-6)
    path = ROOT / "content/audio/spark-rush.wav"
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes((audio * 32767).astype("<i2").tobytes())
    print(f"{path.relative_to(ROOT)}: {DURATION}s, {BPM} BPM, stereo {RATE}Hz")


if __name__ == "__main__":
    write_chart("EASY", [(4, line) for line in EASY_BARS])
    write_chart("NORMAL", NORMAL_BARS)
    make_music()
