"""
Summarise the experiment CSV (one row per trial) into a markdown table and three plots.

Usage:  decision_server/.venv/Scripts/python analysis/summarize.py [Results/trials.csv]
Writes: Results/summary.md, Results/plots/outcomes.png, Results/plots/behaviours.png,
        Results/plots/latency.png

Every selector faced the same seeds, so the comparison is paired: for each scenario and
seed the event is the same, only the decision differs (until the decision is applied).
"""

import sys
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
# Main run plus the extra fixed baselines (always fall_and_curl / always dodge_sideways), same seeds.
CSVS = [Path(a) for a in sys.argv[1:]] or [ROOT / "Results" / "trials.csv", ROOT / "Results" / "trials_baselines.csv"]
OUT = ROOT / "Results"
PLOTS = OUT / "plots"

SELECTORS = ["fixed", "rules", "laya", "gliclass", "fixed:fall_and_curl", "fixed:dodge_sideways"]
SELECTOR_NAMES = {"fixed": "A. Fixed (brace)", "rules": "B. Rules", "laya": "C. Laya", "gliclass": "D. GLiClass",
                  "fixed:fall_and_curl": "A2. Fixed (curl)", "fixed:dodge_sideways": "A3. Fixed (dodge)"}
BEHAVIOURS = ["brace", "protect_head", "stagger_recover", "dodge_sideways", "fall_and_curl", "go_limp", "grab_support"]
SCENARIOS = {1: "S1 bench + falling object", 2: "S2 standing impact"}

# Reference categorical palette (validated: adjacent CVD dE >= 9.1, normal-vision >= 19.6 on the light
# surface). Three slots are under 3:1 contrast, so every bar carries a visible value label and
# summary.md is the table view.
PALETTE = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6"]
# Selectors use slots 1-4 for A-D; the two extra baselines get violet and a neutral gray so they read as reference bars.
SELECTOR_COLORS = {"fixed": PALETTE[0], "rules": PALETTE[1], "laya": PALETTE[2], "gliclass": PALETTE[3],
                   "fixed:fall_and_curl": PALETTE[6], "fixed:dodge_sideways": "#8f8e88"}
SURFACE, INK, INK2, GRID = "#fcfcfb", "#0b0b0b", "#52514e", "#e4e3df"


def style():
    plt.rcParams.update({
        "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "savefig.facecolor": SURFACE,
        "axes.edgecolor": GRID, "axes.labelcolor": INK2, "xtick.color": INK2, "ytick.color": INK2,
        "text.color": INK, "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.8,
        "axes.axisbelow": True, "axes.spines.top": False, "axes.spines.right": False,
        "font.size": 10, "axes.titlesize": 11, "axes.titleweight": "bold", "legend.frameon": False,
    })


def load():
    df = pd.concat([pd.read_csv(p) for p in CSVS if p.exists()], ignore_index=True)
    df["selector"] = pd.Categorical(df["selector"], SELECTORS, ordered=True)
    df["deliberate_fall"] = df.behaviour.isin(["fall_and_curl", "go_limp"])
    df["fell_unintended"] = df.fell.astype(bool) & ~df.deliberate_fall
    return df


def summary_table(df):
    rows = []
    rules = df[df.selector == "rules"].set_index(["scenario", "seed"])["behaviour"]
    for (sc, sel), g in df.groupby(["scenario", "selector"], observed=True):
        agree = (g.set_index(["scenario", "seed"])["behaviour"] == rules.reindex(g.set_index(["scenario", "seed"]).index)).mean()
        stable = g["time_to_stable"].dropna()
        rows.append({
            "scenario": sc, "selector": sel, "n": len(g),
            "head hit %": 100 * g.head_hit.mean(),
            "mean head peak (N*s)": g.head_peak_impulse.mean(),
            "fell %": 100 * g.fell.mean(),
            "unintended fall %": 100 * g.fell_unintended.mean(),
            "upright at end %": 100 * g.recovered.mean(),
            "median time to stable (s)": stable.median() if len(stable) else float("nan"),
            "never stable": int(g["time_to_stable"].isna().sum()),
            "median latency (ms)": g.latency_ms.median(),
            "p95 latency (ms)": g.latency_ms.quantile(0.95),
            "timeouts": int(g.timed_out.sum()), "fallbacks": int(g.fallback.sum()),
            "same choice as rules %": 100 * agree,
            "no event": int((g.event_kind == "none").sum()),
        })
    return pd.DataFrame(rows)


def behaviour_counts(df):
    t = df.groupby(["scenario", "selector", "behaviour"], observed=True).size().unstack(fill_value=0)
    return t.reindex(columns=[b for b in BEHAVIOURS], fill_value=0)


def to_markdown(table, counts, df):
    srcs = ", ".join(f"`{p.relative_to(ROOT) if p.is_relative_to(ROOT) else p}`" for p in CSVS if p.exists())
    lines = [f"# Experiment summary\n", f"Source: {srcs}; {len(df)} trials, seeds {df.seed.min()}-{df.seed.max()} "
             f"for every selector.\n"]
    for sc, title in SCENARIOS.items():
        t = table[table.scenario == sc]
        if t.empty:
            continue
        lines.append(f"\n## {title}\n")
        cols = ["selector", "n", "head hit %", "mean head peak (N*s)", "fell %", "unintended fall %", "upright at end %",
                "median time to stable (s)", "never stable", "median latency (ms)", "p95 latency (ms)",
                "timeouts", "fallbacks", "same choice as rules %"]
        lines.append("| " + " | ".join(cols) + " |")
        lines.append("|" + "---|" * len(cols))
        for _, r in t.iterrows():
            vals = [SELECTOR_NAMES[r.selector]] + [fmt(r[c]) for c in cols[1:]]
            lines.append("| " + " | ".join(vals) + " |")
        lines.append("\nBehaviours chosen (first event of each trial):\n")
        c = counts.loc[sc]
        lines.append("| selector | " + " | ".join(BEHAVIOURS) + " |")
        lines.append("|" + "---|" * (len(BEHAVIOURS) + 1))
        for sel in SELECTORS:
            if sel in c.index and c.loc[sel].sum() > 0:
                lines.append(f"| {SELECTOR_NAMES[sel]} | " + " | ".join(str(int(c.loc[sel, b])) for b in BEHAVIOURS) + " |")
    lines.append("\nDefinitions: *fell* = pelvis ever below 0.6 m (0.45 m seated) or tilted over 45 deg after launch; "
                 "*unintended fall* = fell although the chosen behaviour was not fall_and_curl or go_limp; "
                 "*upright at end* = not fallen when the trial ends; *time to stable* = from the first event until the "
                 "centre of mass first stays below 0.1 m/s for 0.5 s (a body lying still counts); *same choice as rules* = "
                 "share of seeds where the selector picked the rules selector's behaviour.\n")
    (OUT / "summary.md").write_text("\n".join(lines), encoding="utf-8")


def fmt(v):
    if isinstance(v, float):
        return "" if pd.isna(v) else f"{v:.1f}" if abs(v) >= 1 or v == 0 else f"{v:.2f}"
    return str(v)


def label_bars(ax, bars, suffix=""):
    for b in bars:
        h = b.get_height()
        ax.annotate(f"{h:.0f}{suffix}", (b.get_x() + b.get_width() / 2, h), xytext=(0, 2),
                    textcoords="offset points", ha="center", va="bottom", fontsize=8, color=INK2)


def plot_outcomes(table):
    metrics = [("head hit %", "Head hit (% of trials)"), ("unintended fall %", "Unintended fall (% of trials)"),
               ("fell %", "Fell, any reason (% of trials)")]
    fig, axes = plt.subplots(1, 3, figsize=(13, 3.9), sharey=True)
    sels = [s for s in SELECTORS if s in set(table.selector)]
    width = 0.8 / len(sels)
    for ax, (col, title) in zip(axes, metrics):
        for i, sel in enumerate(sels):
            ys = [table[(table.scenario == sc) & (table.selector == sel)][col].squeeze() for sc in SCENARIOS]
            xs = [k + (i - (len(sels) - 1) / 2) * width for k in range(len(SCENARIOS))]
            bars = ax.bar(xs, ys, width, color=SELECTOR_COLORS[sel], label=SELECTOR_NAMES[sel], edgecolor=SURFACE, linewidth=2)
            label_bars(ax, bars)
        ax.set_xticks(range(len(SCENARIOS)), [t.split(" ")[0] for t in SCENARIOS.values()])
        ax.set_title(title, loc="left")
        ax.set_ylim(0, 110)
        ax.grid(axis="x", visible=False)
    axes[0].set_ylabel("% of trials")
    fig.legend(*axes[0].get_legend_handles_labels(), loc="upper center", ncol=len(sels), bbox_to_anchor=(0.5, 1.02))
    fig.text(0.01, -0.02, "S1 = sitting on a bench, object falls from above.  S2 = standing, ball thrown at the body.  "
             "Same seeds for every selector.", color=INK2, fontsize=8)
    fig.tight_layout(rect=(0, 0, 1, 0.92))
    fig.savefig(PLOTS / "outcomes.png", dpi=150, bbox_inches="tight")
    plt.close(fig)


def plot_behaviours(counts):
    fig, axes = plt.subplots(1, 2, figsize=(12, 3.0), sharey=True)
    for ax, (sc, title) in zip(axes, SCENARIOS.items()):
        if sc not in counts.index.get_level_values(0):
            continue
        c = counts.loc[sc].reindex([s for s in ["fixed", "rules", "laya", "gliclass"] if s in counts.loc[sc].index])
        totals = c.sum(axis=1)
        left = pd.Series(0.0, index=c.index)
        for j, b in enumerate(BEHAVIOURS):
            share = 100 * c[b] / totals
            bars = ax.barh([SELECTOR_NAMES[s] for s in c.index], share, left=left, color=PALETTE[j],
                           label=b, edgecolor=SURFACE, linewidth=2, height=0.6)
            for bar, v in zip(bars, share):
                if v >= 9:
                    ax.text(bar.get_x() + bar.get_width() / 2, bar.get_y() + bar.get_height() / 2, f"{v:.0f}%",
                            ha="center", va="center", fontsize=8, color=INK)
            left += share
        ax.set_title(title, loc="left")
        ax.set_xlim(0, 100)
        ax.invert_yaxis()
        ax.grid(axis="y", visible=False)
        ax.set_xlabel("% of trials")
    fig.legend(*axes[0].get_legend_handles_labels(), loc="upper center", ncol=7, bbox_to_anchor=(0.5, 1.04))
    fig.tight_layout(rect=(0, 0, 1, 0.9))
    fig.savefig(PLOTS / "behaviours.png", dpi=150, bbox_inches="tight")
    plt.close(fig)


def plot_latency(df):
    fig, ax = plt.subplots(figsize=(8, 3.2))
    sels = ["fixed", "rules", "laya", "gliclass"]
    for i, sel in enumerate(sels):
        v = df[df.selector == sel].latency_ms.clip(lower=0.001)
        jitter = (pd.Series(range(len(v))) % 9 - 4) * 0.035
        ax.scatter(v, [i + j for j in jitter], s=14, color=SELECTOR_COLORS[sel], alpha=0.8, edgecolors=SURFACE, linewidths=0.6)
        med = v.median()
        ax.plot([med, med], [i - 0.3, i + 0.3], color=INK, linewidth=2)
        ax.annotate(f"median {med:.3g} ms", (v.max(), i), xytext=(10, 0), textcoords="offset points",
                    ha="left", va="center", fontsize=8, color=INK2)
    ax.axvline(200, color=INK2, linewidth=1, linestyle="--")
    ax.annotate("200 ms timeout", (200, -0.6), ha="right", va="bottom", fontsize=8, color=INK2, xytext=(-4, 0),
                textcoords="offset points")
    ax.set_xscale("log")
    ax.set_yticks(range(len(sels)), [SELECTOR_NAMES[s] for s in sels])
    ax.set_ylim(len(sels) - 0.4, -0.7)
    ax.set_xlabel("decision latency, wall clock (ms, log scale)")
    ax.set_title("Decision latency per event (both scenarios)", loc="left")
    ax.grid(axis="y", visible=False)
    fig.tight_layout()
    fig.savefig(PLOTS / "latency.png", dpi=150, bbox_inches="tight")
    plt.close(fig)


def main():
    style()
    PLOTS.mkdir(parents=True, exist_ok=True)
    df = load()
    table = summary_table(df)
    counts = behaviour_counts(df)
    to_markdown(table, counts, df)
    plot_outcomes(table)
    plot_behaviours(counts)
    plot_latency(df)
    pd.set_option("display.width", 200)
    print(table.round(2).to_string(index=False))
    print(f"\nwrote {OUT / 'summary.md'} and {PLOTS}")


if __name__ == "__main__":
    main()
