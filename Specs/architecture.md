# 3Dsiege architecture (v1 skeleton)

```
3Dsiege/
  About/                 # ModMetaData, loadAfter CE + IsoRimZ
  Assemblies/            # build output (gitignored)
  Source/ThreeDSiege/
    ThreeDSiegeMod.cs
    Bridge/              # facades: IsoFloor, CeHeight (reuse, no rewrite)
    Combat/              # cross-floor policy (range/LOS glue)
    Patches/             # Harmony targeting CE (+ optional IsoRimZ Api hooks)
  Specs/                 # acceptance + OPEN questions
```

## Layering
1. **Bridge** — only calls into ProjectZ.Api / CE public types; no algorithm copies.
2. **Combat** — pure policy: given floors + CE heights, decide offsets / allow fire.
3. **Patches** — Harmony Prepare() requires both CE and IsoRimZ; FailSoft disable on miss.

## Branch policy
- `work/*` may hold skeleton and untested drafts.
- `main` only after Cycle confirms in-game via GABS HTTP → RimBridge.
