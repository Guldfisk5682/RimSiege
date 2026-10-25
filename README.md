# 3Dsiege

Combat Extended compatibility for IsoRim Waylith and IsoRimZ on RimWorld 1.7 Unstable.

The compatibility layer adapts floor coordinates, aiming, target acquisition, projectile collisions and projectile drawing. Combat Extended retains its ballistics, cover checks and damage algorithms. Special weapons, including mortars and cluster rockets, are still pending adaptation and validation.

Load after Harmony, Combat Extended, IsoRim Waylith and IsoRimZ.

Build with the .NET 10 SDK against the installed game and mods:

```sh
dotnet build Source/RimSiege/RimSiege.csproj -c Release -p:RimWorldDir="/path/to/RimWorld"
```

The build produces `1.7/Assemblies/RimSiege.dll`. Reference paths for Harmony, Combat Extended, IsoRim and IsoRimZ can be overridden through the project's MSBuild properties.

`Source/RimSiege/Integration` holds external API bindings. `Combat/Geometry`, `Combat/Aiming` and `Combat/Projectiles` hold the floor geometry, shot checks and projectile adapters. Target acquisition adapters remain in `Combat/TargetAcquisitionPatches.cs`.
