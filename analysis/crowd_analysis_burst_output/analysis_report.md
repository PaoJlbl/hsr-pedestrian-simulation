Crowd analysis summary
======================
Input: D:\AAA Learning\Unity\Shenzhenbei_wcx\crowd_state_Burst.csv
Rows: 151560
Scenario: Burst
Time range: 0.48s - 848.09s, 1684 steps
Grid: X=0..29 (30), Z=0..2 (3)
Complete grid: True
Active agents peak: 412 at 333.59s
Max density: 0.2813; adaptive density threshold used: 0.1800
Max speed: 12.4203; rows over 3 speed cap: 1849
Persistent bottleneck event cells: 2
Top bottleneck cell X: 5
Candidate hotspot event cells: 3494
Top candidate hotspot cell X: 8

Interpretation notes:
- The CSV grid is structurally complete and has no missing values or duplicate time-cell rows.
- The absolute density level is low, so bottlenecks are identified as relative local congestion zones.
- Persistent bottleneck events require density plus speed-loss risk; candidate hotspots use density plus local count pressure.
- Several sparse cells have unrealistically high mean_speed values; these rows are exported for inspection and excluded from risk speed aggregation.
- Empty cells have zero density and zero velocity, which is internally consistent.