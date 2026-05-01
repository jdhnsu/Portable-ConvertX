# Portable ConvertX

[English](README.md) | [中文](README_zh.md)

<div align="center">

![平台](https://img.shields.io/badge/平台-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-6%2B-purple)
![许可证](https://img.shields.io/badge/许可证-MIT-green)

**强大的 Windows 桌面格式转换工具，统一管理便携式转换器集合**

</div>

---

## 📖 简介

Portable ConvertX 是一个基于 WPF 的 Windows 桌面应用程序，作为 `TestTools` 中收集的便携式格式转换工具的图形化前端。它提供了直观的界面来简化命令行工具的使用流程，让文件格式转换变得简单高效。

### ✨ 核心特性

- 🎨 **现代化 UI** - 支持浅色/深色主题和 Windows 11 Mica 效果
- 🤖 **AI 智能助手** - 集成 AI 聊天和转换规划功能
- 🔄 **智能路由** - 自动匹配文件类型到最佳转换工具
- 📝 **实时日志** - 子进程执行时实时捕获并显示 stdout/stderr
- ⏹️ **任务控制** - 支持取消长时间运行的转换任务
- 🌐 **路径安全** - 完美支持包含空格和中文字符的路径
- 🔧 **灵活配置** - 基于 JSON 的可扩展转换规则体系
- 🚀 **高性能** - 可选 NVIDIA CUDA 硬件加速

---

## 🚀 快速开始

### 前置要求

- **操作系统**: Windows 10/11
- **.NET SDK**: 6.0 或更高版本
- **TestTools**: 便携式转换工具集合（需单独准备）

### 运行应用

```powershell
# 克隆项目
git clone <repository-url>
cd convertx

# 运行应用
dotnet run --project .\ConvertXPortable\ConvertXPortable.csproj
```

应用会自动从当前目录向上搜索，直到找到 `TestTools/tools.json` 配置文件。

### 构建发布版本

```powershell
# 构建
dotnet build ConvertXPortable\ConvertXPortable.csproj

# 发布（使用提供的脚本）
.\publish.ps1
```

---

## 📋 功能详解

### 1️⃣ 可视化文件转换

- 拖拽文件或点击按钮选择输入文件
- 自动检测文件类型并推荐可用的转换目标格式
- 一键执行转换，实时查看进度和日志

### 2️⃣ AI 智能助手（新增）

- **AI 聊天**: 与 AI 对话，获取转换建议和技巧
- **智能规划**: AI 分析文件并生成最优转换方案
- **历史记录**: 保存和管理 AI 对话历史
- **多模型支持**: 兼容 OpenAI、Anthropic 等主流 API

配置方法：在设置面板中输入 API Key 和模型信息即可启用。

### 3️⃣ 主题与外观

- **浅色主题**: 清爽明亮的界面
- **深色主题**: 护眼舒适的暗色模式
- **系统跟随**: 自动跟随 Windows 系统主题
- **Mica 效果**: Windows 11 专属毛玻璃背景效果

### 4️⃣ 硬件加速

- 可选启用 NVIDIA CUDA 加速
- 显著提升视频和图片转换性能
- 在设置中一键开关

---

## ⚙️ 配置说明

### 转换规则配置

编辑项目根目录的 [`conversions.json`](conversions.json) 文件来自定义转换规则：

```json
{
  "version": "1.0.0",
  "description": "转换规则描述",
  "conversions": [
    {
      "converter": "ImageMagick",
      "from": ["jpg", "png", "webp"],
      "to": ["jpg", "png", "webp", "pdf"],
      "executable": "ImageMagick/magick.exe",
      "argumentTemplate": "\"{input}\" \"{output}\"",
      "category": "image",
      "priority": 20,
      "description": "通用图片转换",
      "writeStdoutToOutput": false,
      "pipeInputToStdin": false,
      "outputArgumentTemplates": {
        "ico": "-define icon:auto-resize=256,128,64,48,32,16"
      }
    }
  ]
}
```

#### 参数模板占位符

`argumentTemplate` 支持以下动态占位符：

| 占位符 | 说明 | 示例 |
|--------|------|------|
| `{input}` | 输入文件完整路径 | `C:\Users\文档\input.jpg` |
| `{output}` | 输出文件完整路径 | `C:\Users\文档\output.png` |
| `{outputDir}` | 输出文件所在目录 | `C:\Users\文档` |
| `{format}` | 目标格式扩展名 | `png` |

#### 特殊选项

- **`writeStdoutToOutput`**: 设为 `true` 时，将工具的标准输出重定向到输出文件（适用于某些命令行工具）
- **`pipeInputToStdin`**: 设为 `true` 时，将输入文件内容通过标准输入传递给工具
- **`outputArgumentTemplates`**: 针对不同输出格式的额外参数配置

### 工具发现配置

应用依赖 `TestTools/tools.json` 来发现可用的转换工具：

```json
{
  "tools": [
    {
      "category": "image",
      "name": "ImageMagick",
      "mainExecutable": "magick.exe",
      "path": "ImageMagick",
      "description": "强大的图像处理工具",
      "executables": ["magick.exe"]
    }
  ]
}
```

---

## 🏗️ 技术架构

### 系统架构图

```
┌─────────────────────────────────────────────┐
│           WPF UI 层 (MainWindow)            │
│  ┌───────────┐ ┌──────────┐ ┌───────────┐  │
│  │ 文件选择  │ │ AI 聊天  │ │ 日志视图  │  │
│  └───────────┘ └──────────┘ └───────────┘  │
└──────────────────┬──────────────────────────┘
                   │ MVVM 数据绑定
┌──────────────────▼──────────────────────────┐
│         应用服务层                           │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │转换路由器     │ │ AI 转换规划器       │   │
│  └──────────────┘  └────────────────────┘   │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │配置服务      │ │  AI 聊天服务        │   │
│  └──────────────┘  └────────────────────┘   │
│  ┌──────────────┐  ┌────────────────────┐   │
│  │转换执行器    │ │  主题管理器         │   │
│  └──────────────┘  └────────────────────┘   │
└──────────────────┬──────────────────────────┘
                   │ 进程执行
┌──────────────────▼──────────────────────────┐
│      外部工具 (TestTools 工具集)             │
│  ImageMagick | FFmpeg | Inkscape | ...      │
└─────────────────────────────────────────────┘
```

### 核心组件

| 组件 | 职责 |
|------|------|
| **MainWindow** | WPF 主窗口，处理用户交互和 UI 更新 |
| **ConversionRouter** | 根据文件扩展名匹配最佳转换规则 |
| **ConfigurationService** | 加载和管理 conversions.json 与 tools.json |
| **ConversionExecutor** | 启动子进程并管理执行生命周期 |
| **ArgumentTemplate** | 渲染参数模板，替换占位符 |
| **PathResolver** | 向上目录搜索机制定位配置文件 |
| **AiChatService** | 与 AI 模型通信（OpenAI/Anthropic 兼容） |
| **AiConversionPlanner** | 使用 AI 分析并生成转换方案 |
| **ThemeManager** | 管理浅色/深色主题切换 |

### 设计模式

- **MVVM**: WPF 数据绑定架构，分离视图与逻辑
- **服务层解耦**: 各服务独立封装，便于测试和维护
- **配置驱动**: 所有转换行为由 JSON 配置控制，无需修改代码

---

## 💡 使用示例

### 示例 1: 图片格式转换

1. 选择输入文件 `photo.jpg`
2. 选择目标格式 `PNG`
3. 点击"开始转换"
4. 应用自动生成命令：`magick.exe "photo.jpg" "photo.png"`
5. 实时查看转换日志

### 示例 2: 使用 AI 规划复杂转换

1. 打开 AI 助手面板
2. 上传需要转换的文件
3. AI 分析文件并推荐最佳转换方案
4. 确认方案后一键执行

### 示例 3: 批量转换

虽然当前版本主要面向单文件转换，但您可以通过：
- 连续选择多个文件进行转换
- 利用 AI 助手优化批量处理策略

---

## ❓ 常见问题

### Q: 应用提示"未找到 TestTools/tools.json"？

**A**: 请确保：
1. 已准备好 `TestTools` 目录及其中的 `tools.json`
2. 将 `TestTools` 放在应用可执行文件的上级目录中
3. 或者从包含 `TestTools` 的目录启动应用

### Q: 如何添加新的转换工具？

**A**: 
1. 将工具放入 `TestTools` 目录
2. 在 `tools.json` 中添加工具定义
3. 在 `conversions.json` 中添加转换规则
4. 重启应用即可自动发现

### Q: AI 功能如何使用？

**A**:
1. 在设置面板配置 AI 提供商（OpenAI 或 Anthropic 兼容）
2. 输入 API Key 和模型名称
3. 点击"测试连接"验证配置
4. 在 AI 助手面板开始对话或生成转换方案

### Q: 支持哪些 AI 模型？

**A**: 任何兼容 OpenAI API 或 Anthropic API 的模型，包括：
- OpenAI GPT 系列
- Anthropic Claude 系列
- 本地部署的兼容 API（如 Ollama、LocalAI）

### Q: 如何处理包含中文或空格的路径？

**A**: 应用使用 `ProcessStartInfo.ArgumentList` 而非字符串拼接，天然支持特殊字符路径，无需额外转义。

### Q: 可以自定义输出文件名吗？

**A**: 当前版本自动推导输出文件名（保持原名，仅改变扩展名）。如需自定义，可在转换完成后手动重命名。

---

## 🛠️ 开发指南

### 项目结构

```
convertx/
├── ConvertXPortable/          # 主应用程序
│   ├── Models/                # 数据模型
│   │   ├── ConversionModels.cs
│   │   └── AiModels.cs
│   ├── Services/              # 业务逻辑服务
│   │   ├── ConfigurationService.cs
│   │   ├── ConversionExecutor.cs
│   │   ├── ConversionRouter.cs
│   │   ├── AiChatService.cs
│   │   ├── AiConversionPlanner.cs
│   │   ├── ThemeManager.cs
│   │   └── ...
│   ├── Controls/              # 自定义控件
│   │   └── MarkdownViewer.cs
│   ├── MainWindow.xaml        # 主界面定义
│   ├── MainWindow.xaml.cs     # 主界面逻辑
│   └── App.xaml.cs            # 应用入口
├── conversions.json           # 转换规则配置
├── publish.ps1                # 发布脚本
└── README.md                  # 项目文档
```

### 添加新功能

1. **新增转换规则**: 编辑 `conversions.json`
2. **新增服务**: 在 `Services/` 目录创建新服务类
3. **扩展 UI**: 修改 `MainWindow.xaml` 和对应的代码隐藏
4. **添加 AI 功能**: 参考 `AiChatService.cs` 的实现模式

### 调试技巧

- 使用 Visual Studio 或 VS Code 附加调试器
- 查看应用日志输出窗口了解运行时信息
- 检查 `conversions.json` 和 `tools.json` 格式是否正确

---

## 📄 许可证

本项目采用 MIT 许可证。

### 关于 ConvertX 源码引用

ConvertX 转换器源码参考自：
https://github.com/C4illin/ConvertX/tree/main/src/converters

本应用不编译或复制 ConvertX 源代码。如果将这些文件下载到 `References/ConvertX/converters/` 目录，请在重新分发前注意遵守 **AGPL-3.0** 许可证要求。

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

### 贡献指南

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

---

## 📞 联系方式

- 📧 邮箱: [email@jdhuan.top]
---

<div align="center">

**为 Windows 用户用心打造 ❤️**

[⭐ 星标此仓库](link-to-repo) · [🐛 报告问题](link-to-issues) · [💡 请求功能](link-to-issues)

</div>
