using ConvertXPortable.Models;
using System.IO;

namespace ConvertXPortable.Services;

public sealed class ConversionRouter(IEnumerable<ConversionRule> rules, IEnumerable<ToolStatus> tools)
{
    private readonly List<ConversionRule> _rules = rules.ToList();
    private readonly Dictionary<string, ToolStatus> _tools = tools.ToDictionary(t => NormalizeName(t.Name), StringComparer.OrdinalIgnoreCase);

    public static string NormalizeExtension(string extension)
    {
        var value = extension.Trim().TrimStart('.').ToLowerInvariant();
        return value switch
        {
            "jpeg" => "jpg",
            "tif" => "tiff",
            "htm" => "html",
            "yml" => "yaml",
            "m4v" => "mp4",
            "m4a" => "aac",
            _ => value
        };
    }

    public IReadOnlyList<string> GetOutputFormats(string inputPath)
    {
        var inputExtension = NormalizeExtension(Path.GetExtension(inputPath));
        return _rules
            .Where(rule => rule.From.Select(NormalizeExtension).Contains(inputExtension))
            .SelectMany(rule => rule.To)
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(format => format)
            .ToList();
    }

    public IReadOnlyList<ConverterOption> GetConverterOptions(string inputPath, string outputFormat)
    {
        var inputExtension = NormalizeExtension(Path.GetExtension(inputPath));
        var normalizedOutput = NormalizeExtension(outputFormat);

        return _rules
            .Where(rule =>
                rule.From.Select(NormalizeExtension).Contains(inputExtension) &&
                rule.To.Select(NormalizeExtension).Contains(normalizedOutput))
            .Select(rule =>
            {
                _tools.TryGetValue(NormalizeName(rule.Converter), out var tool);
                return tool is null
                    ? null
                    : new ConverterOption { Rule = rule, Tool = tool };
            })
            .OfType<ConverterOption>()
            .OrderByDescending(option => option.Tool.IsAvailable)
            .ThenBy(option => option.Rule.Priority)
            .ThenBy(option => option.Rule.Converter)
            .ToList();
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant();
    }
}
