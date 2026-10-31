# Primary timing detail

Exact empirical bootstrap median intervals across available successful independent runs (n <= 5); small n, no familywise or hardware-population guarantee. Per-operation tails are descriptive pooled samples, not independent suite replicates.

All times below are milliseconds. Incomplete cells describe successful attempts only; raw failures remain in summary.json and the archive. Suite p95 is the largest available successful run. Throughput uses the primary metric (process wall for fresh, settled suite for warm).

| Scenario | Contender | Successes | Median [95% interval] | p95 | Primary tests/s median | First useful SQL p95 | Cycle p95 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| durable · 1 / 1 · fresh | IntegreSQL25 | 5/5 | 10,367.5 [8,658.3, 10,871.1] | 10,871.1 | 0.10 | 5,450.2 | 5,527.1 |
| durable · 1 / 1 · fresh | Native templates | 5/5 | 5,340.5 [3,531.9, 9,361.5] | 9,361.5 | 0.19 | 363.9 | 448.9 |
| durable · 1 / 1 · fresh | Respawn | 5/5 | 4,034.8 [2,677.7, 5,442.1] | 5,442.1 | 0.25 | 49.8 | 223.2 |
| durable · 1 / 1 · fresh | TurboTest | 5/5 | 8,761.3 [6,697.8, 14,539.4] | 14,539.4 | 0.11 | 83.2 | 245.9 |
| durable · 1 / 1 · warm | IntegreSQL25 | 5/5 | 213.0 [209.2, 342.4] | 342.4 | 4.70 | 46.2 | 80.7 |
| durable · 1 / 1 · warm | Native templates | 5/5 | 174.8 [172.3, 205.7] | 205.7 | 5.72 | 35.6 | 59.6 |
| durable · 1 / 1 · warm | Respawn | 5/5 | 89.5 [86.0, 126.2] | 126.2 | 11.18 | 14.1 | 30.3 |
| durable · 1 / 1 · warm | TurboTest | 5/5 | 138.6 [134.0, 150.4] | 150.4 | 7.22 | 16.7 | 113.4 |
| durable · 20 / 1 · fresh | IntegreSQL25 | 4/5 | 11,698.4 [11,623.8, 13,334.2] | 13,334.2 | 1.71 | 752.4 | 774.1 |
| durable · 20 / 1 · fresh | Native templates | 5/5 | 6,447.3 [6,196.1, 11,123.2] | 11,123.2 | 3.10 | 414.5 | 452.1 |
| durable · 20 / 1 · fresh | Respawn | 5/5 | 4,587.2 [3,682.3, 7,401.6] | 7,401.6 | 4.36 | 111.8 | 132.4 |
| durable · 20 / 1 · fresh | TurboTest | 5/5 | 8,408.0 [8,004.6, 16,248.5] | 16,248.5 | 2.38 | 24.6 | 134.2 |
| durable · 20 / 1 · warm | IntegreSQL25 | 5/5 | 3,227.3 [2,991.9, 9,831.4] | 9,831.4 | 6.20 | 426.4 | 465.9 |
| durable · 20 / 1 · warm | Native templates | 5/5 | 6,421.1 [2,745.5, 8,420.2] | 8,420.2 | 3.11 | 425.2 | 463.4 |
| durable · 20 / 1 · warm | Respawn | 5/5 | 1,100.9 [948.1, 3,646.5] | 3,646.5 | 18.17 | 154.0 | 182.5 |
| durable · 20 / 1 · warm | TurboTest | 5/5 | 2,475.3 [2,114.7, 2,676.6] | 2,676.6 | 8.08 | 16.1 | 142.9 |
| durable · 100 / 4 · fresh | IntegreSQL25 | 2/5 | 38,664.5 [24,089.1, 53,239.9] | 53,239.9 | 3.01 | 4,997.0 | 5,039.7 |
| durable · 100 / 4 · fresh | Native templates | 5/5 | 14,841.4 [12,632.2, 32,837.2] | 32,837.2 | 6.74 | 1,710.4 | 2,033.3 |
| durable · 100 / 4 · fresh | Respawn | 5/5 | 10,116.4 [6,396.5, 20,872.9] | 20,872.9 | 9.88 | 380.6 | 456.0 |
| durable · 100 / 4 · fresh | TurboTest | 5/5 | 11,523.9 [11,371.5, 19,614.7] | 19,614.7 | 8.68 | 17.5 | 155.9 |
| durable · 100 / 4 · warm | IntegreSQL25 | 4/5 | 31,701.5 [24,614.1, 33,478.0] | 33,478.0 | 3.16 | 5,179.6 | 5,228.3 |
| durable · 100 / 4 · warm | Native templates | 5/5 | 8,743.1 [7,860.8, 31,034.6] | 31,034.6 | 11.44 | 1,173.0 | 1,290.6 |
| durable · 100 / 4 · warm | Respawn | 5/5 | 2,008.3 [1,823.5, 7,010.5] | 7,010.5 | 49.79 | 238.3 | 278.6 |
| durable · 100 / 4 · warm | TurboTest | 5/5 | 2,888.4 [2,828.9, 3,946.7] | 3,946.7 | 34.62 | 17.8 | 161.5 |
| relaxed · 1 / 1 · fresh | IntegreSQL25 | 5/5 | 10,068.5 [4,285.5, 12,128.3] | 12,128.3 | 0.10 | 5,250.9 | 5,327.7 |
| relaxed · 1 / 1 · fresh | Native templates | 5/5 | 4,034.9 [2,978.7, 6,246.7] | 6,246.7 | 0.25 | 275.9 | 360.9 |
| relaxed · 1 / 1 · fresh | Respawn | 5/5 | 4,538.1 [3,783.2, 5,894.4] | 5,894.4 | 0.22 | 32.2 | 119.4 |
| relaxed · 1 / 1 · fresh | TurboTest | 5/5 | 7,703.9 [5,895.1, 8,859.8] | 8,859.8 | 0.13 | 54.4 | 218.7 |
| relaxed · 1 / 1 · warm | IntegreSQL25 | 5/5 | 132.6 [125.4, 311.5] | 311.5 | 7.54 | 40.8 | 59.0 |
| relaxed · 1 / 1 · warm | Native templates | 5/5 | 114.4 [109.2, 121.1] | 121.1 | 8.74 | 32.9 | 52.8 |
| relaxed · 1 / 1 · warm | Respawn | 5/5 | 63.4 [52.6, 80.2] | 80.2 | 15.78 | 18.2 | 24.4 |
| relaxed · 1 / 1 · warm | TurboTest | 5/5 | 135.3 [121.5, 147.3] | 147.3 | 7.39 | 18.7 | 115.2 |
| relaxed · 20 / 1 · fresh | IntegreSQL25 | 5/5 | 16,398.3 [7,653.5, 22,433.2] | 22,433.2 | 1.22 | 5,284.7 | 5,301.6 |
| relaxed · 20 / 1 · fresh | Native templates | 5/5 | 6,246.0 [5,293.3, 8,206.0] | 8,206.0 | 3.20 | 530.8 | 549.5 |
| relaxed · 20 / 1 · fresh | Respawn | 5/5 | 4,989.2 [4,034.3, 5,642.5] | 5,642.5 | 4.01 | 42.7 | 72.1 |
| relaxed · 20 / 1 · fresh | TurboTest | 5/5 | 8,156.3 [7,502.6, 8,307.5] | 8,307.5 | 2.45 | 18.2 | 111.2 |
| relaxed · 20 / 1 · warm | IntegreSQL25 | 4/5 | 10,839.3 [8,069.4, 13,352.2] | 13,352.2 | 1.94 | 5,174.7 | 5,190.4 |
| relaxed · 20 / 1 · warm | Native templates | 5/5 | 2,620.4 [2,587.2, 2,919.7] | 2,919.7 | 7.63 | 514.0 | 532.6 |
| relaxed · 20 / 1 · warm | Respawn | 5/5 | 444.3 [389.4, 602.2] | 602.2 | 45.01 | 41.7 | 52.5 |
| relaxed · 20 / 1 · warm | TurboTest | 5/5 | 2,022.8 [1,935.9, 2,079.8] | 2,079.8 | 9.89 | 16.8 | 110.3 |
| relaxed · 1000 / 4 · fresh | IntegreSQL25 | 3/5 | 221,730.5 [153,716.6, 240,668.6] | 240,668.6 | 4.51 | 4,785.7 | 4,810.5 |
| relaxed · 1000 / 4 · fresh | Native templates | 5/5 | 46,301.3 [46,002.2, 48,263.6] | 48,263.6 | 21.60 | 549.8 | 568.4 |
| relaxed · 1000 / 4 · fresh | Respawn | 5/5 | 9,763.5 [9,663.6, 11,321.3] | 11,321.3 | 102.42 | 45.3 | 56.1 |
| relaxed · 1000 / 4 · fresh | TurboTest | 5/5 | 33,584.4 [33,334.5, 36,601.8] | 36,601.8 | 29.78 | 17.3 | 122.4 |
| relaxed · 1000 / 4 · warm | IntegreSQL25 | 4/5 | 162,125.1 [115,255.3, 200,136.0] | 200,136.0 | 6.17 | 4,054.4 | 4,071.2 |
| relaxed · 1000 / 4 · warm | Native templates | 5/5 | 42,596.2 [41,538.2, 42,911.7] | 42,911.7 | 23.48 | 554.9 | 573.6 |
| relaxed · 1000 / 4 · warm | Respawn | 5/5 | 6,466.7 [6,290.6, 6,802.7] | 6,802.7 | 154.64 | 44.9 | 56.0 |
| relaxed · 1000 / 4 · warm | TurboTest | 5/5 | 27,224.1 [26,800.9, 27,698.5] | 27,698.5 | 36.73 | 16.8 | 120.3 |

Preparation, tests-returned, settlement, final audit, drain, complete/observer totals and raw values are retained in [summary.json](summary.json). Warmup is retained in each raw report. Acquire/identity/application/return operation tails overlap and must not be summed. TurboTest return waits for restoration; SQL/IntegreSQL adapter return can enqueue work, so ReturnMs alone is not a fair ranking.
