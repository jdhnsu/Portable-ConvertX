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
        var inputExtension = ConversionRouter.NormalizeExtension(Path.GetExtension(inputPath));
        string actualInputPath = inputPath;
        string actualOutputPath = outputPath;
        string? intermediateDocxPath = null;

        if (inputExtension == "pdf" && outputFormat.Equals("doc", StringComparison.OrdinalIgnoreCase))
        {
            intermediateDocxPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".docx");
            actualInputPath = inputPath;
            actualOutputPath = intermediateDocxPath;
        }

        var executable = pathResolver.ResolveToolPath(option.Rule.Executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("转换器可执行文件不存在。", executable);
        }

        Directory.CreateDirectory(outputDirectory);

        var arguments = ArgumentTemplate.BuildArguments(
            option.Rule.ArgumentTemplate,
            actualInputPath,
            actualOutputPath,
            outputDirectory,
            outputFormat,
            advancedArguments).ToList();
        var extraTemplate = option.Rule.OutputArgumentTemplates
            .FirstOrDefault(pair => string.Equals(pair.Key, outputFormat, StringComparison.OrdinalIgnoreCase))
            .Value;
        if (!string.IsNullOrWhiteSpace(extraTemplate))
        {
            var outputIndex = arguments.FindIndex(argument => string.Equals(argument, actualOutputPath, StringComparison.OrdinalIgnoreCase));
            var extraArguments = ArgumentTemplate.BuildArguments(
                extraTemplate,
                actualInputPath,
                actualOutputPath,
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
            await using var inputStream = File.OpenRead(actualInputPath);
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
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        _currentProcess = null;
        if (process.ExitCode == 0 && option.Rule.WriteStdoutToOutput)
        {
            await File.WriteAllTextAsync(actualOutputPath, stdoutCapture.ToString(), cancellationToken);
            log("");
            log("Stdout 已写入: " + actualOutputPath);
        }

        if (intermediateDocxPath is not null && process.ExitCode == 0)
        {
            log("");
            log("=== 步骤 2: DOCX → DOC ===");
            log("");

            var step2Arguments = ArgumentTemplate.BuildArguments(
                option.Rule.ArgumentTemplate,
                intermediateDocxPath,
                outputPath,
                outputDirectory,
                "doc",
                advancedArguments).ToList();

            var startInfo2 = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? pathResolver.TestToolsRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };

            foreach (var argument in step2Arguments)
            {
                startInfo2.ArgumentList.Add(argument);
            }

            log("Executable: " + executable);
            log("Arguments: " + string.Join(" ", step2Arguments.Select(QuoteForDisplay)));
            log("");

            using var process2 = new Process { StartInfo = startInfo2, EnableRaisingEvents = true };
            _currentProcess = process2;

            process2.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) log(e.Data);
            };
            process2.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) log(e.Data);
            };

            if (!process2.Start())
            {
                throw new InvalidOperationException("无法启动转换器进程。");
            }

            process2.BeginOutputReadLine();
            process2.BeginErrorReadLine();

            await using var registration2 = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process2.HasExited)
                    {
                        process2.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            });

            await process2.WaitForExitAsync(CancellationToken.None);
            _currentProcess = null;

            try
            {
                if (File.Exists(intermediateDocxPath))
                {
                    File.Delete(intermediateDocxPath);
                    log("");
                    log("已删除中间文件: " + intermediateDocxPath);
                }
            }
            catch
            {
            }

            return process2.ExitCode;
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
