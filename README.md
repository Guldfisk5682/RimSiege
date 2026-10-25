# 3Dsiege

Combat Extended compatibility for IsoRim Waylith and IsoRimZ on RimWorld 1.7 Unstable.

The compatibility layer adapts floor coordinates, aiming, target acquisition, projectile collisions and projectile drawing. Version 0.2 adds cross-floor adaptation for explosive and incendiary effects, fragments, indirect fire, the Diabolus main cannon and mechanoid attached turrets. Combat Extended retains its ballistics, cover checks and damage algorithms; native weapons reuse IsoRimZ's combat adapters.

Load after Harmony, Combat Extended, IsoRim Waylith and IsoRimZ.

Build with the .NET 10 SDK against the installed game and mods:

```sh
dotnet build Source/RimSiege/RimSiege.csproj -c Release -p:RimWorldDir="/path/to/RimWorld"
```

The build produces `1.7/Assemblies/RimSiege.dll`. Reference paths for Harmony, Combat Extended, IsoRim and IsoRimZ can be overridden through the project's MSBuild properties.

`Source/RimSiege/Integration` holds external API bindings. `Combat/Geometry`, `Combat/Aiming` and `Combat/Projectiles` hold the floor geometry, shot checks and projectile adapters. Target acquisition adapters remain in `Combat/TargetAcquisitionPatches.cs`.
