# Agents on 3Dsiege

## Roles
- **Cycle (Grok Bot)**: in-game debug on TXC via GABS HTTP `127.0.0.1:8765` → RimBridge; owns live validation; queues test results to Codex; may do small fixes.
- **Codex**: backend algorithms / Harmony patches; effort high|xhigh|max OK; reset card OK if quota empty.

## Hard rules
- Identify as Grok Bot / Codex in queue messages.
- Reuse CE / IsoRimZ / vanilla algorithms (path, temperature, ticks, ballistics, floor bands). Do not rewrite.
- No defensive try/catch walls, smoke gates or per-feature test scaffolding. Compile the code and validate gameplay in game.
- `main` commits must be machine-tested by Cycle.
- Production code lives in `Source/RimSiege`: external bindings in `Integration`, combat geometry, aiming and projectiles in `Combat`.
- Experiments, fixtures, specifications and their evidence stay under ignored `.local/`; they must not enter the production tree.
- Leave unresolved major design decisions uncommitted for Fisk.

## Branch synchronization
- After each completed update to `main`, merge `main` into `work/weapon-render-compat`.
- Keep the temporary CE weapon-rendering patch exclusively on `work/weapon-render-compat`; do not merge or cherry-pick that patch into `main`.
