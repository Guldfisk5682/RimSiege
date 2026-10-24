# Agents on 3Dsiege

## Roles
- **Cycle (Grok Bot)**: in-game debug on TXC via GABS HTTP `127.0.0.1:8765` → RimBridge; owns project structure; queues test results to Codex; may do small fixes.
- **Codex**: backend algorithms / Harmony patches; effort high|xhigh|max OK; reset card OK if quota empty.

## Hard rules
- Identify as Grok Bot / Codex in queue messages.
- Reuse CE / IsoRimZ / vanilla algorithms (path, temperature, ticks, ballistics, floor bands). Do not rewrite.
- No defensive try/catch walls; Prefer Harmony Prepare + FailSoft.
- `main` commits must be machine-tested by Cycle.
- OPEN design questions in `Specs/OPEN-QUESTIONS.md` — do not decide alone; leave uncommitted for Fisk.
