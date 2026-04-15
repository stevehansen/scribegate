using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Scribegate.Web.Api;

public static class FrontmatterService
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static (JsonElement? metadata, string body) Parse(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.StartsWith("---"))
            return (null, content);

        var endIndex = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
            return (null, content);

        var yamlBlock = content[3..endIndex].Trim();
        var body = content[(endIndex + 4)..].TrimStart('\r', '\n');

        try
        {
            var yamlObj = YamlDeserializer.Deserialize<Dictionary<string, object?>>(yamlBlock);
            if (yamlObj is null)
                return (null, body);

            var json = JsonSerializer.Serialize(yamlObj);
            var element = JsonDocument.Parse(json).RootElement;
            return (element, body);
        }
        catch
        {
            // If YAML parsing fails, treat entire content as body
            return (null, content);
        }
    }

    public static string? ToJson(string content)
    {
        var (metadata, _) = Parse(content);
        return metadata.HasValue ? metadata.Value.GetRawText() : null;
    }
}
