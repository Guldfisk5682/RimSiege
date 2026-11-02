# RimSiege Agent Guide

## Project and dependency ownership

RimSiege is a 3D RimWorld siege mod built on IsoRim Waylith, IsoRimZ (ProjectZ), and IsoRim Waylith - Combat Extended Pack (CEPack), currently targeting RimWorld 1.7 Unstable. This guide describes the CEPack-based line introduced by the 0.4 migration.

- CEPack owns general CE combat adaptation: floor frames, aiming, line of fire, trajectories, collision, explosions, fire, fragments, suppression, shields, and combat rendering.
- ProjectZ owns floors, openings, stairs and ladder links, movement, slab damage, and collapse.
- RimSiege owns siege gameplay: ladder kits and carriers, recovery and clearance policy, enhanced raids, artillery planning, fortified camps, construction, operators, and supplies. It consumes CEPack's public APIs for placement queries, registered target geometry, and surface-explosion rules.

The display name is `RimSiege`, and the author is `Guldfisk5682`. The 0.4 package ID is `Guldfisk.RimSiege`; legacy releases used `Guld.ThreeDSiege`. The workspace may still be named `3Dsiege`. Treat package migrations as explicit release changes. Preserve namespaces, defNames, saved types/fields, and existing installation-folder settings unless a migration is deliberately implemented. Historical strings used as Harmony owners or API registration owners do not require a bulk rename to match the package ID.

Verify behavior against the checked-out code, actual dependency assemblies, and installed mod combination. Archived branches and releases may still use the standalone CE engine.

## Branches and integration workflow

| Branch | Responsibility |
| --- | --- |
| `main` | Validated shared gameplay and CEPack integration; the release source after migration. |
| `work/siege-assault` | Ongoing RimSiege gameplay and consumption of CEPack APIs, including ladders, raids, artillery, and combat-related policy. |

`work/cepack-migration` and focused documentation/fix branches are temporary integration branches. They do not replace the two maintained lines above.

`work/ce-patch` and `work/weapon-render-compat` are retired development lines. Preserve their history, but do not continue synchronization PRs to them, use them as the release build source, or restore their patches into the migrated code. CEPack now supplies combat adaptation and rendering. Branch deletion is a separate maintainer decision.

After integrating gameplay or API-consumer work into `main`, synchronize `main` back to `work/siege-assault` through a separate PR when there is a real difference. Check ancestry and content; report an already synchronized branch rather than creating an empty PR. Propagate project-wide guidance changes to the maintained development branch as well. Prefer traceable merges and explain conflict resolutions.

If a feature needs a new generic combat capability, implement or request a narrow public API in the separate CEPack repository, then consume it from RimSiege. Keep those changes on a focused CEPack branch for upstream review. Do not rebuild a standalone CE frame engine or reach into CEPack's internal caches and scopes from RimSiege.

All PRs follow the validation requirements below. Local commits do not imply an upstream merge, deployment, release, or completed gameplay validation.

## Engineering principles

- Reuse vanilla, CE, ProjectZ, and CEPack algorithms and lifecycles. Inspect the original implementation before changing pathfinding, targeting, ballistic integration, damage, construction, hauling, reloading, temperature, or tick scheduling.
- Preserve complete native trigger semantics, including probability, filters, and state. An event category alone is not equivalent to the native trigger.
- Keep clear ownership between external bindings, combat policy, and raid policy. Avoid catch-all utilities, duplicate coordinate systems, and oversized patch entry points.
- Use actual inheritance, interfaces, or common call sites for compatibility. Keep weapon-specific gameplay choices in definitions or policy; do not restrict general compatibility to selected weapon defNames.
- Fix causes while retaining native formulas and behavior. Avoid broad try/catch blocks, meaningless gates, silent successful fallbacks, and redundant null guards.
- Bound expensive planning and use native reachability/path services. Give temporary query results explicit lifetimes. Avoid continuous tick scans for one-time planning and retained references to abandoned guns, operators, or target snapshots.
- Scope temporary query state and restore it in finally, including when entering another native scope fails. Do not serialize providers, prototype leases, transient contexts, or planning caches.
- Keep experiments, temporary probes, fixtures, test saves, draft specifications, and debugging evidence in ignored `.local/` or a separate test tree/branch. They never belong in `main` or release packages.
- Do not add a harness, smoke test, or gate for each feature. Preserve reusable tests with real regression value; compilation and gameplay validation establish different facts.
- Follow the current user's confirmed scope and design. Leave unresolved major decisions reviewable and unmerged.

## Structure and starting points

```text
Source/RimSiege/
  RimSiegeMod.cs          Entry point, Harmony registration, settings UI,
                         and CEPack provider/rule registration
  SiegeSettings.cs       Optional raid behavior and artillery settings
  Integration/           Public CEPack adapters and retained native bindings
    CeArtillerySight.cs  Placement-query adapter and CE configuration classification
    CeLadderGeometry.cs  Exposed ladder/lift volume and aim policy in virtual cells
    CeSurfaceTerrain.cs  Direct surface-contact terrain policy
    IsoSiege*            ProjectZ ladder/planner and assault bindings
  Combat/
    Aiming/              RimSiege policies: psychic sight, manual turret control,
                         inspect-card notices, and ladder-head interaction
    Projectiles/         Ladder damage policy
  Raids/                 Kits, carriers, recovery, deployment, and raid lifecycle
    Artillery/           Profiles, breach targets, camp planning/deployment,
                         construction, supplies, operators, and abandonment
legacy/Source/RimSiege/   Archived standalone CE adaptation; excluded from compilation
Defs/                    Items and gameplay configuration
Patches/                 XML extensions to definitions and native think trees
Languages/               Localized settings, descriptions, and raid notifications
Assemblies/              Ignored distributable build output
About/                   Metadata, dependencies, preview, and workshop identity
LoadFolders.xml          Root runtime content selection for RimWorld 1.7
.local/                  Ignored specifications, experiments, probes, and evidence
```

| Area | Start here |
| --- | --- |
| General CE range, sight, flight, impacts, shields, suppression, or rendering | CEPack's actual implementation and public API. Determine the runtime verb/projectile type first. |
| Candidate artillery range, arc, or fixed obstruction | `Integration/CeArtillerySight`, then CEPack placement queries. Keep target ranking and planning budgets in `Raids/Artillery`. |
| Ladder/lift hit volume or exposed aim point | `Integration/CeLadderGeometry` and CEPack's target geometry API. |
| Extra terrain damage on an actual surface contact | `Integration/CeSurfaceTerrain` and CEPack's surface-explosion lifecycle. |
| Ladder carrying, recovery, placement, clearance, climbing, or melee touch | `LadderKit`, `SiegeRaidMap`, `SiegeRoles`, deployment/touch patches, and `Integration/IsoSiege*`. |
| Camp position, configuration, and breach targets | `ArtilleryRaidPolicy`, `ArtilleryProfile`, `SupportRoute`, `CampSiting`, `CampField`, `CampCandidate`, `CampPlanner`, `ArtilleryTargets`, and `ArtilleryTargeting`. |
| Construction, operators, infantry release, abandonment, and quest completion | `LordJob_Artillery`, its mech subclass, `NativeSiegePatches`, `ArtilleryCamp`, `ArtilleryMap`, `ArtilleryCompletion`, and ownership patches. |

`Integration` binds mechanisms without owning raid strategy. `Combat` contains deliberate gameplay/interaction policy. `Raids` composes them with native siege lifecycles. Artillery-supported raids retain their original strategy and pawn generation; camp preparation inherits `LordJob_Siege`, and infantry handoff invokes the original strategy's `MakeLordJob`. Identify the active phase before changing interruption or handoff behavior.

The existing ProjectZ ladder planner, clearance, traversal, and touch bindings remain an intentional migration boundary. Do not redesign them as part of a CEPack fix. `legacy/` is historical production source, not a fallback to reactivate or a destination for new implementations.

### Coordinate conventions

CEPack's public points and bounds use ProjectZ virtual space: ground x/z in cells and absolute y in cells. Public `IntVec3` map inputs remain actual floor-band cells. CEPack performs its internal floor-local CE conversion; rendering coordinates are separate.

Use public contracts and existing native floor APIs. Do not reintroduce a fixed legacy CE storey height or globally rewrite real Thing.Map, DrawPos, TrueCenter, or positions. Preserve the original target identity while keeping the actual hit position and floor distinct.

## Building and deployment

`Source/RimSiege/RimSiege.csproj` is the MSBuild project and authoritative C# build configuration, currently targeting .NET 10. It declares references, compiler settings, and output paths. SDK default source discovery stays under that project directory, so root `legacy/` is outside the compilation tree. No command-line legacy exclusion or separate CMake project is needed.

Set the game location once in the shell, then use the normal project build:

```sh
export RIMWORLD_DIR="/path/to/RimWorld"
dotnet build Source/RimSiege/RimSiege.csproj -c Release --nologo
```

Dependency locations are configured through project properties: `RimWorldDir`, `WorkshopDir`, `HarmonyPath`, `CombatExtendedPath`, `IsoRimPath`, `ProjectZPath`, and `CEPackPath`. Override them when building against local dependency candidates; keep machine-specific paths out of tracked project files. Build the matching CEPack assemblies before the consumer when changing an API.

Run relevant compilation, XML syntax, and diff checks. A successful build establishes static validity, not runtime regression coverage. Release builds write `Assemblies/RimSiege.dll`; dependency assemblies remain external. Do not force-add ignored RimSiege DLLs to Git.

Required mods are Harmony, CE, CE Armory, IsoRim Waylith, IsoRimZ, and a compatible CEPack extension API build. Keep About and README consistent. Runtime packages contain metadata, the RimSiege DLL, definitions, patches, translations, README, and license. Exclude source, `legacy/`, build intermediates, dependencies, and `.local/`. After migration, build releases from validated `main` or its approved release revision; no separate rendering combination is required.

Before deployment, verify the target folder, loaded mod combination, source revision, and DLL hash. Preserve installation-folder settings and workshop identity. Activate one local or workshop copy of each package. Coordinate package-ID changes with the actual enabled configuration. If a running process locks a DLL, finish the build first and follow the human's session-specific game-control authorization. Do not overwrite saves or mix the legacy standalone engine with CEPack.

## GABS and RimBridge

Agents must participate in game-state inspection and debugging, not rely solely on source reasoning or compilation.

1. Query GABS listings/status first and prefer attaching to the existing process. Discover bridge tools and parameter schemas. Use current connection configuration and never expose credentials.
2. Begin with read-only inspection of pause state, tick, map, loaded mods, objects, and full exception stacks. Correlate observations with the same process and tested build; confirm local/workshop package origins when relevant.
3. Use developer tools and existing probes for reproducible scenes, then observe native AI. Forced shots/jobs do not prove automatic acquisition, natural reloading, or raid lifecycle behavior.
4. By default the human manages saves, exit, startup, and reload. Follow explicit session authorization for additional control. Avoid repeated pause/restart/reload loops; investigate the actual process and connection first. Read a script before assuming it is read-only.
5. With cooperating agents, establish one game-control owner; other participants use read-only observations and logs.
6. Probes stay observational and outside production. Check Harmony ref/out and return-value handling so logging cannot change business results.

## Validation before PRs

Before opening any PR, including a synchronization PR, complete sufficient regression testing and obtain in-game testing with actual human participation. Agent-only observation and successful builds do not replace this requirement.

- Validate the proposed integration result and actual deployed DLL. A previously tested build does not establish that a later candidate passed.
- Cover affected normal flows, same-floor controls, cross-floor directions/occlusion, and relevant state transitions. Raid work should cover applicable construction, combat interruption, infantry handoff, operator recovery, abandonment, and save/load.
- For planning, targeting, or cache changes, record relevant query counts/timings and object lifetimes. Focused results do not establish long-save or untested stress behavior.
- Record tester, revision or DLL hash, mod combination, scenarios, outcomes, remaining issues, and coverage limits in local evidence. A human's pass applies only to what was tested.
- When gameplay results are pending, finish implementation/static checks and retain a deployable candidate before coordinating validation and opening the PR.

Keep focused PR descriptions concise: the problem, resulting behavior, native reuse, validation, and material limitations. Routine changes usually need only a few sentences. Keep detailed hashes and debugging evidence in local records. Synchronization PRs identify their source revision and any conflict/branch-specific treatment.
