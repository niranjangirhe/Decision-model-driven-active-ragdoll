"""
Decision server for the active ragdoll.

One endpoint, POST /decide. Unity sends the event text and the allowed behaviours; the
server answers with the chosen behaviour, a probability for every allowed behaviour,
the parameters, and how long the model took.

Two models, picked per request with "model":
  * "laya"     - Laya (Convai Innovations), English checkpoint, zero-shot. Three typed
                 questions answered in one forward pass: a choice (behaviour), a score
                 (muscle stiffness) and a yes/no (recover afterwards).
  * "gliclass" - GLiClass zero-shot, behaviour choice only. Unity takes the stiffness and
                 recover flag from its rules selector for this one.

Run:  .venv/Scripts/python -m uvicorn server:app --host 127.0.0.1 --port 8765
"""

import os
import time
from contextlib import asynccontextmanager
from typing import Dict, List, Optional

import torch
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

import behaviours as B

DEVICE = os.environ.get("DECISION_DEVICE") or ("cuda" if torch.cuda.is_available() else "cpu")
LAYA_REPO = os.environ.get("LAYA_REPO", "convaiinnovations/laya")
GLICLASS_REPO = os.environ.get("GLICLASS_REPO", "knowledgator/gliclass-modern-base-v3.0")
# Model weights pinned to the Hugging Face commits used for the results in the README.
LAYA_REVISION = os.environ.get("LAYA_REVISION", "7b928d828b7b0e022f929d9bd2e44165aa270148")
GLICLASS_REVISION = os.environ.get("GLICLASS_REVISION", "ac369222ca4375ca66ebaf7fb5220f223514c035")
# Laya's README reports that the answer depends on which slot an option sits in. Asking the
# same choice once per rotation of the options (all rotations share one forward pass) and
# averaging removes that effect. Set LAYA_ROTATE=0 to ask once in the canonical order.
LAYA_ROTATE = os.environ.get("LAYA_ROTATE", "1") == "1"

models = {}


def load_laya():
    import laya
    agent = laya.load(LAYA_REPO, device=DEVICE, revision=LAYA_REVISION)
    return agent


def load_gliclass():
    from gliclass import GLiClassModel, ZeroShotClassificationPipeline
    from transformers import AutoTokenizer
    model = GLiClassModel.from_pretrained(GLICLASS_REPO, revision=GLICLASS_REVISION)
    tokenizer = AutoTokenizer.from_pretrained(GLICLASS_REPO, revision=GLICLASS_REVISION, add_prefix_space=True)
    device = "cuda:0" if DEVICE == "cuda" else "cpu"
    return ZeroShotClassificationPipeline(model, tokenizer, classification_type="single-label", device=device)


@asynccontextmanager
async def lifespan(app: FastAPI):
    which = os.environ.get("DECISION_MODELS", "laya,gliclass").split(",")
    if "laya" in which:
        models["laya"] = load_laya()
    if "gliclass" in which:
        models["gliclass"] = load_gliclass()
    # Warm up so the first real request does not pay for CUDA initialisation.
    warm = "Character is standing. A moderate impact to the chest from the front."
    for name in list(models):
        for _ in range(3):
            decide_with(name, warm, B.ALL)
    yield


app = FastAPI(title="Ragdoll decision server", lifespan=lifespan)


class DecideRequest(BaseModel):
    text: str
    allowed: List[str] = B.ALL
    model: str = "laya"


class DecideResponse(BaseModel):
    behaviour: str
    behaviours: List[str]          # same order as probabilities
    probabilities: List[float]
    stiffness: Optional[float]     # 0 = limp, 1 = rigid; None when the model does not answer it
    recover: Optional[bool]
    recover_probability: Optional[float]
    latency_ms: float              # model time only, measured on the server
    model: str
    device: str


# ---------------------------------------------------------------------------- Laya

def laya_questions(allowed: List[str]) -> Dict:
    criteria = {b: B.DESCRIPTIONS[b] for b in allowed}
    choice = {"type": "choice", "instructions": B.CHOICE_INSTRUCTIONS, "criteria": criteria}
    questions = {}
    k = len(allowed)
    if LAYA_ROTATE:
        for r in range(k):
            questions[f"behaviour_{r}"] = dict(choice, option_order=[(i + r) % k for i in range(k)])
    else:
        questions["behaviour_0"] = choice
    questions["stiffness"] = {"type": "score", "instructions": B.STIFFNESS_INSTRUCTIONS,
                              "criteria": B.STIFFNESS_LEVELS}
    questions["recover"] = {"type": "noul", "instructions": B.RECOVER_INSTRUCTIONS,
                            "criteria": {"false": B.RECOVER_FALSE, "true": B.RECOVER_TRUE}}
    return questions


def decide_laya(text: str, allowed: List[str]) -> Dict:
    agent = models["laya"]
    answers = agent.predict(text, laya_questions(allowed))["answers"]
    rows = [v for key, v in answers.items() if key.startswith("behaviour_")]
    probs = {b: sum(float(r["probabilities"][b]) for r in rows) / len(rows) for b in allowed}
    levels = len(B.STIFFNESS_LEVELS) - 1
    stiffness = float(answers["stiffness"]["score"]) / levels
    p_recover = float(answers["recover"]["noul"])
    return {"probabilities": probs, "stiffness": min(1.0, max(0.0, stiffness)),
            "recover": p_recover >= 0.5, "recover_probability": p_recover}


# ---------------------------------------------------------------------------- GLiClass

def decide_gliclass(text: str, allowed: List[str]) -> Dict:
    pipe = models["gliclass"]
    labels = [B.GLICLASS_LABELS[b] for b in allowed]
    # In single-label mode the flat output keeps only the top label; the hierarchical output
    # carries the full softmax over all labels, so ask for that with one group.
    results = pipe(text, {"behaviour": labels}, threshold=0.0, return_hierarchical=True)[0]
    score_by_label = {label: float(score) for label, score in results["behaviour"].items()}
    raw = {b: score_by_label.get(B.GLICLASS_LABELS[b], 0.0) for b in allowed}
    total = sum(raw.values()) or 1.0
    return {"probabilities": {b: v / total for b, v in raw.items()},
            "stiffness": None, "recover": None, "recover_probability": None}


# ---------------------------------------------------------------------------- endpoint

def decide_with(model: str, text: str, allowed: List[str]) -> DecideResponse:
    if model not in models:
        raise HTTPException(400, f"model '{model}' is not loaded (loaded: {list(models)})")
    unknown = [b for b in allowed if b not in B.DESCRIPTIONS]
    if unknown or not allowed:
        raise HTTPException(400, f"unknown behaviours: {unknown}")
    if DEVICE == "cuda":
        torch.cuda.synchronize()
    t0 = time.perf_counter()
    with torch.inference_mode():
        out = decide_laya(text, allowed) if model == "laya" else decide_gliclass(text, allowed)
    if DEVICE == "cuda":
        torch.cuda.synchronize()
    latency = (time.perf_counter() - t0) * 1000.0
    probs = out["probabilities"]
    best = max(allowed, key=lambda b: probs[b])
    return DecideResponse(behaviour=best, behaviours=list(allowed),
                          probabilities=[round(probs[b], 6) for b in allowed],
                          stiffness=out["stiffness"], recover=out["recover"],
                          recover_probability=out["recover_probability"],
                          latency_ms=round(latency, 3), model=model, device=DEVICE)


@app.post("/decide", response_model=DecideResponse)
def decide(req: DecideRequest) -> DecideResponse:
    return decide_with(req.model, req.text, req.allowed)


@app.get("/health")
def health():
    import importlib.metadata as md
    return {"models": list(models), "device": DEVICE,
            "gpu": torch.cuda.get_device_name(0) if DEVICE == "cuda" else None,
            "laya_rotate": LAYA_ROTATE,
            "revisions": {"laya": LAYA_REVISION, "gliclass": GLICLASS_REVISION},
            "versions": {p: md.version(p) for p in ("laya", "gliclass", "torch", "transformers")}}
