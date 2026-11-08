"""
FireRescue AI — results/plot_comparison.py
==========================================
Generates comparison plots for Standard PPO vs Recurrent PPO.

USAGE:
    python plot_comparison.py --std  eval_StandardPPO_*.csv
                              --rppo eval_RecurrentPPO_*.csv

REQUIRES:
    pip install pandas matplotlib seaborn
"""

import argparse
import glob
import sys
import os
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import seaborn as sns
from pathlib import Path

# ── Style ─────────────────────────────────────────────────────────────────────
sns.set_theme(style="darkgrid", palette="muted")
COLORS = {"StandardPPO": "#4C9EF5", "RecurrentPPO": "#F56C4C"}


def load_csv(pattern: str, label: str) -> pd.DataFrame:
    files = sorted(glob.glob(pattern))
    if not files:
        print(f"[WARNING] No files matched pattern: {pattern}")
        return pd.DataFrame()
    df = pd.concat([pd.read_csv(f) for f in files], ignore_index=True)
    df["PolicyLabel"] = label
    return df


def print_summary(df: pd.DataFrame, name: str):
    if df.empty:
        print(f"\n{name}: no data")
        return
    print(f"\n{'='*50}")
    print(f"  {name}")
    print(f"{'='*50}")
    print(f"  Episodes:             {len(df)}")
    print(f"  Mean victims rescued: {df['VictimsRescued'].mean():.2f} / {df['VictimsTotal'].iloc[0]}")
    print(f"  Success rate:         {df['AllRescued'].mean()*100:.1f}%")
    print(f"  UAV survival rate:    {df['UAVSurvived'].mean()*100:.1f}%")
    print(f"  Mean mission time:    {df['MissionTimeSecs'].mean():.1f}s")
    print(f"  Mean steps taken:     {df['StepsTaken'].mean():.0f}")


def plot_bar(ax, df_std, df_rppo, col, ylabel, title):
    means = []
    stds  = []
    labels = []
    colors = []
    for df, name in [(df_std, "Standard PPO"), (df_rppo, "Recurrent PPO")]:
        if df.empty: continue
        means.append(df[col].mean())
        stds.append(df[col].std())
        labels.append(name)
        colors.append(COLORS["StandardPPO"] if "Standard" in name else COLORS["RecurrentPPO"])
    bars = ax.bar(labels, means, yerr=stds, color=colors, capsize=5, alpha=0.85)
    ax.set_title(title, fontsize=11, fontweight="bold")
    ax.set_ylabel(ylabel)
    for bar, m in zip(bars, means):
        ax.text(bar.get_x() + bar.get_width()/2, bar.get_height() + 0.02,
                f"{m:.2f}", ha="center", va="bottom", fontsize=9)


def main():
    parser = argparse.ArgumentParser(description="FireRescue AI Result Comparison")
    parser.add_argument("--std",  default="eval_StandardPPO*.csv",  help="Standard PPO CSV glob")
    parser.add_argument("--rppo", default="eval_RecurrentPPO*.csv", help="Recurrent PPO CSV glob")
    parser.add_argument("--out",  default="comparison_plots.png",   help="Output image file")
    args = parser.parse_args()

    # Change to results dir if called from root
    script_dir = Path(__file__).parent
    os.chdir(script_dir)

    df_std  = load_csv(args.std,  "Standard PPO")
    df_rppo = load_csv(args.rppo, "Recurrent PPO")

    if df_std.empty and df_rppo.empty:
        print("ERROR: No CSV files found. Run evaluation first.")
        sys.exit(1)

    print_summary(df_std,  "STANDARD PPO")
    print_summary(df_rppo, "RECURRENT PPO")

    # ── Plots ──────────────────────────────────────────────────────────────
    fig, axes = plt.subplots(2, 3, figsize=(15, 9))
    fig.suptitle("FireRescue AI — Standard PPO vs Recurrent PPO Comparison",
                 fontsize=14, fontweight="bold")

    plot_bar(axes[0, 0], df_std, df_rppo, "VictimsRescued",
             "Victims Rescued", "Avg Victims Rescued per Episode")
    plot_bar(axes[0, 1], df_std, df_rppo, "MissionTimeSecs",
             "Seconds", "Avg Mission Completion Time")
    plot_bar(axes[0, 2], df_std, df_rppo, "StepsTaken",
             "Steps", "Avg Steps per Episode")

    # Success rate bar (boolean → %)
    for df in [df_std, df_rppo]:
        if not df.empty:
            df["SuccessPct"] = df["AllRescued"].astype(float) * 100
            df["UAVPct"]     = df["UAVSurvived"].astype(float) * 100
    plot_bar(axes[1, 0], df_std, df_rppo, "SuccessPct",
             "Success %", "Episode Success Rate (All Victims Rescued)")
    plot_bar(axes[1, 1], df_std, df_rppo, "UAVPct",
             "Survival %", "UAV Survival Rate")

    # Victims rescued distribution (boxplot)
    combined = pd.concat([df_std, df_rppo]).dropna(subset=["VictimsRescued"])
    if not combined.empty:
        sns.boxplot(ax=axes[1, 2], data=combined,
                    x="PolicyLabel", y="VictimsRescued",
                    palette=[COLORS["StandardPPO"], COLORS["RecurrentPPO"]])
        axes[1, 2].set_title("Victims Rescued Distribution", fontsize=11, fontweight="bold")
        axes[1, 2].set_xlabel("")
        axes[1, 2].set_ylabel("Victims Rescued")

    plt.tight_layout()
    plt.savefig(args.out, dpi=150, bbox_inches="tight")
    print(f"\n[OK] Plots saved to: {os.path.abspath(args.out)}")
    plt.show()


if __name__ == "__main__":
    main()
