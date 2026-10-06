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

The experiment has two scenarios with 30 random events each. Every selector faced exactly the same 30 events, with the same object, mass and throw. The first results showed that each model kept repeating one favourite reaction. To check whether the models added anything beyond that favourite, I added two more fixed baselines, "always curl up" and "always dodge". That makes 360 trials in total.

### 1. Picking the right reaction matters

In scenario 1, the character sits on a bench, and an object weighing 1 to 15 kg falls on it from 0.5 to 2.5 m above its head.

| Selector | Head hit | Reactions it chose |
|---|---|---|
| Fixed (brace) | 80% | brace every time |
| Rules | 27% | dodge 13, protect head 10, brace 7 |
| Laya | 0% | curl up every time |
| GLiClass | 7% | dodge every time |
| Always curl up | 0% | |
| Always dodge | 3% | |

Bracing upright leaves the head where the object is going, and the head was hit in 24 of 30 trials. Curling up moves the head down and forward, out of the object's path, and the head was never hit. Leaning sideways works almost as well. Faced with identical events, one reaction leads to a head hit most of the time and another almost never does. This is the case for having a decision layer at all.

### 2. Without training, the models did not read the events

Laya chose "curl up" for all 30 bench events, and GLiClass chose "dodge" for all 30, whatever the sentence said about the object, its weight or the time left. Their strong bench results come from a favourite reaction that happens to suit a seated character. Laya's numbers are the same as "always curl up", and GLiClass's are the same as "always dodge". In the standing scenario both models varied their choices more, but I could not link the variation to anything in the event.

A separate check agrees. I wrote twelve event sentences by hand and listed the reactions I would accept for each. Laya gave an acceptable answer for 5 of them and GLiClass for 4, again mostly by repeating a favourite. This matches Laya's model card, which describes the base checkpoint as a starting point for fine-tuning rather than a zero-shot decision engine. On Convai's own benchmark the base checkpoint scores 0.362, against 0.318 for random guessing, while a fine-tuned checkpoint scores 0.766. Laya also showed the position bias its documentation warns about. When asked in the plain way, it picked whichever reaction was listed first in 10 of 12 cases. All results in this README use the documented fix, which asks once for each ordering of the options and averages the answers. The fix adds about 18 ms to each decision.

Used zero-shot, then, neither model makes an informed decision. A model that reads the event would need training on examples, and the simulation can produce those examples itself (see the section on next steps).

### 3. Rules are only as good as the reactions they call

The rules made sensible choices. For example, they protected the head when an object was heading for it. Even so, they scored 27% head hits, while always curling up scored 0%. The weak point was the reaction itself. "Protect head" places the arms over the head, and a heavy box falls straight through them: in the 10 trials where the rules picked it, the head was still hit 5 times. A better decision cannot rescue a reaction that does not work, so the reactions need testing as carefully as the selector.

### 4. When the hit itself is the event, the choice decides whether the character falls

In scenario 2, the character stands, and a ball weighing 1 to 4 kg is thrown at a random body part from a random direction, at up to 25 m/s.

| Selector | Head hit | Fell (any reason) | Fell by accident | Still upright at the end |
|---|---|---|---|---|
| Fixed (brace) | 13% | 10% | 10% | 90% |
| Rules | 13% | 23% | 10% | 77% |
| Laya | 13% | 50% | 3% | 50% |
| GLiClass | 13% | 13% | 13% | 87% |
| Always curl up | 20% | 93% | 0% | 7% |
| Always dodge | 17% | 13% | 13% | 87% |

Most balls arrive too fast to be seen coming, so the decision is made after the ball has already struck. This is why the head-hit rate is nearly the same for every selector: a blow to the head has already happened by the time any selector decides. The choice changes what happens next. "Fell by accident" counts falls where the chosen reaction was meant to keep the character standing. Bracing kept the character upright in 90% of trials. Laya went down in half of its trials, but 14 of those 15 falls were its own choice to curl up or go limp, and it lost its balance by accident only once. Whether going down on purpose after a hard hit is good or bad depends on what the character is meant to do, and this experiment does not settle that question.

### 5. Fast enough on a GPU, too slow on a CPU

| Selector | Median decision time | 95th percentile |
|---|---|---|
| Rules, fixed | under 0.1 ms | under 0.1 ms |
| GLiClass | 15 ms | 17 ms |
| Laya | 47 ms | 60 ms |

These times run from the moment of the event until Unity has the answer, including the round trip to the local server (an RTX 4070 SUPER). The physics steps every 10 ms, so a Laya decision arrives about five steps after the event, well inside the 200 ms budget. Of 116 model decisions, only one (from Laya) missed the budget, and the rules took over for that event. On a CPU, Laya took about 1.8 s per decision and would miss the budget every time. GLiClass took about 44 ms on the CPU.

### 6. The body itself

The simulated body stands and sits stably, and it moves between reactions smoothly, without snapping. Standing still, with only the ankle balance active, it withstands a 15 N·s shove to the chest from any direction, which is roughly a firm push. It starts to fall at about 25 N·s, which is near the physical limit for a body that cannot take a step. When seated, the curled-up posture withstands harder side pushes than the upright one. The stepping reaction, which aims a recovery step at the point the body is falling toward, helps only a little. At 25 N·s it saved one more push direction out of four than doing nothing, and at 40 N·s and above it saved none.

### Conclusion

The whole pipeline runs in real time. The physics senses an event, the event is described in words, a local model decides within 50 ms, and a simulated body carries out the decision. The decision clearly matters: with the same falling box, one reaction gets the head hit and another does not. Used without training, however, the two models do not make that decision well. Each repeats a favourite answer that happens to be right in one scenario and wrong in the other. The speed and the connections between the parts already work well enough. What limits the system now is how the model is trained and how good its reactions are.

## Next step: from choosing reactions to generating them

At present the decision model chooses from a fixed list of seven reactions. Physics plays out every reaction, but the list itself works like an animation library: someone must design each reaction in advance, and the model can only pick among them.

The natural next step is for the model to output the numbers that drive the body, instead of a name from a list. These would include stiffness for each body part, where each hand should go, where to place the next foot, how far to lower the body, how long to hold, and how fast to recover. The model would produce a new set of numbers for each event and its surroundings, and the physics would turn them into motion that nobody recorded or designed. The flinch would lean a little to the left because the box is a little to the left. A character hit on the leg would limp because that leg has been told to stay weak.

Three further changes point the same way:

- A base motion that keeps going. The character should already be doing something, such as standing, walking, or walking on uneven ground, and reactions should be layered on top. The arms would protect the head while the legs keep walking, and then the reaction would fade and the walk would carry on. A walking controller in the style of SIMBICON could provide this base.
- Training data from the simulation. For each training event, the simulation would try every candidate reaction, keep the one with the best outcome (head not hit, still standing), and use it as the label. The physics labels the data, and the test events are kept separate.
- More kinds of event, such as the ground shifting underfoot or tripping while walking.

## Limitations

- Both models were used zero-shot, and they did not read the events (finding 2). I did not tune the prompt wording to raise their scores.
- There are only 30 events per scenario. Differences of a few percentage points, such as 7% against 3%, are within noise. Large differences, such as 80% against 0%, are not.
- "Protect head" does not stop heavy objects (finding 3). "Grab support" reaches for the railing but cannot hold on to it. There is no get-up reaction, so "recover" only works while the character is still upright. When seated, "stagger and recover" is replaced by a seated brace, because there is no step to take from a bench.
- The stepping reaction only handles small pushes (finding 6).
- Each object triggers only one decision. If a ball is seen coming, its later impact does not trigger a second decision, even if it hits harder than expected.
- The prediction of incoming objects assumes the body stays still, and it reports the first body part on the object's path. A ball aimed at the head can therefore be reported as heading for the shoulder.
- With the rule-based and fixed selectors the results are deterministic: rerunning every trial gave identical rows. With the models, a few trials differed by millimetres, because each reaction starts after the measured wall-clock latency, which varies slightly from run to run.
- Running the experiment from the command line without the editor (`Unity.exe -batchmode`) gives invalid results. In that mode, the physics engine has not finished setting up a newly built body when the body is placed in its start pose. The experiment is therefore run from inside the editor (see below).
- Walking, planned as scenario 3, was not built.

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
