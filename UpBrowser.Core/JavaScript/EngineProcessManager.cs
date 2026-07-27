namespace UpBrowser.Core.JavaScript;

/// <summary>
/// 管理 JsEngineHost 进程的生命周期和连接池。
/// </summary>
public static class EngineProcessManager
{
    private static readonly Dictionary<int, RemoteJsEngineAdapter> _engines = new();
    private static readonly object _lock = new();
    private static int _nextChannelId;

    /// <summary>
    /// 获取或创建指定标签页的远程 JS 引擎适配器。
    /// </summary>
    public static RemoteJsEngineAdapter GetOrCreate(int tabIndex)
    {
        lock (_lock)
        {
            if (_engines.TryGetValue(tabIndex, out var existing))
                return existing;

            var channelId = Interlocked.Increment(ref _nextChannelId);
            var channelName = $"UpBrowser_JS_{channelId}";
            var adapter = new RemoteJsEngineAdapter(channelName, "Jint", tabIndex);
            adapter.StartAsync().Wait();
            _engines[tabIndex] = adapter;
            return adapter;
        }
    }

    /// <summary>
    /// 释放指定标签页的 JS 引擎。
    /// </summary>
    public static void Release(int tabIndex)
    {
        lock (_lock)
        {
            if (_engines.TryGetValue(tabIndex, out var adapter))
            {
                adapter.Dispose();
                _engines.Remove(tabIndex);
            }
        }
    }

    /// <summary>
    /// 释放所有 JS 引擎。
    /// </summary>
    public static void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var adapter in _engines.Values)
                adapter.Dispose();
            _engines.Clear();
        }
    }
}