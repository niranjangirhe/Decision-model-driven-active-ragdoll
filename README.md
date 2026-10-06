# Decision-model-driven active ragdoll

A Unity prototype of a physics-simulated character whose reaction to events (impacts, falling objects, trips) is picked by a small local decision model. The selector idea follows van de Panne's "Composable Controllers for Physics-Based Character Animation" (SIGGRAPH 2001): a selector chooses which specialist controller takes over. Here the selector is a decision model that reads a short text description of the event.

The body is simulated at all times. No kinematic animation plays on it. The decision layer picks a behaviour and its parameters once per event; the physics layer turns that into joint drive targets every physics step.

## Status

| Milestone | State |
|---|---|
| 1. Ragdoll that stands and sits with pose targets | done, see results and limitations below |
| 2. Event sensing, text serialiser, rules selector | not started |
| 3. Decision server (Laya, GLiClass) | not started |
| 4. Experiment harness and results | not started |

## Setup

- Unity 6000.6.5f1 (Unity 6.6), URP 17.6. No extra packages are needed for milestone 1.
- Open `Assets/Scenes/M1_StandSit.unity` and press Play.
- Both the scene and the physics project settings are generated from code. To rebuild them, use the editor menu: `Ragdoll > Build M1 Stand-Sit Scene` and `Ragdoll > Apply Physics Project Settings`.

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
| `Assets/Editor/RagdollSceneBuilder.cs` | Builds the scene and writes physics project settings. |

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

## Limitations and failure cases so far

- **Stagger stepping is weak.** `StepRecovery` aims the swing foot at the capture point and nudges the pelvis upright through the stance hip, in the spirit of SIMBICON (Yin, Loken and van de Panne, 2007). It skips stepping when the capture point is already inside the feet. It helps at 25 N·s (3/4 vs 2/4) but fails at 40 N·s and above in every direction. Traces show two causes. First, the trunk pitches forward during the swing: a full world-frame torso servo on the stance hip kicked the lightly loaded stance foot backward, and the capped version I kept does not hold the trunk. Second, after a step that does capture the fall, the "hold where the legs landed" pose sometimes still tips over. An open-loop version (fixed lift and plant poses) did worse than not reacting at all and was removed.
- **No true lateral recovery without stepping.** After a side push the stiff body pivots on the outer edge of one foot. Once a foot is on its edge, ankle torque only rotates the foot, so the ankle strategy cannot pull the body back. Widening the stance (hip roll 5 degrees) raised the sideways threshold from below 20 N·s to about 25 N·s.
- **Determinism depends on the whole scene.** Three repeats of the same push and stagger gave identical pelvis positions twice and a 2 mm difference once. The other character in the scene was in a different state between runs, which changes the solver's ordering. Batch trials will run one character per scene and reset everything.
- **`stand → sit` without a bench falls**, and `limp` while seated slides off the bench. Both are expected; they are listed so nobody mistakes them for bugs.
- **Real-time play mode needs editor focus.** With the Unity window in the background the player loop stops. This does not affect the scripted tests or the planned batch mode.

![Standing character in brace, seated character in protect_head](Docs/m1_brace_and_seated_protect.png)
