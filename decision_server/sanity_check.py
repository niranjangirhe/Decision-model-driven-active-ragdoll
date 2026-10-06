"""
Sanity check for the decision server: a dozen hand-written event descriptions with the
behaviours I would accept for each. This is NOT the experiment and NOT a calibration or
few-shot set (nothing here is fed to the models); it only shows whether a model reads the
text at all, and how fast it answers. The expected labels are my own judgement.

Usage (server running):  .venv/Scripts/python sanity_check.py [--url http://127.0.0.1:8765]
"""

import argparse
import json
import statistics
import time
import urllib.request

CASES = [
    ("Character is standing. A heavy object (about 10 kg) is falling toward the head, about 0.5 s until impact. Open space to the left.",
     ["dodge_sideways", "protect_head"]),
    ("Character is standing. A light impact to the chest from the front. Open space to the left and to the right.",
     ["brace"]),
    ("Character is standing. A strong impact to the chest from behind. Open space to the left and to the right.",
     ["stagger_recover"]),
    ("Character is standing. A very strong impact to the hips from the left. Wall 0.5 m to the right.",
     ["fall_and_curl"]),
    ("Character is sitting on a bench. A light object (about 1 kg) is falling toward the head, about 0.2 s until impact.",
     ["protect_head"]),
    ("Character is standing. A moderate impact to the left shoulder from the left. A railing is within reach to the right.",
     ["grab_support", "stagger_recover"]),
    ("Character is walking. The left foot hit an obstacle mid-step.",
     ["stagger_recover"]),
    ("Character is standing. Both feet have lost contact with the ground.",
     ["fall_and_curl", "go_limp"]),
    ("Character is sitting on a bench. A very strong impact to the chest from the front.",
     ["fall_and_curl", "brace"]),
    ("Character is standing. A medium-weight object (about 3 kg) is flying toward the head from the right, about 0.3 s until impact. Open space to the left.",
     ["protect_head", "dodge_sideways"]),
    ("Character is lying on the ground. A heavy object (about 8 kg) is falling toward the chest, about 0.4 s until impact.",
     ["fall_and_curl", "protect_head"]),
    ("Character is standing. A moderate impact to the right leg from the right. Open space to the left and to the right.",
     ["stagger_recover", "brace"]),
]


def post(url, payload):
    req = urllib.request.Request(url + "/decide", data=json.dumps(payload).encode(),
                                 headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req) as r:
        body = json.loads(r.read())
    return body, (time.perf_counter() - t0) * 1000.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://127.0.0.1:8765")
    args = ap.parse_args()
    health = json.loads(urllib.request.urlopen(args.url + "/health").read())
    print("server:", health)

    for model in health["models"]:
        hits, round_trips, server_ms, chosen = 0, [], [], {}
        print(f"\n=== {model}")
        for text, ok in CASES:
            body, rt = post(args.url, {"text": text, "model": model})
            b = body["behaviour"]
            hit = b in ok
            hits += hit
            round_trips.append(rt)
            server_ms.append(body["latency_ms"])
            chosen[b] = chosen.get(b, 0) + 1
            p = dict(zip(body["behaviours"], body["probabilities"]))
            extra = ""
            if body["stiffness"] is not None:
                extra = f" stiffness={body['stiffness']:.2f} recover={body['recover']} (p={body['recover_probability']:.2f})"
            print(f"{'ok ' if hit else 'NO '} {b:16s} p={p[b]:.2f}{extra}  | expected {ok}\n     {text}")
        print(f"--> {model}: {hits}/{len(CASES)} acceptable; choices {chosen}")
        print(f"    latency median: server {statistics.median(server_ms):.1f} ms, round trip {statistics.median(round_trips):.1f} ms")


if __name__ == "__main__":
    main()
