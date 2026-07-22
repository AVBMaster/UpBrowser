using System.Text.Json.Serialization;

namespace UpBrowser.Core.JavaScript;

[JsonSerializable(typeof(FetchOptions))]
[JsonSerializable(typeof(FetchResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class UpBrowserJsonContext : JsonSerializerContext
{
}
