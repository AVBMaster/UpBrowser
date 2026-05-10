namespace UpBrowser;

class Program
{
    static async Task Main(string[] args)
    {
        var app = new BrowserApp(1024, 768);
        await app.RunAsync();
        app.Dispose();
    }
}
