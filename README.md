# Decision-model-driven active ragdoll

A Unity prototype of a physics-simulated character whose reaction to events (impacts, falling objects, trips) is picked by a small local decision model. The selector idea follows van de Panne's "Composable Controllers for Physics-Based Character Animation" (SIGGRAPH 2001): a selector chooses which specialist controller takes over. Here the selector is a decision model that reads a short text description of the event.

The body is simulated at all times. No kinematic animation plays on it. The decision layer picks a behaviour and its parameters once per event; the physics layer turns that into joint drive targets every physics step.

## Status

| Milestone | State |
|---|---|
| 1. Ragdoll that stands and sits with pose targets | done, see results and limitations below |
| 2. Event sensing, text serialiser, rules selector | done, see below |
| 3. Decision server (Laya, GLiClass) | done, see below |
| 4. Experiment harness and results | done, see below (command-line `-batchmode` does not work; the in-editor runner does) |

## Setup

- Unity 6000.6.5f1 (Unity 6.6), URP 17.6. No extra packages are needed for milestone 1.
- Scenes:
  - `Assets/Scenes/M1_StandSit.unity`: one standing and one sitting character, poses by keyboard.
  - `Assets/Scenes/S1_BenchFallingObject.unity`: scenario 1, sitting on a bench, object dropped from above.
  - `Assets/Scenes/S2_StandingImpact.unity`: scenario 2, standing, ball thrown at the body.
- All scenes and the physics project settings are generated from code. To rebuild them, use the editor menu: `Ragdoll > Build M1 Stand-Sit Scene`, `Ragdoll > Build Scenario Scenes` and `Ragdoll > Apply Physics Project Settings`.
- In the scenario scenes, N starts the next seed and R reruns the current one. The selector is set on the `ScenarioRunner` object: `fixed`, `rules`, `laya`, `gliclass`, or the test selectors `slow:<ms>`.
- The `laya` and `gliclass` selectors need the decision server (Python 3.11):

```bash
cd decision_server
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
powershell -File run_server.ps1
```

The first start downloads the two pinned checkpoints from Hugging Face. `requirements.lock.txt` is the full `pip freeze` of the environment used for the results.

Keys in play mode (shown in the on-screen panel):

| Key | Action |
|---|---|
| 1 to 7 | stand, sit, brace, protect_head, curl, stagger_step, limp (a seated character gets the seated brace and protect_head) |
| Tab | select the other character |
| P | push the chest with 120 N·s, cycling direction |
| B | toggle balance |
| U | toggle the upright-assist crutch (off by default) |

Play mode only advances while the Unity window has focus. The automated tests below step physics from script instead, so they run without focus and faster than real time.

## Architecture (milestone 1)

| File | Role |
|---|---|
| `Assets/Scripts/Ragdoll/RagdollBuilder.cs` | Builds the 15-link body from primitives as one ArticulationBody tree. |
| `Assets/Scripts/Ragdoll/RagdollJoint.cs` | One driven joint: limits, PD gains, writing drive targets, reading angles. |
| `Assets/Scripts/Ragdoll/PoseLibrary.cs` | Target poses as joint-angle sets in code. |
| `Assets/Scripts/Ragdoll/ActiveRagdollController.cs` | Per-step control: pose blending, per-group stiffness, balance, optional crutch. |
| `Assets/Scripts/Ragdoll/StepRecovery.cs` | Feedback stepping for `stagger_recover` (partial, see limitations). |
| `Assets/Scripts/Sim/SimulationSettings.cs` | Timestep and solver settings in one place. |
| `Assets/Scripts/Debug/RagdollDebugOverlay.cs` | On-screen panel and keyboard controls. |
| `Assets/Scripts/Debug/PoseStabilityTest.cs` | Scripted pose and push tests used for the numbers below. |
| `Assets/Scripts/Sim/SimDriver.cs` | One clock that steps sensors, reaction layer and ragdoll in order (milestone 2). |
| `Assets/Scripts/Sensing/` | Event sensing, text serialiser, event log (milestone 2). |
| `Assets/Scripts/Decision/` | Behaviour list, decision type, fixed and rules selectors (milestone 2). |
| `Assets/Scripts/Reaction/` | Reflex, request, timeout and fallback, behaviour execution (milestone 2). |
| `Assets/Scripts/Scenarios/ScenarioRunner.cs` | Seeded scenario 1 and 2 trials (milestone 2). |
| `Assets/Editor/RagdollSceneBuilder.cs` | Builds the scenes and writes physics project settings. |

Each physics step the controller:

1. blends the current target pose toward the requested pose (smoothstep over a set time, never a snap) and ramps each body group's stiffness toward its requested value;
2. adds balance corrections (or hands the legs to the step controller during a stagger);
3. writes targets and gains into the joint drives.

## Design choices

### Body

15 links: pelvis, abdomen, chest, head, upper arms, forearms (hand merged in), thighs, shins, feet. Height is about 1.75 m. Masses follow the de Leva (1996) segment fractions for a 70 kg adult: pelvis 7.82, abdomen 11.43, chest 11.17, head 4.86, upper arm 1.90, forearm and hand 1.56, thigh 9.91, shin 3.03, foot 0.96 kg (total 70.0 kg). Inertia is computed by Unity from the colliders, scaled to these masses.

Collisions are disabled only between links that touch in the rest pose (parent and child, plus neighbours such as pelvis and thigh). Other self-collisions stay on, so an arm can still hit the head or torso.

### Joints: ArticulationBody

I used ArticulationBody rather than ConfigurableJoint. ArticulationBody solves the whole tree in reduced coordinates (Featherstone), so a long chain like foot to head does not stretch or drift at the joints, which chained ConfigurableJoints do under heavy load and stiff drives. Each joint's drive is the PD controller: torque = k (target − angle) − d (angular velocity).

Joint types: spherical at the spine (two joints), neck, shoulders and hips; revolute at elbows and knees; spherical at the ankles with yaw locked (pitch and roll only).

Every link is built with identity rotation and the joint anchors are left at identity, so in the rest pose the joint axes line up with the world: x is pitch, y is yaw, z is roll. The signs were checked in the editor with the pelvis pinned:

| Joint | Positive value means |
|---|---|
| Hip x | thigh swings back (negative = forward) |
| Knee x | knee bends |
| Ankle x | toes point down |
| Spine, neck x | bend forward |
| Shoulder x | arm swings back (negative = raise forward) |
| Shoulder z (right side) | arm raises sideways; mirrored on the left |
| Elbow x | negative bends the elbow |

### Stiffness per body group

Base gains at multiplier 1:

| Group | Stiffness (N·m/rad) | Damping (N·m·s/rad) | Torque limit (N·m) |
|---|---|---|---|
| Legs | 1500 | 60 | 450 |
| Torso | 1200 | 50 | 350 |
| Arms | 250 | 10 | 120 |
| Neck | 150 | 6 | 60 |

A behaviour sets a multiplier per group. Damping scales with the square root of the multiplier, which keeps the damping ratio constant as the body is made stiffer or softer. `limp` uses a multiplier of 0.02.

### Poses

Defined in `PoseLibrary.cs` as joint-angle sets: stand, sit, brace, protect_head, curl, stagger_step, limp. A pose also carries what balance should do with it (keep the COM over the feet, over the seat, or nothing). `PoseLibrary.Seated(pose)` takes the upper body of a pose and the legs of `sit`, so a seated character can brace or cover its head without trying to stand up.

### Balance

Standing uses an ankle strategy. The controller measures the whole-body centre of mass (COM) and its velocity, compares the COM with the midpoint of the two feet in the pelvis heading frame, and adds a PD offset to the ankle pitch and roll targets: 120 deg/m of error plus 25 deg per m/s, capped at 20 degrees. 40% of the ankle pitch correction is also applied at the hips in the opposite sense.

Sitting keeps the upper-body COM over the pelvis by leaning the lower spine (150 deg/m, 20 deg per m/s, capped at 20 degrees).

The upright-assist torque on the pelvis is an external torque that pulls the pelvis upright. It is not physically honest (no real body can push on itself from outside). It is off by default, toggled with U, and any result that uses it is labelled.

### Timestep and solver

| Setting | Value |
|---|---|
| Fixed timestep | 0.01 s |
| Solver | TGS |
| Solver iterations (position / velocity) | 16 / 4 |
| Enhanced determinism | on |
| Maximum allowed timestep | 0.1 s |

0.01 s was stable for every test below, so I did not go smaller.

### Spawning and resets

A trial reset destroys the body and builds a new one, then places it in the start pose. Moving an existing body to a new pose by setting joint positions does not work reliably: PhysX applies the new joint positions only at the next step, contacts from the old configuration are still generated, and the body gets kicked (in one test it blew up to NaN within a second).

## Results (milestone 1)

All numbers come from `PoseStabilityTest`, which steps physics from script at 0.01 s. Each row starts from a fresh body: 1 s to settle in the start pose, then the pose change or push, then 3 s before measuring.

### Holding poses

| Start → pose | Pelvis height (m) | Pelvis tilt (deg) | Mean tracking error (deg) |
|---|---|---|---|
| stand → stand | 0.99 | 3.2 | 0.3 |
| stand → brace | 0.94 | 3.0 | 0.8 |
| stand → protect_head | 0.92 | 2.1 | 1.5 |
| stand → curl | 0.13 | 121 (falls, intended) | 0.3 |
| stand → limp | 0.09 | 90 (falls, intended) | 4.3 |
| sit → sit | 0.58 | 0.2 | 0.2 |
| sit → seated brace | 0.58 | 0.4 | 0.3 |
| sit → seated protect_head | 0.58 | 0.4 | 0.3 |
| sit → curl | 0.57 | 6.9 | 3.2 |
| sit → limp | 0.49 | 120 (slides off the bench) | 8.9 |

Tracking error is the mean gap between commanded and actual joint angles over all driven axes.

### Pushes to the chest

The impulse is applied to the chest. The reaction starts 0.1 s later. "Upright" means the pelvis is above 0.6 m (0.45 m when seated) and tilted less than 45 degrees after 3 s. Directions are the direction of the push: back, forward, right, left.

Standing:

| Reaction | 15 N·s | 25 N·s | 40 N·s | 60 N·s |
|---|---|---|---|---|
| none (stand) | 4/4 | 2/4 | 0/4 | 0/4 |
| brace | 4/4 | 2/4 | 0/4 | 0/4 |
| stagger (step controller) | 4/4 | 3/4 | 0/4 | 0/4 |
| none, with upright-assist crutch | | 4/4 | 3/4 | 0/4 |

Sitting on the bench:

| Reaction | 25 N·s | 40 N·s | 60 N·s | 90 N·s |
|---|---|---|---|---|
| none (sit) | 4/4 | 2/4 | 1/4 | 1/4 |
| curl | 4/4 | 4/4 | 3/4 | 2/4 |

The seated results already show the effect milestone 4 will measure: the choice of reaction changes the outcome (curling lowers the COM and survives harder sideways pushes).

For scale: a pure ankle strategy can absorb a COM speed of roughly foot-length × sqrt(g / COM height) ≈ 0.12 m × 3.1 s⁻¹ ≈ 0.37 m/s. 25 N·s on 70 kg is 0.36 m/s, so the standing thresholds are in the expected range for a body that does not step.

## Milestone 2: event sensing, text, and the rules selector

### One clock for everything

`SimDriver` steps every per-step component in a fixed order, then physics: the sensor first, then the reaction layer, then the ragdoll controller. In play mode its FixedUpdate does this. In scripted runs `SimDriver.Advance` does it and calls `Physics.Simulate` itself, so trials run faster than real time and the editor does not need focus. `SimDriver.Time` is simulated time in both modes.

### Sensing (`Assets/Scripts/Sensing/EventSensor.cs`)

Every event comes from physics:

- Impact: a `LinkContactReporter` on each link forwards collisions. A collision from a moving rigidbody with an impulse of at least 2 N·s is an impact. The strongest link per object per step is reported, once per object. The direction is the object's velocity one step before contact.
- Incoming: each step the sensor looks for moving rigidbodies within 2.5 m of the chest. It steps each one's ballistic path (gravity, no drag) 1 s ahead at 0.01 s and uses `Collider.ClosestPoint` on every body collider to find the first link it would touch. If that is at least 0.2 s away, an incoming event fires with the time to impact. If it is sooner, there is no time to anticipate, and the sensor waits for the impact instead.
- Trip: a foot touching an object marked `TripHazard` while the character is walking (scenario 3, not built yet).
- FootingLost: both feet off the ground for 0.15 s while the pose expects the feet to carry the body.

Each event also carries the character state (sitting, standing, walking, lying), the free distance in four directions (chest-height raycasts, 2 m range), and the nearest `GraspableSupport` within 1 m.

### Text serialiser (`EventSerializer.cs`)

Fixed templates, binned numbers and invariant culture, so the same event always gives the same string. Strength bins: light below 10, moderate below 25, strong below 45, very strong from 45 N·s. Mass bins: light below 2, medium-weight below 6, heavy from 6 kg. Distances are rounded to 0.5 m. Every string goes to `Results/event_log.txt` (git-ignored) together with the trial setup, the decision, timeouts and late answers. Real examples from the runs below:

> Character is sitting on a bench. A heavy object (about 12 kg) is falling toward the head, about 0.4 s until impact. Open space to the left and to the right.

> Character is standing. A moderate impact to the right shoulder from the right. Wall 1.5 m behind. Open space to the left and to the right. A railing is within reach to the right.

> Character is standing. A very strong impact to the right thigh from the right. Wall 1.0 m to the left. Open space to the right.

### Reflex, latency and fallback (`Assets/Scripts/Reaction/ReactionManager.cs`)

1. As soon as an event is sensed, every joint gets a reflex stiffness bump (×1.6, fading out over 0.15 s).
2. The event text is logged and sent to the selector. The request never blocks: the selector calls back whenever it has an answer, possibly from another thread.
3. The behaviour starts at event time plus the selector's measured wall-clock latency, in simulated time. A 150 ms model therefore costs the character 150 ms of reaction time, both in play mode and in scripted runs. While a request is open, scripted stepping holds simulated time at event time plus the wall-clock time spent so far, so a slow selector cannot get a head start.
4. With no answer after 200 ms of wall-clock time, the rules selector answers, and the decision is flagged `TimedOut` and `Fallback`. A late answer is logged and ignored.
5. After 2 s, if the decision said `recover` and the character is still up, it blends back to its start pose.

Each object gets one decision: the impact of an object that was already anticipated does not trigger a second one. There is a 0.5 s cooldown after a decision. The first event and decision of a trial are kept separately, since that is what the experiment will compare.

I checked this with test selectors that answer from a background thread. `slow:150` answered in 150 ms and its behaviour started 150 ms of sim time after the event. `slow:400` timed out at 200 ms, the rules fallback ran, and the timeout was logged.

### Selectors (`Assets/Scripts/Decision/`)

- Fixed (A): always `brace` at full stiffness.
- Rules (B): if/else on the numbers, never on the text. A head-bound object that is heavy (6 kg or more), still at least 0.3 s away, with open space to one side, leads to `dodge_sideways`; other head-bound objects lead to `protect_head`. Seated impacts below 25 N·s brace and harder ones curl. Standing impacts below 15 N·s brace; up to 60 N·s with a railing in reach, grab it; up to 45 N·s stagger; harder ones curl. Trips stagger, and losing footing curls. The thresholds come from the milestone 1 push tests.

### Behaviours (`BehaviourExecutor.cs`)

| Behaviour | Standing | Seated |
|---|---|---|
| brace | brace pose | seated brace (legs stay in sit) |
| protect_head | protect_head pose | seated protect_head |
| stagger_recover | step controller toward the push | seated brace (no step is possible from a bench; logged) |
| dodge_sideways | lean the trunk and shift the hips to the freer side | lean the trunk sideways, near arm out |
| fall_and_curl | curl, no balance | curl |
| go_limp | limp (stiffness ×0.02) | limp |
| grab_support | reach the near arm toward the support | same, legs in sit |

The decision's stiffness (0 to 1) maps to a group multiplier from 0.05 to 1.5. `grab_support` only reaches: no grasp constraint is made. With no support in reach it is a stub that runs `brace` and says so in the log.

### Scenarios (`Assets/Scripts/Scenarios/ScenarioRunner.cs`)

Every random number comes from `System.Random(seed)` in a fixed order, so a seed gives the same event to every selector. Each trial rebuilds the body, waits 1 s, launches, and runs 5 s.

- Scenario 1: object mass 1 to 15 kg (a box sized for a density of 800 kg/m³), dropped 0.5 to 2.5 m above the head with a horizontal offset of up to 0.35 m.
- Scenario 2: a 1 to 4 kg ball thrown from 3 m at a random azimuth toward a random body part, with a nominal impulse of 10 to 80 N·s (speed capped at 25 m/s). A wall appears 40% of the time (left, right or behind, 0.8 to 1.5 m away) and a railing 30% of the time.

### First run, rules selector, 20 seeds each

| | Scenario 1 (bench) | Scenario 2 (standing) |
|---|---|---|
| Event type | 20 incoming | 18 impact, 2 incoming |
| Behaviours chosen | dodge 9, protect_head 6, brace 5 | stagger 9, grab_support 3, protect_head 3, fall_and_curl 3, brace 2 |
| Head hit | 5 | 3 |
| Fell | 0 | 5 |

This is a smoke test with one selector, so it compares nothing. Milestone 4 runs all four selectors on the same seeds.

## Milestone 3: the decision server

### Server (`decision_server/server.py`)

FastAPI on `127.0.0.1:8765` with one decision endpoint, `POST /decide`, plus `GET /health`. The request carries the event text, the allowed behaviours and the model name. The reply carries the chosen behaviour, a probability for every allowed behaviour, stiffness, the recover flag with its probability, the model time in ms, and the device.

| | Value used |
|---|---|
| Python | 3.11.9 |
| torch | 2.14.1+cu126 |
| laya | 0.4.12, checkpoint `convaiinnovations/laya` (English root) at commit `7b928d8` |
| gliclass | 0.1.21, checkpoint `knowledgator/gliclass-modern-base-v3.0` at commit `ac36922` |
| transformers | 5.20.2 |
| Device | CUDA, NVIDIA GeForce RTX 4070 SUPER (12 GB) |

Both libraries were checked before use. Laya loads safetensors and runs no code from the model repo, even though the repo contains `.py` files. GLiClass's config loader can call `trust_remote_code=True`, but only for encoders transformers does not know; this checkpoint's encoder is ModernBERT, which transformers supports, and the repo has no code files.

### Laya

I read the package README before writing code. The API used is `laya.load(repo, device=..., revision=...)` and `agent.predict(text, questions)`, which answers all typed questions in one forward pass. Each event asks three questions:

| Question | Laya type | Wording | Output used |
|---|---|---|---|
| Which behaviour | `choice` | "How should the character's body react to this event to avoid injury?" with one short description per behaviour (`decision_server/behaviours.py`) | probabilities per behaviour |
| Muscle stiffness | `score` | five levels from "completely limp" to "rigid, resisting with full force" | expected level ÷ 4, so 0 to 1 |
| Recover afterwards | `noul` (Laya's yes/no type, returns P(true)) | "After reacting, should the character try to recover its balance or get back up?" | true if P ≥ 0.5 |

Laya's README warns that a choice answer depends on which slot an option is shown in, and offers `option_order` as the fix. The server asks the choice once per rotation of the 7 options (7 rows, still one forward pass) and averages the probabilities. With rotation off, Laya picked `brace`, the first option, for 10 of the 12 sanity cases below.

No few-shot or calibration examples are used for either model. Laya has no few-shot input, only fine-tuning, and I kept GLiClass zero-shot too so the two are compared on the same footing. Laya's own README says the base checkpoints are "near chance on typed-decisions zero-shot" (0.362 against a 0.318 random baseline). The checkpoint also logs a warning that its calibration temperature for choices with 11 or more options is invalid; our choice has 7.

### GLiClass

Behaviour choice only, zero-shot, single-label, with short natural-language labels ("brace for impact", "protect the head with the arms", and so on). In single-label mode the pipeline's normal output keeps only the top label, so the server asks for the hierarchical output, which carries the full softmax. Unity takes stiffness and the recover flag from the rules selector for the same event.

### Unity client (`Assets/Scripts/Decision/HttpModelSelector.cs`)

`System.Net.Http.HttpClient` on the thread pool, not `UnityWebRequest`, so the answer still arrives while a scripted batch run keeps the main thread busy. The first request of a session took 237 ms and timed out, even after a GET warm-up. The client now sends two throwaway `/decide` requests per model before the first trial. If the server is down or replies with an error, the rules selector answers at once and the error is logged.

### Sanity check (`decision_server/sanity_check.py`)

Twelve hand-written event descriptions, each with the behaviours I would accept. They are not taken from the scenario seeds and are never shown to the models as examples. The set only checks whether a model reads the text at all. The expected labels are my own judgement. Full output is in `decision_server/sanity_output*.txt`.

| Model | Device | Acceptable | Choices | Median model time | Median round trip |
|---|---|---|---|---|---|
| Laya, rotated | CUDA | 5/12 | fall_and_curl 6, brace 4, go_limp 1, dodge 1 | 42 ms | 44 ms |
| Laya, not rotated | CUDA | 5/12 | brace 10, fall_and_curl 2 | 25 ms | 26 ms |
| GLiClass | CUDA | 4/12 | dodge_sideways 8, brace 2, grab 1, go_limp 1 | 12 ms | 14 ms |
| Laya, rotated | CPU | 5/12 (same answers) | | 1782 ms | 1784 ms |
| GLiClass | CPU | 4/12 (same answers) | | 44 ms | 46 ms |

What this shows:

- Both models read little of the text. Laya leans to fall_and_curl and GLiClass to dodge_sideways, whatever the event says. Laya's stiffness stayed between 0.39 and 0.59 and its recover answer was "yes" for all 12 cases, so its parameters carry almost no information either.
- On the GPU both fit easily inside the 200 ms timeout. On CPU, Laya with rotation would always time out and the rules fallback would decide every event.

In Unity, through the full pipeline, Laya answered in 43 to 62 ms and GLiClass in 14 to 20 ms per event. In scenario 1 (5 seeds) Laya chose fall_and_curl and GLiClass dodge_sideways every time. In scenario 2 (6 seeds) Laya chose brace 4 times, dodge once and fall_and_curl once, and GLiClass chose dodge_sideways 5 times and brace once.

### Fixes made while wiring this up

- The standing `dodge_sideways` pose toppled within a second even without an impact: the hips leaned with the trunk and the pose had balance off. The hips now shift the other way as a counterweight and the feet balance stays on. It holds (tilt about 5°) and still moves the head 0.15 to 0.25 m sideways.
- First-request timeout, see the Unity client above.

## Milestone 4: experiment and results

### Running it

The experiment runs every scenario × selector × seed and writes one CSV row per trial. Physics is stepped from script inside play mode, faster than real time, with the camera switched off, so nothing is drawn.

- In the editor: menu `Ragdoll > Run Experiment` (scenarios 1 and 2; fixed, rules, laya, gliclass; seeds 1 to 25). Start the decision server first. If it is not reachable, the model selectors are skipped and an error says so.
- From the Unity CLI against the open editor, which is how the results below were produced:

```bash
unity cmd eval --code "ExperimentBatch.RunWith(\"1,2\", \"fixed,rules,laya,gliclass\", 1, 30, \"Results/trials.csv\"); return 0;"
```

  Play mode only advances while the editor ticks. With the Unity window in the background I kept it ticking by sending `EditorApplication.QueuePlayerLoopUpdate()` every 2 s through the same CLI.
- Summary table and plots:

```bash
decision_server/.venv/Scripts/python analysis/summarize.py
```

  This writes `Results/summary.md` and `Results/plots/*.png` (pandas 3.0.6 and matplotlib 3.11.2, installed in the server's venv).

Command-line batch mode (`Unity.exe -batchmode -executeMethod ExperimentBatch.RunFromCommandLine`) does **not** work; see the failure cases.

### What each CSV row holds

`Results/trials.csv` (selectors A to D) and `Results/trials_baselines.csv` (two extra fixed baselines) have these columns: scenario, seed, selector, the trial setup, the first event's kind and text, the chosen behaviour, the probability of each of the 7 behaviours, stiffness, recover flag, wall-clock and server latency, device, timeout and fallback flags, error and note, number of events handled, head hit and peak head impulse, fell, minimum pelvis height, time to stable posture, upright at the end, and back in the start pose.

- Fell: pelvis below 0.6 m (0.45 m seated) or tilted more than 45° at any time after the launch.
- Unintended fall: fell although the chosen behaviour was not `fall_and_curl` or `go_limp`.
- Time to stable: from the first event until the centre of mass first stays below 0.1 m/s for 0.5 s. A body lying still counts as stable.
- Upright at end: not fallen when the trial ends (6 s after the trial starts).

### Design

Every selector saw the same 30 seeds per scenario, so each seed is the same event (same object, same throw) for every selector until the decision is applied. Besides the four selectors in the brief, I added two more fixed baselines, "always fall_and_curl" and "always dodge_sideways". Each model turned out to have a favourite behaviour, and these baselines show whether a model adds anything beyond that favourite.

360 trials in total. The decision server ran on CUDA (RTX 4070 SUPER).

### Results

![Outcomes per selector](Results/plots/outcomes.png)

| Scenario 1, bench (30 seeds) | head hit | chosen behaviours |
|---|---|---|
| A. Fixed, brace | 80% | brace 30 |
| B. Rules | 27% | dodge 13, protect_head 10, brace 7 |
| C. Laya | 0% | fall_and_curl 30 |
| D. GLiClass | 7% | dodge 30 |
| A2. Fixed, curl | 0% | fall_and_curl 30 |
| A3. Fixed, dodge | 3% | dodge 30 |

No seated trial fell in any condition.

| Scenario 2, standing (30 seeds, 2 balls missed) | head hit | fell, any reason | unintended fall | upright at end |
|---|---|---|---|---|
| A. Fixed, brace | 13% | 10% | 10% | 90% |
| B. Rules | 13% | 23% | 10% | 77% |
| C. Laya | 13% | 50% | 3% | 50% |
| D. GLiClass | 13% | 13% | 13% | 87% |
| A2. Fixed, curl | 20% | 93% | 0% | 7% |
| A3. Fixed, dodge | 17% | 13% | 13% | 87% |

![Behaviours chosen](Results/plots/behaviours.png)

![Decision latency](Results/plots/latency.png)

Decision latency (wall clock, Unity to server and back): Laya median 47 ms (p95 60 ms), GLiClass median 15 ms (p95 17 ms). Rules and fixed take microseconds. There was one timeout in 116 model decisions: Laya, in its first scenario 2 trial, answered after the 200 ms limit, and the rules fallback ran instead.

Full tables, including time to stable and agreement with the rules selector: `Results/summary.md`.

### What the results say

1. In scenario 1 the choice of reaction matters a lot. Always bracing let the falling object hit the head in 80% of trials; always curling up let it hit in none. With 30 trials per condition that gap is far larger than chance.
2. The decision models did not choose by reading the event. Laya chose `fall_and_curl` in all 30 bench trials and GLiClass chose `dodge_sideways` in all 30. Their scenario 1 numbers equal the matching fixed baselines (Laya 0% against always-curl 0%; GLiClass 7% against always-dodge 3%). The good result comes from a constant favourite that happens to suit a seated character, not from a decision. In scenario 2 both models varied their choice more, but without a pattern I could tie to the event text.
3. The rules selector did worse than the best fixed policy in scenario 1 (27% against 0%). Its `protect_head` choice still let the head be hit in 5 of 10 trials: the arms over the head do not cover a heavy box. Hand-written rules are only as good as the behaviours they call.
4. In scenario 2 most head hits happen at the moment of impact, before any decision exists, so head-hit rates barely differ (13 to 20%). Falls differ. Bracing kept 90% of trials upright. The rules selector's falls were mostly its own `fall_and_curl` choices for very strong hits (4 of 7). Laya chose `fall_and_curl` or `go_limp` in 14 of 28 trials and went down every time; those are deliberate falls, not losses of balance. Whether that is a good choice depends on what the experiment counts as success, which this prototype does not settle.
5. Both models fit inside the 200 ms budget on a GPU. On CPU, Laya would not (about 1.8 s per decision, see milestone 3).
6. Determinism. Rerunning all 360 trials gave identical rows for the rules and fixed selectors. Two model-selector trials differed by a few millimetres in pelvis height, because a model's decision starts after its measured wall-clock latency, and that varies from run to run.

With 30 seeds per condition, a difference of 7 to 10 percentage points (for example GLiClass 7% against always-dodge 3%) is within noise.

### Demo video

Menu `Ragdoll > Record Demo > Scenario 1 (bench)` or `Scenario 2 (standing)`. It opens the scene, plays 4 seeds back to back in real time with the debug overlay, and records 18 s of the Game view to `Recordings/demo_<scenario>_<selector>_seed<n>.mp4` (1280×720, 30 fps, Unity Recorder 5.1.7). Keep the Unity window focused while it records. The selector is the one set on the scene's `ScenarioRunner`. I checked the output: an 18 s, 540-frame MP4 with the overlay visible. `Recordings/` is git-ignored.

## Limitations and failure cases so far

- **Stagger stepping is weak.** `StepRecovery` aims the swing foot at the capture point and nudges the pelvis upright through the stance hip, in the spirit of SIMBICON (Yin, Loken and van de Panne, 2007). It skips stepping when the capture point is already inside the feet. It helps at 25 N·s (3/4 vs 2/4) but fails at 40 N·s and above in every direction. Traces show two causes. First, the trunk pitches forward during the swing: a full world-frame torso servo on the stance hip kicked the lightly loaded stance foot backward, and the capped version I kept does not hold the trunk. Second, after a step that does capture the fall, the "hold where the legs landed" pose sometimes still tips over. An open-loop version (fixed lift and plant poses) did worse than not reacting at all and was removed.
- **No true lateral recovery without stepping.** After a side push the stiff body pivots on the outer edge of one foot. Once a foot is on its edge, ankle torque only rotates the foot, so the ankle strategy cannot pull the body back. Widening the stance (hip roll 5 degrees) raised the sideways threshold from below 20 N·s to about 25 N·s.
- **Determinism depends on the whole scene.** Three repeats of the same push and stagger gave identical pelvis positions twice and a 2 mm difference once. The other character in the scene was in a different state between runs, which changes the solver's ordering. Batch trials will run one character per scene and reset everything.
- **`stand → sit` without a bench falls**, and `limp` while seated slides off the bench. Both are expected; they are listed so nobody mistakes them for bugs.
- **Some milestone 2 behaviours are partial.** `grab_support` reaches but never grasps. There is no get-up behaviour, so `recover` only works while the character is still upright. Seated `stagger_recover` is replaced by a seated brace.
- **One decision per object.** If a ball is anticipated, its impact does not trigger a new decision, even when the impact is much harder than expected. Later events from other causes do trigger decisions. Scenario 2 seed 3 shows one: grab_support, then a fall 2.3 s later during the blend back to stand.
- **The incoming prediction ignores the body's own motion** and reports the first link the object's path touches. A ball aimed at the head can be reported as heading for the chest when its arc clips the shoulder first.
- **Impact strength words use the measured collision impulse,** which is lower than the ball's nominal momentum, because the ball bounces off and the body yields. A 41 N·s nominal throw was described as "moderate" in one trial.
- **Both decision models largely ignore the event text** (see the milestone 3 sanity check). Each has a favourite behaviour. This is measured, not tuned away: no prompt wording was adjusted to raise these numbers.
- **Laya's stiffness and recover answers barely vary** (0.39 to 0.59; recover always yes in the sanity set).
- **Laya on CPU is too slow for the 200 ms budget** (about 1.8 s with option rotation).
- **Command-line batch mode does not work.** `Unity.exe -batchmode -executeMethod ExperimentBatch.RunFromCommandLine` runs the whole flow and writes a CSV, but the rows are wrong. In `-batchmode`, PhysX has not registered a freshly built ArticulationBody when the body is placed in its start pose: the reduced coordinates have no DOFs, Unity asserts `GetInboundJointDof(link) == data.dofCount`, and every trial starts from a broken body. On 12 test trials every one "fell" and scenario 1 saw no events, against the editor run of the same seeds. Running one physics step before the snap did not fix it. `-nographics` made no difference. The controller now logs an error when this happens, so such a run cannot pass silently. The in-editor runner produced all the results above.
- **The models' wins are not decisions.** In scenario 1 Laya and GLiClass each chose one behaviour for all 30 seeds; their low head-hit rates come from that favourite, and they match the fixed baseline for it.
- **`protect_head` does not protect against heavy objects.** The rules selector picked it 10 times in scenario 1 and the head was still hit 5 times.
- **Real-time play mode needs editor focus.** With the Unity window in the background the player loop stops. This does not affect the scripted tests or the planned batch mode.

![Standing character in brace, seated character in protect_head](Docs/m1_brace_and_seated_protect.png)

![Scenario 1, seed 2: the rules selector chose dodge_sideways and the 12 kg box lands beside the head](Docs/m2_s1_dodge.png)
