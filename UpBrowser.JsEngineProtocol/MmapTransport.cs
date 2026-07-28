using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace UpBrowser.JsEngineProtocol;

/// <summary>
/// 基于内存映射文件的跨平台 IPC 传输层。
/// Windows 使用命名共享内存节（section），其他平台使用文件映射（file-backed mmap）
/// 以在所有系统上可靠地创建/打开共享内存。
/// 两个独立的共享内存区域（主→子 + 子→主），通过内存内的同步标志协调。
/// 不依赖 EventWaitHandle，在所有平台（Windows/Linux/macOS）上均可工作。
/// </summary>
public class MmapTransport : IDisposable
{
    private const int HeaderSize = 4;
    private const int MaxMsgSize = 10 * 1024 * 1024;
    private const int PageSize = MaxMsgSize + 8;
    private const int SyncOffset = 0;
    private const int HeaderOffset = 4;
    private const int DataOffset = 8;

    // 同步标志状态
    private const int StateIdle = 0;
    private const int StateWriting = 1;
    private const int StateReady = 2;

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly string _channelName;
    private readonly string _bootstrapPath;
    private bool _disposed;

    // 本进程写入的通道（对方读）
    private SharedMmap? _outMmap;
    private MemoryMappedViewAccessor? _outView;

    // 本进程读取的通道（对方写）
    private SharedMmap? _inMmap;
    private MemoryMappedViewAccessor? _inView;

    public MmapTransport(string channelName)
    {
        _channelName = channelName;
        var escaped = channelName.Replace("\\", "_").Replace("/", "_").Replace(":", "_");
        _bootstrapPath = Path.Combine(Path.GetTempPath(), $"upbrowser_mmap_{escaped}.bootstrap");
    }

    public async Task StartServerAsync()
    {
        var outName = MmapKey("child_to_main");
        var inName = MmapKey("main_to_child");

        _outMmap = CreateOrOpen(outName, PageSize);
        _inMmap = CreateOrOpen(inName, PageSize);

        File.WriteAllText(_bootstrapPath, $"{_outMmap.Key}\n{_inMmap.Key}");

        int retryCount = 0;
        while (retryCount < 100)
        {
            if (CheckChildReady()) break;
            await Task.Delay(50);
            retryCount++;
        }
        if (retryCount >= 100)
            throw new TimeoutException("Mmap connect timeout — child did not become ready");

        var idle = BitConverter.GetBytes(StateIdle);
        _outView = _outMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _outView.WriteArray<byte>(SyncOffset, idle, 0, 4);
        _inView = _inMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _inView.WriteArray<byte>(SyncOffset, idle, 0, 4);
    }

    private bool CheckChildReady()
    {
        try
        {
            var view = _inMmap!.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.Read);
            var sync = new byte[4];
            view.ReadArray<byte>(0, sync, 0, 4);
            view.Dispose();
            return BitConverter.ToInt32(sync, 0) == StateReady;
        }
        catch { return false; }
    }

    public async Task ConnectClientAsync()
    {
        string[] readLines = null!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(_bootstrapPath))
                {
                    readLines = File.ReadAllLines(_bootstrapPath);
                    if (readLines.Length >= 2) break;
                }
            }
            catch { }
            await Task.Delay(20);
        }
        if (!File.Exists(_bootstrapPath))
            throw new TimeoutException("Mmap connect timeout — bootstrap file not found");

        readLines = File.ReadAllLines(_bootstrapPath);
        // Line 0 = child's _outMmap (child writes, parent reads → parent's _inMmap)
        // Line 1 = child's _inMmap (child reads, parent writes → parent's _outMmap)
        var childOutKey = readLines[0];
        var childInKey = readLines[1];

        await Task.Delay(200);

        _inMmap = OpenMmapRetry(childOutKey, 10000);
        _outMmap = OpenMmapRetry(childInKey, 10000);

        // Signal the child that we are connected. The child polls its _inMmap
        // (main_to_child) for StateReady; our _outMmap is the same section.
        var readyView = _outMmap.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.ReadWrite);
        readyView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateReady), 0, 4);
        readyView.Dispose();

        // Reset the child's _outMmap (child_to_main) to idle so the child's
        // first SendAsync doesn't see stale data in the handshake state.
        readyView = _inMmap.Shm.CreateViewAccessor(0, 8, MemoryMappedFileAccess.ReadWrite);
        readyView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
        readyView.Dispose();

        _inView = _inMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
        _outView = _outMmap.Shm.CreateViewAccessor(0, PageSize, MemoryMappedFileAccess.ReadWrite);
    }

    private static SharedMmap OpenMmapRetry(string key, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { return OpenMmap(key); }
            catch { }
            Task.Delay(20).Wait();
        }
        throw new TimeoutException($"Mmap connect timeout — mapped file '{key}' not found");
    }

    // ── 跨平台 CreateOrOpen ──────────────────────────────────────
    //
    // System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(name, capacity, access)
    // 在非 Windows 平台上仅接受本地路径样式的 name，不能创建真正的系统级共享节。
    // 解决方式：Windows 直接使用命名节；其他平台使用文件映射（file-backed mmap），
    // 所有进程使用同一条绝对路径作为 key 来共享同一文件。

    private string MmapKey(string suffix)
    {
        if (IsWindows)
            return "Local\\UpBrowser_Mmap_" + _channelName + "_" + suffix;
        return Path.Combine(Path.GetTempPath(), $"upbrowser_mmap_{_channelName}_{suffix}.bin");
    }

    private SharedMmap CreateOrOpen(string key, int capacity)
    {
        if (IsWindows)
            return new SharedMmap(key, MemoryMappedFile.CreateOrOpen(key, capacity, MemoryMappedFileAccess.ReadWrite));
        return OpenFileMmap(key, capacity);
    }

    private static SharedMmap OpenMmap(string key)
    {
        if (IsWindows)
            return new SharedMmap(key, MemoryMappedFile.OpenExisting(key));
        return OpenFileMmap(key, 0);
    }

    private static SharedMmap OpenFileMmap(string key, int capacity)
    {
        var shm = MemoryMappedFile.CreateFromFile(key, FileMode.OpenOrCreate, key, (long)(capacity > 0 ? capacity : PageSize), MemoryMappedFileAccess.ReadWrite);
        return new SharedMmap(key, shm);
    }

    public async Task SendAsync(byte[] data)
    {
        if (_disposed || _outView == null) return;
        var len = data.Length;
        if (len > MaxMsgSize)
            throw new InvalidOperationException($"Message too large: {len}");

        var deadline = DateTime.UtcNow.AddSeconds(10);

        Console.WriteLine($"[MmapTransport] SendAsync START len={len}");
        var waitStart = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var state = BitConverter.ToInt32(sync, 0);
            if (state == StateIdle) break;
            await Task.Delay(1);
            if ((DateTime.UtcNow - waitStart).TotalMilliseconds > 200)
            {
                Console.WriteLine($"[MmapTransport] SendAsync waiting for Idle (current={state}, elapsed={(DateTime.UtcNow - waitStart).TotalMilliseconds:F0}ms)");
                waitStart = DateTime.UtcNow;
            }
        }
        if (DateTime.UtcNow >= deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var finalState = BitConverter.ToInt32(sync, 0);
            throw new TimeoutException($"SendAsync: timeout waiting for channel to become idle (final_state={finalState})");
        }

        var header = BitConverter.GetBytes(len);
        var writing = BitConverter.GetBytes(StateWriting);
        _outView.WriteArray<byte>(SyncOffset, writing, 0, 4);
        _outView.WriteArray<byte>(HeaderOffset, header, 0, HeaderSize);
        if (len > 0)
            _outView.WriteArray<byte>(DataOffset, data, 0, len);

        var ready = BitConverter.GetBytes(StateReady);
        _outView.WriteArray<byte>(SyncOffset, ready, 0, 4);

        Console.WriteLine($"[MmapTransport] SendAsync wrote data, waiting for acknowledge");
        while (DateTime.UtcNow < deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var state = BitConverter.ToInt32(sync, 0);
            if (state == StateIdle) break;
            await Task.Delay(1);
        }
        if (DateTime.UtcNow >= deadline)
        {
            var sync = new byte[4];
            _outView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var finalState = BitConverter.ToInt32(sync, 0);
            throw new TimeoutException($"SendAsync: timeout waiting for peer to acknowledge message (final_state={finalState})");
        }
        Console.WriteLine($"[MmapTransport] SendAsync DONE");
    }

    public async Task<byte[]> ReceiveAsync()
    {
        if (_disposed || _inView == null)
            return Array.Empty<byte>();

        Console.WriteLine($"[MmapTransport] ReceiveAsync START");
        var waitStart = DateTime.UtcNow;
        while (true)
        {
            var sync = new byte[4];
            _inView.ReadArray<byte>(SyncOffset, sync, 0, 4);
            var state = BitConverter.ToInt32(sync, 0);
            if (state == StateReady) break;
            await Task.Delay(1);
            if ((DateTime.UtcNow - waitStart).TotalMilliseconds > 200)
            {
                Console.WriteLine($"[MmapTransport] ReceiveAsync waiting for Ready (current={state}, elapsed={(DateTime.UtcNow - waitStart).TotalMilliseconds:F0}ms)");
                waitStart = DateTime.UtcNow;
            }
        }
        Console.WriteLine($"[MmapTransport] ReceiveAsync state=Ready, reading header");

        var header = new byte[HeaderSize];
        int? len = null;
        for (int i = 0; i < 10; i++)
        {
            _inView.ReadArray<byte>(HeaderOffset, header, 0, HeaderSize);
            var l = BitConverter.ToInt32(header, 0);
            var h2 = new byte[HeaderSize];
            _inView.ReadArray<byte>(HeaderOffset, h2, 0, HeaderSize);
            if (BitConverter.ToInt32(h2, 0) == l) { len = l; break; }
            await Task.Delay(1);
        }
        if (len == null)
            throw new InvalidOperationException("Header inconsistent — connection closed");

        var finalLen = (int)len;
        if (finalLen < 0 || finalLen > MaxMsgSize)
            throw new InvalidOperationException($"Invalid message length: {finalLen}");
        if (finalLen == 0)
        {
            _inView.WriteArray<byte>(SyncOffset, BitConverter.GetBytes(StateIdle), 0, 4);
            return Array.Empty<byte>();
        }

        var data = new byte[finalLen];
        _inView.ReadArray<byte>(DataOffset, data, 0, finalLen);

        var idle = BitConverter.GetBytes(StateIdle);
        _inView.WriteArray<byte>(SyncOffset, idle, 0, 4);
        Console.WriteLine($"[MmapTransport] ReceiveAsync DONE len={finalLen}");

        return data;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _outView?.Dispose(); } catch { }
        try { _inView?.Dispose(); } catch { }
        try { _outMmap?.Dispose(); } catch { }
        try { _inMmap?.Dispose(); } catch { }
        try { File.Delete(_bootstrapPath); } catch { }
    }
}

/// <summary>跨平台共享内存封装</summary>
internal sealed class SharedMmap : IDisposable
{
    public readonly string Key;
    public readonly MemoryMappedFile Shm;
    private bool _disposed;

    public SharedMmap(string key, MemoryMappedFile shm)
    {
        Key = key;
        Shm = shm;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shm.Dispose();
    }
}
