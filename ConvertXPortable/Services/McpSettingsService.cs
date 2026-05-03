using System.IO;
using System.Text.Json;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class McpSettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConvertXPortable",
        "mcp-settings.json");

    public McpSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new McpSettings { Token = GenerateToken() };
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<McpSettings>(json, _jsonOptions) ?? new McpSettings();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                settings.Token = GenerateToken();
            }

            if (settings.Port <= 0)
            {
                settings.Port = 8765;
            }

            return settings;
        }
        catch
        {
            return new McpSettings { Token = GenerateToken() };
        }
    }

    public void Save(McpSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public static string GenerateToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
