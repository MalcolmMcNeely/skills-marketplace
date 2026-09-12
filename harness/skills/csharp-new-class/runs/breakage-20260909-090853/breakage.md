# The two broken versions

Issue #6. model=claude-opus-5[1m] cli=2.1.248 (Claude Code). 256 runs, 107 void, 01:04:16, $46.99.

## The matrix

| Arm | What changed | Layer 3 | Layer 4 | As expected |
|---|---|---|---|---|
| `description` | description broken, body correct | **RED** 25/60, gate 53 | green 5/5 held | yes |
| `body` | body broken, description correct | green 6/6, gate 5 | **RED** 0/8 held | yes |
| `control` | prose reworded, both rules intact | green 60/60, gate 53 | green 5/5 held | yes |
| `good` | the frozen fixture, unchanged | not run | green 5/5 held | yes |

## What #6 asked

| Question | Answer |
|---|---|
| Layer 3 catches a description break, and layer 4 does not | yes |
| Layer 4 catches a body break, and layer 3 does not | yes |
| The two failures are distinguishable | yes |
| The controls stayed quiet | yes |

## Which assertion fell over

Passes out of valid contract runs, per assertion.

| Arm | Guards | Signals |
|---|---|---|
| `description` | A3 5/5, A4 5/5, A5 5/5 | A6 5/5, A7 5/5 |
| `body` | A3 8/8, A4 8/8, A5 8/8 | A6 0/8, A7 0/8 |
| `control` | A3 5/5, A4 5/5, A5 5/5 | A6 5/5, A7 5/5 |
| `good` | A3 5/5, A4 5/5, A5 5/5 | A6 5/5, A7 5/5 |

## Journals

- `description` — `C:/Projects/skills-marketplace/harness/captured/breakage-20260909-090853\break-description.jsonl`
- `body` — `C:/Projects/skills-marketplace/harness/captured/breakage-20260909-090853\break-body.jsonl`
- `control` — `C:/Projects/skills-marketplace/harness/captured/breakage-20260909-090853\break-control.jsonl`
- `good` — `C:/Projects/skills-marketplace/harness/captured/breakage-20260909-090853\break-good.jsonl`
