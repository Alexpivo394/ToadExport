# ToadExport

UI-free core for exporting Autodesk Revit models to RVT and NWC, shipped as NuGet packages for Revit 2020–2027.
It is shared by the ToadExporter add-in (ToadTools) and the ToadFlow export farm.

## Packages

| Package | Revit API | Contents |
|---|---|---|
| `Alexpivo394.Revit.Export` | yes, one package version per Revit year | detached opening with link unloading, workset filtering, NWC export by 3D view, RVT cleanup (links, CAD, point clouds, purge from 2024, compact), local copy of workshared models, version guard |
| `Alexpivo394.Revit.Export.Sources` | no (`net48`, `net10.0`) | Revit Server tree over REST, folder tree, `.rvt` version reader |

## Usage

The Revit package version starts with the Revit year, so a multi-version add-in picks the matching build:

```xml
<PackageVersion Include="Alexpivo394.Revit.Export" Version="$(RevitVersion).*"/>
<PackageVersion Include="Alexpivo394.Revit.Export.Sources" Version="1.*"/>
```

Export one model inside the Revit API context:

```csharp
var exporter = new ModelExporter(application);
var result = exporter.Export(
    new ModelJob("AR.rvt", @"\\nas\projects\AR.rvt", IsServer: false, Rvt: true, Nwc: true),
    new ExportOptions { AllowUpgrade = false },
    @"\\nas\exports",
    progress,   // IExportProgress
    log);       // IExportLog
```

Read a model's Revit version without Revit:

```csharp
int? year = RvtFileInfo.TryReadYear(@"\\nas\projects\AR.rvt");
```

## Versioning and releases

A release tag `X.Y.Z` publishes:

- `Alexpivo394.Revit.Export` as `{year}.X.Y.Z` for every year 2020–2027 (for example `2024.1.0.0`, shown by NuGet as `2024.1.0`);
- `Alexpivo394.Revit.Export.Sources` as `X.Y.Z`.

Push the tag to release; GitHub Actions packs, publishes to nuget.org (`NUGET_API_KEY` secret) and creates a GitHub release.
Local pack: `cd build && DOTNET_ENVIRONMENT=Development dotnet run -c Release -- pack` writes `0.0.0-dev` packages to `output/`.

## License

MIT
