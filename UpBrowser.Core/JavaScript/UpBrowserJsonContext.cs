using System.Text.Json.Serialization;

namespace UpBrowser.Core.JavaScript;

[JsonSerializable(typeof(FetchOptions))]
[JsonSerializable(typeof(FetchResult))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(string[]))]
internal partial class UpBrowserJsonContext : JsonSerializerContext
{
}
