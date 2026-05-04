using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace UpBrowser.Core.JavaScript;

public class JavaScriptEngine : IDisposable
{
    private V8ScriptEngine? _engine;
    private bool _disposed;

    public JavaScriptEngine()
    {
        _engine = new V8ScriptEngine();
        SetupGlobals();
    }

    private void SetupGlobals()
    {
        if (_engine == null) return;

        _engine.AddHostObject("console", new ConsoleHost());

        _engine.Execute(@"
            var document = {
                getElementById: function(id) { return null; },
                getElementsByClassName: function(className) { return []; },
                getElementsByTagName: function(tagName) { return []; },
                createElement: function(tagName) { return {}; },
                body: null,
                head: null,
                title: '',
                location: { href: '', protocol: '' }
            };
            var window = document.defaultView = document;
            var navigator = { userAgent: 'UpBrowser/1.0' };
            var location = document.location;
        ");
    }

    public void Execute(string code)
    {
        _engine?.Execute(code);
    }

    public object? Evaluate(string expression)
    {
        return _engine?.Evaluate(expression);
    }

    public void SetGlobal(string name, object? value)
    {
        _engine?.AddHostObject(name, value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class ConsoleHost
{
    public void log(object? message)
    {
        Console.WriteLine($"[JS] {message}");
    }

    public void error(object? message)
    {
        Console.WriteLine($"[JS Error] {message}");
    }

    public void warn(object? message)
    {
        Console.WriteLine($"[JS Warning] {message}");
    }

    public void info(object? message)
    {
        Console.WriteLine($"[JS Info] {message}");
    }
}