# UpBrowser

> A modern browser engine built with C# and SkiaSharp.  
> 一个使用 C# 和 SkiaSharp 构建的现代浏览器引擎。

## Features / 功能特性

- **Complete Layout Engine / 完整的布局引擎**  
  Supports CSS Block, Inline, Flexbox, Grid, Table, and more.  
  支持 CSS Block、Inline、Flexbox、Grid、Table 等布局模式。

- **Text Rendering / 文本渲染**  
  High-quality text rendering and measurement using SkiaSharp.  
  使用 SkiaSharp 进行高质量文本渲染和测量。

- **DOM Parsing / DOM 解析**  
  HTML parser powered by AngleSharp.  
  基于 AngleSharp 的 HTML 解析器。

- **JavaScript Support / JavaScript 支持**  
  Integrated Jint JavaScript engine.  
  集成 Jint JavaScript 引擎。

- **Cross-Platform / 跨平台**  
  Supports Windows, macOS, and Linux.  
  支持 Windows、macOS、Linux 平台。

- **Hardware Acceleration / 硬件加速**  
  OpenGL GPU acceleration support.  
  支持 OpenGL GPU 加速。

## Requirements / 构建要求

- .NET 10.0 SDK
- Windows / macOS / Linux Operating System  
  Windows / macOS / Linux 操作系统

## Build / 构建项目

```bash
dotnet build
```

## Run / 运行浏览器

```bash
dotnet run
```

## Layout Engine / 布局引擎特性

### Implemented Layout Modes / 已实现的布局模式

1. **Block Layout / Block 布局**
   - Margin collapsing / 外边距折叠
   - Float elements / float 浮动元素
   - Clear floats / clear 清除浮动

2. **Inline Layout / Inline 布局**
   - Text wrapping / 文本换行
   - Inline-block elements / inline-block 元素
   - Text alignment / 文本对齐

3. **Flexbox Layout / Flexbox 布局**
   - `flex-direction` / 主轴方向
   - `flex-wrap` / 换行
   - `justify-content` / 主轴对齐
   - `align-items` / 交叉轴对齐
   - `align-self` / 单个项目对齐

4. **Absolute Positioning / 绝对定位**
   - Containing block calculation / containing block 计算
   - Auto margin centering / auto margin 居中
   - Fixed / absolute positioning / fixed/absolute 定位

5. **Table Layout / 表格布局**
   - Multi-column width calculation / 多列宽度计算
   - Table cell layout / 表格单元格布局

### Text Measurement / 文本测量

Precise text width measurement using SkiaSharp to ensure accurate text rendering.  
使用 SkiaSharp 进行精确的文本宽度测量，确保文本渲染的准确性。

## Project Structure / 项目结构

```
UpBrowser/
├── UpBrowser.Core/          # Core Library / 核心库
│   ├── Dom/                 # DOM Implementation / DOM 实现
│   ├── Layout/              # Layout Engine / 布局引擎
│   │   ├── Grid/           # CSS Grid Layout / CSS Grid 布局
│   │   └── LayoutEngine.cs # Main Layout Engine / 主布局引擎
│   ├── Css/                 # CSS Parsing / CSS 解析
│   └── JavaScript/          # JavaScript Engine / JavaScript 引擎
├── UpBrowser.Rendering/     # Rendering Layer / 渲染层
│   ├── PaintVisitor.cs      # Paint Visitor / 绘制访问者
│   └── ChromeRenderer.cs    # Browser UI Rendering / 浏览器 UI 渲染
├── UpBrowser.Platform/      # Platform-Specific Implementation / 平台特定实现
│   ├── Windows/            # Windows Implementation / Windows 实现
│   ├── Mac/                # macOS Implementation / macOS 实现
│   └── Linux/              # Linux Implementation / Linux 实现
└── UpBrowser/               # Main Application / 主应用程序
```

## Tech Stack / 技术栈

- **SkiaSharp**: 2D Graphics Rendering / 2D 图形渲染
- **AngleSharp**: HTML/CSS Parsing / HTML/CSS 解析
- **ClearScript / Jint**: JavaScript Engine / JavaScript 引擎
- **.NET 10**: Runtime / 运行时

## Roadmap / 开发计划

- [ ] Improve CSS Grid Layout / 完善 CSS Grid 布局
- [ ] Support more CSS properties / 支持更多 CSS 属性
- [ ] Performance optimization / 性能优化
- [ ] DevTools debugging tools / DevTools 调试工具
- [ ] Network request support / 网络请求支持
- [ ] Improve JavaScript supporting / 完善JS支持

## Contributing / 贡献与建议

Browser engine development is a complex and long-term process. Community contributions and suggestions are welcome!  
浏览器引擎的开发是一个复杂且长期的过程，欢迎社区贡献代码和提出建议！

## License / 许可证

MIT License
