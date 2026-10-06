# Decision-model-driven active ragdoll

A physically simulated character reacts to surprise events (a box falling on it, a ball hitting it), and a small local language model decides how it reacts. The model reads a one-line description of what just happened, picks a reaction and how tense the muscles should be, and the physics carries the reaction out through the joint motors. No animation clip is ever played on the body; every frame of motion comes from the rigid-body simulation.

The idea goes back to Faloutsos, van de Panne and Terzopoulos, *Composable Controllers for Physics-Based Character Animation* (SIGGRAPH 2001), where a supervisor decides which specialist controller takes over the body. Here that supervisor is a decision model that reads text.

The same event, two decisions: a 14 kg box falls toward a seated character.

![Same falling box, brace on the left, curl up on the right](Docs/media/s1_same_event_two_decisions.gif)

The character sitting on a bench, four different falling objects, reactions chosen by the rule-based selector (protect the head, dodge sideways, brace, dodge):

![Scenario 1, bench](Docs/media/s1_bench_rules.gif)

The character standing, four balls thrown at it (step to recover; reach for a railing, which it cannot hold on to yet, so it falls; cover the head; curl up):

![Scenario 2, standing](Docs/media/s2_standing_rules.gif)

The panel at the top shows what the decision layer saw and did: the event sentence, the chosen reaction with its probability, the time the decision took, and the stiffness the muscles run at.

## How it works

1. **The body.** A 15-part humanoid built from capsules and boxes (70 kg, human mass distribution). Every joint is a motor that pulls toward a target angle, with a stiffness that can be turned up (rigid) or down (limp) per body region: legs, torso, arms, neck. A small balance controller keeps the centre of mass over the feet, or over the seat when sitting.
2. **Sensing.** The simulation watches for events: a collision impulse on a body part, an object whose flight path will hit the body within the next second, a foot hitting an obstacle, both feet leaving the ground.
3. **Describing.** The event becomes one English sentence from fixed templates, for example: *"Character is sitting on a bench. A heavy object (about 12 kg) is falling toward the head, about 0.4 s until impact. Open space to the left and to the right."*
4. **A reflex first.** The moment an event is sensed, every muscle tenses briefly. The decision has not arrived yet; a person flinches before they think.
5. **Deciding.** The sentence goes to a decision selector, which picks one of seven reactions (brace, protect head, stagger and recover, dodge sideways, fall and curl up, go limp, grab a support) plus muscle stiffness and whether to recover afterwards. The reaction starts as soon as the answer arrives, so a slow model costs the character reaction time, just as it would in a real game. If no answer comes within 200 ms, hand-written rules decide instead.
6. **Acting.** The chosen reaction sets new joint targets and stiffness; the physics produces the motion. After two seconds, if the decision said to recover and the character is still up, it returns to sitting or standing.

Four selectors were compared:

| Selector | What it is |
|---|---|
| Fixed | Always the same reaction (brace). The baseline. |
| Rules | Hand-written if/else on the numbers behind the sentence (object mass, time to impact, impulse, free space). It never reads the text. |
| Laya | Convai's Laya, an open-weights model that answers typed questions (pick one, score, yes/no) in one forward pass. Asked three questions per event: which reaction, how stiff, recover or not. |
| GLiClass | Knowledgator's zero-shot text classifier. Picks the reaction only; stiffness comes from the rules. |

Both language models run unchanged (zero-shot) on a local server, on the GPU.

## Findings

The experiment: two scenarios, 30 random events each, and every selector faced exactly the same 30 events (same object, same mass, same throw). After the four selectors above turned out to repeat one favourite reaction, I added two more fixed baselines, "always curl up" and "always dodge", to see whether the models add anything beyond their favourite. 360 trials in all.

### 1. Picking the right reaction matters

Scenario 1: the character sits on a bench and an object of 1 to 15 kg falls on it from 0.5 to 2.5 m above the head.

| Selector | Head hit | Reactions it chose |
|---|---|---|
| Fixed (brace) | 80% | brace every time |
| Rules | 27% | dodge 13, protect head 10, brace 7 |
| Laya | 0% | curl up every time |
| GLiClass | 7% | dodge every time |
| Always curl up | 0% | |
| Always dodge | 3% | |

Bracing upright leaves the head where the object is going, and it was hit in 24 of 30 trials. Curling up moves the head down and forward, out of the falling path, and it was never hit. Leaning sideways works almost as well. On the same events, the choice of reaction is the difference between a head hit most of the time and almost never. That is the case for having a decision layer at all.

### 2. The language models did not read the events

Laya chose "curl up" for all 30 bench events and GLiClass chose "dodge" for all 30, whatever the sentence said about the object, its weight or the time left. Their excellent bench results come from that one favourite reaction happening to suit a seated character: Laya's numbers equal "always curl up", and GLiClass's equal "always dodge". In the standing scenario both varied their choices more, but not in a way I could tie to the event.

A separate check points the same way. On twelve hand-written event sentences with the reactions I would accept, Laya gave an acceptable answer for 5 and GLiClass for 4, again mostly by repeating a favourite. Laya's own documentation says its base model is near chance on this kind of task without fine-tuning, and this matches. Laya also showed the position bias its documentation warns about: asked in the plain way, it picked whichever reaction was listed first in 10 of 12 cases. Every result here uses the documented fix (asking once per ordering of the options and averaging), which costs about 18 ms extra.

So, used zero-shot, neither model is making an informed decision. A model that reads the event would need training on examples, and the simulation can produce those examples itself (see the last section).

### 3. Rules are only as good as the reactions they call

The rules made sensible choices, protecting the head when an object was heading for it, yet scored 27% head hits against 0% for always curling. The reason is the reaction, not the choice: "protect head" puts the arms over the head, and a heavy box falls through them; the head was still hit in 5 of the 10 trials where the rules picked it. Better decisions cannot fix a reaction that does not work, so the library of reactions has to be tested as carefully as the selector.

### 4. When the hit is the event, the choice decides the fall, not the injury

Scenario 2: the character stands and a 1 to 4 kg ball is thrown at a random body part from a random direction, at up to 25 m/s.

| Selector | Head hit | Fell (any reason) | Fell by accident | Still upright at the end |
|---|---|---|---|---|
| Fixed (brace) | 13% | 10% | 10% | 90% |
| Rules | 13% | 23% | 10% | 77% |
| Laya | 13% | 50% | 3% | 50% |
| GLiClass | 13% | 13% | 13% | 87% |
| Always curl up | 20% | 93% | 0% | 7% |
| Always dodge | 17% | 13% | 13% | 87% |

Most balls arrive too fast to see coming, so the decision is made after the ball has already hit. Head-hit rates are therefore nearly the same for every selector: a hit to the head has happened before anyone could decide. What the choice changes is what happens next. "Fell by accident" counts falls where the chosen reaction was meant to stay standing. Bracing kept 90% of trials upright. Laya went down in half the trials, but almost all of those were its own choice to curl up or go limp; it lost its balance by accident only once. Whether choosing to go down after a hard hit is good or bad depends on what one wants the character to do, which this experiment does not settle.

### 5. Fast enough on a GPU, not on a CPU

| Selector | Median decision time | 95th percentile |
|---|---|---|
| Rules, fixed | under 0.1 ms | under 0.1 ms |
| GLiClass | 15 ms | 17 ms |
| Laya | 47 ms | 60 ms |

Times are measured from the moment of the event until Unity has the answer, including the trip to the local server (RTX 4070 SUPER). Physics runs every 10 ms, so a Laya decision arrives about five physics steps after the event, well inside the 200 ms budget. Of 116 model decisions, one (from Laya) missed the budget and the rules took over. On a CPU, Laya took about 1.8 s per decision and would miss the budget every time, while GLiClass took about 44 ms.

### 6. The body itself

The simulated body stands and sits stably and blends between reactions without snapping. Standing still, with only the ankle balance, it takes a 15 N·s shove to the chest from any direction (roughly a firm push) and starts to fall at about 25 N·s; a body that cannot step has a physical limit near there. Sitting, curled up, it takes harder side pushes than sitting upright. The stepping reaction (a recovery step aimed at where the body is falling) helps only a little: at 25 N·s it saved one more push direction out of four than not reacting, and at 40 N·s and above it did not save any.

### Conclusion

The pipeline works end to end and in real time: events are sensed from the physics, described in words, decided on by a local model within 50 ms, and carried out by a physically simulated body. The decision clearly matters; on the same falling box, one reaction gets the head hit and another does not. But the two language models, used without training, do not yet make that decision; they repeat a favourite answer that happens to be right in one scenario and wrong in another. The limits that showed up are the training of the model and the quality of the reactions it can call, not the speed or the plumbing.

## From choosing reactions to generating them

Today the decision model chooses from a fixed list of seven reactions. Even though every reaction is played out by physics, the list itself works like an animation library: someone has to design each reaction in advance, and the model can only pick among them.

The natural next step is for the decision model to output the numbers that drive the body directly, instead of a name from a list: stiffness per body part, where each hand should go, where to put the next foot, how much to lower the body, how long to hold, and how fast to recover. Given the event and the surroundings, it would produce a new set of numbers each time, and the physics would turn them into motion that nobody recorded or designed: a flinch that is a bit to the left because the box is a bit to the left, or a limp because the hit leg has been told to stay weak. In the same direction:

- **A base motion that keeps going.** The character should be doing something (standing, walking, walking on uneven ground) and reactions should be layered on top of it: the arms protect the head while the legs keep walking, then the reaction fades out and the walk carries on. A walking controller in the spirit of SIMBICON would provide the base.
- **Training data from the simulation.** For each training event, try the candidate reactions in the physics, keep the one that turned out best (head not hit, still standing), and train the model on those. The physics labels the data, and the test events stay separate.
- **More events.** The ground shifting under the feet, tripping while walking.

## Limitations

- The language models were used zero-shot, and they did not read the events (finding 2). No prompt wording was tuned to raise their scores.
- Thirty events per scenario: differences of a few percentage points, such as 7% against 3%, are within noise. The large differences, such as 80% against 0%, are not.
- "Protect head" does not stop heavy objects (finding 3). "Grab support" reaches for a railing but never holds on to it. There is no get-up reaction, so "recover" only works while the character is still upright. Seated "stagger and recover" is replaced by a seated brace, since there is no step to take from a bench.
- The stepping reaction only works for small pushes (finding 6).
- Each object gets one decision. If a ball is seen coming, its later impact does not trigger a second decision, even if it hits harder than expected.
- The prediction of incoming objects assumes the body stands still and reports the first body part on the object's path, so a ball aimed at the head can be reported as heading for the shoulder.
- Results are deterministic for the rule-based and fixed selectors: rerunning all trials gave identical rows. With the models, a few trials differed by millimetres, because the reaction starts after the measured wall-clock latency, which varies slightly.
- Running the experiment from the command line without the editor (`Unity.exe -batchmode`) produces invalid results: in that mode the physics engine has not finished setting up a newly built body when it is placed in its start pose. The experiment is run from inside the editor instead (see below).
- Walking (scenario 3 in the original plan) was not built.

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

Unity 6000.6.5f1 (Unity 6.6) with URP. Python 3.11 for the decision server and the analysis scripts.

**Scenes.** Open `Assets/Scenes/S1_BenchFallingObject.unity` or `Assets/Scenes/S2_StandingImpact.unity` and press Play. Events happen one after another with a new random seed each time, and the panel shows each decision. Choose the selector on the `ScenarioRunner` object (`fixed`, `rules`, `laya`, `gliclass`). Play mode only runs while the Unity window has focus. The scenes and physics settings are generated from code: `Ragdoll > Build Scenario Scenes` and `Ragdoll > Apply Physics Project Settings`.

**Decision server** (needed for `laya` and `gliclass`):

```bash
cd decision_server
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
powershell -File run_server.ps1
```

The first start downloads the two models. `requirements.lock.txt` is the exact environment used. `sanity_check.py` runs the twelve hand-written sentences against the server.

**Experiment.** `Ragdoll > Run Experiment` runs both scenarios with all four selectors and writes `Results/trials.csv`, one row per trial. Physics is stepped from script, faster than real time, with nothing drawn. The same run can be started from the Unity command-line tool against the open editor:

```bash
unity cmd eval --code "ExperimentBatch.RunWith(\"1,2\", \"fixed,rules,laya,gliclass\", 1, 30, \"Results/trials.csv\"); return 0;"
```

**Summary and figures.**

```bash
decision_server/.venv/Scripts/python analysis/summarize.py
```

This writes `Results/summary.md` (full tables, including time to a stable posture and agreement between selectors) and `Results/plots/`.

**Videos.** `Ragdoll > Record Demo` records a scenario with Unity Recorder to `Recordings/`. `analysis/make_gifs.py` turns the recordings into the GIFs above.

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
| `Docs/media/` | GIFs and stills used here |

The models: Laya by Convai Innovations (Apache-2.0) and GLiClass by Knowledgator (Apache-2.0).
