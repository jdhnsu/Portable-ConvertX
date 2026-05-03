# Portable ConvertX

[English](README.md) | [中文](README_zh.md)

**基于 WPF 的 Windows 桌面格式转换工具，作为 `TestTools` 中便携式转换器的图形化前端。**

---

## 快速开始

### 前置要求

- **操作系统**: Windows 10/11
- **.NET SDK**: 6.0 或更高
- **TestTools**: 便携式转换工具集（需单独准备）

### 运行

```powershell
git clone <repository-url>
cd convertx
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

应用从当前目录向上搜索，直到找到 `TestTools/tools.json`。

### 构建发布版本

```powershell
dotnet build ConvertXPortable\ConvertXPortable.csproj
.\publish.ps1   # 交互式脚本 — 询问版本号，创建 zip + 7z 分卷
```

---

## 功能

- 拖拽或按钮选择文件
- 自动检测文件类型并推荐目标格式
- 实时显示转换过程的 stdout/stderr
- 取消长时间运行的任务
- 路径支持空格和中文字符
- JSON 配置驱动（添加工具无需改代码）
- AI 聊天和转换规划（设置中配置 API key）
- 浅色/深色/跟随系统主题 + Windows 11 Mica 效果
- 可选 NVIDIA CUDA 加速

---

## 配置

### 转换规则 (`conversions.json`)

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

**`argumentTemplate` 占位符**:

| 占位符 | 说明 |
|--------|------|
| `{input}` | 输入文件完整路径 |
| `{output}` | 输出文件完整路径 |
| `{outputDir}` | 输出文件所在目录 |
| `{format}` | 目标格式扩展名 |
| `{inputFormat}` | 归一化后的输入扩展名 (jpeg→jpg, tif→tiff 等) |

**特殊选项**:
- `writeStdoutToOutput`: 将 stdout 重定向到输出文件
- `pipeInputToStdin`: 将输入文件通过 stdin 传入进程
- `outputArgumentTemplates`: 按输出格式的额外参数
- `priority`: 数值越低优先级越高

### 工具发现配置 (`TestTools/tools.json`)

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

## 添加新转换器

1. 将工具放入 `TestTools/<name>/`
2. 在 `TestTools/tools.json` 的 `tools[]` 中添加条目
3. 在 `conversions.json` 中添加 `ConversionRule`
4. 重启应用 — 自动发现新工具

---

## 架构

```
ConvertXPortable/
├── Models/
│   ├── ConversionModels.cs    # 所有模型 + AppViewModel (INotifyPropertyChanged)
│   └── AiModels.cs
├── Services/
│   ├── PathResolver.cs        # 工作区根检测，路径解析
│   ├── ConfigurationService.cs # 加载 tools.json + conversions.json
│   ├── ConversionRouter.cs    # 输入扩展名 → 输出格式 → 转换器选项
│   ├── ConversionExecutor.cs  # 启动子进程，支持取消
│   ├── ArgumentTemplate.cs    # 占位符替换 + 命令行拆分
│   ├── AiChatService.cs       # OpenAI/Anthropic API 通信
│   ├── AiConversionPlanner.cs
│   └── ThemeManager.cs
├── Controls/
│   └── MarkdownViewer.cs
├── MainWindow.xaml(.cs)
└── App.xaml.cs
```

**关键设计**:
- WPF + WinForms（仅 `FolderBrowserDialog` 使用 WinForms）
- `ProcessStartInfo.ArgumentList` 处理空格/Unicode 路径
- `process.Kill(entireProcessTree: true)` 用于取消
- JSON 配置使用 `PropertyNameCaseInsensitive`, `JsonCommentHandling.Skip`, `AllowTrailingCommas`

---

## 扩展名别名

`ConversionRouter.NormalizeExtension`: `jpeg→jpg`, `tif→tiff`, `htm→html`, `yml→yaml`, `m4v→mp4`, `m4a→aac`

---

## 技术栈

- .NET 8.0-windows (WPF + WinForms)
- 无 NuGet 依赖
- 无测试，无 CI

---

## 许可证

MIT

ConvertX 转换器源码参考自 https://github.com/C4illin/ConvertX — 使用这些文件需遵守 AGPL-3.0。(未直接使用或编译 ConvertX 代码，只参考了部分设计和实现细节)