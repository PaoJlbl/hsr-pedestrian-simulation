# Burst Control Analysis

## Data Quality
- Three experiments were found: Level 0 no control, Level 1 exit metering, Level 2 metering + gate guidance.
- The first six core metrics contain zero placeholders: T_total, T_core, T_belt, and Imbalance are all 0 in the export.
- Control trigger fields are also 0, and gate pass counts are 0, so the exported control-state logger likely did not capture dynamic intervention states.
- The actual visualizations therefore separate measured outputs from presentation-only assumptions.

## Actual Export Findings
- Average travel time decreases from 62.193s to 61.580s and 61.179s.
- Level 2 improves average travel time by 1.63% relative to Level 0.
- Total failed agents change from 1747 to 1733 and 1733.
- Wrong exits decrease from 1139 to 1090, an improvement of 4.30%.
- Main belt peak density changes from 0.1689 to 0.1622.
- Core X8 peak density changes from 0.1000 to 0.1067.

## Interpretation
- Level 1 mainly reduces average travel time and failed-agent count slightly, consistent with exit metering smoothing inflow.
- Level 2 further reduces average travel time and wrong-exit behavior, consistent with gate guidance improving route choice.
- The actual exported core bottleneck durations cannot support a claim about reduced T_core/T_belt because they are all zero.
- The illustrative charts show how the expected control effect can be presented, but those values must be described as assumptions unless rerun/export logic provides measured values.