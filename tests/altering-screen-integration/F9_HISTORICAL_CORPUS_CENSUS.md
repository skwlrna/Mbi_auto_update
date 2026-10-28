# F9 historical error corpus · offline coverage census (2026-10-28)

Source-of-truth is the actual `skwlrna/Mbi_auto_log/runtime-errors` Git tree (238 `summary.json` files as observed). The directory names are historical incident **identifiers only**, not evidence of root cause or recovery. No personal filesystem path, packet payload, screenshot, or private details copied here.

| Set | Logs | Status in PR #341 |
|---|---:|---|
| Multi-altering `다중가공` | 118 | 41 sampled incident-to-offline safety assertions (L2/L3); 77 NOT independently reproduced |
| Older single auto-altering | 66 | Not part of these F9-specific incident checks |
| Crafting | 32 | Not part of these F9-specific incident checks |
| Other (Abyss etc.) | 22 | Not part of these F9-specific incident checks |
| **All** | **238** | **Do not declare universal historical pass** |

## Claim policy

- A L2 rule PASS demonstrates its specific policy guard; it is **not** a reenactment of the actual bug.
- Approved public PNG L3 checks demonstrate *individual* still-frame recognition, not temporal/CLI/live behavior.
- Earlier 27 late-October cases plus 14 additional 2026-10-04..16 examples are separately tested in `HistoricalIncidentRegression.cs`. Some early and recent cases cover the same failure family.
- Unmapped logs are **unreplayed**, not failed and not passed. Original private images, timed CLI logs and input replays cannot be inferred from IDs.
- In particular, N02 receipt-stock test, F05 consumption ledger, updater rollback and 7-lane scheduling prove their separate recorded contracts, not all these incidents.
- To promote any row to **historical replay PASS**, a repeatable before/after offline fixture or real-game record plus the target action/expected receipt is required.
- Keep PR #341 draft, `main` unchanged, no releases or automatic updating before an explicit deployment decision.

## Full multi-altering log inventory

| # | Incident ID | Offline coverage | Evidence |
|---:|---|---|---|
| 1 | `20261004_115732_231_다중가공_오류_4851da` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_115732_231_다중가공_오류_4851da/summary.json) |
| 2 | `20261004_123237_029_다중가공_오류_cfb1f0` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_123237_029_다중가공_오류_cfb1f0/summary.json) |
| 3 | `20261004_123556_255_다중가공_오류_7b1487` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_123556_255_다중가공_오류_7b1487/summary.json) |
| 4 | `20261004_193657_760_다중가공_오류_72a21c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_193657_760_다중가공_오류_72a21c/summary.json) |
| 5 | `20261005_082836_832_다중가공_오류_8db42c` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_082836_832_다중가공_오류_8db42c/summary.json) |
| 6 | `20261005_100516_848_다중가공_오류_54f0d5` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_100516_848_다중가공_오류_54f0d5/summary.json) |
| 7 | `20261005_113359_828_다중가공_오류_c9606c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_113359_828_다중가공_오류_c9606c/summary.json) |
| 8 | `20261005_170318_351_다중가공_오류_a1ea2e` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_170318_351_다중가공_오류_a1ea2e/summary.json) |
| 9 | `20261006_115518_473_다중가공_오류_816ec7` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_115518_473_다중가공_오류_816ec7/summary.json) |
| 10 | `20261006_123317_809_다중가공_오류_7a24b4` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_123317_809_다중가공_오류_7a24b4/summary.json) |
| 11 | `20261006_125152_694_다중가공_오류_2316c7` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_125152_694_다중가공_오류_2316c7/summary.json) |
| 12 | `20261006_130336_689_다중가공_오류_7d271d` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_130336_689_다중가공_오류_7d271d/summary.json) |
| 13 | `20261006_132031_896_다중가공_오류_cb2db6` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_132031_896_다중가공_오류_cb2db6/summary.json) |
| 14 | `20261006_134037_119_다중가공_오류_543d2c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_134037_119_다중가공_오류_543d2c/summary.json) |
| 15 | `20261006_155349_838_다중가공_오류_090c5f` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_155349_838_다중가공_오류_090c5f/summary.json) |
| 16 | `20261006_155540_769_다중가공_오류_271a09` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_155540_769_다중가공_오류_271a09/summary.json) |
| 17 | `20261006_191106_154_다중가공_오류_a41119` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_191106_154_다중가공_오류_a41119/summary.json) |
| 18 | `20261006_194238_916_다중가공_오류_fecd18` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_194238_916_다중가공_오류_fecd18/summary.json) |
| 19 | `20261006_205958_382_다중가공_오류_713509` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_205958_382_다중가공_오류_713509/summary.json) |
| 20 | `20261006_212841_222_다중가공_오류_92f6ee` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_212841_222_다중가공_오류_92f6ee/summary.json) |
| 21 | `20261006_214547_434_다중가공_오류_d5fd60` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-06/20261006_214547_434_다중가공_오류_d5fd60/summary.json) |
| 22 | `20261007_040512_938_다중가공_오류_9d14eb` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_040512_938_다중가공_오류_9d14eb/summary.json) |
| 23 | `20261007_131416_390_다중가공_오류_c400ce` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_131416_390_다중가공_오류_c400ce/summary.json) |
| 24 | `20261007_142330_531_다중가공_오류_35372c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_142330_531_다중가공_오류_35372c/summary.json) |
| 25 | `20261007_154753_585_다중가공_오류_1b7817` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_154753_585_다중가공_오류_1b7817/summary.json) |
| 26 | `20261007_164402_549_다중가공_오류_5437f3` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_164402_549_다중가공_오류_5437f3/summary.json) |
| 27 | `20261007_172137_558_다중가공_오류_9fc3cb` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_172137_558_다중가공_오류_9fc3cb/summary.json) |
| 28 | `20261007_180451_166_다중가공_오류_165420` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_180451_166_다중가공_오류_165420/summary.json) |
| 29 | `20261007_181632_958_다중가공_오류_a1dd6b` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_181632_958_다중가공_오류_a1dd6b/summary.json) |
| 30 | `20261007_183404_033_다중가공_오류_c03cef` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_183404_033_다중가공_오류_c03cef/summary.json) |
| 31 | `20261007_192126_580_다중가공_오류_4f48c3` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_192126_580_다중가공_오류_4f48c3/summary.json) |
| 32 | `20261007_202053_925_다중가공_오류_55a2fd` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_202053_925_다중가공_오류_55a2fd/summary.json) |
| 33 | `20261007_221623_924_다중가공_오류_c59ecd` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-07/20261007_221623_924_다중가공_오류_c59ecd/summary.json) |
| 34 | `20261008_040537_693_다중가공_오류_a714ae` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_040537_693_다중가공_오류_a714ae/summary.json) |
| 35 | `20261008_070227_447_다중가공_오류_3e38ea` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_070227_447_다중가공_오류_3e38ea/summary.json) |
| 36 | `20261008_193435_510_다중가공_오류_3d2362` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_193435_510_다중가공_오류_3d2362/summary.json) |
| 37 | `20261008_202607_624_다중가공_오류_c9cd93` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_202607_624_다중가공_오류_c9cd93/summary.json) |
| 38 | `20261008_205241_173_다중가공_오류_aeb749` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_205241_173_다중가공_오류_aeb749/summary.json) |
| 39 | `20261008_205626_975_다중가공_오류_f27b5b` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_205626_975_다중가공_오류_f27b5b/summary.json) |
| 40 | `20261008_213057_024_다중가공_오류_b89b5a` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_213057_024_다중가공_오류_b89b5a/summary.json) |
| 41 | `20261008_233158_237_다중가공_오류_d8e608` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_233158_237_다중가공_오류_d8e608/summary.json) |
| 42 | `20261008_233300_203_다중가공_오류_53892a` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_233300_203_다중가공_오류_53892a/summary.json) |
| 43 | `20261008_233307_877_다중가공_오류_46eadb` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_233307_877_다중가공_오류_46eadb/summary.json) |
| 44 | `20261008_233328_001_다중가공_오류_ab27b2` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_233328_001_다중가공_오류_ab27b2/summary.json) |
| 45 | `20261008_233424_315_다중가공_오류_0cd796` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-08/20261008_233424_315_다중가공_오류_0cd796/summary.json) |
| 46 | `20261009_030857_673_다중가공_오류_ef86a0` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_030857_673_다중가공_오류_ef86a0/summary.json) |
| 47 | `20261009_070014_590_다중가공_오류_27c4ff` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_070014_590_다중가공_오류_27c4ff/summary.json) |
| 48 | `20261009_114648_466_다중가공_오류_22063e` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_114648_466_다중가공_오류_22063e/summary.json) |
| 49 | `20261009_132011_501_다중가공_오류_e82aa2` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_132011_501_다중가공_오류_e82aa2/summary.json) |
| 50 | `20261009_134157_814_다중가공_오류_7a6fb4` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_134157_814_다중가공_오류_7a6fb4/summary.json) |
| 51 | `20261009_153034_563_다중가공_오류_5b0bd0` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_153034_563_다중가공_오류_5b0bd0/summary.json) |
| 52 | `20261009_161250_061_다중가공_오류_04724d` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_161250_061_다중가공_오류_04724d/summary.json) |
| 53 | `20261009_181302_400_다중가공_오류_b0fad1` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_181302_400_다중가공_오류_b0fad1/summary.json) |
| 54 | `20261009_193507_811_다중가공_오류_ac1a46` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_193507_811_다중가공_오류_ac1a46/summary.json) |
| 55 | `20261009_200354_285_다중가공_오류_779274` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_200354_285_다중가공_오류_779274/summary.json) |
| 56 | `20261009_211205_903_다중가공_오류_670260` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-09/20261009_211205_903_다중가공_오류_670260/summary.json) |
| 57 | `20261010_061544_029_다중가공_오류_a43189` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_061544_029_다중가공_오류_a43189/summary.json) |
| 58 | `20261010_164330_343_다중가공_오류_77bbd7` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_164330_343_다중가공_오류_77bbd7/summary.json) |
| 59 | `20261010_175050_979_다중가공_오류_125170` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_175050_979_다중가공_오류_125170/summary.json) |
| 60 | `20261010_183002_813_다중가공_오류_153786` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_183002_813_다중가공_오류_153786/summary.json) |
| 61 | `20261010_193714_577_다중가공_오류_ee5889` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_193714_577_다중가공_오류_ee5889/summary.json) |
| 62 | `20261010_210958_411_다중가공_오류_c59696` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_210958_411_다중가공_오류_c59696/summary.json) |
| 63 | `20261010_224803_392_다중가공_오류_fce110` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_224803_392_다중가공_오류_fce110/summary.json) |
| 64 | `20261010_234907_658_다중가공_오류_f59f86` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-10/20261010_234907_658_다중가공_오류_f59f86/summary.json) |
| 65 | `20261011_083524_686_다중가공_오류_aacddc` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-11/20261011_083524_686_다중가공_오류_aacddc/summary.json) |
| 66 | `20261011_115353_602_다중가공_오류_05f09b` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-11/20261011_115353_602_다중가공_오류_05f09b/summary.json) |
| 67 | `20261011_115459_989_다중가공_오류_256130` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-11/20261011_115459_989_다중가공_오류_256130/summary.json) |
| 68 | `20261011_171539_325_다중가공_오류_88bc1a` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-11/20261011_171539_325_다중가공_오류_88bc1a/summary.json) |
| 69 | `20261012_103036_306_다중가공_오류_68e230` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-12/20261012_103036_306_다중가공_오류_68e230/summary.json) |
| 70 | `20261012_153448_430_다중가공_오류_8926a9` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-12/20261012_153448_430_다중가공_오류_8926a9/summary.json) |
| 71 | `20261013_130735_537_다중가공_오류_b2701c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_130735_537_다중가공_오류_b2701c/summary.json) |
| 72 | `20261013_141648_988_다중가공_오류_b7dc34` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_141648_988_다중가공_오류_b7dc34/summary.json) |
| 73 | `20261013_171313_231_다중가공_오류_7ca153` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_171313_231_다중가공_오류_7ca153/summary.json) |
| 74 | `20261013_212456_531_다중가공_오류_1da5df` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_212456_531_다중가공_오류_1da5df/summary.json) |
| 75 | `20261013_215206_379_다중가공_오류_7f6b8c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_215206_379_다중가공_오류_7f6b8c/summary.json) |
| 76 | `20261014_225002_119_다중가공_오류_978d3f` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-14/20261014_225002_119_다중가공_오류_978d3f/summary.json) |
| 77 | `20261014_225037_737_다중가공_오류_0cf836` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-14/20261014_225037_737_다중가공_오류_0cf836/summary.json) |
| 78 | `20261014_225259_971_다중가공_오류_fa1b8d` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-14/20261014_225259_971_다중가공_오류_fa1b8d/summary.json) |
| 79 | `20261014_225514_754_다중가공_오류_881a47` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-14/20261014_225514_754_다중가공_오류_881a47/summary.json) |
| 80 | `20261014_230352_248_다중가공_오류_c4d66d` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-14/20261014_230352_248_다중가공_오류_c4d66d/summary.json) |
| 81 | `20261015_052440_176_다중가공_오류_c2bc1d` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-15/20261015_052440_176_다중가공_오류_c2bc1d/summary.json) |
| 82 | `20261015_185702_127_다중가공_오류_455ced` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-15/20261015_185702_127_다중가공_오류_455ced/summary.json) |
| 83 | `20261015_185707_343_다중가공_오류_69bdd5` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-15/20261015_185707_343_다중가공_오류_69bdd5/summary.json) |
| 84 | `20261015_201543_010_다중가공_오류_ff9f53` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-15/20261015_201543_010_다중가공_오류_ff9f53/summary.json) |
| 85 | `20261016_000111_535_다중가공_오류_cba1f2` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-16/20261016_000111_535_다중가공_오류_cba1f2/summary.json) |
| 86 | `20261016_051737_090_다중가공_오류_93d10b` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-16/20261016_051737_090_다중가공_오류_93d10b/summary.json) |
| 87 | `20261016_123646_658_다중가공_오류_544cd7` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-16/20261016_123646_658_다중가공_오류_544cd7/summary.json) |
| 88 | `20261016_224222_478_다중가공_오류_2a4c0c` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-16/20261016_224222_478_다중가공_오류_2a4c0c/summary.json) |
| 89 | `20261017_003423_332_다중가공_오류_479a9f` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-17/20261017_003423_332_다중가공_오류_479a9f/summary.json) |
| 90 | `20261017_162728_538_다중가공_오류_9d162b` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-17/20261017_162728_538_다중가공_오류_9d162b/summary.json) |
| 91 | `20261017_203358_583_다중가공_오류_faf956` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-17/20261017_203358_583_다중가공_오류_faf956/summary.json) |
| 92 | `20261018_175524_501_다중가공_오류_b9ce3e` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-18/20261018_175524_501_다중가공_오류_b9ce3e/summary.json) |
| 93 | `20261020_234501_149_다중가공_오류_04b71d` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-20/20261020_234501_149_다중가공_오류_04b71d/summary.json) |
| 94 | `20261021_023609_577_다중가공_오류_1caaf6` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-21/20261021_023609_577_다중가공_오류_1caaf6/summary.json) |
| 95 | `20261021_132452_590_다중가공_오류_652003` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-21/20261021_132452_590_다중가공_오류_652003/summary.json) |
| 96 | `20261022_234754_315_다중가공_오류_5a1537` | Not independently replayed | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-22/20261022_234754_315_다중가공_오류_5a1537/summary.json) |
| 97 | `20261023_060800_480_다중가공_오류_3afdff` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-23/20261023_060800_480_다중가공_오류_3afdff/summary.json) |
| 98 | `20261023_150720_292_다중가공_오류_a01d04` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-23/20261023_150720_292_다중가공_오류_a01d04/summary.json) |
| 99 | `20261023_153131_555_다중가공_오류_8ae168` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-23/20261023_153131_555_다중가공_오류_8ae168/summary.json) |
| 100 | `20261023_175806_055_다중가공_오류_83e291` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-23/20261023_175806_055_다중가공_오류_83e291/summary.json) |
| 101 | `20261024_033339_942_다중가공_오류_294154` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-24/20261024_033339_942_다중가공_오류_294154/summary.json) |
| 102 | `20261024_153253_640_다중가공_오류_3f0cd6` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-24/20261024_153253_640_다중가공_오류_3f0cd6/summary.json) |
| 103 | `20261024_181341_184_다중가공_오류_d6b9db` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-24/20261024_181341_184_다중가공_오류_d6b9db/summary.json) |
| 104 | `20261024_183512_324_다중가공_오류_e3a380` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-24/20261024_183512_324_다중가공_오류_e3a380/summary.json) |
| 105 | `20261024_233624_817_다중가공_오류_a55311` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-24/20261024_233624_817_다중가공_오류_a55311/summary.json) |
| 106 | `20261025_071501_242_다중가공_오류_3bca7a` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-25/20261025_071501_242_다중가공_오류_3bca7a/summary.json) |
| 107 | `20261025_071617_142_다중가공_오류_900f3e` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-25/20261025_071617_142_다중가공_오류_900f3e/summary.json) |
| 108 | `20261025_071735_473_다중가공_오류_0f6521` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-25/20261025_071735_473_다중가공_오류_0f6521/summary.json) |
| 109 | `20261026_115417_304_다중가공_오류_26dc63` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-26/20261026_115417_304_다중가공_오류_26dc63/summary.json) |
| 110 | `20261026_115823_510_다중가공_오류_81ead6` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-26/20261026_115823_510_다중가공_오류_81ead6/summary.json) |
| 111 | `20261026_161326_135_다중가공_오류_5dc7b7` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-26/20261026_161326_135_다중가공_오류_5dc7b7/summary.json) |
| 112 | `20261026_174515_690_다중가공_오류_0594dd` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-26/20261026_174515_690_다중가공_오류_0594dd/summary.json) |
| 113 | `20261026_190933_946_다중가공_오류_8e33a5` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-26/20261026_190933_946_다중가공_오류_8e33a5/summary.json) |
| 114 | `20261027_015827_686_다중가공_오류_67e542` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-27/20261027_015827_686_다중가공_오류_67e542/summary.json) |
| 115 | `20261028_104145_140_다중가공_오류_1497e5` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-28/20261028_104145_140_다중가공_오류_1497e5/summary.json) |
| 116 | `20261028_110114_166_다중가공_오류_3f74e2` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-28/20261028_110114_166_다중가공_오류_3f74e2/summary.json) |
| 117 | `20261028_133046_928_다중가공_오류_e1cd91` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-28/20261028_133046_928_다중가공_오류_e1cd91/summary.json) |
| 118 | `20261028_143857_172_다중가공_오류_d49a5d` | L2/L3 sample safety assertion | [summary](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-28/20261028_143857_172_다중가공_오류_d49a5d/summary.json) |

## Other historical error inventory (out of F9 scope)

For repository-wide audit completeness the remaining 120 source paths are listed by identity only; they have **not** been individually regression-tested as F9.

### Older auto-altering: 66

- [20260930_231558_265_자동_가공_오류_ddebe8](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-30/20260930_231558_265_자동_가공_오류_ddebe8/summary.json)
- [20260930_233152_470_자동_가공_오류_13c1e0](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-30/20260930_233152_470_자동_가공_오류_13c1e0/summary.json)
- [20260930_234723_814_자동_가공_오류_e11ae8](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-30/20260930_234723_814_자동_가공_오류_e11ae8/summary.json)
- [20261001_004448_759_자동_가공_시작_실패_525987](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_004448_759_자동_가공_시작_실패_525987/summary.json)
- [20261001_004504_860_자동_가공_시작_실패_0c3521](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_004504_860_자동_가공_시작_실패_0c3521/summary.json)
- [20261001_004538_612_자동_가공_오류_f23fb1](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_004538_612_자동_가공_오류_f23fb1/summary.json)
- [20261001_010657_306_자동_가공_오류_9e4fd7](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_010657_306_자동_가공_오류_9e4fd7/summary.json)
- [20261001_090701_991_자동_가공_시작_실패_825e6f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_090701_991_자동_가공_시작_실패_825e6f/summary.json)
- [20261001_120429_410_자동_가공_오류_b8b21c](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_120429_410_자동_가공_오류_b8b21c/summary.json)
- [20261001_120633_795_자동_가공_시작_실패_7ca7d9](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_120633_795_자동_가공_시작_실패_7ca7d9/summary.json)
- [20261001_122100_378_자동_가공_오류_0a6aee](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_122100_378_자동_가공_오류_0a6aee/summary.json)
- [20261001_123239_810_자동_가공_오류_ce6ac2](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_123239_810_자동_가공_오류_ce6ac2/summary.json)
- [20261001_123810_210_자동_가공_오류_85c603](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_123810_210_자동_가공_오류_85c603/summary.json)
- [20261001_124533_719_자동_가공_오류_02df5f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_124533_719_자동_가공_오류_02df5f/summary.json)
- [20261001_125310_530_자동_가공_오류_76c7de](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_125310_530_자동_가공_오류_76c7de/summary.json)
- [20261001_125652_661_자동_가공_오류_1e3999](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_125652_661_자동_가공_오류_1e3999/summary.json)
- [20261001_125727_817_자동_가공_오류_a165cf](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_125727_817_자동_가공_오류_a165cf/summary.json)
- [20261001_130212_307_자동_가공_오류_366c23](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_130212_307_자동_가공_오류_366c23/summary.json)
- [20261001_130640_664_자동_가공_오류_a918c9](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_130640_664_자동_가공_오류_a918c9/summary.json)
- [20261001_131353_465_자동_가공_오류_fa4e4d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_131353_465_자동_가공_오류_fa4e4d/summary.json)
- [20261001_131947_519_자동_가공_오류_9a43ff](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_131947_519_자동_가공_오류_9a43ff/summary.json)
- [20261001_132643_638_자동_가공_오류_4813f6](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_132643_638_자동_가공_오류_4813f6/summary.json)
- [20261001_140218_919_자동_가공_오류_be5b41](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_140218_919_자동_가공_오류_be5b41/summary.json)
- [20261001_141034_080_자동_가공_오류_952c8e](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_141034_080_자동_가공_오류_952c8e/summary.json)
- [20261001_141230_484_자동_가공_오류_e28e82](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_141230_484_자동_가공_오류_e28e82/summary.json)
- [20261001_150948_028_자동_가공_오류_8dd88f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_150948_028_자동_가공_오류_8dd88f/summary.json)
- [20261001_151432_320_자동_가공_오류_11348d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_151432_320_자동_가공_오류_11348d/summary.json)
- [20261001_154913_646_자동_가공_오류_b834f2](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_154913_646_자동_가공_오류_b834f2/summary.json)
- [20261001_154939_831_자동_가공_오류_7f3665](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_154939_831_자동_가공_오류_7f3665/summary.json)
- [20261001_170734_776_자동_가공_오류_67892f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_170734_776_자동_가공_오류_67892f/summary.json)
- [20261001_180739_629_자동_가공_오류_cb8e6d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_180739_629_자동_가공_오류_cb8e6d/summary.json)
- [20261001_184827_341_자동_가공_오류_3e5ea3](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_184827_341_자동_가공_오류_3e5ea3/summary.json)
- [20261002_001447_486_자동_가공_오류_9d6a01](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_001447_486_자동_가공_오류_9d6a01/summary.json)
- [20261003_110739_887_자동_가공_오류_34a720](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_110739_887_자동_가공_오류_34a720/summary.json)
- [20261003_114054_616_자동_가공_오류_3a0f44](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_114054_616_자동_가공_오류_3a0f44/summary.json)
- [20261003_114106_940_자동_가공_오류_d4df62](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_114106_940_자동_가공_오류_d4df62/summary.json)
- [20261003_132413_673_자동_가공_오류_0ad23b](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_132413_673_자동_가공_오류_0ad23b/summary.json)
- [20261003_141352_919_자동_가공_오류_f96d10](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_141352_919_자동_가공_오류_f96d10/summary.json)
- [20261003_143416_149_자동_가공_오류_a9e657](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_143416_149_자동_가공_오류_a9e657/summary.json)
- [20261003_161953_899_자동_가공_오류_774c4b](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_161953_899_자동_가공_오류_774c4b/summary.json)
- [20261003_192404_433_자동_가공_오류_b3641f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_192404_433_자동_가공_오류_b3641f/summary.json)
- [20261004_010036_441_자동_가공_오류_736117](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_010036_441_자동_가공_오류_736117/summary.json)
- [20261004_055122_417_자동_가공_오류_711700](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_055122_417_자동_가공_오류_711700/summary.json)
- [20261004_080126_682_자동_가공_오류_57ea96](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_080126_682_자동_가공_오류_57ea96/summary.json)
- [20261004_112633_053_자동_가공_오류_045780](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_112633_053_자동_가공_오류_045780/summary.json)
- [20261004_112811_388_자동_가공_오류_9289a7](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_112811_388_자동_가공_오류_9289a7/summary.json)
- [20261004_115401_146_자동_가공_오류_74a005](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_115401_146_자동_가공_오류_74a005/summary.json)
- [20261004_115732_137_자동_가공_오류_2a02ad](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_115732_137_자동_가공_오류_2a02ad/summary.json)
- [20261004_120532_593_자동_가공_오류_1cfdd8](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_120532_593_자동_가공_오류_1cfdd8/summary.json)
- [20261004_123236_948_자동_가공_오류_6f84f0](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_123236_948_자동_가공_오류_6f84f0/summary.json)
- [20261004_123454_389_자동_가공_오류_11ef44](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_123454_389_자동_가공_오류_11ef44/summary.json)
- [20261004_123556_188_자동_가공_오류_6017b1](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_123556_188_자동_가공_오류_6017b1/summary.json)
- [20261004_162858_227_자동_가공_오류_e37a5e](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_162858_227_자동_가공_오류_e37a5e/summary.json)
- [20261004_175836_783_자동_가공_오류_a46e24](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_175836_783_자동_가공_오류_a46e24/summary.json)
- [20261004_190843_872_자동_가공_오류_ee53a4](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_190843_872_자동_가공_오류_ee53a4/summary.json)
- [20261004_193604_149_자동_가공_오류_d64cc7](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_193604_149_자동_가공_오류_d64cc7/summary.json)
- [20261004_193657_672_자동_가공_오류_fa1f91](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_193657_672_자동_가공_오류_fa1f91/summary.json)
- [20261004_202730_615_자동_가공_오류_36e345](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_202730_615_자동_가공_오류_36e345/summary.json)
- [20261004_210325_823_자동_가공_오류_a02501](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_210325_823_자동_가공_오류_a02501/summary.json)
- [20261005_053915_172_자동_가공_오류_333805](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_053915_172_자동_가공_오류_333805/summary.json)
- [20261005_065112_304_자동_가공_오류_22a408](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_065112_304_자동_가공_오류_22a408/summary.json)
- [20261005_072149_357_자동_가공_오류_54ca51](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_072149_357_자동_가공_오류_54ca51/summary.json)
- [20261005_082836_739_자동_가공_오류_96b0cd](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_082836_739_자동_가공_오류_96b0cd/summary.json)
- [20261005_100516_767_자동_가공_오류_541025](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_100516_767_자동_가공_오류_541025/summary.json)
- [20261005_113359_715_자동_가공_오류_3c38d2](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_113359_715_자동_가공_오류_3c38d2/summary.json)
- [20261005_170318_276_자동_가공_오류_9c5445](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-05/20261005_170318_276_자동_가공_오류_9c5445/summary.json)

### Crafting: 32

- [20261002_132523_737_제작_오류_03c854](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_132523_737_제작_오류_03c854/summary.json)
- [20261002_153240_183_제작_오류_d49f2b](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_153240_183_제작_오류_d49f2b/summary.json)
- [20261002_172124_615_제작_오류_17c3b8](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_172124_615_제작_오류_17c3b8/summary.json)
- [20261002_173921_318_제작_오류_5d135b](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_173921_318_제작_오류_5d135b/summary.json)
- [20261002_173946_783_제작_오류_0151b5](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_173946_783_제작_오류_0151b5/summary.json)
- [20261002_183657_369_제작_오류_538b2d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_183657_369_제작_오류_538b2d/summary.json)
- [20261002_194724_635_제작_오류_5e48b5](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_194724_635_제작_오류_5e48b5/summary.json)
- [20261002_202026_692_제작_오류_20e4ef](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_202026_692_제작_오류_20e4ef/summary.json)
- [20261002_205559_050_제작_오류_968fc5](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_205559_050_제작_오류_968fc5/summary.json)
- [20261002_212547_247_제작_오류_3fcbe6](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_212547_247_제작_오류_3fcbe6/summary.json)
- [20261002_213850_712_제작_오류_4adf44](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_213850_712_제작_오류_4adf44/summary.json)
- [20261002_221656_571_제작_오류_8f860f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_221656_571_제작_오류_8f860f/summary.json)
- [20261002_234413_766_제작_오류_7ac7ca](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_234413_766_제작_오류_7ac7ca/summary.json)
- [20261003_001856_571_제작_오류_e0ffd7](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_001856_571_제작_오류_e0ffd7/summary.json)
- [20261003_004816_716_제작_오류_1a4a5d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_004816_716_제작_오류_1a4a5d/summary.json)
- [20261003_010210_234_제작_오류_8bcfb6](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_010210_234_제작_오류_8bcfb6/summary.json)
- [20261003_012235_743_제작_오류_13232e](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_012235_743_제작_오류_13232e/summary.json)
- [20261003_110344_170_제작_오류_bef4fa](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_110344_170_제작_오류_bef4fa/summary.json)
- [20261003_122230_520_제작_오류_f96626](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_122230_520_제작_오류_f96626/summary.json)
- [20261003_163024_998_제작_오류_185838](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_163024_998_제작_오류_185838/summary.json)
- [20261003_192821_433_제작_오류_c4d8be](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_192821_433_제작_오류_c4d8be/summary.json)
- [20261003_201347_097_제작_오류_6a4261](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_201347_097_제작_오류_6a4261/summary.json)
- [20261003_203211_996_제작_오류_0a4e3f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_203211_996_제작_오류_0a4e3f/summary.json)
- [20261003_210950_118_제작_오류_964cc1](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_210950_118_제작_오류_964cc1/summary.json)
- [20261003_215551_636_제작_오류_34e904](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_215551_636_제작_오류_34e904/summary.json)
- [20261003_224128_380_제작_오류_081182](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_224128_380_제작_오류_081182/summary.json)
- [20261003_233240_965_제작_오류_74d598](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_233240_965_제작_오류_74d598/summary.json)
- [20261003_235908_399_제작_오류_6495e8](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-03/20261003_235908_399_제작_오류_6495e8/summary.json)
- [20261004_004820_287_제작_오류_3f446a](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-04/20261004_004820_287_제작_오류_3f446a/summary.json)
- [20261020_203159_667_제작_오류_c21da3](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-20/20261020_203159_667_제작_오류_c21da3/summary.json)
- [20261027_231506_188_제작_오류_7195ee](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-27/20261027_231506_188_제작_오류_7195ee/summary.json)
- [20261027_233206_238_제작_오류_046916](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-27/20261027_233206_238_제작_오류_046916/summary.json)

### Other: 22

- [20260929_053030_718_어비스_매크로_오류_71a5e6](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-29/20260929_053030_718_어비스_매크로_오류_71a5e6/summary.json)
- [20260929_120625_801_어비스_매크로_오류_8f8e9d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-29/20260929_120625_801_어비스_매크로_오류_8f8e9d/summary.json)
- [20260929_123740_965_어비스_매크로_오류_132d53](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-29/20260929_123740_965_어비스_매크로_오류_132d53/summary.json)
- [20260929_204628_087_어비스_단계_자동복구_시작_e48152](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-09-29/20260929_204628_087_어비스_단계_자동복구_시작_e48152/summary.json)
- [20261001_010025_512_자동채집_오류_e1702c](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_010025_512_자동채집_오류_e1702c/summary.json)
- [20261001_010051_537_자동채집_시작_실패_d8a997](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_010051_537_자동채집_시작_실패_d8a997/summary.json)
- [20261001_010150_932_자동채집_오류_f7d692](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_010150_932_자동채집_오류_f7d692/summary.json)
- [20261001_061120_891_던전_강제_퇴장_지연_6d1c66](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_061120_891_던전_강제_퇴장_지연_6d1c66/summary.json)
- [20261001_061151_660_어비스_매크로_오류_33079d](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_061151_660_어비스_매크로_오류_33079d/summary.json)
- [20261001_090735_214_자동_채집_시작_실패_f89019](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_090735_214_자동_채집_시작_실패_f89019/summary.json)
- [20261001_110446_673_자동_채집_오류_b61d61](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_110446_673_자동_채집_오류_b61d61/summary.json)
- [20261001_204707_090_자동_채집_오류_fe5343](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_204707_090_자동_채집_오류_fe5343/summary.json)
- [20261001_204738_283_자동_채집_오류_df26b1](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_204738_283_자동_채집_오류_df26b1/summary.json)
- [20261001_210540_452_자동_채집_오류_b85538](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_210540_452_자동_채집_오류_b85538/summary.json)
- [20261001_210604_156_자동_채집_오류_37367b](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_210604_156_자동_채집_오류_37367b/summary.json)
- [20261001_210709_397_자동_채집_오류_124ff5](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_210709_397_자동_채집_오류_124ff5/summary.json)
- [20261001_211030_757_자동_채집_오류_3aa096](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_211030_757_자동_채집_오류_3aa096/summary.json)
- [20261001_211338_404_자동_채집_오류_fa4b15](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_211338_404_자동_채집_오류_fa4b15/summary.json)
- [20261001_215211_245_자동_채집_오류_bccfa0](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-01/20261001_215211_245_자동_채집_오류_bccfa0/summary.json)
- [20261002_060943_678_던전_강제_퇴장_지연_d4517f](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_060943_678_던전_강제_퇴장_지연_d4517f/summary.json)
- [20261002_061014_820_어비스_매크로_오류_250802](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-02/20261002_061014_820_어비스_매크로_오류_250802/summary.json)
- [20261013_002953_757_어비스_매크로_오류_378c41](https://github.com/skwlrna/Mbi_auto_log/blob/main/runtime-errors/2026-10-13/20261013_002953_757_어비스_매크로_오류_378c41/summary.json)

