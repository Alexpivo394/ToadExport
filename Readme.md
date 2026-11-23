<h1 align="center">ToadExport</h1>

<p align="center">
  Unattended export of Revit models to RVT and NWC, as a library.<br>
  Revit 2020–2027.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Alexpivo394.Revit.Export"><img src="https://img.shields.io/nuget/v/Alexpivo394.Revit.Export?label=Revit.Export" alt="Alexpivo394.Revit.Export on NuGet"></a>
  <a href="https://www.nuget.org/packages/Alexpivo394.Revit.Export.Sources"><img src="https://img.shields.io/nuget/v/Alexpivo394.Revit.Export.Sources?label=Revit.Export.Sources" alt="Alexpivo394.Revit.Export.Sources on NuGet"></a>
  <a href="https://github.com/Alexpivo394/ToadExport/actions/workflows/Build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Alexpivo394/ToadExport/Build.yml?label=build" alt="Build"></a>
  <a href="License.md"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
</p>

---

ToadExport is the export core behind the ToadExporter add-in in [ToadTools](https://github.com/Alexpivo394/ToadTools) and the scheduled exports of [ToadFlow](https://github.com/Alexpivo394/ToadFlow). It has no UI: you give it a model path and an output folder, it gives you a clean RVT, an NWC, or both, and tells you exactly what happened.

It is written for batch runs that nobody watches, so the awkward cases are handled rather than left to a dialog box:

- **Never upgrades a model by accident.** The version guard reads the Revit year from the `.rvt` file and refuses a model saved in another release, unless you set `AllowUpgrade`.
- **Never touches the source.** A model is opened detached from central, with worksets preserved.
- **Opens fast.** With `SkipLinks`, the model is copied to a temporary folder and opened with its links unloaded; worksets can be filtered by keyword.
- **Cleans the RVT.** Removes Revit links, CAD imports, and point clouds, purges unused elements (Revit 2024 and later), and compacts the file.
- **Exports NWC by 3D view** with every Navisworks exporter option exposed, and answers the exporter's dialogs itself.
- **Leaves no half-written files.** The result is staged and then published; when an export fails, the previous file stays in place.
- **Says when a retry is worth it.** The result separates a broken model from a Revit glitch that usually passes in a fresh Revit process.

## Packages

| Package | Targets | Revit API | What it does |
|---|---|---|---|
| [`Alexpivo394.Revit.Export`](https://www.nuget.org/packages/Alexpivo394.Revit.Export) | per Revit year | yes | Exports one model inside the Revit API context |
| [`Alexpivo394.Revit.Export.Sources`](https://www.nuget.org/packages/Alexpivo394.Revit.Export.Sources) | `net47`, `net8.0`, `net10.0` | no | Lists models on Revit Server or in a folder; reads the Revit year of an `.rvt` |

The version of `Alexpivo394.Revit.Export` starts with the Revit year: use `2024.*` for Revit 2024.

## Exporting a model

```xml
<PackageReference Include="Alexpivo394.Revit.Export" Version="$(RevitVersion).*"/>
```

```csharp
var exporter = new ModelExporter(application);

ModelResult result = exporter.Export(
    new ModelJob("AR.rvt", @"\\nas\projects\AR.rvt", IsServer: false, Rvt: true, Nwc: true),
    new ExportOptions { AllowUpgrade = false, NwcViewName = "Navisworks" },
    outputFolder: @"\\nas\exports",
    progress,   // IExportProgress: status text, progress, and the abort flag
    log);       // IExportLog: one line per step
```

`Export` runs in the Revit API context and does not throw on a bad model. Everything is in the result:

| Member | Meaning |
|---|---|
| `Rvt`, `Nwc` | `Done`, `Skipped`, `Failed`, or `NotRequested` for each format |
| `Note` | Why a step was skipped, or the error text |
| `RevitFailure` | Revit failed internally, not the model. A retry in a new Revit process usually passes |
| `NwcMissing` | The view exists but the exporter produced no file. Worth a retry |
| `CloseFailed` | The model could not be closed. Restart Revit before reusing the process |

`RunValidator.Errors(jobs, options, outputFolder)` checks a whole run before the first model is opened: an empty job list, a missing NWC view name, an output folder that is not writable, or one that would overwrite a source model.

## Finding models

```xml
<PackageReference Include="Alexpivo394.Revit.Export.Sources" Version="1.*"/>
```

`Sources` has no Revit dependency, so a service or a settings window can browse models without starting Revit.

```csharp
ITreeSource server = new RevitServerSource("revit-server-01", "2024");   // Revit Server REST API
ITreeSource folder = new FolderSource(@"\\nas\projects");

foreach (SourceEntry entry in await server.GetChildrenAsync(server.RootPath, ct))
    Console.WriteLine(entry.IsFolder ? $"[{entry.Name}]" : entry.ModelPath);

int? year = RvtFileInfo.TryReadYear(@"\\nas\projects\AR.rvt");   // 2024, without opening Revit
```

`ServerCatalog` wraps a source with a cache on disk, so a large Revit Server tree opens instantly and refreshes in the background.

## Building

Requires the .NET 10 SDK.

```powershell
dotnet test --project tests/Alexpivo394.Revit.Export.Tests
dotnet build source/Alexpivo394.Revit.Export -c Release.R24
```

Each Revit year has its own configuration, from `Release.R20` to `Release.R27`. The tests cover everything that runs without Revit.

To pack locally, run the build pipeline. It writes `0.0.0-dev` packages to `output/`:

```powershell
cd build
$env:DOTNET_ENVIRONMENT = "Development"; dotnet run -c Release -- pack
```

## Releasing

A tag `X.Y.Z` publishes `Alexpivo394.Revit.Export` as `{year}.X.Y.Z` for every Revit year from 2020 to 2027 and `Alexpivo394.Revit.Export.Sources` as `X.Y.Z`, then creates a GitHub release with the packages attached.

## Related projects

| Project | Role |
|---|---|
| [ToadTools](https://github.com/Alexpivo394/ToadTools) | Revit add-ins; ToadExporter is the UI for this library |
| [ToadFlow](https://github.com/Alexpivo394/ToadFlow) | Runs exports on a schedule across a farm of Revit machines |
| [ToadParamCheck](https://github.com/Alexpivo394/ToadParamCheck) | The sibling core for parameter (LOI) checks |

Missing something? [Open an issue](https://github.com/Alexpivo394/ToadExport/issues/new).

## License

[MIT](License.md)
