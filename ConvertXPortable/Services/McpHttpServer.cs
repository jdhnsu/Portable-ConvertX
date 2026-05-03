using System.Net;
using System.Text;
using System.Text.Json;
using System.IO;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class McpHttpServer(
    PathResolver pathResolver,
    ConversionRouter router,
    IReadOnlyList<ToolStatus> toolStatuses)
{
    private readonly Dictionary<string, McpJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _singleJobLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private HttpListener? _listener;
    private CancellationTokenSource? _serverCts;
    private McpSettings _settings = new();

    public bool IsRunning => _listener?.IsListening == true;
    public string BaseUrl => $"http://127.0.0.1:{_settings.Port}/";
    public string DocsUrl => $"{BaseUrl}docs";

    public Task StartAsync(McpSettings settings)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _settings = settings;
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _serverCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_serverCts.Token));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _serverCts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }
        finally
        {
            _listener = null;
            _serverCts?.Dispose();
            _serverCts = null;
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            if (request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(context, new
                {
                    status = "ok",
                    version = "v1",
                    workspaceRoot = pathResolver.WorkspaceRoot,
                    tools = new
                    {
                        available = toolStatuses.Count(status => status.IsAvailable),
                        total = toolStatuses.Count
                    }
                });
                return;
            }

            if (request.HttpMethod == "GET" && (path == "/" || path == "/docs"))
            {
                await WriteHtmlAsync(context, BuildDocsHtml());
                return;
            }

            if (path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAuthorized(request))
                {
                    await WriteJsonAsync(context, new { error = "unauthorized" }, HttpStatusCode.Unauthorized);
                    return;
                }

                await HandleMcpAsync(context);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !IsAuthorized(request))
            {
                await WriteJsonAsync(context, new { error = "unauthorized" }, HttpStatusCode.Unauthorized);
                return;
            }

            if (request.HttpMethod == "GET" && path == "/api/formats")
            {
                var inputPath = request.QueryString["inputPath"] ?? "";
                ValidateInputPath(inputPath);
                await WriteJsonAsync(context, new { formats = router.GetOutputFormats(inputPath) });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/api/converters")
            {
                var inputPath = request.QueryString["inputPath"] ?? "";
                var outputFormat = request.QueryString["outputFormat"] ?? "";
                ValidateInputPath(inputPath);
                var converters = router.GetConverterOptions(inputPath, outputFormat)
                    .Select(option => new
                    {
                        converter = option.Rule.Converter,
                        description = option.Rule.Description,
                        category = option.Rule.Category,
                        priority = option.Rule.Priority,
                        isAvailable = option.Tool.IsAvailable,
                        executablePath = option.Tool.ExecutablePath
                    });
                await WriteJsonAsync(context, new { converters });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/convert")
            {
                var convertRequest = await ReadJsonAsync<McpConvertRequest>(request);
                var job = CreateJob(convertRequest);
                _jobs[job.JobId] = job;
                _ = Task.Run(() => RunJobAsync(job, convertRequest));
                await WriteJsonAsync(context, new { jobId = job.JobId, status = job.Status, outputPath = job.OutputPath }, HttpStatusCode.Accepted);
                return;
            }

            if (path.StartsWith("/api/jobs/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleJobRouteAsync(context, path);
                return;
            }

            await WriteJsonAsync(context, new { error = "not found" }, HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context, new { error = ex.Message }, HttpStatusCode.BadRequest);
        }
    }

    private async Task HandleMcpAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "POST")
        {
            await WriteJsonRpcErrorAsync(context, null, -32600, "MCP endpoint expects HTTP POST.");
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = GetJsonRpcId(root);

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            await WriteJsonRpcErrorAsync(context, id, -32600, "Invalid JSON-RPC request.");
            return;
        }

        var method = methodElement.GetString() ?? "";
        if (id is null && method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteBytesAsync(context, [], "application/json; charset=utf-8", HttpStatusCode.Accepted);
            return;
        }

        try
        {
            var result = method switch
            {
                "initialize" => BuildMcpInitializeResult(),
                "ping" => new { },
                "tools/list" => new { tools = BuildMcpTools() },
                "tools/call" => await HandleMcpToolCallAsync(root),
                _ => throw new McpJsonRpcException(-32601, $"Method not found: {method}")
            };
            await WriteJsonRpcResultAsync(context, id, result);
        }
        catch (McpJsonRpcException ex)
        {
            await WriteJsonRpcErrorAsync(context, id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            await WriteJsonRpcErrorAsync(context, id, -32000, ex.Message);
        }
    }

    private static object BuildMcpInitializeResult()
    {
        return new
        {
            protocolVersion = "2025-03-26",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "convertx-portable",
                version = "1.0.0"
            }
        };
    }

    private object[] BuildMcpTools()
    {
        return
        [
            BuildTool("convertx_list_formats", "List output formats supported for a local input file path.",
                new Dictionary<string, object> { ["inputPath"] = new { type = "string", description = "Local input file path." } },
                ["inputPath"]),
            BuildTool("convertx_list_converters", "List converters available for a local input file and output format.",
                new Dictionary<string, object>
                {
                    ["inputPath"] = new { type = "string", description = "Local input file path." },
                    ["outputFormat"] = new { type = "string", description = "Target output format, for example jpg, mp4, pdf." }
                },
                ["inputPath", "outputFormat"]),
            BuildTool("convertx_convert", "Create an asynchronous ConvertX conversion job. This uses ConvertX rules and never runs arbitrary shell commands.",
                new Dictionary<string, object>
                {
                    ["inputPath"] = new { type = "string", description = "Local input file path." },
                    ["outputDirectory"] = new { type = "string", description = "Local output directory." },
                    ["outputFormat"] = new { type = "string", description = "Target output format." },
                    ["converter"] = new { type = "string", description = "Optional converter name." },
                    ["advancedArguments"] = new { type = "string", description = "Optional extra converter arguments." }
                },
                ["inputPath", "outputDirectory", "outputFormat"]),
            BuildTool("convertx_get_job", "Get ConvertX conversion job status.",
                new Dictionary<string, object> { ["jobId"] = new { type = "string", description = "Job id returned by convertx_convert." } },
                ["jobId"]),
            BuildTool("convertx_get_log", "Get full log text for a ConvertX conversion job.",
                new Dictionary<string, object> { ["jobId"] = new { type = "string", description = "Job id returned by convertx_convert." } },
                ["jobId"]),
            BuildTool("convertx_cancel_job", "Cancel a queued or running ConvertX conversion job.",
                new Dictionary<string, object> { ["jobId"] = new { type = "string", description = "Job id returned by convertx_convert." } },
                ["jobId"])
        ];
    }

    private static object BuildTool(string name, string description, Dictionary<string, object> properties, string[] required)
    {
        return new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties,
                required
            }
        };
    }

    private async Task<object> HandleMcpToolCallAsync(JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new McpJsonRpcException(-32602, "tools/call requires params.name.");
        }

        var name = nameElement.GetString() ?? "";
        using var emptyArgs = JsonDocument.Parse("{}");
        var arguments = parameters.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
            ? args
            : emptyArgs.RootElement;

        try
        {
            var result = name switch
            {
                "convertx_list_formats" => BuildFormatsResult(GetRequiredString(arguments, "inputPath")),
                "convertx_list_converters" => BuildConvertersResult(GetRequiredString(arguments, "inputPath"), GetRequiredString(arguments, "outputFormat")),
                "convertx_convert" => await CreateMcpConvertJobAsync(arguments),
                "convertx_get_job" => ToJobDto(GetJob(GetRequiredString(arguments, "jobId"))),
                "convertx_get_log" => new { jobId = GetRequiredString(arguments, "jobId"), log = GetJob(GetRequiredString(arguments, "jobId")).GetLog() },
                "convertx_cancel_job" => CancelJobForTool(GetRequiredString(arguments, "jobId")),
                _ => throw new McpJsonRpcException(-32602, $"Unknown tool: {name}")
            };
            return BuildToolTextResult(JsonSerializer.Serialize(result, _jsonOptions), isError: false);
        }
        catch (McpJsonRpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildToolTextResult(ex.Message, isError: true);
        }
    }

    private object BuildFormatsResult(string inputPath)
    {
        ValidateInputPath(inputPath);
        return new { formats = router.GetOutputFormats(inputPath) };
    }

    private object BuildConvertersResult(string inputPath, string outputFormat)
    {
        ValidateInputPath(inputPath);
        return new
        {
            converters = router.GetConverterOptions(inputPath, outputFormat)
                .Select(option => new
                {
                    converter = option.Rule.Converter,
                    description = option.Rule.Description,
                    category = option.Rule.Category,
                    priority = option.Rule.Priority,
                    isAvailable = option.Tool.IsAvailable,
                    executablePath = option.Tool.ExecutablePath
                })
        };
    }

    private Task<object> CreateMcpConvertJobAsync(JsonElement arguments)
    {
        var request = new McpConvertRequest
        {
            InputPath = GetRequiredString(arguments, "inputPath"),
            OutputDirectory = GetRequiredString(arguments, "outputDirectory"),
            OutputFormat = GetRequiredString(arguments, "outputFormat"),
            Converter = GetOptionalString(arguments, "converter"),
            AdvancedArguments = GetOptionalString(arguments, "advancedArguments")
        };
        var job = CreateJob(request);
        _jobs[job.JobId] = job;
        _ = Task.Run(() => RunJobAsync(job, request));
        return Task.FromResult<object>(new { jobId = job.JobId, status = job.Status, outputPath = job.OutputPath });
    }

    private object CancelJobForTool(string jobId)
    {
        var job = GetJob(jobId);
        job.Cancellation.Cancel();
        if (job.Status is "queued" or "running")
        {
            job.Status = "canceled";
            job.FinishedAt = DateTime.Now;
        }

        return ToJobDto(job);
    }

    private McpJob GetJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new InvalidOperationException("Job not found.");
        }

        return job;
    }

    private static object BuildToolTextResult(string text, bool isError)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text }
            },
            isError
        };
    }

    private static string GetRequiredString(JsonElement arguments, string name)
    {
        var value = GetOptionalString(arguments, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }

    private static string GetOptionalString(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static object? GetJsonRpcId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind == JsonValueKind.Null ? null : id.Clone();
    }

    private async Task WriteJsonRpcResultAsync(HttpListenerContext context, object? id, object result)
    {
        await WriteJsonAsync(context, new { jsonrpc = "2.0", id, result });
    }

    private async Task WriteJsonRpcErrorAsync(HttpListenerContext context, object? id, int code, string message)
    {
        await WriteJsonAsync(context, new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        });
    }

    private sealed class McpJsonRpcException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private async Task HandleJobRouteAsync(HttpListenerContext context, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !_jobs.TryGetValue(parts[2], out var job))
        {
            await WriteJsonAsync(context, new { error = "job not found" }, HttpStatusCode.NotFound);
            return;
        }

        if (context.Request.HttpMethod == "GET" && parts.Length == 3)
        {
            await WriteJsonAsync(context, ToJobDto(job));
            return;
        }

        if (context.Request.HttpMethod == "GET" && parts.Length == 4 && parts[3].Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(context, job.GetLog());
            return;
        }

        if (context.Request.HttpMethod == "POST" && parts.Length == 4 && parts[3].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            job.Cancellation.Cancel();
            if (job.Status is "queued" or "running")
            {
                job.Status = "canceled";
                job.FinishedAt = DateTime.Now;
            }

            await WriteJsonAsync(context, ToJobDto(job));
            return;
        }

        await WriteJsonAsync(context, new { error = "not found" }, HttpStatusCode.NotFound);
    }

    private McpJob CreateJob(McpConvertRequest request)
    {
        ValidateInputPath(request.InputPath);
        if (string.IsNullOrWhiteSpace(request.OutputFormat))
        {
            throw new InvalidOperationException("outputFormat is required.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.GetDirectoryName(request.InputPath) ?? ""
            : request.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);
        ValidateOutputDirectory(outputDirectory);

        var options = router.GetConverterOptions(request.InputPath, request.OutputFormat);
        var option = PickConverter(options, request.Converter);
        var outputFormat = ConversionRouter.NormalizeExtension(request.OutputFormat);
        var outputPath = BuildOutputPath(request.InputPath, outputDirectory, outputFormat);

        return new McpJob
        {
            InputPath = request.InputPath,
            OutputDirectory = outputDirectory,
            OutputFormat = outputFormat,
            OutputPath = outputPath,
            Converter = option.Rule.Converter
        };
    }

    private async Task RunJobAsync(McpJob job, McpConvertRequest request)
    {
        await _singleJobLock.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
        try
        {
            if (job.Cancellation.IsCancellationRequested)
            {
                job.Status = "canceled";
                job.FinishedAt = DateTime.Now;
                return;
            }

            job.Status = "running";
            job.StartedAt = DateTime.Now;
            var options = router.GetConverterOptions(request.InputPath, job.OutputFormat);
            var option = PickConverter(options, request.Converter);
            var executor = new ConversionExecutor(pathResolver);
            var exitCode = await executor.ExecuteAsync(
                option,
                request.InputPath,
                job.OutputPath,
                job.OutputDirectory,
                job.OutputFormat,
                request.AdvancedArguments,
                job.AppendLog,
                job.Cancellation.Token).ConfigureAwait(false);
            job.ExitCode = exitCode;
            job.Status = job.Cancellation.IsCancellationRequested
                ? "canceled"
                : exitCode == 0 ? "succeeded" : "failed";
        }
        catch (OperationCanceledException)
        {
            job.Status = "canceled";
            job.AppendLog("Canceled.");
        }
        catch (Exception ex)
        {
            job.Status = "failed";
            job.ErrorMessage = ex.Message;
            job.AppendLog(ex.ToString());
        }
        finally
        {
            job.FinishedAt = DateTime.Now;
            _singleJobLock.Release();
        }
    }

    private ConverterOption PickConverter(IReadOnlyList<ConverterOption> options, string converter)
    {
        if (options.Count == 0)
        {
            throw new InvalidOperationException("No converter matches the requested input and output format.");
        }

        var option = string.IsNullOrWhiteSpace(converter)
            ? options.FirstOrDefault(candidate => candidate.Tool.IsAvailable) ?? options[0]
            : options.FirstOrDefault(candidate => candidate.Rule.Converter.Equals(converter, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Converter not found: {converter}");
        if (!option.Tool.IsAvailable)
        {
            throw new InvalidOperationException($"Converter is not available: {option.Rule.Converter}");
        }

        return option;
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (!_settings.RequireToken)
        {
            return true;
        }

        var expected = "Bearer " + _settings.Token;
        return string.Equals(request.Headers["Authorization"], expected, StringComparison.Ordinal);
    }

    private static void ValidateInputPath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new InvalidOperationException("inputPath is required.");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input file does not exist.", inputPath);
        }

        using (File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
        }
    }

    private static void ValidateOutputDirectory(string outputDirectory)
    {
        var probe = Path.Combine(outputDirectory, ".convertx-mcp-write-test.tmp");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    private static string BuildOutputPath(string inputPath, string outputDirectory, string outputFormat)
    {
        var extension = outputFormat.TrimStart('.');
        if (extension.Contains('.', StringComparison.Ordinal))
        {
            extension = extension.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
        }

        return Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}.{extension}");
    }

    private object ToJobDto(McpJob job)
    {
        return new
        {
            jobId = job.JobId,
            status = job.Status,
            inputPath = job.InputPath,
            outputPath = job.OutputPath,
            outputDirectory = job.OutputDirectory,
            outputFormat = job.OutputFormat,
            converter = job.Converter,
            exitCode = job.ExitCode,
            errorMessage = job.ErrorMessage,
            createdAt = job.CreatedAt,
            startedAt = job.StartedAt,
            finishedAt = job.FinishedAt,
            logSummary = job.GetLogSummary()
        };
    }

    private async Task<T> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, _jsonOptions)
            ?? throw new InvalidOperationException("Invalid JSON body.");
    }

    private async Task WriteJsonAsync(HttpListenerContext context, object value, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        await WriteBytesAsync(context, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", statusCode);
    }

    private async Task WriteHtmlAsync(HttpListenerContext context, string html)
    {
        await WriteBytesAsync(context, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", HttpStatusCode.OK);
    }

    private async Task WriteTextAsync(HttpListenerContext context, string text)
    {
        await WriteBytesAsync(context, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", HttpStatusCode.OK);
    }

    private static async Task WriteBytesAsync(HttpListenerContext context, byte[] bytes, string contentType, HttpStatusCode statusCode)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    private string BuildDocsHtml()
    {
        var tokenHelp = _settings.RequireToken
            ? $"<p><strong>Authorization:</strong> <code>Bearer {_settings.Token}</code></p>"
            : "<p><strong>Authorization:</strong> disabled</p>";
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <title>ConvertX MCP HTTP API</title>
  <style>
    body { font-family: Segoe UI, Microsoft YaHei, sans-serif; margin: 32px; color: #1f2937; line-height: 1.55; }
    code, pre { background: #0f172a; color: #e5e7eb; border-radius: 8px; }
    code { padding: 2px 5px; }
    pre { padding: 14px; overflow: auto; }
    h1, h2 { color: #0f172a; }
  </style>
</head>
<body>
  <h1>ConvertX MCP HTTP API</h1>
  <p>Base URL: <code>{{BaseUrl.TrimEnd('/')}}</code></p>
  <p>Standard MCP endpoint: <code>{{BaseUrl.TrimEnd('/')}}/mcp</code></p>
  {{tokenHelp}}
  <h2>Codex / OpenCode MCP Config</h2>
  <pre>{
  "mcp": {
    "convertx": {
      "type": "remote",
      "url": "{{BaseUrl.TrimEnd('/')}}/mcp",
      "headers": {
        "Authorization": "Bearer {{_settings.Token}}"
      },
      "enabled": true
    }
  }
}</pre>
  <p>Available MCP tools: <code>convertx_list_formats</code>, <code>convertx_list_converters</code>, <code>convertx_convert</code>, <code>convertx_get_job</code>, <code>convertx_get_log</code>, <code>convertx_cancel_job</code>.</p>
  <h2>Endpoints</h2>
  <ul>
    <li><code>GET /health</code></li>
    <li><code>GET /api/formats?inputPath=...</code></li>
    <li><code>GET /api/converters?inputPath=...&outputFormat=...</code></li>
    <li><code>POST /api/convert</code></li>
    <li><code>GET /api/jobs/{jobId}</code></li>
    <li><code>GET /api/jobs/{jobId}/log</code></li>
    <li><code>POST /api/jobs/{jobId}/cancel</code></li>
  </ul>
  <h2>Convert Example</h2>
  <pre>curl -X POST "{{BaseUrl.TrimEnd('/')}}/api/convert" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {{_settings.Token}}" \
  -d "{\"inputPath\":\"D:\\in\\sample.png\",\"outputDirectory\":\"D:\\out\",\"outputFormat\":\"jpg\"}"</pre>
</body>
</html>
""";
    }
}
