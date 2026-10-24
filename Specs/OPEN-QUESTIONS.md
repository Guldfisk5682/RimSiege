# OPEN — do not commit a decision to main; wait for Fisk

1. **Height space mapping**
   - IsoRimZ `Heights.Storey = 1.02f` (wall units per floor)
   - CE `CollisionVertical.WallCollisionHeight = 2f` and projectile `shotHeight` in CE space
   - How do we convert `floorIndex * Storey` into CE `shotHeight` / collision Y without fighting CE roofs?

2. **Patch surface**
   - Preferred working hypothesis: patch CE only (CollisionVertical / Verb_LaunchProjectileCE / ProjectileCE launch height), leave IsoRimZ CombatGate off.
   - Alternative: force CombatGate on under CE (rejected for v1 unless Fisk overrides).

3. **Lobbed vs direct**
   - Mortars / grenades vs rifle LOS across floors — use ProjectZ.Api.RegisterLobbedShot or CE gravity only?

4. **SiegeElevation relationship**
   - Separate mod later vs package inside 3Dsiege — deferred.
