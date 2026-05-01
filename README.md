# Portable ConvertX

[English](README.md) | [中文](README_zh.md)

<div align="center">

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-6%2B-purple)
![License](https://img.shields.io/badge/license-MIT-green)

**A powerful Windows desktop format conversion tool that unifies portable converter collections**

</div>

---

## 📖 Introduction

Portable ConvertX is a WPF-based Windows desktop application that serves as a graphical frontend for portable format conversion tools collected in `TestTools`. It provides an intuitive interface to simplify the use of command-line tools, making file format conversion simple and efficient.

### ✨ Core Features

- 🎨 **Modern UI** - Supports light/dark themes and Windows 11 Mica effect
- 🤖 **AI Assistant** - Integrated AI chat and conversion planning features
- 🔄 **Smart Routing** - Automatically matches file types to the best conversion tools
- 📝 **Real-time Logging** - Captures and displays stdout/stderr during subprocess execution
- ⏹️ **Task Control** - Supports canceling long-running conversion tasks
- 🌐 **Path Safety** - Perfectly supports paths with spaces and Chinese characters
- 🔧 **Flexible Configuration** - JSON-based extensible conversion rule system
- 🚀 **High Performance** - Optional NVIDIA CUDA hardware acceleration

---

## 🚀 Quick Start

### Installation

1. Download the latest release from [Feishu](https://my.feishu.cn/docx/HNvrdXQpqoCFK1xY3itcS2rensd?from=from_copylink).


### Building from Source

#### Prerequisites

- **Operating System**: Windows 10/11
- **.NET SDK**: Version 6.0 or higher
- **TestTools**: Portable conversion tool collection (prepared separately)

### Running the Application

```powershell
# Clone the repository
git clone <repository-url>
cd convertx

# Run the application
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

The application automatically searches upward from the current directory until it finds the `TestTools/tools.json` configuration file.

### Building Release Version

```powershell
# Build
dotnet build ConvertXPortable\ConvertXPortable.csproj

# Publish (using provided script)
.\publish.ps1
```

---

## 📋 Feature Details

### 1️⃣ Visual File Conversion

- Drag and drop files or click buttons to select input files
- Automatically detect file types and recommend available target formats
- One-click conversion execution with real-time progress and logs

### 2️⃣ AI Assistant (New)

- **AI Chat**: Converse with AI to get conversion suggestions and tips
- **Smart Planning**: AI analyzes files and generates optimal conversion plans
- **History Management**: Save and manage AI conversation history
- **Multi-model Support**: Compatible with mainstream APIs like OpenAI and Anthropic

Configuration: Enter API Key and model information in the settings panel to enable.

### 3️⃣ Themes & Appearance

- **Light Theme**: Clean and bright interface
- **Dark Theme**: Eye-comfortable dark mode
- **System Follow**: Automatically follows Windows system theme
- **Mica Effect**: Exclusive frosted glass background effect for Windows 11

### 4️⃣ Hardware Acceleration

- Optional NVIDIA CUDA acceleration
- Significantly improves video and image conversion performance
- One-click toggle in settings

---

## ⚙️ Configuration Guide

### Conversion Rules Configuration

Edit the [`conversions.json`](conversions.json) file in the project root to customize conversion rules:

```json
{
  "version": "1.0.0",
  "description": "Conversion rules description",
  "conversions": [
    {
      "converter": "ImageMagick",
      "from": ["jpg", "png", "webp"],
      "to": ["jpg", "png", "webp", "pdf"],
      "executable": "ImageMagick/magick.exe",
      "argumentTemplate": "\"{input}\" \"{output}\"",
      "category": "image",
      "priority": 20,
      "description": "General image conversion",
      "writeStdoutToOutput": false,
      "pipeInputToStdin": false,
      "outputArgumentTemplates": {
        "ico": "-define icon:auto-resize=256,128,64,48,32,16"
      }
    }
  ]
}
```

#### Parameter Template Placeholders

`argumentTemplate` supports the following dynamic placeholders:

| Placeholder | Description | Example |
|-------------|-------------|---------|
| `{input}` | Full path to input file | `C:\Users\Documents\input.jpg` |
| `{output}` | Full path to output file | `C:\Users\Documents\output.png` |
| `{outputDir}` | Directory of output file | `C:\Users\Documents` |
| `{format}` | Target format extension | `png` |

#### Special Options

- **`writeStdoutToOutput`**: When set to `true`, redirects the tool's standard output to the output file (suitable for certain command-line tools)
- **`pipeInputToStdin`**: When set to `true`, pipes input file content through standard input to the tool
- **`outputArgumentTemplates`**: Additional parameter configuration for different output formats

### Tool Discovery Configuration

The application relies on `TestTools/tools.json` to discover available conversion tools:

```json
{
  "tools": [
    {
      "category": "image",
      "name": "ImageMagick",
      "mainExecutable": "magick.exe",
      "path": "ImageMagick",
      "description": "Powerful image processing tool",
      "executables": ["magick.exe"]
    }
  ]
}
```

---

## 🏗️ Technical Architecture

### System Architecture Diagram

```
┌─────────────────────────────────────────────┐
│           WPF UI Layer (MainWindow)         │
│  ┌───────────┐ ┌──────────┐ ┌───────────┐  │
│  │ File Pick │ │ AI Chat  │ │ Log View  │  │
│  └───────────┘ └──────────┘ └───────────┘  │
└──────────────────┬──────────────────────────┘
                   │ MVVM Data Binding
┌──────────────────▼──────────────────────────┐
│         Application Services Layer          │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │ConversionRouter│ │ AiConversionPlanner│   │
│  └──────────────┘  └────────────────────┘   │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │ConfigService │  │  AiChatService     │   │
│  └──────────────┘  └────────────────────┘   │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │ConvExecutor  │  │  ThemeManager      │   │
│  └──────────────┘  └────────────────────┘   │
└──────────────────┬──────────────────────────┘
                   │ Process Execution
┌──────────────────▼──────────────────────────┐
│      External Tools (TestTools Collection)  │
│  ImageMagick | FFmpeg | Inkscape | ...      │
└─────────────────────────────────────────────┘
```

### Core Components

| Component | Responsibility |
|-----------|----------------|
| **MainWindow** | WPF main window, handles user interaction and UI updates |
| **ConversionRouter** | Matches best conversion rules based on file extensions |
| **ConfigurationService** | Loads and manages conversions.json and tools.json |
| **ConversionExecutor** | Starts subprocesses and manages execution lifecycle |
| **ArgumentTemplate** | Renders parameter templates, replaces placeholders |
| **PathResolver** | Upward directory search mechanism to locate configuration files |
| **AiChatService** | Communicates with AI models (OpenAI/Anthropic compatible) |
| **AiConversionPlanner** | Uses AI to analyze and generate conversion plans |
| **ThemeManager** | Manages light/dark theme switching |

### Design Patterns

- **MVVM**: WPF data binding architecture, separates view from logic
- **Service Layer Decoupling**: Services independently encapsulated for easier testing and maintenance
- **Configuration-Driven**: All conversion behaviors controlled by JSON configuration, no code modification needed

---

## 💡 Usage Examples

### Example 1: Image Format Conversion

1. Select input file `photo.jpg`
2. Choose target format `PNG`
3. Click "Start Conversion"
4. Application automatically generates command: `magick.exe "photo.jpg" "photo.png"`
5. View conversion logs in real-time

### Example 2: Using AI to Plan Complex Conversions

1. Open AI Assistant panel
2. Upload files that need conversion
3. AI analyzes files and recommends optimal conversion plan
4. Confirm plan and execute with one click

### Example 3: Batch Conversion

Although the current version mainly targets single-file conversion, you can:
- Continuously select multiple files for conversion
- Use AI assistant to optimize batch processing strategies

---

## ❓ FAQ

### Q: Application shows "TestTools/tools.json not found"?

**A**: Please ensure:
1. `TestTools` directory and its `tools.json` are prepared
2. Place `TestTools` in the parent directory of the application executable
3. Or launch the application from a directory containing `TestTools`

### Q: How to add new conversion tools?

**A**: 
1. Place the tool in the `TestTools` directory
2. Add tool definition in `tools.json`
3. Add conversion rules in `conversions.json`
4. Restart the application for automatic discovery

### Q: How to use AI features?

**A**:
1. Configure AI provider in settings panel (OpenAI or Anthropic compatible)
2. Enter API Key and model name
3. Click "Test Connection" to verify configuration
4. Start conversations or generate conversion plans in the AI Assistant panel

### Q: Which AI models are supported?

**A**: Any model compatible with OpenAI API or Anthropic API, including:
- OpenAI GPT series
- Anthropic Claude series
- Locally deployed compatible APIs (such as Ollama, LocalAI)

### Q: How to handle paths with Chinese characters or spaces?

**A**: The application uses `ProcessStartInfo.ArgumentList` instead of string concatenation, natively supporting special character paths without additional escaping.

### Q: Can I customize output filenames?

**A**: The current version automatically derives output filenames (keeping original name, only changing extension). For customization, manually rename after conversion completes.

---

## 🛠️ Development Guide

### Project Structure

```
convertx/
├── ConvertXPortable/          # Main application
│   ├── Models/                # Data models
│   │   ├── ConversionModels.cs
│   │   └── AiModels.cs
│   ├── Services/              # Business logic services
│   │   ├── ConfigurationService.cs
│   │   ├── ConversionExecutor.cs
│   │   ├── ConversionRouter.cs
│   │   ├── AiChatService.cs
│   │   ├── AiConversionPlanner.cs
│   │   ├── ThemeManager.cs
│   │   └── ...
│   ├── Controls/              # Custom controls
│   │   └── MarkdownViewer.cs
│   ├── MainWindow.xaml        # Main interface definition
│   ├── MainWindow.xaml.cs     # Main interface logic
│   └── App.xaml.cs            # Application entry point
├── conversions.json           # Conversion rules configuration
├── publish.ps1                # Publish script
└── README.md                  # Project documentation
```

### Adding New Features

1. **Add conversion rules**: Edit `conversions.json`
2. **Add new services**: Create new service classes in `Services/` directory
3. **Extend UI**: Modify `MainWindow.xaml` and corresponding code-behind
4. **Add AI features**: Reference implementation pattern in `AiChatService.cs`

### Debugging Tips

- Attach debugger using Visual Studio or VS Code
- Check application log output window for runtime information
- Verify `conversions.json` and `tools.json` format correctness

---

## 📄 License

This project is licensed under the MIT License.

### About ConvertX Source Code Reference

ConvertX converter source code referenced from:
https://github.com/C4illin/ConvertX/tree/main/src/converters

This application does not compile or copy ConvertX source code. If downloading these files to `References/ConvertX/converters/` directory, please note **AGPL-3.0** license requirements before redistribution.

---

## 🤝 Contributing

Issues and Pull Requests are welcome!

### Contribution Guidelines

1. Fork this repository
2. Create feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📞 Contact

- 📧 Email: [email@jdhuan.top]
---

<div align="center">

**Made with ❤️ for Windows Users**

[⭐ Star this repo](link-to-repo) · [🐛 Report Bug](link-to-issues) · [💡 Request Feature](link-to-issues)

</div>
