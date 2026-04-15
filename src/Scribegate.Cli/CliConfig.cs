using System.Text.Json;

namespace Scribegate.Cli;

public class CliConfig
{
    public string? Host { get; set; }
    public string? Token { get; set; }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "scribegate");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static CliConfig Load()
    {
        // Environment variables take precedence
        var envHost = Environment.GetEnvironmentVariable("SCRIBEGATE_HOST");
        var envToken = Environment.GetEnvironmentVariable("SCRIBEGATE_TOKEN");

        CliConfig? config = null;
        if (File.Exists(ConfigFile))
        {
            try
            {
                var json = File.ReadAllText(ConfigFile);
                config = JsonSerializer.Deserialize<CliConfig>(json, JsonOptions);
            }
            catch { /* ignore corrupt config */ }
        }

        config ??= new CliConfig();
        if (envHost is not null) config.Host = envHost;
        if (envToken is not null) config.Token = envToken;

        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigFile, json);
    }
}
