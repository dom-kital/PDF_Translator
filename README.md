# PDF翻译器
一种基于调用ollama服务而实现的PDF翻译器。该翻译器具备GUI和CLI两种交互模式。
原理：以下提供基于 PdfPig（读取）+ QuestPDF（生成）的 C# 完整解决方案，包含 CLI 和 GUI，支持仅翻译和双语对照两种模式，并可以预览指定页面翻译效果。
## 1 目录结构
- PdfTranslator.Core        // 类库 (共享核心逻辑)
- PdfTranslator.CLI         // 控制台应用 (命令行接口)
- PdfTranslator.GUI         // Windows Forms 应用 (图形界面)
## 2 搭建安装环境
### 2.1 利用dotnet命令行安装PdfTranslator.Core所需库
```
dotnet add package UglyToad.PdfPig
dotnet add package QuestPDF
dotnet add package Microsoft.Extensions.Http
dotnet add package System.Text.Json
```
### 2.2  利用dotnet命令行安装PdfTranslator.CLI所需库
```
dotnet add package System.CommandLine
dotnet add package System.CommandLine.NamingConventionBinder
```
