"""Generate the original track '긴 파동' and its authored hold-focused charts."""
from pathlib import Path
import json
import math
import re
import wave

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
RATE, BPM, DURATION = 44100, 100, 44
BEAT = 60 / BPM
FIRST_US, BEAT_US = 2_000_000, 600_000

# Each line is one four-beat bar. A full beat separates consecutive main holds.
EASY_BARS = [
    "7Ch[4:3],,,,",
    "8Ch[4:3],,,9C,",
    "9Ch[4:3],,,,",
    "4Ch[4:3],,,5C,",
    "7Rh[4:3],,,,",
    "5Rh[4:3],,,6R,",
    "3Rh[4:3],,,,",
    "1Rh[4:3],,,2R,",
    "6Ch[4:3],,,,",
    "2Ch[4:3],,,3C,",
    "4Rh[4:3],,,,",
    "8Rh[4:3],,,9R,",
    "1Ch[4:3],,,,",
    "5Ch[4:3],,,6R,",
    "2Rh[4:3],,,,",
    "9Rh[4:3],,,7C,",
]

# Paired holds, staggered overlaps, both directions, then three-color bodies.
NORMAL_BARS = [
    "7Ch[4:3]/4Ch[4:3],,1C,,",
    "8Ch[4:3]/5Ch[4:3],,2C,,",
    "9Ch[4:3]/6Ch[4:3],,3C,,",
    "7Rh[4:3]/8Rh[4:3],,9R,,",
    "4Rh[4:3],5Rh[4:2],6R,,",
    "1Rh[4:3],2Rh[4:2],3R,,",
    "7Ch[4:3],1Ch[4:2],4C,,",
    "8Ch[4:3],2Ch[4:2],5C,,",
    "7Ch[4:3],3Rh[4:2],9Ch[4:1],,",
    "8Ch[4:3],4Rh[4:2],5Ch[4:1],,",
    "1Rh[4:3],9Ch[4:2],3Rh[4:1],,",
    "7Rh[4:3],5Ch[4:2],8Rh[4:1],,",
    "7Ch[4:3]/4Ch[4:3],1Ch[4:2],,,",
    "8Ch[4:3]/2Ch[4:3],5Ch[4:2],,,",
    "4Rh[4:3]/6Rh[4:3],5Rh[4:2],,,",
    "1Rh[4:3]/3Rh[4:3],2Rh[4:2],,,",
]


# Every core beat has a new input. Three-key reads stay on one physical row/column.
# Before each three-key head, the preceding holds finish a full beat earlier.
HARD_BARS = [
    "7Ch[4:3]/4Ch[4:1],1C,4Ch[4:1],1C,",
    "8Ch[4:3]/5Ch[4:1],2C,5Ch[4:1],2C,",
    "9Ch[4:3]/6Ch[4:1],3C,6Ch[4:1],3C,",
    "7Rh[4:2]/8Rh[4:1],9R,8R,456C,",
    "4Rh[4:3]/5Rh[4:1],6R,5Rh[4:1],6R,",
    "1Rh[4:3]/2Rh[4:1],3R,2Rh[4:1],3R,",
    "1Ch[4:3]/7Ch[4:1],4C,7Ch[4:1],4C,",
    "2Ch[4:2]/8Ch[4:1],5C,8C,14C/7R,",
    "4Ch[4:3]/1Ch[4:1],7R,1Rh[4:1],7R,",
    "5Ch[4:3]/2Ch[4:1],8R,2Rh[4:1],8R,",
    "6Rh[4:3]/4Rh[4:1],5C,4C,4R/5C,",
    "3Rh[4:2]/1Rh[4:1],2C,1C,789R,",
    "6Ch[4:3]/9Ch[4:1],3R,9R,9C/3R,",
    "8Rh[4:3]/9Rh[4:1],7C,9C,9R/7C,",
    "5Rh[4:3]/6Rh[4:1],4C,6C,6R/4C,",
    "2Rh[4:2]/3Rh[4:1],1C,3C,78R/9C,",
]


def validate_hard(events):
    heads = {}
    for note in events:
        heads.setdefault(note[2], []).append(note)
    expected_beats = [FIRST_US + beat * BEAT_US for beat in range(64)]
    if sorted(heads) != expected_beats:
        raise ValueError("HARD must have new inputs on all 64 quarter-note beats")
    triples = [at for at, notes in heads.items() if len(notes) == 3]
    if len(triples) > 4 or any(b - a <= BEAT_US for a, b in zip(triples, triples[1:])):
        raise ValueError("HARD three-key heads must be sparse and isolated")
    holds = [note for note in events if note[3] is not None]
    boundaries = sorted({n[2] for n in events} | {n[3] for n in holds})
    samples = boundaries + [(a + b) // 2 for a, b in zip(boundaries, boundaries[1:])]
    for at in samples:
        # Include hold ends as well as fresh heads to keep release boundaries conservative.
        required = {n[0] for n in holds if n[2] <= at <= n[3]} | {n[0] for n in heads.get(at, [])}
        if len(required) > 3:
            raise ValueError(f"HARD requires more than three keys at {at}us: {required}")
        if len(required) == 3:
            rows = {(key - 1) // 3 for key in required}
            columns = {(key - 1) % 3 for key in required}
            if len(rows) != 1 and len(columns) != 1:
                raise ValueError(f"HARD three-key requirement is not physically aligned at {at}us: {required}")
    fades = 0
    for axis in ("R", "C"):
        for lane in range(3):
            lane_holds = [n for n in holds if n[1] == axis
                          and ((n[0] - 1) % 3 if axis == "C" else 2 - (n[0] - 1) // 3) == lane]
            for end in {n[3] for n in lane_holds}:
                before = {n[0] for n in lane_holds if n[2] < end <= n[3]}
                after = {n[0] for n in lane_holds if n[2] <= end < n[3]}
                if len(before) > len(after) > 0:
                    fades += 1
    if fades < 16:
        raise ValueError("HARD must include asymmetric composite-hold releases throughout")
    two_key_beats = sum(len({n[0] for n in holds if n[2] <= at < n[3]}
                            | {n[0] for n in heads[at]}) == 2 for at in expected_beats)
    return {"quarter_note_beats": len(heads), "three_key_heads": len(triples),
            "two_key_beats": two_key_beats, "composite_release_changes": fades}


def write_chart(difficulty, bars):
    events, last_head, hold_end = [], {}, {}
    for bar, line in enumerate(bars):
        slots = line.split(",")
        if len(slots) != 5 or slots[-1]:
            raise ValueError(f"{difficulty}: bar {bar + 1} must contain four beats")
        for beat, chord in enumerate(slots[:-1]):
            start = FIRST_US + (bar * 4 + beat) * BEAT_US
            for token in filter(None, chord.split("/")):
                match = re.fullmatch(r"([1-9]+)([RC])(?:h\[4:(\d+)\])?", token)
                if match is None:
                    raise ValueError(f"Invalid authored token: {token}")
                keys, axis, length = match.groups()
                if length is not None and (len(keys) != 1 or int(length) < 1):
                    raise ValueError(f"Invalid hold: {token}")
                end = start + int(length) * BEAT_US if length is not None else None
                for digit in keys:
                    key = int(digit)
                    if last_head.get(key) == start or start <= hold_end.get(key, -1):
                        raise ValueError(f"{difficulty}: key {key} overlaps at bar {bar + 1}, beat {beat + 1}")
                    last_head[key] = start
                    if end is not None:
                        hold_end[key] = end
                    events.append((key, axis, start, end))
    end_us = FIRST_US + (len(bars) * 4 + 4) * BEAT_US
    if end_us > DURATION * 1_000_000 or end_us <= max(max(n[2] + 90_001, n[3] or n[2]) for n in events):
        raise ValueError(f"{difficulty}: invalid end marker")
    extra = validate_hard(events) if difficulty == "HARD" else {}
    sections = {
        "EASY": ["기본 유지", "방향 전환", "양방향 유지", "마무리"],
        "NORMAL": ["2색 합성 롱노트와 단타", "시작점이 다른 롱노트 겹침", "양방향 롱노트", "3색 합성 롱노트"],
        "HARD": ["합성 롱잡의 분리와 재합류 · 4마디 끝은 한 방향 3레인",
                 "행·열 전환 롱잡 · 8마디 끝은 합성 2키와 반대 방향 1키",
                 "양방향 롱잡 · 12마디 끝은 한 행의 검정 합성",
                 "2키 롱잡 변형 · 16마디 끝은 한 행의 합성 2키와 반대 방향 1키"],
    }[difficulty]
    lines = ["&format=numpad-chart-v1", "&title=긴 파동", "&artist=Numpad Nine · Original Demo",
             f"&difficulty={difficulty}", "&audio=../audio/long-wave.wav", "&first=2", "&inote=", f"({BPM}){{4}}"]
    if difficulty == "HARD":
        lines += ["// 본 구간 64박은 모두 4비트 새 입력. 8비트 분할 없음.",
                  "// 유지·종료·새 입력을 합쳐 최대 3키. 3키가 필요한 시점은 물리 넘패드의 같은 행 또는 열.",
                  "// 3키 동타는 4·8·12·16마디 끝에만 배치. 합성 롱노트는 짧은 색부터 먼저 종료."]
    for index, line in enumerate(bars):
        if index % 4 == 0:
            lines += ["", f"// {index + 1}~{index + 4}마디: {sections[index // 4]}"]
        lines.append(line)
    lines += ["", "// 마지막 롱노트 종료 후 음원 릴리스.", ",,,,", "E", ""]
    path = ROOT / f"content/charts/long-wave-{difficulty.lower()}.npd"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")
    holds = [n for n in events if n[3] is not None]
    peak = max(sum(n[2] <= at < n[3] for n in holds) for at in {n[2] for n in holds})
    print(json.dumps({"chart": str(path.relative_to(ROOT)), "heads": len(events), "holds": len(holds),
                      "taps": len(events) - len(holds), "peak_holds": peak, "end_seconds": end_us / 1e6,
                      **extra}))


def make_music():
    audio = np.zeros((RATE * DURATION, 2), dtype=np.float64)
    rng = np.random.default_rng(1104)

    def add(at, samples, gain, pan=0):
        start = round(at * RATE)
        end = min(len(audio), start + len(samples))
        samples = samples[:end - start] * gain
        audio[start:end, 0] += samples * math.sqrt((1 - pan) / 2)
        audio[start:end, 1] += samples * math.sqrt((1 + pan) / 2)

    def voice(midi, duration, kind="pad"):
        t = np.arange(round(duration * RATE)) / RATE
        hz = 440 * 2 ** ((midi - 69) / 12)
        if kind == "pluck":
            return np.minimum(t / 0.006, 1) * np.exp(-t * 6) * (
                np.sin(2 * np.pi * hz * t) + 0.18 * np.sin(4 * np.pi * hz * t))
        attack = 0.025 if kind == "lead" else 0.07
        envelope = np.minimum(t / attack, 1) * np.minimum((duration - t) / 0.22, 1)
        return envelope * (0.60 * np.sin(2 * np.pi * hz * t)
                           + 0.22 * np.sin(2 * np.pi * hz * 1.003 * t)
                           + 0.10 * np.sin(4 * np.pi * hz * t))

    chords = [(57, 60, 64, 67), (53, 57, 60, 64), (48, 55, 60, 64), (55, 59, 62, 67)]
    for bar in range(16):
        chord = chords[bar % 4]
        at = 2 + bar * 4 * BEAT
        for index, note in enumerate(chord):
            add(at, voice(note, 4 * BEAT + 0.16), 0.095, (index - 1.5) * 0.35)
        add(at, voice(chord[0] - 12, 3.5 * BEAT), 0.19)
        lead = chord[[2, 1, 3, 2][bar % 4]] + 12
        add(at, voice(lead, 3 * BEAT + 0.12, "lead"), 0.15, -0.12)
        add(at + 3 * BEAT, voice(chord[1] + 12, 0.48, "pluck"), 0.12, 0.18)
        for beat in range(4):
            t = np.arange(round(0.30 * RATE)) / RATE
            if beat % 2 == 0:
                kick = np.sin(2 * np.pi * (45 * t + 12 * (1 - np.exp(-t * 35)))) * np.exp(-t * 17)
                add(at + beat * BEAT, kick, 0.24)
            else:
                rim = (rng.normal(0, 0.20, len(t)) + np.sin(2 * np.pi * 195 * t)) * np.exp(-t * 40)
                add(at + beat * BEAT, rim, 0.075, -0.22)
            hat_t = np.arange(round(0.07 * RATE)) / RATE
            noise = rng.normal(0, 1, len(hat_t))
            hat = (noise - np.roll(noise, 1)) * np.exp(-hat_t * 85)
            add(at + beat * BEAT, hat, 0.027, 0.30)
            if beat in (1, 2):
                add(at + beat * BEAT, voice(chord[beat] + 12, 0.45, "pluck"), 0.085, 0.2 if beat == 1 else -0.2)
    for index, note in enumerate(chords[0]):
        add(0.25, voice(note, 1.45), 0.065, (index - 1.5) * 0.3)
        add(40.4, voice(note, 3.1), 0.11, (index - 1.5) * 0.3)
    # A quiet stereo echo softens the sustained lead without external samples.
    delay = round(0.30 * RATE)
    audio[delay:] += audio[:-delay, ::-1].copy() * 0.10
    fade = np.minimum(np.arange(len(audio)) / (RATE * 0.025), 1)
    fade *= np.minimum((len(audio) - 1 - np.arange(len(audio))) / (RATE * 0.65), 1)
    audio = np.tanh(audio * fade[:, None] * 1.25)
    audio *= 0.86 / max(np.max(np.abs(audio)), 1e-6)
    path = ROOT / "content/audio/long-wave.wav"
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes((audio * 32767).astype("<i2").tobytes())
    print(f"{path.relative_to(ROOT)}: {DURATION}s, {BPM} BPM, stereo {RATE}Hz")


if __name__ == "__main__":
    write_chart("EASY", EASY_BARS)
    write_chart("NORMAL", NORMAL_BARS)
    write_chart("HARD", HARD_BARS)
    make_music()
