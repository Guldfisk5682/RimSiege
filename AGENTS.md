# RimSiege Agent Guide

## Project

RimSiege is a 3D RimWorld siege mod built on IsoRim Waylith and IsoRimZ (ProjectZ), currently targeting RimWorld 1.7 Unstable. Its two main areas are:

- Combat Extended (CE) compatibility for cross-floor combat: coordinates, line of fire, trajectories, collision, explosions, fire, fragments, suppression, and shields.
- Siege mechanics extending vanilla, IsoRimZ, and CE behavior: ladder carriers and recovery, enhanced assaults, artillery support, and fortified gun positions.

The display name is `RimSiege`, and the author is `Guldfisk5682`. The workspace and installed mod may still use the historical name `3Dsiege`. Production code is under `Source/RimSiege`, and the package ID remains `Guld.ThreeDSiege` for existing save references. Native mod settings use the installation folder name and mod class; preserve an existing installation's folder name during upgrades. Verify behavior against the checked-out branch, actual dependency assemblies, and installed mod combination.

## Long-lived branches

| Branch | Responsibility |
| --- | --- |
| `main` | Shared, validated integration of CE compatibility and RimSiege gameplay. |
| `work/ce-patch` | Ongoing 3D compatibility work for CE, special weapons, and related combat mechanics. |
| `work/siege-assault` | Ongoing development of RimSiege siege mechanics, including ladders, raid tactics, and artillery support. |
| `work/weapon-render-compat` | The latest mainline plus the temporary CE weapon-rendering patch, maintained as the release combination with working weapon rendering. |

After merging or rebasing work from `work/siege-assault` or `work/ce-patch` into `main`, open separate synchronization PRs in these directions:

1. `main` → `work/weapon-render-compat`, keeping release builds current and validating the rendering combination.
2. `main` → `work/ce-patch`, keeping future compatibility work current with shared changes.
3. `main` → `work/siege-assault`, keeping future siege development current with compatibility fixes and shared changes.

Include the originating development branch in this synchronization review. Check both content differences and ancestry; report an already synchronized branch instead of creating empty PRs. Propagate project-wide guidance changes to these branches as well.

Keep the temporary weapon-rendering patch exclusively on `work/weapon-render-compat`. Do not merge, cherry-pick, or copy it into `main` or either development branch. Build releases from the synchronized and validated rendering branch. If IsoRim later supplies equivalent native rendering, remove the temporary patch after verifying that replacement in game.

Prefer traceable merges when synchronizing shared long-lived branches. Synchronization PRs must identify the mainline revision, explain conflict resolutions, and account for branch-specific patches. All PRs follow the validation requirements below.

## Engineering principles

- Reuse vanilla, CE, and IsoRimZ algorithms and lifecycles through existing APIs or narrow patches. Inspect the original implementation before changing pathfinding, targeting, ballistic integration, damage, construction, hauling, reloading, temperature, or tick scheduling.
- Preserve complete native trigger semantics, including probability, filters, and state. Checking an event category alone is not equivalent to invoking its original trigger.
- Keep cohesive modules with clear boundaries between external bindings, geometry, combat adaptation, and raid policy. Avoid catch-all utilities and oversized patch entry points.
- Apply compatibility through actual inheritance, interfaces, or common call sites. Put gameplay choices in their configuration and policy layers; avoid restricting general compatibility to a few weapon defNames.
- Fix causes while retaining native formulas and behavior. Do not hide failures behind broad try/catch blocks, meaningless gates, silent fallbacks, or redundant null guards.
- Bound expensive planning queries and reuse native reachability and path services. Give temporary query results explicit lifetimes. Avoid continuous tick scans for one-time planning and retained references to abandoned batteries, operators, or target snapshots.
- Keep production code and release data in the production tree. Experiments, temporary probes, fixtures, test saves, draft specifications, and debugging evidence belong in ignored `.local/` or a separate test tree/branch, never in `main` or release packages.
- Do not add a new test harness, smoke test, or gate for every feature. Retain reusable tests with real regression value; basic static checks and gameplay validation serve different purposes.
- Follow the current user's instructions and confirmed design decisions. Leave unresolved major design decisions reviewable and unmerged, and explain them to the maintainer.

## Structure and ownership

Some modules may exist only on their development branch until integration. Check the current branch and the corresponding long-lived branch before extending or debugging a feature.

```text
Source/RimSiege/
  RimSiegeMod.cs          Mod entry point, Harmony registration, settings UI
  SiegeSettings.cs        Optional siege behavior and artillery settings
  Integration/           External vanilla/CE/IsoRimZ APIs, reflection bindings,
                         and adapters to existing algorithms
  Combat/
    Geometry/            Floor/map-band coordinates, CE heights, bounds,
                         floor planes, and visual coordinate conversion
    Aiming/              3D lines of fire, cover, range, indirect fire, lasers,
                         psychic weapons, and related combat compatibility
    Projectiles/         Projectile positions, collision, blockers, impacts,
                         fuses, fragments, shields, and projectile visuals
    TargetAcquisitionPatches.cs
                         CE acquisition calls bridged to IsoRimZ combat logic
    Rendering/           Temporary CE equipment rendering, rendering branch only
  Raids/                 Ladder items, carrier duties, recovery, assault routes,
                         and raid lifecycle integration
    Artillery/           Profiles, breach targets, camp planning, deployment,
                         supplies, operators, and artillery lifecycle
Defs/                    Items and gameplay configuration, including ladder kits
                         and artillery profiles
Patches/                 XML extensions to vanilla definitions and think trees
Languages/               Localized settings, item descriptions, raid notifications
Assemblies/              Ignored build output; only distributable RimSiege DLLs
About/                   Mod metadata, dependencies, and load order
LoadFolders.xml          Version-specific selection of the root content folder
LICENSE                  MIT license
.local/                  Ignored experiments, specs, probes, evidence, and archives
```

### Starting points for changes

| Area | Start here |
| --- | --- |
| Floor offsets, doubled heights, collision or visual drift | `Combat/Geometry`: `FloorCoordinates`, `CombatHeights`, `FloorPlanes`, and `VisualCoordinates`; then trace the relevant projectile call sites. |
| Range, cover, line of fire, target acquisition, firing positions | `Combat/Aiming`, `TargetAcquisitionPatches`, and `Integration/IsoCombatAi`. Preserve IsoRimZ targeting and movement rules. |
| Explosions, fire, indirect fire, lasers, suppression, shields | The corresponding `Aiming`/`Projectiles` patches and original CE/vanilla methods. Identify the actual runtime verb and projectile types first. |
| Ladder carrying, recovery after incapacitation, deployment, climbing | `Raids`: `LadderKit`, `SiegeRaidMap`, `SiegeRoles`, `RaidLifecyclePatches`, and deployment patches; external ladder/route bindings are in `Integration/IsoSiege*`. |
| Camp position, artillery configuration, breach targets | `ArtilleryRaidPolicy`, `ArtilleryProfile`, `SupportRoute`, `CampSiting`, `CampField`, `CampCandidate`, `CampPlanner`, `ArtilleryTargets`, and `ArtilleryTargeting`. |
| Construction, operators, supplies, infantry release, abandonment, quest completion | `LordJob_Artillery`, `NativeSiegePatches`, `ArtilleryCamp`, `ArtilleryMap`, `ArtilleryCompletion`, and ownership patches. |
| Weapon rendering | `Combat/Rendering/CeEquipmentDrawMeshCompat.cs` on the rendering branch. Projectile visuals separately use `Geometry` and `Projectiles`. |

`Integration` binds external mechanisms without owning raid policy. `Combat` supplies combat adaptation and geometry without depending on artillery gameplay decisions. `Raids` composes these capabilities into siege rules. Temporary planning objects and query caches must not become persistent save data or long-lived map indexes.

Artillery-supported raids retain their original ordinary, smart, or breaching strategy and pawn generation. `LordJob_Artillery` inherits `LordJob_Siege` for camp preparation; infantry handoff invokes the original strategy's `MakeLordJob`. Identify the active phase before changing behavior so construction interruption rules do not accidentally replace the entire raid lifecycle.

### Coordinate conventions

Keep ProjectZ map-band coordinates, CE ground x/z with continuous ballistic height, and IsoRim visual coordinates distinct. Use existing `Geometry` and `Integration` conversions; `CombatHeights` is the source of truth for height mapping.

Do not globally rewrite real `Thing.DrawPos`, `TrueCenter`, or map positions to repair a local call site. Establish the coordinate space expected by each caller and check launch, target, collision, explosion, and rendering paths for duplicate conversion.

## Building and deployment

The build entry point is `Source/RimSiege/RimSiege.csproj`, currently targeting .NET 10. Locate the game and dependency assemblies in the current environment. Configure paths through project properties such as `RimWorldDir`, `HarmonyPath`, `CombatExtendedPath`, `IsoRimPath`, and `ProjectZPath`.

```sh
dotnet build Source/RimSiege/RimSiege.csproj -c Release -p:RimWorldDir="/path/to/RimWorld" --nologo
```

Run relevant compilation, XML syntax, and diff checks. A successful build establishes static validity, not runtime regression coverage or gameplay correctness.

Release builds write `Assemblies/RimSiege.dll` at the repository root. `LoadFolders.xml` loads that root for RimWorld 1.7. Keep shared definitions, XML patches, and translations in the root `Defs`, `Patches`, and `Languages` directories. Introduce version subdirectories only when supported game versions actually need different content.

The required mods are Harmony, Combat Extended, Combat Extended Armory, IsoRim Waylith, and IsoRimZ. Keep `About/About.xml` and the README consistent. Release packages contain runtime content, mod metadata, the README, and the license; exclude source/build intermediates, dependency assemblies, and all experimental probes or `.local` content. Build the published rendering combination from the synchronized and game-validated rendering branch.

Before deployment, verify the target installation, loaded mod combination, source revision, and DLL SHA256. If the running game locks a DLL, finish the reviewable build first, then coordinate one exit with the human tester and verify hashes after replacement. Do not force-kill the game or overwrite saves. Keep experimental probes separate from production assemblies and out of release packages.

## GABS and RimBridge

Agents must participate in game-state inspection and debugging rather than relying solely on source reasoning or successful compilation.

1. Query game listings and status through GABS first, then prefer attaching to the existing process. Discover RimBridge capabilities and their parameter schemas. Use current connection configuration rather than hardcoded machine-specific ports, and never expose credentials in logs or replies.
2. Start with read-only inspection of the current tick, pause state, map, loaded mods, objects, and complete exception stacks. Correlate logs and observations with the same process and tested build.
3. Use developer tools and existing probes for reproducible scenarios, then observe native AI behavior. Forced shots or assigned jobs do not establish automatic acquisition, natural reloading, or raid lifecycle correctness.
4. By default, the human manages saves, exit, startup, and reload; the agent builds, deploys, and inspects through the bridge. Follow session-specific authorization for further game control. Avoid repeated pause/restart/reload loops; investigate configuration and the actual process before restarting a connection failure. Read a script's commands before assuming its filename implies read-only behavior.
5. When multiple agents cooperate, establish a single game-control owner. Other participants should use read-only observations and log analysis to avoid overwriting the test scene.
6. Temporary probes must remain observational. In particular, check Harmony handling of `ref/out` arguments and return values so logging cannot modify business results. Keep probes and their evidence in `.local/` or a separate test tree.

## Required validation before PRs

**Before opening any PR, including branch-synchronization PRs, complete sufficient regression testing for the change and obtain in-game testing with actual human participation.** Agent-only observation and successful builds do not replace this requirement.

- Validate the actual proposed integration result and deployed DLL. Rendering combinations require validation of that branch's build, not a pass borrowed from another build.
- Cover relevant normal flows, same-floor regressions, cross-floor directions and occlusion, and affected state transitions. Raid changes should exercise applicable construction, combat interruption, infantry handoff, operator recovery, abandonment, and save/load behavior.
- For performance-sensitive planning, targeting, or cache changes, record query counts, timings, and object lifetimes. Do not extrapolate a focused pass to long saves or untested stress scenarios.
- Record the tester, revision or DLL hash, mod combination, scenarios, outcomes, remaining issues, and coverage limits. A human's pass applies only to the scenarios actually tested.
- If human gameplay results are pending, finish implementation and static checks, retain a deployable build, and coordinate testing before opening the PR.

PR descriptions must explain the concrete problem, resulting behavior, reuse of native mechanisms, validation, and material limits for a reviewer without this conversation. Synchronization PRs must identify their mainline source revision and treatment of branch-specific patches.
