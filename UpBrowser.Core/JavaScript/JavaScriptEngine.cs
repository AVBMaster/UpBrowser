using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using DomDocument = UpBrowser.Core.Dom.Document;
using DomElement = UpBrowser.Core.Dom.Element;

namespace UpBrowser.Core.JavaScript;

public class JavaScriptEngine : IDisposable
{
    private V8ScriptEngine? _engine;
    private bool _disposed;
    private DocumentHost? _documentHost;
    private DomDocument? _currentDocument;
    private int _nextTimerId = 1;
    private readonly Dictionary<int, TimerInfo> _timers = new();
    private readonly object _timersLock = new();

    [ThreadStatic]
    internal static JavaScriptEngine? Current;

    public event Action? OnDomChanged;

    public JavaScriptEngine()
    {
        _engine = new V8ScriptEngine();
        SetupGlobals();
    }

    private void SetupGlobals()
    {
        if (_engine == null) return;

        _engine.AddHostObject("console", new ConsoleHost());
        _engine.AddHostObject("__upbrowser", new UpBrowserBuiltins(this));

        _engine.Execute(@"
            var window = this;
            var globalThis = this;
            var self = this;

            function setTimeout(fn, ms) { return __upbrowser.setTimeout(fn, ms || 0); }
            function setInterval(fn, ms) { return __upbrowser.setInterval(fn, ms || 0); }
            function clearTimeout(id) { __upbrowser.clearTimeout(id); }
            function clearInterval(id) { __upbrowser.clearInterval(id); }
            function requestAnimationFrame(fn) { setTimeout(fn, 16); }
            function alert(msg) { __win.alert(msg); }
            function confirm(msg) { return __win.confirm(msg); }
            function prompt(msg, def) { return __win.prompt(msg, def); }
            function decodeURI(str) { return __upbrowser.decodeURI(str); }
            function encodeURI(str) { return __upbrowser.encodeURI(str); }
            function parseInt(s, r) { return __upbrowser.parseInt(s, r); }
            function parseFloat(s) { return __upbrowser.parseFloat(s); }
            function isNaN(v) { return __upbrowser.isNaN(v); }
            function isFinite(v) { return __upbrowser.isFinite(v); }
        ");

        _engine.AddHostObject("__win", new WindowHost());
        _engine.AddHostObject("navigator", new NavigatorHost());
        _engine.AddHostObject("location", new LocationHost());
        _engine.AddHostObject("history", new HistoryHost());
    }

    public void LoadDocument(DomDocument document)
    {
        _currentDocument = document;
        _documentHost = new DocumentHost(document);

        if (_engine != null)
        {
            _engine.AddHostObject("document", _documentHost);
        }

        MarkDirty();
    }

    public void Execute(string code)
    {
        if (_engine == null || string.IsNullOrEmpty(code)) return;
        Current = this;
        try
        {
            _engine.Execute(code);
            MarkDirty();
        }
        catch (ScriptEngineException ex)
        {
            Console.WriteLine($"[JS Error] {ex.ErrorDetails}");
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression)
    {
        if (_engine == null) return null;
        Current = this;
        try
        {
            return _engine.Evaluate(expression);
        }
        catch
        {
            return null;
        }
        finally
        {
            Current = null;
        }
    }

    public void SetGlobal(string name, object? value)
    {
        _engine?.AddHostObject(name, value);
    }

    public ElementHost? GetElementHost(DomElement element)
    {
        return new ElementHost(element);
    }

    public void DispatchEvent(DomElement element, string eventType)
    {
        var targetHost = new ElementHost(element);
        var evt = new ScriptEvent(eventType, targetHost);
        targetHost.DispatchEvent(evt);
    }

    public void TickTimers()
    {
        List<Action> due = new();
        lock (_timersLock)
        {
            var now = Environment.TickCount64;
            foreach (var kv in _timers.ToList())
            {
                if (now >= kv.Value.DueTime)
                {
                    due.Add(() =>
                    {
                        try
                        {
                            kv.Value.Callback.Invoke(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[JS Timer] {ex.Message}");
                        }
                    });

                    if (kv.Value.Interval > 0)
                        kv.Value.DueTime = now + kv.Value.Interval;
                    else
                        _timers.Remove(kv.Key);
                }
            }
        }

        foreach (var action in due)
        {
            Current = this;
            try { action(); }
            finally { Current = null; }
        }
    }

    public bool NeedsReLayout { get; set; }

    public void MarkDirty()
    {
        NeedsReLayout = true;
        OnDomChanged?.Invoke();
    }

    public void ClearDirty()
    {
        NeedsReLayout = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_timersLock) _timers.Clear();
        _engine?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // Called from UpBrowserBuiltins
    internal int SetTimer(ScriptObject callback, int delayMs, bool recurring)
    {
        lock (_timersLock)
        {
            var id = _nextTimerId++;
            _timers[id] = new TimerInfo
            {
                Callback = callback,
                DueTime = Environment.TickCount64 + Math.Max(1, delayMs),
                Interval = recurring ? Math.Max(1, delayMs) : 0
            };
            return id;
        }
    }

    internal void ClearTimer(int id)
    {
        lock (_timersLock) _timers.Remove(id);
    }
}

internal class TimerInfo
{
    public ScriptObject Callback { get; set; } = null!;
    public long DueTime { get; set; }
    public int Interval { get; set; }
}

public class WindowHost
{
    public void alert(object? message)
    {
        var msg = message?.ToString() ?? "";
        Console.WriteLine("");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.WriteLine($"  JavaScript Alert:");
        Console.WriteLine($"  {msg}");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.WriteLine("  Press Enter to continue...");
        Console.WriteLine("");
        try { System.Console.ReadLine(); } catch { }
    }

    public bool confirm(object? message)
    {
        var msg = message?.ToString() ?? "";
        Console.WriteLine("");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.WriteLine($"  JavaScript Confirm:");
        Console.WriteLine($"  {msg}");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.Write("  (y/N): ");
        try
        {
            var key = System.Console.ReadLine()?.Trim().ToLowerInvariant();
            return key == "y" || key == "yes";
        }
        catch { return false; }
    }

    public string? prompt(object? message, object? defaultValue)
    {
        var msg = message?.ToString() ?? "";
        var def = defaultValue?.ToString() ?? "";
        Console.WriteLine("");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.WriteLine($"  JavaScript Prompt:");
        Console.WriteLine($"  {msg}");
        Console.WriteLine($"═══════════════════════════════════════");
        Console.Write($"  [{def}]: ");
        try
        {
            var input = System.Console.ReadLine();
            return string.IsNullOrEmpty(input) ? def : input;
        }
        catch { return def; }
    }
}

public class UpBrowserBuiltins
{
    private readonly JavaScriptEngine _engine;

    public UpBrowserBuiltins(JavaScriptEngine engine) => _engine = engine;

    public int setTimeout(ScriptObject fn, int ms) => _engine.SetTimer(fn, ms, false);
    public int setInterval(ScriptObject fn, int ms) => _engine.SetTimer(fn, ms, true);
    public void clearTimeout(int id) => _engine.ClearTimer(id);
    public void clearInterval(int id) => _engine.ClearTimer(id);

    public string decodeURI(string str) => Uri.UnescapeDataString(str);
    public string encodeURI(string str) => Uri.EscapeDataString(str);
    public int parseInt(string s, int radix)
    {
        try { return Convert.ToInt32(s, radix); }
        catch { return int.TryParse(s, out var r) ? r : 0; }
    }
    public double parseFloat(string s) => double.TryParse(s, out var r) ? r : double.NaN;
    public bool isNaN(object? v)
    {
        if (v == null) return true;
        if (v is double dv) return double.IsNaN(dv);
        if (v is float fv) return double.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed)) return double.IsNaN(parsed);
        return true;
    }
    public bool isFinite(object? v)
    {
        if (v == null) return false;
        if (v is double dv) return !double.IsInfinity(dv) && !double.IsNaN(dv);
        if (v is float fv) return !float.IsInfinity(fv) && !float.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed))
            return !double.IsInfinity(parsed) && !double.IsNaN(parsed);
        return false;
    }
}

public class ConsoleHost
{
    public void log(params object?[] args) => WriteLine("[JS]", args);
    public void error(params object?[] args) => WriteLine("[JS Error]", args);
    public void warn(params object?[] args) => WriteLine("[JS Warning]", args);
    public void info(params object?[] args) => WriteLine("[JS Info]", args);

    private static void WriteLine(string prefix, object?[] args)
    {
        Console.WriteLine(prefix + " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }
}

public class NavigatorHost
{
    public string userAgent => "UpBrowser/1.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    public string appName => "UpBrowser";
    public string appVersion => "1.0";
    public string platform => "Win32";
    public string language => "zh-CN";
    public string[] languages => new[] { "zh-CN", "en" };
    public bool cookieEnabled => true;
    public bool onLine => true;
}

public class LocationHost
{
    public string href { get; set; } = "upbrowser://local";
    public string protocol { get; set; } = "upbrowser:";
    public string hostname { get; set; } = "local";
    public string host { get; set; } = "local";
    public string port { get; set; } = "";
    public string pathname { get; set; } = "/";
    public string search { get; set; } = "";
    public string hash { get; set; } = "";
    public string origin { get; set; } = "upbrowser://local";

    public void assign(string url) { href = url; }
    public void replace(string url) { href = url; }
    public void reload() { }
}

public class HistoryHost
{
    public int length => 0;
    public int scrollRestoration { get; set; }
    public void back() { }
    public void forward() { }
    public void go(int delta) { }
    public void pushState(object? state, string title, string? url) { }
    public void replaceState(object? state, string title, string? url) { }
}
