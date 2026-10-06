# Decision-model-driven active ragdoll

This project is a physically simulated character that reacts to surprise events, such as a box falling on it or a ball hitting it. A small decision model, running on the same machine, decides how it reacts. The model reads a one-line description of what just happened and answers with a reaction and a muscle stiffness. The joint motors then carry out that reaction in the physics simulation. The body never plays an animation clip: every frame of its motion comes from the rigid-body simulation.

A decision model is different from a chatbot-style language model: it does not write text. It reads the description once and gives a probability to each answer in a fixed list, which makes it fast enough to run inside a game loop.

Below, the same falling box (14 kg) meets two different decisions. On the left the character braces, and the box hits its head. On the right it curls up, and the box misses.

![Same falling box, brace on the left, curl up on the right](Docs/media/s1_same_event_two_decisions.gif)

Here the character sits on a bench while four different objects fall toward it. The rule-based selector picks the reactions: protect the head, dodge sideways, brace, and dodge again.

![Scenario 1, bench](Docs/media/s1_bench_rules.gif)

Here the character stands while four balls are thrown at it. The reactions are a recovery step, a reach for the railing (the character cannot hold on to it yet, so it falls), covering the head, and curling up.

![Scenario 2, standing](Docs/media/s2_standing_rules.gif)

The panel at the top of each clip shows what the decision layer saw and did: the sentence describing the event, the chosen reaction and its probability, how long the decision took, and how stiff the muscles are.

## How it works

Each event passes through six stages, from the physics to the decision and back to the physics.

1. The body is a humanoid made of 15 capsules and boxes, weighing 70 kg in total with a human mass distribution. Every joint has a motor that pulls it toward a target angle. The motor stiffness can be raised (rigid) or lowered (limp) separately for four regions: legs, torso, arms and neck. A small balance controller keeps the centre of mass over the feet when standing, or over the seat when sitting.
2. The simulation watches for events: a collision impulse on a body part, an object whose flight path will reach the body within the next second, a foot striking an obstacle, or both feet leaving the ground.
3. Each event is turned into one English sentence built from fixed templates, for example: *"Character is sitting on a bench. A heavy object (about 12 kg) is falling toward the head, about 0.4 s until impact. Open space to the left and to the right."*
4. As soon as an event is sensed, every muscle tenses briefly. This reflex happens before any decision arrives, in the same way that a person flinches before they have time to think.
5. The sentence goes to a decision selector. It picks one of seven reactions (brace, protect head, stagger and recover, dodge sideways, fall and curl up, go limp, grab a support), a muscle stiffness, and whether to recover afterwards. The reaction starts only when the answer arrives, so a slow model costs the character reaction time, as it would in a game. If no answer comes within 200 ms, a set of hand-written rules decides instead.
6. The chosen reaction sets new joint targets and stiffness, and the physics produces the motion. Two seconds later, if the decision asked for recovery and the character is still upright, it returns to sitting or standing.

I compared four selectors:

| Selector | What it is |
|---|---|
| Fixed | Always the same reaction (brace). The baseline. |
| Rules | Hand-written if/else on the numbers behind the sentence (object mass, time to impact, impulse, free space). It never reads the text. |
| Laya | Convai's Laya, an open-weights decision model (421M parameters, built on a ModernBERT-large encoder). It answers typed questions (pick one, score, yes/no) in one forward pass and never generates text. Asked three questions per event: which reaction, how stiff, recover or not. |
| GLiClass | Knowledgator's zero-shot text classifier, also built on a ModernBERT encoder. Picks the reaction only; stiffness comes from the rules. |

Both models run on the GPU through a local server. Neither was fine-tuned: they are used zero-shot, exactly as released.

## Findings

Each scenario ran 30 random events, and every selector faced the same 30 events, with the same object, mass and throw. Whether the head is hit or the character falls depends as much on how well each reaction is built as on which one is chosen, so those outcomes are not used here to rank the selectors. The full outcome tables are in `Results/summary.md`.

### What the models chose

| Selector | Sitting, object falling | Standing, ball thrown |
|---|---|---|
| Rules | dodge 13, protect head 10, brace 7 | stagger and recover 12, grab support 5, protect head 4, curl up 4, brace 3 |
| Laya | curl up 30 | brace 12, curl up 9, go limp 5, dodge 1, stagger and recover 1 |
| GLiClass | dodge 30 | dodge 20, grab support 5, brace 3 |

Two standing trials recorded no decision, so those rows add up to 28.

The models changed their answers between the two scenes. Sitting on the bench, each gave the same answer every time: Laya always curled up and GLiClass always dodged. Standing, both spread their answers over three to five reactions. Within a scene, though, the choice did not follow the details of the event, such as the object's weight, the time left before impact or where it would land. The rules, which read only those numbers, varied far more. On twelve hand-written test sentences, Laya gave an acceptable answer for 5 and GLiClass for 4.

This matches Laya's model card, which presents the base checkpoint as a starting point for fine-tuning rather than a zero-shot decision engine. On Convai's own benchmark the base checkpoint scores 0.362, against 0.318 for random guessing, and a fine-tuned checkpoint scores 0.766. Laya also favoured whichever option was listed first (10 of 12 test sentences). Every result here uses the fix its documentation suggests, asking once for each ordering of the options and averaging, which adds about 18 ms.

### Decision time and memory

| Selector | Size | GPU memory | Median time | 95th percentile | On a CPU |
|---|---|---|---|---|---|
| Rules | a few if/else lines | none | under 0.1 ms | under 0.1 ms | under 0.1 ms |
| GLiClass | about 150M parameters | 0.6 GB | 15 ms | 17 ms | about 44 ms |
| Laya | 421M parameters | 1.7 GB | 47 ms | 60 ms | about 1.8 s |

Times run from the moment of the event until Unity has the answer, including the round trip to the local server on an RTX 4070 SUPER. GPU memory is each model's peak while answering. The physics steps every 10 ms, so a Laya answer arrives about five steps after the event, well inside the 200 ms budget. Only one of 116 model decisions missed the budget, and the rules took over for that event. On a CPU, Laya would miss the budget every time, while GLiClass would still fit.

### What the results show

The pipeline works end to end and in real time: the physics senses an event, the event becomes a sentence, a model decides within 50 ms on a GPU, and the simulated body carries out the decision. The GIF at the top shows why the decision matters, since the same falling box ends differently depending on the reaction.

Used zero-shot, however, the decision models do not use the details of the event. Their answer depends mostly on the scene, so in practice they behave like one fixed reaction per scene. They are also expensive for what they do. Laya needs 1.7 GB of GPU memory and 47 ms to reach a choice that the rules reach in under 0.1 ms without a GPU.

### How it could be improved

- Fine-tune the decision model on examples labelled by the simulation. For each training event, try every reaction in the physics and keep the one with the best outcome as the label. Laya's own benchmark shows the size of the gain fine-tuning can bring.
- Use a much smaller model. Before the event becomes a sentence, it is already a handful of numbers (mass, speed, time to impact, where it will hit, free space around the body). A small policy trained with reinforcement learning, or a small classifier, could read those numbers directly. It would need a fraction of the memory and time, and it could run on a CPU.
- Improve the reactions themselves. "Protect head" raises the arms, but a heavy box falls straight through them. "Grab support" reaches for the railing but cannot hold on to it. There is no reaction for getting up after a fall.

## Next step: from choosing reactions to generating them

At present the decision model chooses from a fixed list of seven reactions. Physics plays out every reaction, but the list itself works like an animation library: someone must design each reaction in advance.

The next step is for the model to output the numbers that drive the body instead of a name from a list: stiffness for each body part, where each hand should go, where to place the next foot, and how fast to recover. The physics would turn those numbers into motion that nobody recorded. The flinch would lean a little to the left because the box is a little to the left, and a character hit on the leg would limp because that leg has been told to stay weak. A small reinforcement-learning policy fits this well, since its output is already a set of numbers. Reactions should also be layered on a base motion that keeps going, such as walking on uneven ground, so the arms protect the head while the legs keep walking.

## Limitations

- There are only 30 events per scenario, so differences of a few trials are within noise.
- Each object triggers only one decision, and the prediction of incoming objects assumes the body stays still.
- The ankle balance withstands a firm push (about 15 N·s to the chest). The stepping reaction adds little beyond that.
- Running the experiment without the editor (`Unity.exe -batchmode`) gives invalid results, because the physics engine has not finished setting up a newly built body when the body is placed in its start pose. The experiment is run from inside the editor instead.
- Walking was planned but not built.

## How it is built

| Part | Choice | Why |
|---|---|---|
| Joints | Unity ArticulationBody, spherical joints at the spine, neck, shoulders, hips and ankles (ankle yaw locked), hinge joints at the knees and elbows | It solves the whole body as one chain, so long chains do not stretch or jitter under stiff motors, which chained ConfigurableJoints do |
| Joint control | PD drives toward target angles; base stiffness 1500 / 1200 / 250 / 150 N·m/rad for legs / torso / arms / neck, damping scaled with the square root of the stiffness multiplier | A behaviour can make a region rigid or limp while keeping the damping ratio constant |
| Reactions | Joint-angle sets defined in code (stand, sit, brace, protect head, curl, dodge, reach, limp), blended over 0.1 to 0.35 s, with seated versions that keep the legs on the bench; stepping is a feedback controller that aims the foot at the capture point | No animation assets |
| Balance | Ankle strategy: ankle pitch and roll offsets from the centre-of-mass error and velocity relative to the feet. Seated: lower-spine lean keeps the upper body over the pelvis. An optional upright torque on the pelvis exists as a labelled crutch and is off in every result | |
| Simulation | Fixed timestep 0.01 s, TGS solver, 16 position and 4 velocity iterations, enhanced determinism | Stable with these stiffnesses; same seed gives the same event for every selector |
| Masses | de Leva (1996) segment fractions for 70 kg | |
| Text | Fixed templates, binned numbers (strength: light, moderate, strong, very strong; mass: light, medium-weight, heavy), distances rounded to 0.5 m; every sentence is logged | Same event always gives the same sentence |
| Timing | Reflex stiffness bump ×1.6 fading over 0.15 s; the reaction starts at event time plus measured latency; 200 ms timeout with rules fallback | |
| Decision server | FastAPI on localhost, `POST /decide`; Laya 0.4.12 and GLiClass 0.1.21 with model weights pinned to fixed Hugging Face commits; torch 2.14.1 with CUDA 12.6 | |

## Running it

You need Unity 6000.6.5f1 (Unity 6.6) with URP, and Python 3.11 for the decision server and the analysis scripts.

### Scenes

Open `Assets/Scenes/S1_BenchFallingObject.unity` or `Assets/Scenes/S2_StandingImpact.unity` and press Play. Events then happen one after another, each with a new random seed, and the panel shows every decision. To change the selector, set it on the `ScenarioRunner` object (`fixed`, `rules`, `laya` or `gliclass`). Play mode only runs while the Unity window has focus. The scenes and physics settings are generated from code, through `Ragdoll > Build Scenario Scenes` and `Ragdoll > Apply Physics Project Settings`.

### Decision server

The `laya` and `gliclass` selectors need the decision server:

```bash
cd decision_server
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
powershell -File run_server.ps1
```

The first start downloads both models. `requirements.lock.txt` records the exact environment used for the results. `sanity_check.py` sends the twelve hand-written sentences to the server.

### Experiment

`Ragdoll > Run Experiment` runs both scenarios with all four selectors and writes one row per trial to `Results/trials.csv`. The physics is stepped from a script, faster than real time, with nothing drawn on screen. You can also start the same run from the Unity command-line tool against the open editor:

```bash
unity cmd eval --code "ExperimentBatch.RunWith(\"1,2\", \"fixed,rules,laya,gliclass\", 1, 30, \"Results/trials.csv\"); return 0;"
```

### Summary and figures

```bash
decision_server/.venv/Scripts/python analysis/summarize.py
```

This writes `Results/summary.md` and the figures in `Results/plots/`. The summary has the full tables, including the time each trial took to reach a stable posture and how often the selectors agreed.

### Videos

`Ragdoll > Record Demo` records a scenario to `Recordings/` with Unity Recorder. `analysis/make_gifs.py` turns those recordings into the GIFs shown above.

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scripts/Ragdoll/` | Body builder, joint drives, reaction poses, controller, stepping |
| `Assets/Scripts/Sensing/` | Event detection, sentence templates, event log |
| `Assets/Scripts/Decision/` | Reaction list, fixed and rule-based selectors, HTTP client for the model selectors |
| `Assets/Scripts/Reaction/` | Reflex, request, timeout and fallback, turning a decision into body targets |
| `Assets/Scripts/Scenarios/`, `Assets/Scripts/Experiment/` | Seeded scenarios, trial measurement, CSV rows |
| `Assets/Editor/` | Scene builder, experiment runner, demo recorder |
| `decision_server/` | Model server, prompts, sanity check |
| `analysis/` | Summary tables, figures, GIFs |
| `Results/` | Trial data, summary, figures |
| `Docs/media/` | GIFs used in this README |

Both models are released under the Apache-2.0 licence: Laya by Convai Innovations and GLiClass by Knowledgator.
