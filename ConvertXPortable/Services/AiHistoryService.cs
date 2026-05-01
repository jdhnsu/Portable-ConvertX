using System.IO;
using System.Text.Json;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class AiHistoryService
{
    private const int MaxHistoryItems = 60;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string HistoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ConvertXPortable",
        "ai-history.json");

    public AiHistoryDocument Load()
    {
        if (!File.Exists(HistoryPath))
        {
            return new AiHistoryDocument();
        }

        try
        {
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<AiHistoryDocument>(json, _jsonOptions) ?? new AiHistoryDocument();
        }
        catch
        {
            return new AiHistoryDocument();
        }
    }

    public void Save(AiHistoryDocument document)
    {
        var directory = Path.GetDirectoryName(HistoryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        while (document.Items.Count > MaxHistoryItems)
        {
            document.Items.RemoveAt(document.Items.Count - 1);
        }

        var json = JsonSerializer.Serialize(document, _jsonOptions);
        File.WriteAllText(HistoryPath, json);
    }
}
