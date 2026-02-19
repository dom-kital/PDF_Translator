# PDFTranslator

📄 **PDFTranslator** 是一款基于 [Ollama](https://ollama.ai/) 的本地 PDF 翻译工具，提供图形界面（GUI）和命令行界面（CLI），支持双语对照和仅译文两种翻译模式，并尽可能保持原始 PDF 的排版（包括文本、图像和图形）。

![GUI Screenshot](.\GUI_screenshot.PNG) <!-- 如果您有截图，请替换为实际路径 -->

##目录结构
```bash
PDFTranslator/
├── PDFTranslator.sln
├── PDFTranslator.Core/               # 核心库
│   ├── PDFTranslator.Core.csproj
│   ├── TranslationOptions.cs          # 翻译配置选项
│   ├── OllamaService.cs               # Ollama API 客户端
│   ├── ServiceCollectionExtensions.cs # 依赖注入扩展
│   └── PdfTranslator.cs                # PDF 翻译核心逻辑
├── PDFTranslator.CLI/                 # 命令行界面
│   ├── PDFTranslator.CLI.csproj
│   └── Program.cs                      # 命令行入口
├── PDFTranslator.GUI/                  # 图形界面
│   ├── PDFTranslator.GUI.csproj
│   ├── Program.cs                       # GUI 入口
│   ├── App.axaml                        # 应用定义
│   ├── App.axaml.cs
│   ├── ViewModels/                       # 视图模型
│   │   ├── ViewModelBase.cs
│   │   └── MainWindowViewModel.cs
│   ├── Views/                             # 视图
│   │   ├── MainWindow.axaml
│   │   └── MainWindow.axaml.cs
│   └── Assets/                             # 资源文件（如图标）
├── README.md                               # 项目说明
├── LICENSE                                  # 许可证文件
└── .gitignore                               # Git 忽略文件
```

## ✨ 主要功能

- **🖥️ 双界面支持**：提供跨平台图形界面（基于 Avalonia）和 .NET 命令行工具，满足不同使用习惯。
- **🌐 本地翻译**：通过 Ollama 调用本地大语言模型（如 `llama3.2`、`qwen2.5` 等），无需联网，保护文档隐私。
- **📄 两种翻译模式**：
  - **双语对照**：保留原文，在原文下方添加蓝色半透明译文，便于对比学习。
  - **仅译文**：用白色矩形覆盖原文，并在原位置绘制黑色译文（适合背景为白色的文档）。
- **🖼️ 保留原始排版**：尽量保持原 PDF 的文本位置、字体大小、图像和矢量图形，译文位置基于原文位置动态计算。
- **⚙️ 灵活配置**：支持通过命令行参数或 GUI 选项指定 Ollama 模型、翻译模式等。
- **📦 开源免费**：基于 AGPL-3.0 许可证发布，欢迎贡献代码。

## 🚀 快速开始

### 系统要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（或 .NET 8/9 兼容版本）
- [Ollama](https://ollama.ai/) 已安装并运行（默认地址 `http://localhost:11434`）
- 至少一个翻译模型（例如 `llama3.2`：`ollama pull llama3.2`）

### 下载与编译

```bash
git clone https://github.com/yourname/PDFTranslator.git
cd PDFTranslator
dotnet restore
dotnet build
```

### 运行 GUI 版本
```bash
dotnet run --project PDFTranslator.GUI
```
### 运行 CLI 版本
```bash
dotnet run --project PDFTranslator.CLI -- <输入PDF> <输出PDF> [选项]
```
#### 示例
```bash
# 仅译文模式
dotnet run --project PDFTranslator.CLI -- input.pdf output.pdf

# 双语对照模式，使用模型 qwen2.5
dotnet run --project PDFTranslator.CLI -- input.pdf output.pdf --mode bilingual --model qwen2.5
```
#### 命令行选项
| 选项  |说明   |
| ------------ | ------------ |
| - -mode, -m <模式>  | 翻译模式：translate（仅译文）或 bilingual（双语对照），默认 translate  |
|  --model <模型名> | Ollama 模型名称，默认 llama3.2  |
| --translate-images, -ti <true/false>  | 是否翻译图片中的文字（预留，暂未实现），默认 false  |
|  --help, -h |  显示帮助信息 |

### 缺点
- 字体支持：默认字体不支持中文，需手动配置中文字体（如上所述）。
- 仅译文模式的背景：目前用白色矩形覆盖原文，若原 PDF 背景非白色，会留下白色块。后续可考虑提取背景色或使用半透明覆盖。
- 图片翻译：尚未实现图片文字识别与翻译（计划集成 Tesseract 或多模态模型）。
- 文本块合并：当前每个文本块（可能为单词或字符）单独翻译，可能导致上下文割裂，后续版本将优化为按行或段落合并翻译。