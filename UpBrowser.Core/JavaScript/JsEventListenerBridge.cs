using System.Collections.Concurrent;
using UpBrowser.Core.Dom;
using DomEvent = UpBrowser.Core.Dom.Event;

namespace UpBrowser.Core.JavaScript;

public class JsEventListenerEntry
{
    public int CallbackId { get; set; }
    public object? JsCallback { get; set; }
    public bool UseCapture { get; set; }
    public bool Once { get; set; }
    public bool Passive { get; set; }
    public AbortSignal? Signal { get; set; }
}

public class JsEventListenerBridge
{
    private readonly BrowserJsEngineFacade _facade;
    private readonly ConcurrentDictionary<string, List<JsEventListenerEntry>> _listeners = new();
    private readonly object _lock = new();

    public JsEventListenerBridge(BrowserJsEngineFacade facade)
    {
        _facade = facade;
    }

    public int AddListener(string type, object jsCallback, bool useCapture = false, bool once = false, bool passive = false, AbortSignal? signal = null)
    {
        var callbackId = _facade.StoreJsFunction(jsCallback);
        var entry = new JsEventListenerEntry
        {
            CallbackId = callbackId,
            JsCallback = jsCallback,
            UseCapture = useCapture,
            Once = once,
            Passive = passive,
            Signal = signal
        };

        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var list))
            {
                list = new List<JsEventListenerEntry>();
                _listeners[type] = list;
            }
            list.Add(entry);
        }

        if (signal != null && !signal.Aborted)
            signal.AddCallback(() => RemoveListener(type, callbackId));

        return callbackId;
    }

    public void RemoveListener(string type, int callbackId)
    {
        lock (_lock)
        {
            if (_listeners.TryGetValue(type, out var list))
            {
                list.RemoveAll(l => l.CallbackId == callbackId);
                if (list.Count == 0)
                    _listeners.TryRemove(type, out _);
            }
        }
        _facade.RemoveJsFunction(callbackId);
    }

    public void RemoveListener(string type, object jsCallback)
    {
        lock (_lock)
        {
            if (_listeners.TryGetValue(type, out var list))
            {
                list.RemoveAll(l => ReferenceEquals(l.JsCallback, jsCallback));
                if (list.Count == 0)
                    _listeners.TryRemove(type, out _);
            }
        }
    }

    public List<JsEventListenerEntry> GetListeners(string type, bool capture = false)
    {
        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var list))
                return new List<JsEventListenerEntry>();

            return list.Where(l => l.UseCapture == capture).ToList();
        }
    }

    public bool DispatchEvent(string type, DomEvent evt, object? target = null)
    {
        List<JsEventListenerEntry> listeners;
        lock (_lock)
        {
            if (!_listeners.TryGetValue(type, out var all))
                return true;
            listeners = all.ToList();
        }

        foreach (var listener in listeners)
        {
            if (listener.Signal?.Aborted == true)
            {
                RemoveListener(type, listener.CallbackId);
                continue;
            }

            if (listener.Once)
                RemoveListener(type, listener.CallbackId);

            try
            {
                if (target != null)
                    _facade.InvokeJsFunction(listener.CallbackId, target);
                else
                    _facade.InvokeJsFunction(listener.CallbackId);
            }
            catch { }
        }

        return true;
    }

    public void ClearListeners()
    {
        lock (_lock)
        {
            foreach (var kvp in _listeners)
            {
                foreach (var entry in kvp.Value)
                    _facade.RemoveJsFunction(entry.CallbackId);
            }
            _listeners.Clear();
        }
    }

    public int ListenerCount
    {
        get
        {
            lock (_lock)
                return _listeners.Sum(kvp => kvp.Value.Count);
        }
    }
}
