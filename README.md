# UpBrowser

一个用 C# 和 SkiaSharp 构建的现代浏览器引擎。

## 功能特性

- **完整的布局引擎**：支持 CSS Block、Inline、Flexbox、Grid、Table 等布局模式
- **文本渲染**：使用 SkiaSharp 进行高质量文本渲染和测量
- **DOM 解析**：基于 AngleSharp 的 HTML 解析器
- **JavaScript 支持**：集成 Jint JavaScript 引擎
- **跨平台**：支持 Windows、macOS、Linux 平台
- **硬件加速**：支持 OpenGL GPU 加速

## 构建要求

- .NET 10.0 SDK
- Windows/macOS/Linux 操作系统

## 构建项目

```bash
dotnet build
```

## 运行浏览器

```bash
dotnet run
```

## 布局引擎特性

### 已实现的布局模式

1. **Block 布局**
   - margin collapsing（外边距折叠）
   - float 浮动元素
   - clear 清除浮动

2. **Inline 布局**
   - 文本换行
   - inline-block 元素
   - 文本对齐

3. **Flexbox 布局**
   - flex-direction（主轴方向）
   - flex-wrap（换行）
   - justify-content（主轴对齐）
   - align-items（交叉轴对齐）
   - align-self（单个项目对齐）

4. **绝对定位**
   - containing block 计算
   - auto margin 居中
   - fixed/absolute 定位

5. **表格布局**
   - 多列宽度计算
   - 表格单元格布局

### 文本测量

使用 SkiaSharp 进行精确的文本宽度测量，确保文本渲染的准确性。

## 项目结构

```
UpBrowser/
├── UpBrowser.Core/          # 核心库
│   ├── Dom/                 # DOM 实现
│   ├── Layout/              # 布局引擎
│   │   ├── Grid/           # CSS Grid 布局
│   │   └── LayoutEngine.cs # 主布局引擎
│   ├── Css/                 # CSS 解析
│   └── JavaScript/          # JavaScript 引擎
├── UpBrowser.Rendering/     # 渲染层
│   ├── PaintVisitor.cs      # 绘制访问者
│   └── ChromeRenderer.cs    # 浏览器 UI 渲染
├── UpBrowser.Platform/      # 平台特定实现
│   ├── Windows/            # Windows 实现
│   ├── Mac/                # macOS 实现
│   └── Linux/              # Linux 实现
└── UpBrowser/               # 主应用程序
```

## 技术栈

- **SkiaSharp**：2D 图形渲染
- **AngleSharp**：HTML/CSS 解析
- **ClearScript/Jint**：JavaScript 引擎
- **.NET 10**：运行时

## 开发计划

- [ ] 完善 CSS Grid 布局
- [ ] 支持更多 CSS 属性
- [ ] 性能优化
- [ ] DevTools 调试工具
- [ ] 网络请求支持

## 贡献与建议

浏览器引擎的开发是一个复杂且长期的过程，欢迎社区贡献代码和提出建议！

## 许可证

MIT License
