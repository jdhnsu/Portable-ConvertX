using System.IO;
using System.Text;

namespace ConvertXPortable.Services;

public static class ArgumentTemplate
{
    public static IReadOnlyList<string> BuildArguments(
        string template,
        string inputPath,
        string outputPath,
        string outputDirectory,
        string outputFormat,
        string advancedArguments)
    {
        var inputFormat = ConversionRouter.NormalizeExtension(Path.GetExtension(inputPath));
        var replaced = template
            .Replace("{input}", inputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{output}", outputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{outputDir}", outputDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{inputFormat}", inputFormat, StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", outputFormat, StringComparison.OrdinalIgnoreCase);

        var arguments = SplitCommandLine(replaced).ToList();
        if (!string.IsNullOrWhiteSpace(advancedArguments))
        {
            arguments.AddRange(SplitCommandLine(advancedArguments));
        }

        return arguments;
    }

    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                AddCurrent();
                continue;
            }

            current.Append(ch);
        }

        AddCurrent();
        return args;

        void AddCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            args.Add(current.ToString());
            current.Clear();
        }
    }
}
