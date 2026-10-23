<p align="center">
    <h1 align="center">ToadExport</h1>
    <p align="center">Revit model export to RVT and NWC for Revit 2020–2027</p>
</p>

## Libraries for Revit model export

This repository contains the export core used by the [ToadTools](https://github.com/Alexpivo394/ToadTools) ToadExporter add-in and by the [ToadFlow](https://github.com/Alexpivo394/ToadFlow) export farm. The core has no UI and is packed into separate NuGet packages for easy connection to your add-in or service.

The Revit package version matches the Revit release, for example use `2024.*` for Revit 2024. A model is always exported in the Revit version it was saved in: the version guard refuses to open a model saved in another release, unless the caller explicitly allows an upgrade.

Didn't find the feature you need? [Tell me](https://github.com/Alexpivo394/ToadExport/issues/new)


## Alexpivo394.Revit.Export

[![Nuget](https://img.shields.io/nuget/vpre/Alexpivo394.Revit.Export?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.Export)
[![Downloads](https://img.shields.io/nuget/dt/Alexpivo394.Revit.Export?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.Export)

Export of one model inside the Revit API context: detached opening with link unloading, workset filtering, NWC export by 3D view, RVT cleanup (Revit links, CAD, point clouds, purge from Revit 2024, compact), local copy of workshared models and the version guard.

Installation:

```text
<PackageReference Include="Alexpivo394.Revit.Export" Version="$(RevitVersion).*"/>
```

Usage:

```csharp
var exporter = new ModelExporter(application);
var result = exporter.Export(
    new ModelJob("AR.rvt", @"\\nas\projects\AR.rvt", IsServer: false, Rvt: true, Nwc: true),
    new ExportOptions { AllowUpgrade = false },
    @"\\nas\exports",
    progress,   // IExportProgress
    log);       // IExportLog
```

## Alexpivo394.Revit.Export.Sources

[![Nuget](https://img.shields.io/nuget/vpre/Alexpivo394.Revit.Export.Sources?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.Export.Sources)
[![Downloads](https://img.shields.io/nuget/dt/Alexpivo394.Revit.Export.Sources?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.Export.Sources)

Model sources without the Revit API (`net48`, `net10.0`): Revit Server tree over REST, folder tree and the `.rvt` version reader.

Installation:

```text
<PackageReference Include="Alexpivo394.Revit.Export.Sources" Version="1.*"/>
```

Usage:

```csharp
int? year = RvtFileInfo.TryReadYear(@"\\nas\projects\AR.rvt");
```

## Releases

A tag `X.Y.Z` publishes `Alexpivo394.Revit.Export` as `{year}.X.Y.Z` for every Revit year 2020–2027 and `Alexpivo394.Revit.Export.Sources` as `X.Y.Z`, then creates a GitHub release with the packages.

Local pack: `cd build && DOTNET_ENVIRONMENT=Development dotnet run -c Release -- pack` writes `0.0.0-dev` packages to `output/`.
