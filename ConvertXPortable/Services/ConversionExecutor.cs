using System.Diagnostics;
using System.IO;
using System.Text;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class ConversionExecutor(PathResolver pathResolver)
{
    private Process? _currentProcess;

    public async Task<int> ExecuteAsync(
        ConverterOption option,
        string inputPath,
        string outputPath,
        string outputDirectory,
        string outputFormat,
        string advancedArguments,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var executable = pathResolver.ResolveToolPath(option.Rule.Executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("转换器可执行文件不存在。", executable);
        }

        Directory.CreateDirectory(outputDirectory);

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

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? pathResolver.TestToolsRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = option.Rule.PipeInputToStdin,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        log("Executable: " + executable);
        log("Arguments: " + string.Join(" ", arguments.Select(QuoteForDisplay)));
        log("");

        var stdoutCapture = new StringBuilder();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _currentProcess = process;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                if (option.Rule.WriteStdoutToOutput)
                {
                    stdoutCapture.AppendLine(e.Data);
                }

                log(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动转换器进程。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (option.Rule.PipeInputToStdin)
        {
            await using var inputStream = File.OpenRead(inputPath);
            await inputStream.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
            process.StandardInput.Close();
        }

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may already have exited between HasExited and Kill.
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        _currentProcess = null;
        if (process.ExitCode == 0 && option.Rule.WriteStdoutToOutput)
        {
            await File.WriteAllTextAsync(outputPath, stdoutCapture.ToString(), cancellationToken);
            log("");
            log("Stdout 已写入: " + outputPath);
        }

        return process.ExitCode;
    }

    public void Cancel()
    {
        var process = _currentProcess;
        if (process is null || process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
    }

    private static string QuoteForDisplay(string argument)
    {
        return argument.Contains(' ') ? "\"" + argument + "\"" : argument;
    }
}
