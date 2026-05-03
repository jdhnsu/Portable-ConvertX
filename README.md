# Portable ConvertX

[English](README.md) | [中文](README_zh.md)

**A Windows desktop format conversion tool that serves as a graphical frontend for portable converters in `TestTools`.**

---

## Quick Start

### Prerequisites

- **OS**: Windows 10/11
- **.NET SDK**: 6.0 or higher
- **TestTools**: Portable converter collection (must be prepared separately)

### Run

```powershell
git clone <repository-url>
cd convertx
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

The app searches upward from the current directory until it finds `TestTools/tools.json`.

### Build Release

```powershell
dotnet build ConvertXPortable\ConvertXPortable.csproj
.\publish.ps1   # Interactive — asks for version, creates zip + 7z split volumes
```

---

## Features

- Drag-and-drop or button file selection
- Auto-detects file type and suggests target formats
- Real-time stdout/stderr logging during conversion
- Cancel long-running tasks
- Paths with spaces and Chinese characters work natively
- JSON-based conversion rules (no code changes needed to add tools)
- AI chat and conversion planning (configurable API key in settings)
- Light/dark/system theme with Windows 11 Mica effect
- Optional NVIDIA CUDA acceleration

---

## Configuration

### Conversion Rules (`conversions.json`)

```json
{
  "conversions": [
    {
      "converter": "ImageMagick",
      "from": ["jpg", "png", "webp"],
      "to": ["jpg", "png", "webp", "pdf"],
      "executable": "ImageMagick/magick.exe",
      "argumentTemplate": "\"{input}\" \"{output}\"",
      "priority": 20,
      "outputArgumentTemplates": {
        "ico": "-define icon:auto-resize=256,128,64,48,32,16"
      }
    }
  ]
}
```

**Placeholders in `argumentTemplate`**:

| Placeholder | Description |
|-------------|-------------|
| `{input}` | Full path to input file |
| `{output}` | Full path to output file |
| `{outputDir}` | Output directory |
| `{format}` | Target format extension |
| `{inputFormat}` | Normalized input extension (jpeg→jpg, tif→tiff, etc.) |

**Special options**:
- `writeStdoutToOutput`: redirect stdout to output file
- `pipeInputToStdin`: pipe input file to process stdin
- `outputArgumentTemplates`: per-format extra args
- `priority`: lower = preferred when multiple converters match

### Tool Discovery (`TestTools/tools.json`)

```json
{
  "tools": [
    {
      "category": "image",
      "name": "ImageMagick",
      "path": "ImageMagick",
      "mainExecutable": "magick.exe",
      "executables": ["magick.exe"]
    }
  ]
}
```

---

## Adding a New Converter

1. Place the portable tool in `TestTools/<name>/`
2. Add entry in `TestTools/tools.json` under `tools[]`
3. Add a `ConversionRule` in `conversions.json`
4. Restart the app — it auto-discovers new tools

---

## Architecture

```
ConvertXPortable/
├── Models/
│   ├── ConversionModels.cs    # All models + AppViewModel (INotifyPropertyChanged)
│   └── AiModels.cs
├── Services/
│   ├── PathResolver.cs        # Workspace root detection, path resolution
│   ├── ConfigurationService.cs # Loads tools.json + conversions.json
│   ├── ConversionRouter.cs    # Input extension → output formats → converter options
│   ├── ConversionExecutor.cs  # Spawns child process, cancellation support
│   ├── ArgumentTemplate.cs    # Token replacement + command-line splitting
│   ├── AiChatService.cs       # OpenAI/Anthropic API communication
│   ├── AiConversionPlanner.cs
│   └── ThemeManager.cs
├── Controls/
│   └── MarkdownViewer.cs
├── MainWindow.xaml(.cs)
└── App.xaml.cs
```

**Key design decisions**:
- WPF + WinForms (WinForms only for `FolderBrowserDialog`)
- `ProcessStartInfo.ArgumentList` for space/unicode-safe argument passing
- `process.Kill(entireProcessTree: true)` for cancellation
- JSON configs use `PropertyNameCaseInsensitive`, `JsonCommentHandling.Skip`, `AllowTrailingCommas`

---

## Extension Aliases

`ConversionRouter.NormalizeExtension`: `jpeg→jpg`, `tif→tiff`, `htm→html`, `yml→yaml`, `m4v→mp4`, `m4a→aac`

---

## Tech Stack

- .NET 8.0-windows (WPF + WinForms)
- No NuGet dependencies
- No tests, no CI

---

## License

MIT

ConvertX converter source code referenced from https://github.com/C4illin/ConvertX — AGPL-3.0 applies if using those files. (The ConvertX code is not used or compiled directly, only some of the design and implementation details are referenced.)