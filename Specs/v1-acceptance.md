# v1 acceptance — basic cross-floor combat + CE compatibility

## Goal
With Combat Extended + IsoRim Waylith + IsoRimZ loaded on RimWorld 1.7 Unstable, a drafted colonist on floor A can target and fire at a hostile on floor B (adjacent storey) using CE ballistics, with LOS/height informed by IsoRimZ floor bands.

## Non-goals (v1)
- SiegeElevation ladders / siege walls
- Rewriting pathfinding, temperature exchange, or tick schedulers
- Re-enabling IsoRimZ CombatGate patches wholesale while CE is loaded (double height systems)

## Acceptance (DevMode or persistent save)
1. Player.log shows `[ProjectZ] Combat Extended is loaded` (combat patches off) AND `[3Dsiege]` bridge active.
2. Same-floor CE fire still works (regression).
3. Cross-floor: shooter floor 1, target floor 0 (or reverse), CE verb can acquire target when open sky column; miss/blocked when solid slab blocks LOS per chosen height map.
4. No defensive try/catch spam; FailSoft-style opt-out only where Harmony Prepare fails.
5. main branch commits only after items 2–3 pass on this machine via GABS/RimBridge.

## Reuse
- Floors: `ProjectZ.Api` (FloorOf, HasFloors, CellOnFloor, Distance3D, RegisterLobbedShot if needed)
- Ballistics: CE `CollisionVertical`, `ProjectileCE`, `Verb_LaunchProjectileCE`
- Path / temperature / ticks: vanilla + IsoRimZ existing patches — do not fork
