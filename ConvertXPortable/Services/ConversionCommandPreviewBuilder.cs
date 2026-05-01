using System.IO;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class ConversionCommandPreviewBuilder(PathResolver pathResolver)
{
    public CommandPreview Build(
        ConverterOption option,
        string inputPath,
        string outputPath,
        string outputDirectory,
        string outputFormat,
        string advancedArguments)
    {
        var executable = pathResolver.ResolveToolPath(option.Rule.Executable);
        var arguments = BuildArguments(option, inputPath, outputPath, outputDirectory, outputFormat, advancedArguments);

        return new CommandPreview
        {
            Converter = option.Rule.Converter,
            ExecutablePath = executable,
            Arguments = arguments,
            OutputPath = outputPath,
            AdvancedArguments = advancedArguments,
            DisplayCommand = QuoteForPowerShell(executable) + " " + string.Join(" ", arguments.Select(QuoteForPowerShell))
        };
    }

    public static IReadOnlyList<string> BuildArguments(
        ConverterOption option,
        string inputPath,
        string outputPath,
        string outputDirectory,
        string outputFormat,
        string advancedArguments)
    {
        var arguments = ArgumentTemplate.BuildArguments(
            option.Rule.ArgumentTemplate,
            inputPath,
            outputPath,
            outputDirectory,
            outputFormat,
            advancedArguments).ToList();
        var extraTemplate = option.Rule.OutputArgumentTemplates
            .FirstOrDefault(pair => string.Equals(pair.Key, outputFormat, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.IsNullOrWhiteSpace(extraTemplate))
        {
            var outputIndex = arguments.FindIndex(argument => string.Equals(argument, outputPath, StringComparison.OrdinalIgnoreCase));
            var extraArguments = ArgumentTemplate.BuildArguments(
                extraTemplate,
                inputPath,
                outputPath,
                outputDirectory,
                outputFormat,
                "");
            if (outputIndex >= 0)
            {
                arguments.InsertRange(outputIndex, extraArguments);
            }
            else
            {
                arguments.AddRange(extraArguments);
            }
        }

        return arguments;
    }

    private static string QuoteForPowerShell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
