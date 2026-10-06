"""
Turn the demo recordings (Recordings/*.mp4, made with Ragdoll > Record Demo) into the GIFs used in
the README (Docs/media/*.gif). Each trial is trimmed to the part where the event happens.

Usage:  decision_server/.venv/Scripts/python analysis/make_gifs.py
Needs opencv-python-headless in addition to the analysis requirements.
"""

from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
REC = ROOT / "Recordings"
OUT = ROOT / "Docs" / "media"
FPS = 10


def read_frames(path, segments, width):
    """Frames from each (start_s, end_s) segment, resized to `width`, as RGB arrays."""
    cap = cv2.VideoCapture(str(path))
    src_fps = cap.get(cv2.CAP_PROP_FPS)
    frames = []
    for start, end in segments:
        t = start
        while t < end:
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(round(t * src_fps)))
            ok, f = cap.read()
            if not ok:
                break
            h = int(f.shape[0] * width / f.shape[1])
            frames.append(cv2.cvtColor(cv2.resize(f, (width, h), interpolation=cv2.INTER_AREA), cv2.COLOR_BGR2RGB))
            t += 1.0 / FPS
    return frames


def save_gif(frames, path):
    imgs = [Image.fromarray(f).quantize(colors=128, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE) for f in frames]
    imgs[0].save(path, save_all=True, append_images=imgs[1:], duration=int(1000 / FPS), loop=0, optimize=True)
    print(f"{path.name}: {len(frames)} frames, {path.stat().st_size / 1e6:.1f} MB")


def trial_segments(trials, seconds_per_trial, start=0.9, end=3.4):
    """The interesting window of each trial: launch is 1 s after the trial starts."""
    return [(i * seconds_per_trial + start, i * seconds_per_trial + end) for i in range(trials)]


def side_by_side(left, right, labels, crop):
    """Crop the same region from two clips, label each, and join them horizontally."""
    x0, y0, x1, y1 = crop
    out = []
    for a, b in zip(left, right):
        tiles = []
        for img, label in zip((a, b), labels):
            tile = np.ascontiguousarray(img[y0:y1, x0:x1])
            bar = np.full((44, tile.shape[1], 3), 20, np.uint8)
            cv2.putText(bar, label, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (255, 255, 255), 2, cv2.LINE_AA)
            tiles.append(np.vstack([bar, tile]))
        gap = np.full((tiles[0].shape[0], 6, 3), 255, np.uint8)
        out.append(np.hstack([tiles[0], gap, tiles[1]]))
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    save_gif(read_frames(REC / "demo_S1_rules.mp4", trial_segments(4, 4.5), 960), OUT / "s1_bench_rules.gif")
    save_gif(read_frames(REC / "demo_S2_rules.mp4", trial_segments(4, 4.5, 0.9, 3.6), 960), OUT / "s2_standing_rules.gif")

    # Same seed, same falling box, two decisions. Full-resolution frames, cropped around the bench.
    seg = [(0.9, 3.3)]
    left = read_frames(REC / "cmp_S1_seed8_fixed.mp4", seg, 1280)
    right = read_frames(REC / "cmp_S1_seed8_laya.mp4", seg, 1280)
    n = min(len(left), len(right))
    frames = side_by_side(left[:n], right[:n], ["brace: box hits the head", "fall_and_curl: box misses"],
                          crop=(390, 200, 890, 650))
    frames = [cv2.resize(f, (f.shape[1] * 3 // 4, f.shape[0] * 3 // 4), interpolation=cv2.INTER_AREA) for f in frames]
    save_gif(frames, OUT / "s1_same_event_two_decisions.gif")


if __name__ == "__main__":
    main()
