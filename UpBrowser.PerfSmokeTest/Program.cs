using System;
using UpBrowser.Core.Performance;
using UpBrowser.Core.Performance.Diagnostics;
using UpBrowser.Core.Performance.Memory;
using UpBrowser.Core.Performance.Rendering;
using UpBrowser.Core.Performance.Resources;
using UpBrowser.Core.Performance.Scheduling;
using UpBrowser.Core.Performance.Compositor;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using SkiaSharp;

class Program
{
    static int Main()
    {
        int passed = 0, failed = 0;

        Run("Hub initializes and exposes subsystems", () =>
        {
            var hub = new PerformanceHub();
            hub.Initialize();
            Assert(hub.Enabled, "enabled");
            Assert(hub.StyleCache is not null, "style cache");
            Assert(hub.LayoutCache is not null, "layout cache");
            Assert(hub.Scheduler is not null, "scheduler");
            Assert(hub.LongTasks is not null, "long task observer");
            Assert(hub.ResourceCache is not null, "resource cache");
            Assert(hub.ImagePool is not null, "image pool");
            Assert(hub.Tiles is not null, "tiles");
            Assert(hub.Rasterizer is not null, "rasterizer");
            Assert(hub.PredictiveScheduler is not null, "predictive");
            Assert(hub.CompositorReplayer is not null, "replayer");
            Assert(hub.Registry.MemoryPressure is not null, "memory pressure");
            Assert(hub.Registry is not null, "registry");
            Assert(hub.Api is not null, "api");
            hub.Shutdown();
            Assert(!hub.Enabled, "shutdown");
        }, ref passed, ref failed);

        Run("Style cache hit/miss tracking", () =>
        {
            var cache = new SharedStyleCache();
            var k = new SharedStyleCache.Key(1, 2, 3, 4, 5, 6, 7);
            Assert(cache.TryGetShared(k) is null, "miss");
            var s1 = cache.GetOrAdd(k, _ => new UpBrowser.Core.Dom.ComputedStyle());
            var s2 = cache.GetOrAdd(k, _ => new UpBrowser.Core.Dom.ComputedStyle());
            Assert(ReferenceEquals(s1, s2), "shared instance");
            Assert(cache.Hits == 1, "hit count");
            Assert(cache.Insertions == 1, "insertion count");
        }, ref passed, ref failed);

        Run("Layout cache invalidation", () =>
        {
            var cache = new LayoutCache();
            var el = new UpBrowser.Core.Dom.HtmlElement("div");
            var key = new LayoutCacheKey(el, 100, 1000, 16, 1.0f);
            cache.Store(el, key, new LayoutBox(), 100, 200, 1, 0);
            DirtyState.ClearAll(el);
            Assert(cache.TryGetCached(el, key, out _), "cache hit when clean");
            DirtyState.AddSelf(el, DirtyFlags.Layout);
            Assert(!cache.TryGetCached(el, key, out _), "cache miss when dirty");
        }, ref passed, ref failed);

        Run("Dirty flags propagation", () =>
        {
            var root = new UpBrowser.Core.Dom.HtmlElement("div");
            var child = new UpBrowser.Core.Dom.HtmlElement("span");
            root.AppendChild(child);
            DirtyState.AddSelf(root, DirtyFlags.Layout);
            DirtyState.AddChildren(root, DirtyFlags.Layout);
            DirtyState.AddSelf(child, DirtyFlags.AllLayout);
            DirtyState.AddChildren(child, DirtyFlags.AllLayout);
            Assert(!DirtyState.IsClean(root), "root dirty");
            Assert(!DirtyState.IsClean(child), "child dirty");
            DirtyState.ClearAll(root);
            DirtyState.ClearAll(child);
            Assert(DirtyState.IsClean(root), "root clean after clear");
            Assert(DirtyState.IsClean(child), "child clean after clear");
        }, ref passed, ref failed);

        Run("Cooperative scheduler runs tasks in priority order", () =>
        {
            var s = new CooperativeScheduler();
            var log = new List<string>();
            s.PostTask(() => log.Add("normal"), TaskPriority.Normal);
            s.PostTask(() => log.Add("immediate"), TaskPriority.Immediate);
            s.PostTask(() => log.Add("low"), TaskPriority.Low);
            s.Drain();
            Assert(log[0] == "immediate", $"first task was {log[0]}");
            Assert(log.Count == 3, "all tasks ran");
        }, ref passed, ref failed);

        Run("Long task observer fires on long work", () =>
        {
            var obs = new LongTaskObserver(new LongTaskObserver.Options { ThresholdMicros = 30_000 });
            int fired = 0;
            obs.OnLongTask += _ => fired++;
            obs.Observe("slow", TaskPriority.Normal, () => Thread.Sleep(60));
            obs.Observe("fast", TaskPriority.Normal, () => Thread.SpinWait(100));
            Assert(fired == 1, $"fired={fired}");
        }, ref passed, ref failed);

        Run("Resource cache LRU eviction", () =>
        {
            var cache = new ResourceCache();
            cache.SetCapacity(100);
            for (int i = 0; i < 10; i++)
                cache.Put("k" + i, new ResourceResponse { Body = new byte[20], ContentLength = 20 });
            Assert(cache.Evictions > 0, $"evictions={cache.Evictions}");
        }, ref passed, ref failed);

        Run("Tile manager tiles for rect", () =>
        {
            var tm = new TileManager(new TileManager.Config { TileSizePixels = 100 });
            var rect = new SKRect(0, 0, 250, 250);
            var keys = new List<TileKey>(tm.TilesForRect(rect, 1));
            Assert(keys.Count == 9, $"got {keys.Count} tiles");
        }, ref passed, ref failed);

        Run("Tile manager LRU eviction out of bounds", () =>
        {
            var tm = new TileManager();
            tm.GetOrCreate(new TileKey(0, 0, 1));
            tm.GetOrCreate(new TileKey(100, 100, 1));
            tm.EvictOutOfBounds(new SKRect(0, 0, 1000, 1000));
            Assert(tm.ActiveCount == 1, $"active={tm.ActiveCount}");
        }, ref passed, ref failed);

        Run("Tile rasterizer enqueue and complete", () =>
        {
            var tm = new TileManager();
            var ras = new TileRasterizer(tm);
            ras.Enqueue(new TileKey(0, 0, 1));
            var started = ras.TryStartNext();
            Assert(started is not null, "started");
            Assert(ras.InFlight == 1, "in flight");
            ras.Complete(started!.Value, null, "no raster");
            Assert(ras.InFlight == 0, "completed");
        }, ref passed, ref failed);

        Run("Memory pressure monitor classification", () =>
        {
            var m = new MemoryPressureMonitor();
            m.ReportUsage(100 * 1024 * 1024);
            Assert(m.Level == MemoryPressureLevel.Nominal, "nominal");
            m.ReportUsage(600 * 1024 * 1024);
            Assert(m.Level == MemoryPressureLevel.Moderate, "moderate");
            m.ReportUsage(3L * 1024 * 1024 * 1024);
            Assert(m.Level >= MemoryPressureLevel.High, "high");
        }, ref passed, ref failed);

        Run("Memory pressure responder receives callbacks", () =>
        {
            var m = new MemoryPressureMonitor();
            var r = new CountingResponder();
            m.Register(r);
            m.ReportUsage(3L * 1024 * 1024 * 1024);
            Assert(r.PressureEvents >= 1, $"pressure events={r.PressureEvents}");
            m.Unregister(r);
            m.ReportUsage(3L * 1024 * 1024 * 1024);
            int after = r.PressureEvents;
            m.ReportUsage(3L * 1024 * 1024 * 1024);
            Assert(r.PressureEvents == after, "unregistered receives nothing");
        }, ref passed, ref failed);

        Run("PerformanceMetrics records vitals", () =>
        {
            var m = new PerformanceMetrics();
            m.RecordFirstPaint();
            m.RecordFirstContentfulPaint();
            m.RecordLargestContentfulPaint();
            m.RecordTimeToInteractive();
            m.AddBlockingTime(10_000_000);
            m.AddLayoutShift(150);
            m.RecordFirstInputDelay(2_000_000);
            Assert(m.HasFirstPaint, "FP");
            Assert(m.HasFirstContentfulPaint, "FCP");
            Assert(m.HasLargestContentfulPaint, "LCP");
            Assert(m.HasTimeToInteractive, "TTI");
            Assert(m.TotalBlockingTimeNanos == 10_000_000, "TBT");
            Assert(m.CumulativeLayoutShiftMicros == 150, "CLS");
            Assert(m.FirstInputDelayNanos == 2_000_000, "FID");
        }, ref passed, ref failed);

        Run("TimingScope records to accumulator", () =>
        {
            var acc = new TimingAccumulator();
            using (TimingScope.Measure(acc))
            {
                long s = 0;
                for (int i = 0; i < 10000; i++) s += i;
                GC.KeepAlive(s);
            }
            Assert(acc.Count == 1, "count");
            Assert(acc.MeanNanos > 0, "mean");
        }, ref passed, ref failed);

        Run("Hub RunFrame advances scheduler", () =>
        {
            var hub = new PerformanceHub();
            hub.Initialize();
            int n = 0;
            hub.Scheduler.PostTask(() => n++);
            hub.Scheduler.PostTask(() => n++);
            hub.Scheduler.PostTask(() => n++);
            hub.RunFrame(CooperativeScheduler.FrameBudget.For60Fps);
            Assert(n == 3, $"ran {n}");
        }, ref passed, ref failed);

        Run("LongTaskObserver integration with Metrics TBT", () =>
        {
            var hub = new PerformanceHub();
            hub.Initialize();
            hub.LongTasks.Observe("test", TaskPriority.Normal, () => Thread.Sleep(60));
            Assert(hub.Registry.Metrics.TotalBlockingTimeNanos > 0, "TBT > 0");
        }, ref passed, ref failed);

        Run("Lazy load controller triggers visibility callback", () =>
        {
            var ll = new LazyLoadController();
            bool called = false;
            ll.Register("img.png", new SKRect(0, 0, 100, 100), 50, _ => called = true);
            int triggered = ll.UpdateViewport(new SKRect(0, 0, 800, 600));
            Assert(triggered == 1, "triggered");
            Assert(called, "callback fired");
            Assert(ll.Loaded == 1, "loaded counter");
        }, ref passed, ref failed);

        Run("IdleCallbackScheduler runs within budget", () =>
        {
            var idle = new IdleCallbackScheduler();
            int n = 0;
            for (int i = 0; i < 100; i++)
                idle.Register(_ => { n++; Thread.Sleep(1); }, TimeSpan.FromSeconds(5));
            idle.Run(20);
            Assert(n < 100, $"ran {n} of 100");
            Assert(idle.Fired > 0, "fired");
        }, ref passed, ref failed);

        Run("Pipeline timings accumulate samples", () =>
        {
            PipelineTimings.Layout.Reset();
            using (TimingScope.Measure(PipelineTimings.Layout)) { Thread.SpinWait(1000); }
            Assert(PipelineTimings.Layout.Count >= 1, "count");
            Assert(PipelineTimings.Layout.MeanNanos > 0, "mean");
        }, ref passed, ref failed);

        Run("Predictive scheduler enqueues ahead of viewport", () =>
        {
            var tm = new TileManager(new TileManager.Config { TileSizePixels = 256 });
            var ras = new TileRasterizer(tm);
            var ps = new PredictiveTileScheduler(tm, ras);
            ps.UpdateVelocity(0, 5000);  // fast downward scroll
            int scheduled = ps.SchedulePreRasters(new SKRect(0, 0, 1024, 768), 1);
            Assert(scheduled > 0, $"scheduled {scheduled}");
        }, ref passed, ref failed);

        Run("PerformanceApi.Snapshot produces valid JSON", () =>
        {
            var hub = new PerformanceHub();
            hub.Initialize();
            hub.Registry.Metrics.RecordFirstPaint();
            var snap = hub.Api.Snapshot();
            Assert(snap.Contains("firstPaintMs"), "FP in snapshot");
            Assert(snap.Contains("metrics"), "metrics");
            Assert(snap.Contains("longTasks"), "longTasks");
            Assert(snap.Contains("pipeline"), "pipeline");
            Assert(snap.Contains("memory"), "memory");
        }, ref passed, ref failed);

        Run("Compositor display list records commands", () =>
        {
            var dl = new CompositorDisplayList();
            dl.Add(CompositorCommand.Save());
            dl.Add(CompositorCommand.Translate(10, 20));
            dl.Add(CompositorCommand.ClipRect(new SKRect(0, 0, 100, 100)));
            dl.Add(CompositorCommand.DrawRect(new SKRect(0, 0, 50, 50), SKColors.Red));
            dl.Add(CompositorCommand.Restore());
            Assert(dl.CommandCount == 5, $"count={dl.CommandCount}");
        }, ref passed, ref failed);

        Console.WriteLine();
        Console.WriteLine($"=== Results: {passed} passed, {failed} failed ===");
        return failed == 0 ? 0 : 1;
    }

    sealed class CountingResponder : MemoryResponder
    {
        public int PressureEvents;
        public int ReleaseEvents;
        public override string Name => "counting";
        public override void OnMemoryPressure(MemoryPressureLevel l) => PressureEvents++;
        public override void OnMemoryRelease(MemoryPressureLevel l) => ReleaseEvents++;
    }

    static void Run(string name, Action body, ref int passed, ref int failed)
    {
        try
        {
            body();
            Console.WriteLine($"  PASS  {name}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  {name}: {ex.Message}");
            failed++;
        }
    }

    static void Assert(bool cond, string msg)
    {
        if (!cond) throw new Exception("Assertion failed: " + msg);
    }
}
