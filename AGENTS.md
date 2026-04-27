# AGENTS.md

## Build & run

```powershell
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

Single-project solution (`ConvertXPortable`), `net8.0-windows`, WPF + WinForms (only WinForms used is `FolderBrowserDialog`). No NuGet dependencies. No tests, no CI.

## Config files

- `conversions.json` — conversion rules (what tool converts which extensions, argument template, etc.)
  - Linked into the .csproj with `CopyToOutputDirectory=PreserveNewest`.
  - The app prefers the workspace copy in the repo root, falls back to the output copy.
- `TestTools/tools.json` — tool inventory (name, category, executables, paths relative to `TestTools/`).

Both are deserialized with `PropertyNameCaseInsensitive`, `JsonCommentHandling.Skip`, and `AllowTrailingCommas`.

## Workspace discovery

`PathResolver` searches upward from `Environment.CurrentDirectory` and `AppContext.BaseDirectory` until it finds `TestTools/tools.json`. The directory containing `TestTools/` becomes the workspace root. **Always run from the repo root** or a child directory of it, or the app won't find its tools.

Tool paths in `conversions.json` are relative to `TestTools/` (e.g. `"ImageMagick/magick.exe"` resolves to `TestTools/ImageMagick/magick.exe`).

## Architecture

| Dir/File | Role |
|---|---|
| `ConvertXPortable/Models/ConversionModels.cs` | All models + `AppViewModel` (INotifyPropertyChanged) |
| `ConvertXPortable/Services/PathResolver.cs` | Workspace root detection, path resolution |
| `ConvertXPortable/Services/ConfigurationService.cs` | Loads and validates `tools.json` and `conversions.json` |
| `ConvertXPortable/Services/ConversionRouter.cs` | Matches input extension → output formats → converter options |
| `ConvertXPortable/Services/ConversionExecutor.cs` | Spawns child process with cancellation support |
| `ConvertXPortable/Services/ArgumentTemplate.cs` | Token replacement (`{input}`, `{output}`, `{outputDir}`, `{format}`, `{inputFormat}`) + command-line splitting |
| `ConvertXPortable/MainWindow.xaml(.cs)` | UI (drag-drop, file/dir pickers, log viewer, tool status list) |
| `TestTools/` | Portable converter executables |
| `test/` | Manual test inputs/outputs; `test/in/conversions.json` has a variant dasel rule (not used by the app) |

## Conversion rule fields

- `executable` — path relative to `TestTools/`
- `argumentTemplate` — supports `{input}`, `{output}`, `{outputDir}`, `{format}`, `{inputFormat}`
- `outputArgumentTemplates` — per-format extra args injected before the output arg (e.g. ffmpeg codec selection)
- `pipeInputToStdin` — pipes input file to process stdin (used by dasel)
- `writeStdoutToOutput` — captures stdout and writes it to the output file
- `priority` — lower = preferred when multiple converters match the same (input,output) pair

## Extension aliases

`ConversionRouter.NormalizeExtension` maps: `jpeg→jpg`, `tif→tiff`, `htm→html`, `yml→yaml`, `m4v→mp4`, `m4a→aac`.

## Process execution quirks

- Arguments go through `ProcessStartInfo.ArgumentList`, so paths with spaces/Chinese work without shell quoting.
- Cancellation calls `process.Kill(entireProcessTree: true)`.
- `WorkingDirectory` is set to the executable's directory, falling back to `TestToolsRoot`.
- The `{inputFormat}` placeholder resolves to the normalized input extension.

## Adding a new converter

1. Place the portable tool in `TestTools/<name>/`.
2. Add an entry in `TestTools/tools.json` under `tools[]`.
3. Add a `ConversionRule` in `conversions.json` with the executable path and argument template.
4. Update `TestTools/agent.md` for human-readable documentation.
