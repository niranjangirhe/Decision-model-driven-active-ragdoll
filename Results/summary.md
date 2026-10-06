# Experiment summary

Source: `Results\trials.csv`, `Results\trials_baselines.csv`; 360 trials, seeds 1-30 for every selector.


## S1 bench + falling object

| selector | n | head hit % | mean head peak (N*s) | fell % | unintended fall % | upright at end % | median time to stable (s) | never stable | median latency (ms) | p95 latency (ms) | timeouts | fallbacks | same choice as rules % |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A. Fixed (brace) | 30 | 80.0 | 17.9 | 0.0 | 0.0 | 100.0 | 0.66 | 0 | 0.01 | 0.01 | 0 | 0 | 23.3 |
| B. Rules | 30 | 26.7 | 2.4 | 0.0 | 0.0 | 100.0 | 0.66 | 0 | 0.01 | 0.01 | 0 | 0 | 100.0 |
| C. Laya | 30 | 0.0 | 0.0 | 0.0 | 0.0 | 100.0 | 0.74 | 0 | 46.8 | 59.9 | 0 | 0 | 0.0 |
| D. GLiClass | 30 | 6.7 | 0.10 | 0.0 | 0.0 | 100.0 | 0.81 | 0 | 15.4 | 17.5 | 0 | 0 | 43.3 |
| A2. Fixed (curl) | 30 | 0.0 | 0.0 | 0.0 | 0.0 | 100.0 | 0.68 | 0 | 0.01 | 0.01 | 0 | 0 | 0.0 |
| A3. Fixed (dodge) | 30 | 3.3 | 0.19 | 0.0 | 0.0 | 100.0 | 0.82 | 0 | 0.01 | 0.01 | 0 | 0 | 43.3 |

Behaviours chosen (first event of each trial):

| selector | brace | protect_head | stagger_recover | dodge_sideways | fall_and_curl | go_limp | grab_support |
|---|---|---|---|---|---|---|---|
| A. Fixed (brace) | 30 | 0 | 0 | 0 | 0 | 0 | 0 |
| B. Rules | 7 | 10 | 0 | 13 | 0 | 0 | 0 |
| C. Laya | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| D. GLiClass | 0 | 0 | 0 | 30 | 0 | 0 | 0 |
| A2. Fixed (curl) | 0 | 0 | 0 | 0 | 30 | 0 | 0 |
| A3. Fixed (dodge) | 0 | 0 | 0 | 30 | 0 | 0 | 0 |

## S2 standing impact

| selector | n | head hit % | mean head peak (N*s) | fell % | unintended fall % | upright at end % | median time to stable (s) | never stable | median latency (ms) | p95 latency (ms) | timeouts | fallbacks | same choice as rules % |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A. Fixed (brace) | 30 | 13.3 | 3.8 | 10.0 | 10.0 | 90.0 | 0.41 | 1 | 0.01 | 0.01 | 0 | 0 | 10.0 |
| B. Rules | 30 | 13.3 | 3.8 | 23.3 | 10.0 | 76.7 | 0.59 | 2 | 0.01 | 0.02 | 0 | 0 | 93.3 |
| C. Laya | 30 | 13.3 | 3.8 | 50.0 | 3.3 | 50.0 | 1.3 | 0 | 47.3 | 58.4 | 1 | 1 | 3.3 |
| D. GLiClass | 30 | 13.3 | 3.8 | 13.3 | 13.3 | 86.7 | 1.1 | 2 | 15.0 | 17.0 | 0 | 0 | 10.0 |
| A2. Fixed (curl) | 30 | 20.0 | 3.9 | 93.3 | 0.0 | 6.7 | 2.8 | 0 | 0.01 | 0.01 | 0 | 0 | 13.3 |
| A3. Fixed (dodge) | 30 | 16.7 | 3.9 | 13.3 | 13.3 | 86.7 | 0.97 | 4 | 0.01 | 0.01 | 0 | 0 | 0.0 |

Behaviours chosen (first event of each trial):

| selector | brace | protect_head | stagger_recover | dodge_sideways | fall_and_curl | go_limp | grab_support |
|---|---|---|---|---|---|---|---|
| A. Fixed (brace) | 28 | 0 | 0 | 0 | 0 | 0 | 0 |
| B. Rules | 3 | 4 | 12 | 0 | 4 | 0 | 5 |
| C. Laya | 12 | 0 | 1 | 1 | 9 | 5 | 0 |
| D. GLiClass | 3 | 0 | 0 | 20 | 0 | 0 | 5 |
| A2. Fixed (curl) | 0 | 0 | 0 | 0 | 28 | 0 | 0 |
| A3. Fixed (dodge) | 0 | 0 | 0 | 28 | 0 | 0 | 0 |

Definitions: *fell* = pelvis ever below 0.6 m (0.45 m seated) or tilted over 45 deg after launch; *unintended fall* = fell although the chosen behaviour was not fall_and_curl or go_limp; *upright at end* = not fallen when the trial ends; *time to stable* = from the first event until the centre of mass first stays below 0.1 m/s for 0.5 s (a body lying still counts); *same choice as rules* = share of seeds where the selector picked the rules selector's behaviour.
