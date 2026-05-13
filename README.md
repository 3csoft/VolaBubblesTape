# VolaBubbles Tape

A real-time **order flow tape indicator** for [Quantower](https://www.quantower.com), inspired by ATAS-style trade tapes and DOM heatmaps.

VolaBubbles renders a continuously moving lane to the right of the latest candle:

- **Delta bubbles** — aggressor buy/sell deltas aggregated per short time bucket and price level, rendered as colored bubbles whose size scales with the absolute delta. Volume is printed on top of each bubble.
- **Bid / Ask trail** — sampled bid and ask prices flow with the tape, so you can see how the spread evolved second-by-second.
- **Level 2 heatmap** (optional) — periodic snapshots of the order book aggregated into vertical price-level columns, colored by a hot palette (dark blue -> purple -> red -> yellow) based on a running-max with decay window. Bigger limit orders glow brighter.

All elements share the same speed and drift left-to-right with a configurable pixel-per-second rate.

## Screenshots

> Add screenshots in an `images/` folder and reference them here:
>
> `![Tape with heatmap](images/tape-heatmap.png)`

## Requirements

- Quantower **v1.145+** (tested on v1.145.17)
- A vendor with **Level 1** data (for bubbles and bid/ask). The L2 heatmap requires a vendor that streams **Level 2 / DOM** data (e.g. Rithmic, CQG, Bookmap-like feeds).
- .NET 8 SDK if you want to build from source.

## Install

### Option A: drop-in DLL (no compilation)

1. Download the latest `VolaBubbles.zip` from the [Releases](../../releases) page.
2. Extract it into `C:\Quantower\Settings\Scripts\Indicators\VolaBubbles\` (create the folder if needed).
3. In Quantower, on a chart, click `Indicators` -> `Custom` -> `VolaBubbles Tape`.

### Option B: build from source

```powershell
git clone https://github.com/<your-user>/VolaBubbles.git
cd VolaBubbles
dotnet build VolaBubbles\VolaBubbles\VolaBubbles.csproj -c Release
```

The build output is copied directly into your Quantower indicators folder. The path is auto-resolved by the `.csproj`; you can override it with environment variables (see below).

#### Environment variables (optional)

The `.csproj` looks for these. If unset, sensible defaults are used:

| Variable | Default | What it points to |
| --- | --- | --- |
| `QuantowerBin` | `C:\Quantower\TradingPlatform\v1.145.17\bin` | Folder containing `TradingPlatform.BusinessLayer.dll` |
| `QuantowerIndicators` | `C:\Quantower\Settings\Scripts\Indicators\VolaBubbles` | Where the built `.dll` is copied |

Set persistently:

```powershell
setx QuantowerBin "C:\Quantower\TradingPlatform\v1.146.0\bin"
setx QuantowerIndicators "C:\Quantower\Settings\Scripts\Indicators\VolaBubbles"
```

Open a fresh shell after `setx` for the values to be visible.

## Parameters

### Delta bubbles

- **Delta Threshold** — minimum `|buy - sell|` per bucket to spawn a bubble.
- **Price Step (0 = TickSize)** — price aggregation step; `0` falls back to the symbol's tick size.
- **Bucket (ms)** — aggregation window for bubbles.
- **Max Bubbles On Tape** — hard cap on simultaneously alive bubbles.
- **Max Bubble Radius** — pixel cap on bubble radius.
- **Show Volume Text** — print the bucket's volume on the bubble.
- **Positive / Negative Delta Color** — bubble fill colors.

### Tape geometry & motion

- **Tape Width (bars)** — how far to the right of the last candle the spawn point sits, in chart bar widths.
- **Tape Speed (px/sec)** — left-to-right drift speed.

### Bid / Ask

- **Show Bid/Ask Lines** — toggles the moving bid/ask trail.
- **Bid Line Color / Ask Line Color** — colors for the trail; a faded extension to the spawn edge marks the current live value.

### L2 Heatmap

- **Show L2 Heatmap** — master toggle. Subscribes to the L2 stream when enabled.
- **Heatmap Price Range (+/-)** — vertical extent around current mid-price, in instrument units (e.g. `10.0` on gold = +/- $10).
- **Heatmap Sample (ms)** — DOM sampling interval.
- **Heatmap Decay (sec)** — running-max window for color normalization.
- **Heatmap Gamma (0.1..2.0)** — `<1` boosts low-end intensity, `>1` compresses it.
- **Heatmap Min Alpha %** — cutoff threshold: levels weaker than this are not drawn.
- **Heatmap Max Levels (cap)** — safety cap on the number of L2 levels requested per side.
- **Heatmap Opacity (0.0..1.0)** — global opacity multiplier on the hot palette.

## How the heatmap normalizes color

For each rendered cell:

1. `t = size / runningMax` where `runningMax` is the largest level size observed within the **decay window** (default 30 seconds).
2. `t = pow(t, Gamma)` shapes sensitivity to small/large orders.
3. If `t < MinAlphaPercent / 100`, the cell is fully transparent.
4. Otherwise the color is interpolated across a 5-stop hot palette (dark blue -> purple -> red -> yellow), then scaled by `HeatmapOpacity`.

## Roadmap

- Per-price persistent cell trail (instead of per-snapshot columns).
- Open-order indicator pulse (already prototyped in `VolaBubbles.cs`).
- Historical L2 replay window.
- Marketplace submission.

## Disclaimer

This software is provided **for educational and personal-research purposes only**. It is not financial advice. Trading futures, equities, forex, crypto, or any other instrument involves substantial risk; you can lose more than your initial deposit. The author is not responsible for any financial losses incurred from using this indicator. **Test on a simulator before using on a live account.**

## License

Released under the [MIT License](LICENSE). Edit `LICENSE` to put your real name in the copyright line before publishing.
