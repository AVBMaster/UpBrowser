# UpBrowser — Agent Guide

## Build & run

```powershell
dotnet build                          # build all projects
dotnet run --project UpBrowser        # launch the browser app
```

## Project structure

| Project | Description |
|---------|-------------|
| `UpBrowser/` | Desktop app entry — `Program.cs` → `BrowserApp.RunAsync()` |
| `UpBrowser.Core/` | Core engine — CSS, DOM, Layout, JS, Network, Performance, Fonts |
| `UpBrowser.Rendering/` | SkiaSharp rendering, Chrome UI, DevTools |
| `UpBrowser.Platform/` | Platform abstraction — Win32 / X11 / Cocoa |
| `UpBrowser.Native/` | Native P/Invoke interop (IME, etc.) |
| `UpBrowser.Input/` | Input method (IME) |
| `UpBrowser.Core.Tests/` | xUnit tests + hand-rolled micro-benchmarks |
| `UpBrowser.PerfSmokeTest/` | Performance smoke-test console app |

Solution format: `.slnx` (new XML-based format, VS 2022+ / `dotnet` CLI).

Project dependency order: `Core` → `Platform`+`Input`+`Native` → `Rendering` → `UpBrowser` (app).

## Framework & toolchain

- **.NET 10.0**, nullable enabled, implicit usings everywhere.
- `AllowUnsafeBlocks` in: `UpBrowser`, `Rendering`, `Platform`, `Native`.
- **AOT**, UpBrowser is based on AOT and JsEngineHost is normal(js engine can't aot) , so avoid reflection and make sure the project is cross-platfrom.
- **SkiaSharp 4.150.1** for all rendering (CPU + OpenGL GPU).
- **AngleSharp** for HTML parsing, **JavaScriptEngineSwitcher.*`** for JS engines.
- Some documents about html standard in ./docs.
- Embedded resources in `UpBrowser.Core/Resources/Html/` and `Resources/Css/`.
- No `Directory.Build.props` — each project self-configures.

## Testing

- **xUnit** (`Microsoft.NET.Test.Sdk` 18.7.0).
- Tests are only in `UpBrowser.Core.Tests/`.
- `UpBrowser.Core.Tests/Performance/` contains ~14 test files for performance subsystems.
- `UpBrowser.Core.Tests/Benchmarks/MicroBenchmarks.cs` — hand-rolled throughput tests using `ITestOutputHelper`, runnable via `dotnet test`.
- No integration tests (no browser-level UI tests).
- However,there are some problems in test,so never run test!!!

## Project conventions

- Namespace matches folder structure (e.g. `UpBrowser.Core.Performance.Scheduling`).
- Single solution file at root: `UpBrowser.slnx`.
- No CI workflows, no pre-commit hooks, no lint/styling config.
- `docs/` directories contain reference notes about DOM/CSS/browser API surface.
- Test pages: `test_css_features.html`, `test_js.html`, `test_wrapping.html`.
- Never never lose the exist function,unless user want to delete or change it.
- Use Chinese in chat, but use English in code.
