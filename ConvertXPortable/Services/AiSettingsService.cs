using System.IO;
using System.Text.Json;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class AiSettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConvertXPortable",
        "ai-settings.json");

    public AiSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AiSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AiSettings>(json, _jsonOptions) ?? new AiSettings();
        }
        catch
        {
            return new AiSettings();
        }
    }

    public void Save(AiSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
