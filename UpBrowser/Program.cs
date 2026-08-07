using System.Runtime.InteropServices;

namespace UpBrowser;

class Program
{
    static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            Log($"[Main] UnhandledException: {ex.GetType().FullName}: {ex.Message}");
            Log(ex.StackTrace ?? "[Main] No stack trace");
        };

        Log("[Main] Starting UpBrowser");
        Log($"[Main] OS: {Environment.OSVersion.VersionString}");
        Log($"[Main] Platform: {RuntimeInformation.OSDescription}");
        Log($"[Main] Arch: {RuntimeInformation.OSArchitecture}");

        try
        {
            Log("[Main] Creating BrowserApp...");
            var app = new BrowserApp(1024, 768);
            Log("[Main] BrowserApp created successfully");

            Log("[Main] Starting RunAsync...");
            await app.RunAsync();
            Log("[Main] RunAsync completed");
        }
        catch (Exception ex)
        {
            Log($"[Main] CRASH in managed code: {ex.GetType().FullName}: {ex.Message}");
            Log(ex.StackTrace ?? "[Main] No stack trace");
        }
        finally
        {
            Log("[Main] Cleanup complete");
        }
    }

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        try
        {
            File.AppendAllText("upbrowser_startup.log", $"{msg}\n");
        }
        catch { }
    }
}
