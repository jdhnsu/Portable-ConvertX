using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class AiConversionPlanner(
    PathResolver pathResolver,
    ConversionRouter router,
    ConversionCommandPreviewBuilder previewBuilder,
    AiChatService chatService)
{
    private static readonly HashSet<string> VideoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "avi", "mov", "webm", "m4v"
    };

    public async Task<AiPlanResult> PlanAsync(
        AiSettings settings,
        string inputPath,
        string outputDirectory,
        string outputFormat,
        string userRequest,
        CancellationToken cancellationToken)
    {
        ValidateInput(inputPath, outputDirectory, outputFormat);

        var normalizedFormat = ConversionRouter.NormalizeExtension(outputFormat);
        var options = router.GetConverterOptions(inputPath, normalizedFormat);
        if (options.Count == 0)
        {
            throw new InvalidOperationException("当前输入格式和目标格式没有匹配的转换规则。");
        }

        var mediaInfo = await TryReadMediaInfoAsync(options, inputPath, cancellationToken);
        var nvidiaInfo = await DetectNvidiaAsync(options, cancellationToken);
        var messages = BuildPlannerMessages(inputPath, outputDirectory, normalizedFormat, userRequest, options, mediaInfo, nvidiaInfo);
        var raw = await chatService.SendAsync(settings, messages, cancellationToken);
        var recommendation = TryParseRecommendation(raw);
        var option = PickOption(options, recommendation.Converter);
        var advancedArguments = recommendation.AdvancedArguments.Trim();

        if (ShouldPreferNvidia(option, normalizedFormat, nvidiaInfo) &&
            !advancedArguments.Contains("nvenc", StringComparison.OrdinalIgnoreCase) &&
            !advancedArguments.Contains("-hwaccel cuda", StringComparison.OrdinalIgnoreCase))
        {
            advancedArguments = string.IsNullOrWhiteSpace(advancedArguments)
                ? "-hwaccel cuda -c:v h264_nvenc"
                : $"{advancedArguments} -hwaccel cuda -c:v h264_nvenc";
        }

        var outputPath = BuildOutputPath(inputPath, outputDirectory, normalizedFormat);
        var preview = previewBuilder.Build(option, inputPath, outputPath, outputDirectory, normalizedFormat, advancedArguments);
        if (!string.IsNullOrWhiteSpace(recommendation.PowerShellCommand))
        {
            preview = new CommandPreview
            {
                Converter = preview.Converter,
                ExecutablePath = preview.ExecutablePath,
                Arguments = preview.Arguments,
                OutputPath = preview.OutputPath,
                AdvancedArguments = preview.AdvancedArguments,
                DisplayCommand = recommendation.PowerShellCommand.Trim()
            };
        }

        return new AiPlanResult
        {
            Option = option,
            Preview = preview,
            Explanation = string.IsNullOrWhiteSpace(recommendation.Explanation)
                ? raw
                : recommendation.Explanation,
            Risk = recommendation.Risk,
            IsReadOnlyCommand = recommendation.IsReadOnlyCommand,
            RawResponse = raw
        };
    }

    public async Task<string> AnalyzeFailureAsync(
        AiSettings settings,
        string command,
        string logText,
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new AiChatMessage
            {
                Role = "system",
                Content = "你是 ConvertXPortable 的转换错误分析助手。请用中文简洁说明可能原因和下一步调整，不要编造不存在的工具。"
            },
            new AiChatMessage
            {
                Role = "user",
                Content = $"命令:\n{command}\n\n日志:\n{logText}"
            }
        };

        return await chatService.SendAsync(settings, messages, cancellationToken);
    }

    private static IReadOnlyList<AiChatMessage> BuildPlannerMessages(
        string inputPath,
        string outputDirectory,
        string outputFormat,
        string userRequest,
        IReadOnlyList<ConverterOption> options,
        string mediaInfo,
        string nvidiaInfo)
    {
        var candidates = string.Join("\n", options.Select(option =>
            $"- converter={option.Rule.Converter}; available={option.Tool.IsAvailable}; priority={option.Rule.Priority}; executable={option.Rule.Executable}; template={option.Rule.ArgumentTemplate}; description={option.Rule.Description}; outputArgs={JsonSerializer.Serialize(option.Rule.OutputArgumentTemplates)}"));

        var system = """
你是 ConvertXPortable 的转换规划 agent。只能从候选转换器中选择一个，不要发明工具或路径。
返回严格 JSON，不要 Markdown，不要代码块，格式:
{
 "converter": "候选转换器名称",
  "advancedArguments": "只填写额外参数；不要包含输入路径、输出路径或可执行文件",
  "powerShellCommand": "一条可直接在 Windows PowerShell 执行的完整命令，必须包含可执行文件、输入路径、输出路径和参数",
  "isReadOnlyCommand": false,
  "explanation": "推荐理由和参数说明",
  "risk": "风险或注意事项；没有则为空字符串"
}
如果用户只是询问查看文件、探测媒体、列目录、查看工具版本等读取类需求，可以返回只读 PowerShell 命令并把 isReadOnlyCommand 设为 true。
如果是转换、写文件、删除、移动、下载、安装或修改系统状态，isReadOnlyCommand 必须是 false。
""";

        var user = $"""
输入文件: {inputPath}
输出目录: {outputDirectory}
目标格式: {outputFormat}
用户需求: {userRequest}

候选转换器:
{candidates}

媒体探测:
{mediaInfo}

硬件探测:
{nvidiaInfo}
""";

        return [new AiChatMessage { Role = "system", Content = system }, new AiChatMessage { Role = "user", Content = user }];
    }

    private static AiRecommendation TryParseRecommendation(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        try
        {
            return JsonSerializer.Deserialize<AiRecommendation>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AiRecommendation { Explanation = raw };
        }
        catch
        {
            return new AiRecommendation { Explanation = raw };
        }
    }

    private static ConverterOption PickOption(IReadOnlyList<ConverterOption> options, string requestedConverter)
    {
        var exact = options.FirstOrDefault(option =>
            string.Equals(option.Rule.Converter, requestedConverter, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return options.FirstOrDefault(option => option.Tool.IsAvailable) ?? options[0];
    }

    private static void ValidateInput(string inputPath, string outputDirectory, string outputFormat)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("输入文件不存在。", inputPath);
        }

        if (string.IsNullOrWhiteSpace(outputFormat))
        {
            throw new InvalidOperationException("请先选择目标输出格式。");
        }

        Directory.CreateDirectory(outputDirectory);

        using (File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
        }

        var probePath = Path.Combine(outputDirectory, ".convertx-write-test.tmp");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);
    }

    private async Task<string> TryReadMediaInfoAsync(
        IReadOnlyList<ConverterOption> options,
        string inputPath,
        CancellationToken cancellationToken)
    {
        if (!options.Any(option => option.Rule.Converter.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return "非 ffmpeg 转换，未执行媒体探测。";
        }

        var ffprobe = ResolveSiblingTool(options, "ffprobe.exe");
        if (ffprobe is null)
        {
            return "未找到 ffprobe。";
        }

        return await RunProbeAsync(ffprobe, ["-v", "error", "-show_format", "-show_streams", inputPath], cancellationToken);
    }

    private async Task<string> DetectNvidiaAsync(IReadOnlyList<ConverterOption> options, CancellationToken cancellationToken)
    {
        if (!options.Any(option => option.Rule.Converter.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return "非 ffmpeg 转换，未执行 NVIDIA 探测。";
        }

        var nvidia = await RunProbeAsync("nvidia-smi.exe", ["--query-gpu=name", "--format=csv,noheader"], cancellationToken);
        var ffmpeg = ResolveSiblingTool(options, "ffmpeg.exe");
        var encoders = ffmpeg is null
            ? "未找到 ffmpeg。"
            : await RunProbeAsync(ffmpeg, ["-hide_banner", "-encoders"], cancellationToken);

        var hasNvidia = !nvidia.StartsWith("探测失败", StringComparison.OrdinalIgnoreCase);
        var hasNvenc = encoders.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
        return $"NVIDIA GPU: {(hasNvidia ? nvidia.Trim() : "未检测到")}; ffmpeg NVENC: {(hasNvenc ? "支持" : "未检测到")}";
    }

    private string? ResolveSiblingTool(IReadOnlyList<ConverterOption> options, string fileName)
    {
        foreach (var option in options)
        {
            var executable = pathResolver.ResolveToolPath(option.Rule.Executable);
            var directory = Path.GetDirectoryName(executable);
            if (directory is null)
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string> RunProbeAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "探测失败: 无法启动进程。";
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            var text = string.IsNullOrWhiteSpace(output) ? error : output;
            return string.IsNullOrWhiteSpace(text) ? $"退出码 {process.ExitCode}" : Truncate(text, 4000);
        }
        catch (Exception ex)
        {
            return "探测失败: " + ex.Message;
        }
    }

    private static bool ShouldPreferNvidia(ConverterOption option, string outputFormat, string nvidiaInfo)
    {
        return option.Rule.Converter.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) &&
            VideoFormats.Contains(outputFormat) &&
            nvidiaInfo.Contains("ffmpeg NVENC: 支持", StringComparison.OrdinalIgnoreCase) &&
            !nvidiaInfo.Contains("NVIDIA GPU: 未检测到", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOutputPath(string inputPath, string outputDirectory, string outputFormat)
    {
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = outputFormat.TrimStart('.');
        if (extension.Contains('.', StringComparison.Ordinal))
        {
            extension = extension.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
        }

        return Path.Combine(outputDirectory, $"{fileName}.{extension}");
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "\n...";
    }
}
