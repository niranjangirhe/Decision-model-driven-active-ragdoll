"""
The behaviour list and the wording each model sees. Kept in one place so the prompt text
is easy to read and report. No test scenario appears here: these are generic descriptions.
"""

ALL = ["brace", "protect_head", "stagger_recover", "dodge_sideways",
       "fall_and_curl", "go_limp", "grab_support"]

# Laya choice criteria: label -> description. Laya's README advises semantic labels and
# warns against yes/no-style keys, so the keys are the behaviour names themselves.
DESCRIPTIONS = {
    "brace": "tense the body, bend the knees and put the arms forward to absorb a hit",
    "protect_head": "cover the head with both arms and tuck the chin",
    "stagger_recover": "take a quick step in the direction of the push to keep balance",
    "dodge_sideways": "lean or move the body sideways out of the object's path",
    "fall_and_curl": "let the body go down and curl up into a ball",
    "go_limp": "relax all muscles completely and let the body go loose",
    "grab_support": "reach out and hold on to a nearby railing or support",
}

CHOICE_INSTRUCTIONS = "How should the character's body react to this event to avoid injury?"

STIFFNESS_INSTRUCTIONS = "How tense should the character's muscles be for this reaction?"
STIFFNESS_LEVELS = [
    "completely limp",
    "relaxed",
    "moderately tense",
    "firm",
    "rigid, resisting with full force",
]

RECOVER_INSTRUCTIONS = "After reacting, should the character try to recover its balance or get back up?"
RECOVER_FALSE = "stay down and do not try to recover"
RECOVER_TRUE = "try to recover balance or get back up"

# GLiClass labels: short natural-language phrases work better than identifiers for zero-shot.
GLICLASS_LABELS = {
    "brace": "brace for impact",
    "protect_head": "protect the head with the arms",
    "stagger_recover": "take a recovery step to keep balance",
    "dodge_sideways": "dodge sideways out of the way",
    "fall_and_curl": "fall down and curl up",
    "go_limp": "go limp",
    "grab_support": "grab a nearby railing or support",
}
