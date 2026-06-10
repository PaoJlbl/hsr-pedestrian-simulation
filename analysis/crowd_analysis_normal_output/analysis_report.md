Crowd analysis summary
======================
Input: D:\AAA Learning\Unity\Shenzhenbei_wcx\crowd_state_normal.csv
Rows: 64080
Scenario: Normal
Time range: 0.48s - 356.80s, 712 steps
Grid: X=0..29 (30), Z=0..2 (3)
Complete grid: True
Active agents peak: 173 at 248.04s
Max density: 0.1800; adaptive density threshold used: 0.1351
Max speed: 12.2064; rows over 3 speed cap: 241
Persistent bottleneck event cells: 0
Top bottleneck cell X: None
Candidate hotspot event cells: 1469
Top candidate hotspot cell X: 8

Interpretation notes:
- The CSV grid is structurally complete and has no missing values or duplicate time-cell rows.
- The absolute density level is low, so bottlenecks are identified as relative local congestion zones.
- Persistent bottleneck events require density plus speed-loss risk; candidate hotspots use density plus local count pressure.
- Several sparse cells have unrealistically high mean_speed values; these rows are exported for inspection and excluded from risk speed aggregation.
- Empty cells have zero density and zero velocity, which is internally consistent.