using System.Text.Json;
using ConvertXPortable.Models;
using System.IO;

namespace ConvertXPortable.Services;

public sealed class ConfigurationService(PathResolver pathResolver)
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public ToolCatalog LoadToolCatalog()
    {
        if (!File.Exists(pathResolver.ToolsJsonPath))
        {
            return new ToolCatalog();
        }

        var json = File.ReadAllText(pathResolver.ToolsJsonPath);
        return JsonSerializer.Deserialize<ToolCatalog>(json, _jsonOptions) ?? new ToolCatalog();
    }

    public ConversionManifest LoadConversionManifest()
    {
        if (!File.Exists(pathResolver.ConversionsJsonPath))
        {
            return new ConversionManifest();
        }

        var json = File.ReadAllText(pathResolver.ConversionsJsonPath);
        return JsonSerializer.Deserialize<ConversionManifest>(json, _jsonOptions) ?? new ConversionManifest();
    }

    public IReadOnlyList<ToolStatus> GetToolStatuses(ToolCatalog catalog)
    {
        return catalog.Tools
            .Select(tool =>
            {
                var executable = !string.IsNullOrWhiteSpace(tool.MainExecutable)
                    ? tool.MainExecutable
                    : tool.Executables.FirstOrDefault() ?? "";
                var fullPath = ResolveExistingToolPath(tool, executable);

                return new ToolStatus
                {
                    Name = tool.Name,
                    Category = tool.Category,
                    ExecutablePath = fullPath,
                    IsAvailable = File.Exists(fullPath)
                };
            })
            .OrderBy(status => status.Category)
            .ThenBy(status => status.Name)
            .ToList();
    }

    private string ResolveExistingToolPath(ToolDefinition tool, string executable)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(executable))
        {
            candidates.Add(executable);
        }

        if (!string.IsNullOrWhiteSpace(tool.MainExecutable))
        {
            candidates.Add(tool.MainExecutable);
        }

        candidates.AddRange(tool.Executables);

        if (!string.IsNullOrWhiteSpace(tool.Path))
        {
            var toolPath = tool.Path.TrimEnd('/', '\\');
            candidates.AddRange(candidates.ToArray().Select(candidate => Path.Combine(toolPath, candidate)));
        }

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = pathResolver.ResolveToolPath(candidate);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return pathResolver.ResolveToolPath(executable);
    }
}
